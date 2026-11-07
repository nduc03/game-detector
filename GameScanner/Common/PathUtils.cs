namespace GameScanner;

internal static class PathUtils
{
    public static string NormalizeDirectoryPath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.Trim().TrimEnd('\\', '/');
        }
    }

    public static string NormalizeFilePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.Trim();
        }
    }

    /// <summary>Bỏ khoảng trắng và dấu nháy quanh đường dẫn; trả về null nếu rỗng.</summary>
    public static string? Unquote(string? raw)
    {
        string? trimmed = raw?.Trim().Trim('"').Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Lấy đường dẫn .exe từ giá trị DisplayIcon / UninstallString
    /// (vd. "C:\a\b.exe",0 hoặc C:\a\b.exe /uninstall). Trả về null nếu không phải .exe.
    /// </summary>
    public static string? ExtractExePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string s = raw.Trim();

        // Bỏ chỉ số icon ở cuối: "path.exe,0"
        int commaIdx = s.LastIndexOf(',');
        if (commaIdx > 0 && commaIdx > s.LastIndexOf('.'))
            s = s[..commaIdx].Trim();

        if (s.StartsWith('"'))
        {
            int endQuote = s.IndexOf('"', 1);
            if (endQuote > 1)
                s = s[1..endQuote];
        }
        else
        {
            int exeIdx = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIdx > 0)
                s = s[..(exeIdx + 4)];
        }

        return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s.Trim() : null;
    }
}
