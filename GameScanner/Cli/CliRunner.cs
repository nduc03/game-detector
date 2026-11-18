using System.Text;
using System.Text.Json;

namespace GameScanner;

/// <summary>
/// Chế độ dòng lệnh (Unix filter), bật khi có ít nhất một cờ dòng lệnh:
///   GameScanner.exe --cli                    quét máy: Collect -> Heuristic -> Rules (nếu có rules.json)
///   GameScanner.exe --rules path\rules.json  chỉ định file rules
///   GameScanner.exe --no-exclude-output      bỏ "excluded" khỏi JSON đầu ra
///   ... | GameScanner.exe --rules rules.json nhận JSON từ stdin và áp dụng rules lên đó
/// Không có cờ nào thì mở giao diện. Việc stdin/stdout bị chuyển hướng KHÔNG tự bật chế độ này,
/// vì nhiều trình khởi chạy (Git Bash, IDE, script) luôn nối pipe vào app và sẽ làm app "không mở".
/// </summary>
internal static class CliRunner
{
    private static readonly string[] CliFlags = ["--cli", "--rules", "-r", "--no-exclude-output"];
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static bool ShouldRun(string[] args) =>
        args.Any(a => CliFlags.Contains(a, StringComparer.OrdinalIgnoreCase));

    public static int Run(string[] args)
    {
        // App là WinExe nên không có console riêng: gắn vào console của tiến trình gọi để log hiện ra đó.
        NativeConsole.AttachToParentConsole();
        using StreamWriter? stderrFile = RedirectLogToStderrFile();

        try
        {
            bool noExcludeOutput = HasFlag(args, "--no-exclude-output");
            string? explicitRulesPath = GetArgValue(args, "--rules") ?? GetArgValue(args, "-r");
            RulesConfig? rules = RulesLoader.Load(RulesLoader.Resolve(explicitRulesPath));

            string? stdin = NativeConsole.IsStdinRedirected ? ReadStdin() : null;

            PipelinePacket packet;
            if (!string.IsNullOrWhiteSpace(stdin))
            {
                // Nhận dữ liệu từ bước trước trong pipeline; chỉ áp rules khi truyền --rules tường minh
                packet = PacketValidator.Parse(stdin);
                if (!string.IsNullOrWhiteSpace(explicitRulesPath) && rules is not null)
                    packet = new IncludeExcludeRulesStage(rules).Process(packet);
            }
            else
            {
                packet = RunFullScan(rules);
            }

            if (noExcludeOutput)
                packet.Excluded = null;

            WriteStdout(JsonSerializer.Serialize(packet, AppJsonContext.Default.PipelinePacket));
            return 0;
        }
        catch (Exception ex)
        {
            // Mã thoát khác 0 để bước phía sau trong pipeline biết bước này lỗi.
            Log.Info($"[error] {ex.Message}");
            return 1;
        }
        finally
        {
            Log.SetSink(null);
        }
    }

    private static PipelinePacket RunFullScan(RulesConfig? rules)
    {
        var packet = HeuristicFilterStage.Process(RegistryAndLauncherCollector.Collect());

        if (rules is null)
        {
            Log.Info("[rules] Không tìm thấy file cấu hình rules -> tự động bỏ qua bước lọc rules.");
            return packet;
        }

        return new IncludeExcludeRulesStage(rules).Process(packet);
    }

    // stdin/stdout được đọc ghi trực tiếp bằng UTF-8 thay vì đổi Console.InputEncoding/OutputEncoding,
    // vì đổi encoding sẽ đổi luôn code page của cửa sổ dòng lệnh đang gọi và không tự trả lại.

    private static string ReadStdin()
    {
        using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void WriteStdout(string json)
    {
        using var writer = new StreamWriter(Console.OpenStandardOutput(), Utf8NoBom);
        writer.WriteLine(json);
    }

    /// <summary>
    /// stderr bị chuyển hướng ra file/pipe thì ghi log bằng UTF-8 (như bản console trước đây);
    /// còn khi stderr là cửa sổ console thì để Console.Error tự dùng encoding của console.
    /// </summary>
    private static StreamWriter? RedirectLogToStderrFile()
    {
        if (!NativeConsole.IsStderrRedirected)
            return null;

        var writer = new StreamWriter(Console.OpenStandardError(), Utf8NoBom) { AutoFlush = true };
        Log.SetSink(writer.WriteLine);
        return writer;
    }

    private static string? GetArgValue(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
}
