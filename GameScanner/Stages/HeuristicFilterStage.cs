namespace GameScanner;

/// <summary>
/// Bước Heuristic: chấm điểm các mục đang ở "excluded" (Collect đặt mọi mục ở đó), mục được nhận là game
/// thì chuyển sang "included". Không đụng tới mục đã ở "included" (do bước khác chọn), nên chạy lại nhiều lần
/// hay đặt sau bước khác đều an toàn.
/// - Mục có exe hợp lệ và phát hiện từ launcher (launcherGame): được chọn (trừ gói redistributable).
/// - Mục còn lại (vd. chỉ có trong Registry Uninstall): mặc định 0 điểm, chỉ khi có dấu hiệu
///   cộng điểm (game engine, thư mục Games...) đạt ngưỡng thì mới được chọn; tên/publisher giống
///   phần mềm thông thường sẽ bị trừ điểm.
/// - Mục không có file exe tồn tại, hoặc nằm trong thư mục Windows: không được chọn.
/// </summary>
internal static class HeuristicFilterStage
{
    private const int MinScoreToKeep = 45;

    // Thư mục Windows thực tế của máy (thường là C:\Windows), lấy từ hệ thống thay vì viết cứng
    private static readonly string WindowsDir =
        PathUtils.NormalizeDirectoryPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

    // Điểm cộng/trừ cho mục Registry Uninstall (điểm khởi đầu là 0)
    private const int GameEngineScore = 50;     // một mình dấu hiệu engine đã đủ qua ngưỡng
    private const int GamesFolderScore = 20;    // chỉ là dấu hiệu yếu, không đủ qua ngưỡng nếu đứng một mình
    private const int NonGameNamePenalty = -45;
    private const int NonGamePublisherPenalty = -40;

    private static readonly string[] NonGamePublishers =
    [
        "Microsoft Corporation", "Google LLC", "Adobe Inc.", "JetBrains",
        "Oracle Corporation", "Mozilla", "VideoLAN", "7-Zip", "WinRAR",
        "Realtek", "Intel Corporation", "NVIDIA Corporation", "Advanced Micro Devices",
        "Docker Inc.", "Git", "Node.js", "Python Software Foundation", "Logitech", "Razer Inc."
    ];

    private static readonly string[] NonGameNameKeywords =
    [
        "driver", "sdk", "runtime", "redistributable", "visual c++", ".net",
        "webview2", "update", "helper", "service", "browser", "office",
        "studio", "intellij", "vscode", "antivirus", "vpn", "discord",
        "spotify", "obs studio", "geforce experience", "steam", "epic games launcher",
        "riot client", "ubisoft connect", "ea app", "gog galaxy", "battle.net"
    ];

    private static readonly string[] GameEngineIndicators =
    [
        "UnityPlayer.dll", "steam_api.dll", "steam_api64.dll", "EOSSDK-Win64-Shipping.dll",
        "galaxy64.dll", "uplay_r1_loader64.dll", "upc_r2_loader64.dll", "nvngx_dlss.dll",
        "amd_fidelityfx_dx12.dll", "fmod.dll", "fmodstudio.dll", "bink2w64.dll", "AkSoundEngine.dll"
    ];

    public static PipelinePacket Process(PipelinePacket packet)
    {
        packet.Included ??= [];
        if (packet.Excluded is null)
        {
            Log.Info("[heuristic] Packet không có \"excluded\" -> không có gì để chấm điểm.");
            return packet;
        }

        var remaining = new List<AppEntry>();
        int promoted = 0;
        foreach (AppEntry entry in packet.Excluded)
        {
            if (IsGame(entry))
            {
                packet.Included.Add(entry);
                promoted++;
            }
            else
            {
                remaining.Add(entry);
            }
        }

        packet.Excluded = remaining;
        Log.Info($"[heuristic] Chọn thêm {promoted} game từ excluded (ngưỡng >= {MinScoreToKeep}): "
                 + $"{packet.Included.Count} included, {remaining.Count} excluded.");
        return packet;
    }

    private static bool IsGame(AppEntry item)
    {
        if (item.Redistributable == true)
            return false;

        // Mọi thứ nằm trong thư mục Windows (C:\Windows\...) đều là thành phần hệ thống
        if (IsUnderWindowsDir(item.InstallDir) || IsUnderWindowsDir(item.ExePath))
            return false;

        if (string.IsNullOrWhiteSpace(item.ExePath) || !File.Exists(item.ExePath))
            return false;

        return item.LauncherGame == true || ScoreRegistryApp(item) >= MinScoreToKeep;
    }

    private static bool IsUnderWindowsDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || WindowsDir.Length == 0)
            return false;

        return path.Equals(WindowsDir, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(WindowsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreRegistryApp(AppEntry item)
    {
        int score = 0;

        if (!string.IsNullOrWhiteSpace(item.InstallDir) && Directory.Exists(item.InstallDir))
        {
            if (HasGameEngineSignature(item, item.InstallDir))
                score += GameEngineScore;

            if (item.InstallDir.Contains("Games", StringComparison.OrdinalIgnoreCase) ||
                item.InstallDir.Contains("SteamLibrary", StringComparison.OrdinalIgnoreCase))
                score += GamesFolderScore;
        }

        string name = item.Name ?? "";
        if (NonGameNameKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            score += NonGameNamePenalty;

        if (item.Publisher is { } publisher &&
            NonGamePublishers.Any(p => publisher.Contains(p, StringComparison.OrdinalIgnoreCase)))
            score += NonGamePublisherPenalty;

        return score;
    }

    private static bool HasGameEngineSignature(AppEntry item, string installDir)
    {
        var exes = new List<string>(item.CandidateExes ?? []);
        if (!string.IsNullOrWhiteSpace(item.ExePath))
            exes.Add(item.ExePath);

        if (exes.Any(e => e.EndsWith("-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (Directory.Exists(Path.Combine(installDir, "Engine", "Binaries")))
            return true;

        if (ContainsEngineLibrary(installDir))
            return true;

        foreach (string exe in exes)
        {
            string? exeDir = Path.GetDirectoryName(exe);
            if (string.IsNullOrWhiteSpace(exeDir))
                continue;

            string exeName = Path.GetFileNameWithoutExtension(exe);
            if (Directory.Exists(Path.Combine(exeDir, $"{exeName}_Data")) || ContainsEngineLibrary(exeDir))
                return true;
        }

        return false;
    }

    private static bool ContainsEngineLibrary(string dir) =>
        GameEngineIndicators.Any(indicator => File.Exists(Path.Combine(dir, indicator)));
}