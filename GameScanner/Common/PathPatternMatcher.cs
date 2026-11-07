using System.Text.RegularExpressions;

namespace GameScanner;

internal static class PathPatternMatcher
{
    /// <summary>
    /// "regex:..." -> regex; có '*' -> wildcard khớp toàn chuỗi; còn lại -> chứa chuỗi con.
    /// Không phân biệt hoa thường.
    /// </summary>
    public static bool IsMatch(string? value, List<string>? patterns)
    {
        if (string.IsNullOrWhiteSpace(value) || patterns is null)
            return false;

        foreach (string pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            bool matched;
            if (pattern.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
            {
                matched = Regex.IsMatch(value, pattern[6..], RegexOptions.IgnoreCase);
            }
            else if (pattern.Contains('*'))
            {
                string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                matched = Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
            }
            else
            {
                matched = value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
            }

            if (matched)
                return true;
        }

        return false;
    }
}
