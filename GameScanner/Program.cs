using System.Text;
using System.Text.Json;

namespace GameScanner;

/// <summary>
/// Cách dùng:
///   GameScanner.exe                          quét máy: Collect -> Heuristic -> Rules (nếu có rules.json)
///   GameScanner.exe --rules path\rules.json  chỉ định file rules
///   GameScanner.exe --no-exclude-output      bỏ "excluded" khỏi JSON đầu ra
///   ... | GameScanner.exe --rules rules.json nhận JSON từ stdin và áp dụng rules lên đó
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        bool noExcludeOutput = HasFlag(args, "--no-exclude-output");
        string? explicitRulesPath = GetArgValue(args, "--rules") ?? GetArgValue(args, "-r");
        RulesConfig? rules = RulesLoader.Load(RulesLoader.Resolve(explicitRulesPath));

        string? stdin = Console.IsInputRedirected ? await Console.In.ReadToEndAsync() : null;

        PipelinePacket packet;
        if (!string.IsNullOrWhiteSpace(stdin))
        {
            // Nhận dữ liệu từ bước trước trong pipeline; chỉ áp rules khi truyền --rules tường minh
            packet = JsonSerializer.Deserialize(stdin, AppJsonContext.Default.PipelinePacket) ?? new PipelinePacket();
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
        return 0;
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
