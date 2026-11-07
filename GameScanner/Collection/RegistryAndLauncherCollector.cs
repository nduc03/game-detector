using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GameScanner;

/// <summary>
/// Stage 1: thu thập game/chương trình từ các launcher và Registry Uninstall,
/// gộp trùng theo thư mục cài đặt rồi chọn exe cho từng mục.
/// </summary>
internal static class RegistryAndLauncherCollector
{
    private const string SteamworksRedistAppId = "228980";

    private static readonly string[] LauncherKeyPrefixes = ["Steam App ", "Uplay Install ", "Riot Game "];
    private static readonly string[] LauncherPathMarkers = [@"\steamapps\common\", @"\Epic Games\", @"\HoYoPlay\", @"\Battle.net\"];
    private static readonly string[] LauncherPublishers = ["Cognosphere", "miHoYo", "Blizzard"];

    private static readonly (RegistryHive Hive, RegistryView View, string Path)[] UninstallKeys =
    [
        (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.CurrentUser, RegistryView.Registry32, @"Software\Microsoft\Windows\CurrentVersion\Uninstall")
    ];

    public static List<ScanItem> Collect()
    {
        Log.Info("[collect] Đang quét Registry và các Game Launcher trên mọi ổ đĩa...");

        var raw = new List<ScanItem>();
        Run("Steam", CollectSteam, raw);
        Run("EpicGames", CollectEpic, raw);
        Run("UbisoftConnect", CollectUbisoft, raw);
        Run("GOGGalaxy", CollectGog, raw);
        Run("EAApp", CollectEa, raw);
        Run("RiotGames", CollectRiot, raw);
        Run("WindowsUninstallRegistry", CollectUninstallRegistry, raw);

        var items = Deduplicate(raw);
        foreach (var item in items)
            ExecutableResolver.Resolve(item);

        Log.Info($"[collect] Thu thập xong {items.Count} mục ứng viên.");
        return items;
    }

    #region Launchers

    private static void CollectSteam(List<ScanItem> list)
    {
        string? steamPath = ReadRegistryValue(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath")
                         ?? ReadRegistryValue(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath")
                         ?? ReadRegistryValue(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Valve\Steam", "InstallPath");

        if (string.IsNullOrWhiteSpace(steamPath))
            return;

        steamPath = PathUtils.NormalizeDirectoryPath(steamPath);
        if (!Directory.Exists(steamPath))
            return;

        foreach (string library in GetSteamLibraryFolders(steamPath))
        {
            string steamAppsDir = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamAppsDir))
                continue;

            foreach (string acfFile in Directory.EnumerateFiles(steamAppsDir, "appmanifest_*.acf"))
            {
                string content = File.ReadAllText(acfFile);
                string? appId = MatchVdfValue(content, "appid");
                string? name = MatchVdfValue(content, "name");
                string? installDirName = MatchVdfValue(content, "installdir");

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDirName))
                    continue;

                list.Add(new ScanItem
                {
                    Name = name,
                    InstallDir = PathUtils.NormalizeDirectoryPath(Path.Combine(steamAppsDir, "common", installDirName)),
                    IsLauncher = true,
                    IsRedistributable = appId == SteamworksRedistAppId
                });
            }
        }
    }

