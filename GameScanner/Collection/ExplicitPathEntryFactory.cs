namespace GameScanner;

/// <summary>
/// Tạo mục mới từ đường dẫn tuyệt đối người dùng khai báo trong includePaths
/// (dành cho game không nằm trong bất kỳ collector nào).
/// </summary>
internal static class ExplicitPathEntryFactory
{
    /// <summary>
    /// True nếu pattern là đường dẫn tuyệt đối "thuần": không có tiền tố "regex:", không có '*'.
    /// </summary>
    public static bool IsLiteralAbsolutePath(string pattern, out string path)
    {
        path = PathUtils.Unquote(pattern) ?? "";

        return path.Length > 0
            && !path.StartsWith("regex:", StringComparison.OrdinalIgnoreCase)
            && !path.Contains('*')
            && Path.IsPathFullyQualified(path);
    }

    /// <summary>
    /// - Đường dẫn là file .exe: dùng chính file đó làm exePath, installDir là thư mục chứa nó.
    /// - Đường dẫn là thư mục: coi là installDir, quét và chọn exe như các collector.
    /// Trả về null nếu đường dẫn không tồn tại hoặc là file không phải .exe.
    /// </summary>
    public static AppEntry? TryCreate(string path)
    {
        if (File.Exists(path))
            return CreateFromExe(PathUtils.NormalizeFilePath(path));

        if (Directory.Exists(path))
        {
            string dir = PathUtils.NormalizeDirectoryPath(path);
            var item = new ScanItem { Name = Path.GetFileName(dir), InstallDir = dir };
            ExecutableResolver.Resolve(item);
            return item.ToEntry();
        }

        return null;
    }

    private static AppEntry? CreateFromExe(string exe)
    {
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return null;

        var item = new ScanItem
        {
            Name = Path.GetFileNameWithoutExtension(exe),
            InstallDir = Path.GetDirectoryName(exe),
            ExePath = exe
        };
        ExecutableResolver.Resolve(item);

        // Người dùng chỉ định đích danh file này nên nó luôn là exe chính,
        // kể cả khi resolver coi nó là file phụ trợ (vd. tên chứa "setup", "launcher"...).
        if (!string.Equals(item.ExePath, exe, StringComparison.OrdinalIgnoreCase))
        {
            if (item.ExePath is not null)
                item.CandidateExes.Insert(0, item.ExePath);

            item.CandidateExes.RemoveAll(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase));
            item.ExePath = exe;
        }

        return item.ToEntry();
    }
}
