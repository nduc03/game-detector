namespace GameScanner;

/// <summary>Kiểm tra và tìm chương trình của pipeline tùy chỉnh.</summary>
internal static class ExecutableLocator
{
    private static readonly string[] Extensions = [".exe", ".com", ".bat", ".cmd"];

    /// <summary>
    /// Trả về đường dẫn đầy đủ tới chương trình, hoặc null nếu không hợp lệ. Chấp nhận:
    /// - đường dẫn tuyệt đối tới file .exe/.com/.bat/.cmd đang tồn tại (cho phép %BIẾN_MÔI_TRƯỜNG%);
    /// - tên lệnh trần có trong PATH hoặc cạnh GameScanner.exe (python, node, pwsh...).
    /// </summary>
    public static string? Resolve(string? raw)
    {
        string? value = PathUtils.Unquote(raw);
        if (value is null)
            return null;

        try
        {
            value = Environment.ExpandEnvironmentVariables(value);

            bool isBareName = value.IndexOfAny(['\\', '/', ':']) < 0;
            if (!isBareName)
                return Path.IsPathFullyQualified(value) ? FindWithExtension(Path.GetFullPath(value)) : null;

            foreach (string dir in SearchDirectories())
            {
                string? found = FindWithExtension(Path.Combine(dir, value));
                if (found is not null)
                    return found;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Chuỗi không phải đường dẫn hợp lệ.
        }

        return null;
    }

    private static IEnumerable<string> SearchDirectories()
    {
        yield return AppContext.BaseDirectory;

        string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return dir.Trim('"');
    }

    private static string? FindWithExtension(string path)
    {
        string extension = Path.GetExtension(path);
        if (extension.Length > 0 && Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return File.Exists(path) ? path : null;

        // Chưa ghi đuôi (python, node...) hoặc tên có dấu chấm (python3.12): thử lần lượt từng đuôi chạy được.
        foreach (string candidate in Extensions.Select(e => path + e))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
