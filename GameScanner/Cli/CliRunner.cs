using System.Text;
using System.Text.Json;

namespace GameScanner;

/// <summary>
/// Chế độ dòng lệnh (Unix filter) giữ nguyên như trước khi có GUI:
///   GameScanner.exe --cli                    quét máy: Collect -> Heuristic -> Rules (nếu có rules.json)
///   GameScanner.exe --rules path\rules.json  chỉ định file rules
///   GameScanner.exe --no-exclude-output      bỏ "excluded" khỏi JSON đầu ra
///   ... | GameScanner.exe --rules rules.json nhận JSON từ stdin và áp dụng rules lên đó
///   GameScanner.exe > scan.json              stdin/stdout bị chuyển hướng cũng tự chạy chế độ này
/// Thêm --gui để luôn mở giao diện kể cả khi stdin/stdout đang bị chuyển hướng.
/// </summary>
internal static class CliRunner
{
    private static readonly string[] CliFlags = ["--cli", "--rules", "-r", "--no-exclude-output"];

    public static bool ShouldRun(string[] args)
    {
        if (HasFlag(args, "--gui"))
            return false;

        if (args.Any(a => CliFlags.Contains(a, StringComparer.OrdinalIgnoreCase)))
            return true;

        return NativeConsole.IsStdinRedirected || NativeConsole.IsStdoutRedirected;
    }

    public static int Run(string[] args)
    {
        NativeConsole.AttachToParentConsole();
        TryUseUtf8Console();

        try
        {
            bool noExcludeOutput = HasFlag(args, "--no-exclude-output");
            string? explicitRulesPath = GetArgValue(args, "--rules") ?? GetArgValue(args, "-r");
            RulesConfig? rules = RulesLoader.Load(RulesLoader.Resolve(explicitRulesPath));

            string? stdin = NativeConsole.IsStdinRedirected ? Console.In.ReadToEnd() : null;

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

            Console.Out.WriteLine(JsonSerializer.Serialize(packet, AppJsonContext.Default.PipelinePacket));
            Console.Out.Flush();
            return 0;
        }
        catch (Exception ex)
        {
            // Mã thoát khác 0 để bước phía sau trong pipeline biết bước này lỗi.
            Log.Info($"[error] {ex.Message}");
            return 1;
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

    private static void TryUseUtf8Console()
    {
        try
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Không có console thật (chỉ có pipe/file): giữ mặc định.
        }
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
