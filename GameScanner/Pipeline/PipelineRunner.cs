using System.Text.Json;

namespace GameScanner;

internal enum StepKind
{
    /// <summary>Có sẵn, luôn chạy đầu tiên, không tắt được: thu thập từ Registry và launcher, mọi mục vào excluded.</summary>
    Collect,

    /// <summary>Có sẵn: chọn game từ excluded sang included bằng heuristic.</summary>
    Heuristic,

    /// <summary>Chương trình ngoài do người dùng định nghĩa.</summary>
    Custom,

    /// <summary>Có sẵn, luôn chạy cuối, không xóa được: áp danh sách nhớ (rules.json trong thư mục dữ liệu).</summary>
    Remember
}

/// <summary>Một bước trong pipeline của GUI.</summary>
internal sealed record PipelineStep(StepKind Kind, string DisplayName, bool Enabled, CustomStage? Custom = null)
{
    public bool CanDisable => Kind != StepKind.Collect;

    public bool CanDelete => Kind == StepKind.Custom;

    /// <summary>
    /// Thứ tự chạy: Collect -> Heuristic -> các pipeline tùy chỉnh theo thứ tự người dùng sắp -> Danh sách nhớ.
    /// Danh sách nhớ luôn đứng cuối, kể cả khi thêm pipeline mới.
    /// </summary>
    public static List<PipelineStep> BuildAll(ScannerSettings settings)
    {
        var steps = new List<PipelineStep>
        {
            new(StepKind.Collect, "Thu thập (Registry + launcher)", Enabled: true),
            new(StepKind.Heuristic, "Heuristic (chọn game từ excluded)", settings.HeuristicStageEnabled)
        };

        steps.AddRange(settings.CustomStages.Select(s => new PipelineStep(StepKind.Custom, s.DisplayName, s.Enabled, s.Clone())));
        steps.Add(new PipelineStep(StepKind.Remember, "Danh sách nhớ (include / exclude)", settings.RememberStageEnabled));
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
    /// <param name="rules">Danh sách nhớ dùng cho bước <see cref="StepKind.Remember"/> (không bị sửa).</param>
    public static async Task<PipelinePacket> RunAsync(
        IReadOnlyList<PipelineStep> steps, PipelinePacket input, RulesConfig rules, CancellationToken cancellationToken)
    {
        PipelinePacket packet = Clone(input);
        RulesConfig rulesCopy = RulesStore.Clone(rules);
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
                        StepKind.Collect => RegistryAndLauncherCollector.CollectPacket(),
                        StepKind.Heuristic => HeuristicFilterStage.Process(packet),
                        StepKind.Remember => new IncludeExcludeRulesStage(rulesCopy).Process(packet),
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

    private static PipelinePacket Clone(PipelinePacket packet) =>
        PacketValidator.Parse(JsonSerializer.Serialize(packet, AppJsonContext.Default.PipelinePacket));
}
