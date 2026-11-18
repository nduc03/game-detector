using System.Text;

namespace GameScanner;

/// <summary>
/// Biến chèn vào tham số của pipeline tùy chỉnh:
///   $in  -> đường dẫn file chứa packet đầu vào (trước khi xử lý)
///   $out -> đường dẫn file để chương trình ghi packet đã xử lý
/// </summary>
internal static class StageArguments
{
    public const string In = "$in";
    public const string Out = "$out";

    private static readonly string[] Variables = [Out, In];

    public static bool Contains(string? arguments, string variable)
    {
        bool found = false;
        Replace(arguments, (name, _) =>
        {
            found |= name == variable;
            return null;
        });
        return found;
    }

    /// <summary>
    /// Thay $in / $out bằng đường dẫn thật. Biến nằm ngoài cặp nháy kép được tự bọc nháy
    /// (đường dẫn tạm có thể chứa khoảng trắng); biến đã nằm trong cặp nháy thì chèn nguyên văn.
    /// </summary>
    public static string Expand(string? arguments, string inPath, string outPath) =>
        Replace(arguments, (name, inQuotes) =>
        {
            string? path = name switch
            {
                In => inPath,
                Out => outPath,
                _ => null
            };

            if (path is null)
                return null;

            return inQuotes ? path : $"\"{path}\"";
        });

    /// <summary>Duyệt chuỗi tham số, gọi <paramref name="replace"/> cho từng biến; trả null để giữ nguyên biến.</summary>
    private static string Replace(string? arguments, Func<string, bool, string?> replace)
    {
        if (string.IsNullOrEmpty(arguments))
            return "";

        var result = new StringBuilder(arguments.Length + 64);
        bool inQuotes = false;

        for (int i = 0; i < arguments.Length; i++)
        {
            char c = arguments[i];

            if (c == '"' && (i == 0 || arguments[i - 1] != '\\'))
            {
                inQuotes = !inQuotes;
                result.Append(c);
                continue;
            }

            string? variable = c == '$' ? MatchVariable(arguments, i) : null;
            if (variable is null)
            {
                result.Append(c);
                continue;
            }

            result.Append(replace(variable, inQuotes) ?? arguments.Substring(i, variable.Length));
            i += variable.Length - 1;
        }

        return result.ToString();
    }

    /// <summary>Biến bắt đầu tại vị trí <paramref name="index"/> (không tính $input, $output... là biến).</summary>
    private static string? MatchVariable(string text, int index)
    {
        foreach (string variable in Variables)
        {
            if (string.Compare(text, index, variable, 0, variable.Length, StringComparison.OrdinalIgnoreCase) != 0)
                continue;

            int end = index + variable.Length;
            if (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                continue;

            return variable;
        }

        return null;
    }
}