    private static HashSet<string> GetSteamLibraryFolders(string steamPath)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamPath };
        string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

        if (!File.Exists(vdfPath))
            return folders;

        string content = File.ReadAllText(vdfPath);
        foreach (Match m in Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
        {
            string libPath = m.Groups[1].Value.Replace(@"\\", @"\");
            if (Directory.Exists(libPath))
                folders.Add(PathUtils.NormalizeDirectoryPath(libPath));
        }

        return folders;
    }

    private static void CollectEpic(List<ScanItem> list)
    {
        var manifestDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? appData = ReadRegistryValue(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Epic Games\EpicGamesLauncher", "AppDataPath")
                       ?? ReadRegistryValue(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Epic Games\EpicGamesLauncher", "AppDataPath")
                       ?? ReadRegistryValue(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Epic Games\EOS", "ModSdkMetadataDir");

        if (!string.IsNullOrWhiteSpace(appData))
        {
            manifestDirs.Add(Path.Combine(appData, "Manifests"));
            manifestDirs.Add(appData);
        }

        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(programData))
            manifestDirs.Add(Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests"));

        foreach (string dir in manifestDirs.Where(Directory.Exists))
        {
            foreach (string itemFile in Directory.EnumerateFiles(dir, "*.item"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(itemFile));
                    var root = doc.RootElement;

                    string? name = GetJsonString(root, "DisplayName");
                    string? installLocation = GetJsonString(root, "InstallLocation");
                    string? launchExe = GetJsonString(root, "LaunchExecutable");

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installLocation))
                        continue;

                    string installDir = PathUtils.NormalizeDirectoryPath(installLocation);
                    list.Add(new ScanItem
                    {
                        Name = name,
                        InstallDir = installDir,
                        ExePath = string.IsNullOrWhiteSpace(launchExe)
                            ? null
                            : PathUtils.NormalizeFilePath(Path.Combine(installDir, launchExe.Replace('/', '\\'))),
                        IsLauncher = true
                    });
                }
                catch
                {
                    // Bỏ qua manifest lỗi cú pháp hoặc không đọc được
                }
            }
        }
    }

    private static void CollectUbisoft(List<ScanItem> list)
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var installsKey = baseKey.OpenSubKey(@"SOFTWARE\Ubisoft\Launcher\Installs");
            if (installsKey is null)
                continue;

            foreach (string subKeyName in installsKey.GetSubKeyNames())
            {
                using var gameKey = installsKey.OpenSubKey(subKeyName);
                string? installDir = gameKey?.GetValue("InstallDir")?.ToString();
                if (string.IsNullOrWhiteSpace(installDir))
                    continue;

                string normDir = PathUtils.NormalizeDirectoryPath(installDir);
                string name = ReadRegistryValue(
                                  RegistryHive.LocalMachine, view,
                                  $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Uplay Install {subKeyName}",
                                  "DisplayName")
                              ?? Path.GetFileName(normDir);

                list.Add(new ScanItem { Name = name, Publisher = "Ubisoft", InstallDir = normDir, IsLauncher = true });
            }
        }
    }

    private static void CollectGog(List<ScanItem> list)
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var gamesKey = baseKey.OpenSubKey(@"SOFTWARE\GOG.com\Games");
            if (gamesKey is null)
                continue;

            foreach (string subKeyName in gamesKey.GetSubKeyNames())
            {
                using var gameKey = gamesKey.OpenSubKey(subKeyName);
                string? path = gameKey?.GetValue("path")?.ToString() ?? gameKey?.GetValue("PATH")?.ToString();
                string? gameName = gameKey?.GetValue("gameName")?.ToString() ?? gameKey?.GetValue("GAMENAME")?.ToString();
                string? exe = gameKey?.GetValue("exe")?.ToString() ?? gameKey?.GetValue("EXE")?.ToString();

                if (string.IsNullOrWhiteSpace(path))
                    continue;

                string normDir = PathUtils.NormalizeDirectoryPath(path);
                list.Add(new ScanItem
                {
                    Name = !string.IsNullOrWhiteSpace(gameName) ? gameName : Path.GetFileName(normDir),
                    InstallDir = normDir,
                    ExePath = string.IsNullOrWhiteSpace(exe) ? null : PathUtils.NormalizeFilePath(exe),
                    IsLauncher = true
                });
            }
        }
    }

    private static void CollectEa(List<ScanItem> list)
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var eaKey = baseKey.OpenSubKey(@"SOFTWARE\Electronic Arts\EA Core\Installed Games");
            if (eaKey is null)
                continue;

            foreach (string subKeyName in eaKey.GetSubKeyNames())
            {
                using var gameKey = eaKey.OpenSubKey(subKeyName);
                string? installDir = gameKey?.GetValue("install_dir")?.ToString();
                if (string.IsNullOrWhiteSpace(installDir))
                    continue;

                string normDir = PathUtils.NormalizeDirectoryPath(installDir);
                list.Add(new ScanItem { Name = Path.GetFileName(normDir), Publisher = "Electronic Arts", InstallDir = normDir, IsLauncher = true });
            }
        }
    }

    private static void CollectRiot(List<ScanItem> list)
    {
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData))
            return;

        string metadataDir = Path.Combine(programData, "Riot Games", "Metadata");
        if (!Directory.Exists(metadataDir))
            return;

        foreach (string subDir in Directory.EnumerateDirectories(metadataDir))
        {
            foreach (string yamlFile in Directory.EnumerateFiles(subDir, "*.product_settings.yaml"))
            {
                var m = Regex.Match(File.ReadAllText(yamlFile), @"product_install_full_path:\s*""?([^""\r\n]+)""?", RegexOptions.IgnoreCase);
                if (!m.Success)
                    continue;

                string gameName = Path.GetFileName(subDir).Replace(".live", "", StringComparison.OrdinalIgnoreCase);
                list.Add(new ScanItem
                {
                    Name = $"Riot - {gameName}",
                    Publisher = "Riot Games",
                    InstallDir = PathUtils.NormalizeDirectoryPath(m.Groups[1].Value.Trim()),
                    IsLauncher = true
                });
            }
        }
    }

    #endregion

    #region Registry Uninstall

    private static void CollectUninstallRegistry(List<ScanItem> list)
    {
        foreach (var (hive, view, regPath) in UninstallKeys)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstallKey = baseKey.OpenSubKey(regPath);
            if (uninstallKey is null)
                continue;

            foreach (string subKeyName in uninstallKey.GetSubKeyNames())
            {
                try
                {
                    using var appKey = uninstallKey.OpenSubKey(subKeyName);
                    string? name = appKey?.GetValue("DisplayName")?.ToString()?.Trim();
                    if (appKey is null || string.IsNullOrWhiteSpace(name))
                        continue;

                    if (appKey.GetValue("SystemComponent") is 1)
                        continue;
                    if (!string.IsNullOrWhiteSpace(appKey.GetValue("ReleaseType")?.ToString()))
                        continue;

                    string? publisher = appKey.GetValue("Publisher")?.ToString()?.Trim();
                    string? installLocation = PathUtils.Unquote(appKey.GetValue("InstallLocation")?.ToString());
                    string? iconExe = PathUtils.ExtractExePath(appKey.GetValue("DisplayIcon")?.ToString());
                    string? uninstallExe = PathUtils.ExtractExePath(appKey.GetValue("UninstallString")?.ToString());

                    // Không có InstallLocation: suy ra từ thư mục của icon/uninstaller (nếu file tồn tại)
                    if (string.IsNullOrWhiteSpace(installLocation))
                    {
                        string? anchorExe = new[] { iconExe, uninstallExe }.FirstOrDefault(e => e is not null && File.Exists(e));
                        installLocation = anchorExe is null ? null : Path.GetDirectoryName(anchorExe);
                    }

                    if (string.IsNullOrWhiteSpace(installLocation) && string.IsNullOrWhiteSpace(iconExe))
                        continue;

                    list.Add(new ScanItem
                    {
                        Name = name,
                        Publisher = publisher,
                        InstallDir = string.IsNullOrWhiteSpace(installLocation)
                            ? null
                            : PathUtils.NormalizeDirectoryPath(installLocation),
                        ExePath = iconExe is null ? null : PathUtils.NormalizeFilePath(iconExe),
                        IsLauncher = IsLauncherEntry(subKeyName, publisher, installLocation)
                    });
                }
                catch
                {
                    // Bỏ qua key bị khóa quyền đọc
                }
            }
        }
    }

    private static bool IsLauncherEntry(string subKeyName, string? publisher, string? installLocation)
    {
        if (LauncherKeyPrefixes.Any(p => subKeyName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (string.IsNullOrWhiteSpace(installLocation))
            return false;

        return LauncherPathMarkers.Any(m => installLocation.Contains(m, StringComparison.OrdinalIgnoreCase))
            || (publisher is not null
                && LauncherPublishers.Any(p => publisher.Contains(p, StringComparison.OrdinalIgnoreCase)));
    }

    #endregion

    #region Deduplicate

    /// <summary>Gộp các mục trùng thư mục cài đặt (vd. cùng một game xuất hiện ở launcher và Registry).</summary>
    private static List<ScanItem> Deduplicate(List<ScanItem> raw)
    {
        var byDir = new Dictionary<string, ScanItem>(StringComparer.OrdinalIgnoreCase);
        var noDir = new List<ScanItem>();

        foreach (var item in raw)
        {
            if (string.IsNullOrWhiteSpace(item.InstallDir))
            {
                noDir.Add(item);
            }
            else if (byDir.TryGetValue(item.InstallDir, out var existing))
            {
                existing.IsLauncher |= item.IsLauncher;
                existing.IsRedistributable |= item.IsRedistributable;

                if (string.IsNullOrWhiteSpace(existing.Publisher))
                    existing.Publisher = item.Publisher;
                if (string.IsNullOrWhiteSpace(existing.ExePath))
                    existing.ExePath = item.ExePath;
            }
            else
            {
                byDir[item.InstallDir] = item;
            }
        }

        return byDir.Values.Concat(noDir).ToList();
    }

    #endregion

    #region Helpers

    private static void Run(string source, Action<List<ScanItem>> collect, List<ScanItem> target)
    {
        try
        {
            collect(target);
        }
        catch (Exception ex)
        {
            Log.Info($"[collect:{source}] Cảnh báo: {ex.Message}");
        }
    }

    private static string? ReadRegistryValue(RegistryHive hive, RegistryView view, string subKeyPath, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var subKey = baseKey.OpenSubKey(subKeyPath);
            return subKey?.GetValue(valueName)?.ToString();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? MatchVdfValue(string content, string key)
    {
        var m = Regex.Match(content, $"\"{Regex.Escape(key)}\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? GetJsonString(JsonElement element, string propName) =>
        element.TryGetProperty(propName, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString()
            : null;

    #endregion
}
