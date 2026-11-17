using System.Text.Json;

namespace GameScanner;

internal enum StepKind
{
    /// <summary>Có sẵn: Collect + Heuristic (nguồn dữ liệu, không dùng packet đầu vào).</summary>
    Scan,

    /// <summary>Có sẵn: lọc theo rules.json.</summary>
    Rules,

    /// <summary>Chương trình ngoài do người dùng định nghĩa.</summary>
    Custom
}

/// <summary>Một bước trong pipeline của GUI.</summary>
internal sealed record PipelineStep(StepKind Kind, string DisplayName, bool Enabled, CustomStage? Custom = null)
{
    /// <summary>Thứ tự chạy: Quét hệ thống -> Rules -> các pipeline tùy chỉnh theo thứ tự người dùng sắp.</summary>
    public static List<PipelineStep> BuildAll(ScannerSettings settings)
    {
        string rulesName = string.IsNullOrWhiteSpace(settings.RulesPath)
            ? "tự tìm rules.json"
            : Path.GetFileName(settings.RulesPath);

        var steps = new List<PipelineStep>
        {
            new(StepKind.Scan, "Quét hệ thống (Collect + Heuristic)", settings.ScanStageEnabled),
            new(StepKind.Rules, $"Rules ({rulesName})", settings.RulesStageEnabled)
        };

        steps.AddRange(settings.CustomStages.Select(s => new PipelineStep(StepKind.Custom, s.DisplayName, s.Enabled, s.Clone())));
        return steps;
    }
}

/// <summary>Một bước pipeline thất bại; toàn bộ kết quả của lần chạy bị hủy.</summary>
internal sealed class PipelineException(string stepName, string message, Exception inner) : Exception(message, inner)
{
    public string StepName { get; } = stepName;
}

internal static class PipelineRunner
{
    /// <summary>
    /// Chạy lần lượt các bước, kết quả bước trước là đầu vào bước sau, sau mỗi bước kiểm tra lại cấu trúc packet.
    /// Bất kỳ bước nào lỗi (vì bất kỳ lý do gì) thì dừng ngay và ném <see cref="PipelineException"/>:
    /// không có kết quả trung gian nào được trả về, và packet đầu vào không bị sửa.
    /// </summary>
    public static async Task<PipelinePacket> RunAsync(
        IReadOnlyList<PipelineStep> steps, PipelinePacket input, string? rulesPath, CancellationToken cancellationToken)
    {
        PipelinePacket packet = Clone(input);
        string runDirectory = AppPaths.CreateRunDirectory();

        try
        {
            for (int i = 0; i < steps.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                PipelineStep step = steps[i];
                Log.Info($"[pipeline] ({i + 1}/{steps.Count}) Bắt đầu: {step.DisplayName}");

                try
                {
                    packet = step.Kind switch
                    {
                        StepKind.Scan => HeuristicFilterStage.Process(RegistryAndLauncherCollector.Collect()),
                        StepKind.Rules => RunRules(packet, rulesPath),
                        StepKind.Custom => await CustomStageRunner.RunAsync(step.Custom!, packet, runDirectory, i + 1, cancellationToken),
                        _ => throw new InvalidOperationException($"Loại bước không hỗ trợ: {step.Kind}")
                    };

                    // Bước có sẵn cũng đi qua cùng bộ kiểm tra cấu trúc như pipeline tùy chỉnh.
                    packet = PacketValidator.Parse(JsonSerializer.Serialize(packet, AppJsonContext.Default.PipelinePacket));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new PipelineException(step.DisplayName, ex.Message, ex);
                }

                Log.Info($"[pipeline] Xong: {step.DisplayName} -> {packet.Included.Count} included, {packet.Excluded?.Count ?? 0} excluded.");
            }

            return packet;
        }
        finally
        {
            try
            {
                Directory.Delete(runDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // File tạm còn bị giữ: để hệ thống dọn sau.
            }
        }
    }

    private static PipelinePacket RunRules(PipelinePacket packet, string? explicitPath)
    {
        string? path;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            // Người dùng đã chỉ định file rules: thiếu file là lỗi chứ không âm thầm bỏ qua.
            if (!File.Exists(explicitPath))
                throw new FileNotFoundException($"Không tìm thấy file rules đã chọn: {explicitPath}");
            path = explicitPath;
        }
        else
        {
            path = RulesLoader.Resolve(null);
            if (path is null)
            {
                Log.Info("[rules] Không tìm thấy rules.json -> bỏ qua bước lọc rules.");
                return packet;
            }
        }

        return new IncludeExcludeRulesStage(RulesLoader.LoadStrict(path)).Process(packet);
    }

    private static PipelinePacket Clone(PipelinePacket packet) =>
        PacketValidator.Parse(JsonSerializer.Serialize(packet, AppJsonContext.Default.PipelinePacket));
}
