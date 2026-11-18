using System.Text.RegularExpressions;

namespace GameScanner;

internal static class PathPatternMatcher
{
    /// <summary>
    /// Các loại pattern (đều không phân biệt hoa thường, '/' và '\' coi như nhau):
    /// - "regex:..."                 -> biểu thức chính quy;
    /// - có '*'                      -> wildcard, khớp toàn chuỗi;
    /// - đường dẫn tuyệt đối (C:\...) -> chính đường dẫn đó hoặc mọi thứ nằm bên trong nó, đúng ranh giới thư mục
    ///                                  ("D:\Games\A" khớp "D:\Games\A\a.exe" nhưng không khớp "D:\Games\AB");
    /// - còn lại                     -> chứa chuỗi con.
    /// </summary>
    public static bool IsMatch(string? value, List<string>? patterns)
    {
        if (string.IsNullOrWhiteSpace(value) || patterns is null)
            return false;

        foreach (string pattern in patterns)
        {
            if (!string.IsNullOrWhiteSpace(pattern) && IsMatch(value, pattern))
                return true;
        }

        return false;
    }

    public static bool IsMatch(string value, string pattern)
    {
        if (pattern.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
            return Regex.IsMatch(value, pattern[6..], RegexOptions.IgnoreCase);

        string normalizedValue = value.Replace('/', '\\');
        string normalizedPattern = (PathUtils.Unquote(pattern) ?? "").Replace('/', '\\');
        if (normalizedPattern.Length == 0)
            return false;

        if (normalizedPattern.Contains('*'))
        {
            string regex = "^" + Regex.Escape(normalizedPattern).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(normalizedValue, regex, RegexOptions.IgnoreCase);
        }

        if (IsAbsolutePath(normalizedPattern))
            return AppIdentity.IsUnder(normalizedValue, normalizedPattern);

        return normalizedValue.Contains(normalizedPattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Đường dẫn tuyệt đối kiểu Windows (C:\..., \\server\share) hoặc kiểu Unix (/...).</summary>
    public static bool IsAbsolutePath(string path) =>
        (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\')
        || path.StartsWith("\\\\", StringComparison.Ordinal)
        || (path.StartsWith('\\') && Path.DirectorySeparatorChar == '/');
}
