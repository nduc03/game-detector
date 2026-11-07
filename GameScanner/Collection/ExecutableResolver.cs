using System.Text.RegularExpressions;

namespace GameScanner;

/// <summary>
/// Quét file .exe trong thư mục cài đặt, loại file phụ trợ (uninstaller, crash handler, redist...),
/// chọn 1 exe chính và đưa phần hợp lệ còn lại vào CandidateExes.
/// </summary>
internal static class ExecutableResolver
{
    private const int MaxScanDepth = 3;

    private static readonly string[] HelperNameKeywords =
    [
        "unins", "setup", "installer", "redist", "directx", "crash", "reporter", "handler",
        "easyanticheat", "eac_launcher", "battleye", "beclient", "beservice",
        "cefprocess", "webhelper", "updater", "patcher", "benchmark",
        "activation", "cleanup", "touchup", "dotnet", "physx"
    ];

    private static readonly string[] HelperPathMarkers =
    [
        @"\__installer\", @"\_commonredist\", @"\directx\", @"\vcredist\"
    ];

    public static void Resolve(ScanItem item)
    {
        var valid = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfValid(string path)
        {
            string full = PathUtils.NormalizeFilePath(path);
            if (!IsHelperExecutable(full) && seen.Add(full))
                valid.Add(full);
        }

        // exe do manifest/registry cung cấp (Epic, GOG, DisplayIcon): giữ lại nếu tồn tại và không phải file phụ trợ
        string? preset = null;
        if (!string.IsNullOrWhiteSpace(item.ExePath) && File.Exists(item.ExePath))
        {
            string full = PathUtils.NormalizeFilePath(item.ExePath);
            if (!IsHelperExecutable(full))
            {
                preset = full;
                AddIfValid(full);
            }
        }

        if (!string.IsNullOrWhiteSpace(item.InstallDir) && Directory.Exists(item.InstallDir))
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = MaxScanDepth,
                IgnoreInaccessible = true,
                AttributesToSkip = 0
            };

            foreach (string exe in Directory.EnumerateFiles(item.InstallDir, "*.exe", options))
                AddIfValid(exe);
        }

        item.ExePath = preset ?? SelectBest(item.Name, item.InstallDir, valid);
        item.CandidateExes = valid
            .Where(x => !string.Equals(x, item.ExePath, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static bool IsHelperExecutable(string exePath)
    {
        string lowerName = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
        string lowerFull = exePath.ToLowerInvariant();

        return HelperPathMarkers.Any(lowerFull.Contains)
            || HelperNameKeywords.Any(lowerName.Contains);
    }

    private static string? SelectBest(string appName, string? installDir, List<string> candidates)
    {
        if (candidates.Count == 0)
            return null;

        string cleanAppName = NormalizeName(appName);
        string cleanFolderName = string.IsNullOrWhiteSpace(installDir)
            ? ""
            : NormalizeName(Path.GetFileName(installDir));

        return candidates.MaxBy(exe => Score(exe, cleanAppName, cleanFolderName));
    }

    private static double Score(string exe, string cleanAppName, string cleanFolderName)
    {
        double score = 0;
        string fileName = Path.GetFileNameWithoutExtension(exe);
        string cleanExeName = NormalizeName(fileName);
        string exeDir = Path.GetDirectoryName(exe) ?? "";

        // Unity: có thư mục <ExeName>_Data đi kèm
        if (Directory.Exists(Path.Combine(exeDir, $"{fileName}_Data")))
            score += 100;

        // Unreal: *-Win64-Shipping.exe
        if (fileName.EndsWith("Shipping", StringComparison.OrdinalIgnoreCase))
            score += 90;

        // Giống tên game / tên thư mục cài đặt
        if (cleanAppName.Length > 0)
        {
            if (cleanExeName == cleanAppName)
                score += 60;
            else if (cleanExeName.Contains(cleanAppName) || cleanAppName.Contains(cleanExeName))
                score += 35;
        }

        if (cleanFolderName.Length > 0 && cleanExeName == cleanFolderName)
            score += 50;

        // File lớn hơn thường là exe chính (tối đa +30)
        try
        {
            score += Math.Min(30.0, new FileInfo(exe).Length / (2.0 * 1024 * 1024));
        }
        catch (IOException)
        {
            // Bỏ qua file không đọc được kích thước
        }

        return score;
    }

    private static string NormalizeName(string name) =>
        Regex.Replace(name, "[^a-zA-Z0-9]", "").ToLowerInvariant();
}
