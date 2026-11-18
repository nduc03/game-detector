using System.Text.Json;

namespace GameScanner;

internal static class RulesLoader
{
    private const string DefaultFileName = "rules.json";

    /// <summary>
    /// Có đường dẫn tường minh (--rules) thì dùng nó; nếu không thì tự tìm rules.json
    /// ở thư mục làm việc hiện tại rồi tới thư mục chứa file .exe. Trả về null nếu không có file.
    /// </summary>
    public static string? Resolve(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return File.Exists(explicitPath) ? explicitPath : null;

        return new[] { Environment.CurrentDirectory, AppContext.BaseDirectory }
            .Select(dir => Path.Combine(dir, DefaultFileName))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Đọc file rules cho pipeline của GUI: file hỏng là lỗi (ném exception) chứ không bỏ qua.</summary>
    public static RulesConfig LoadStrict(string path)
    {
        try
        {
            var config = JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.RulesConfig)
                ?? throw new InvalidDataException($"File rules rỗng: {path}");
            config.IncludePaths ??= [];
            config.ExcludePaths ??= [];
            Log.Info($"[rules] Dùng file cấu hình: {path}");
            return config;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"File rules '{path}' không phải JSON hợp lệ: {ex.Message}", ex);
        }
    }

    public static RulesConfig? Load(string? path)
    {
        if (path is null)
            return null;

        try
        {
            var config = JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.RulesConfig);
            if (config is not null)
                Log.Info($"[rules] Đã tự động kích hoạt file cấu hình: {path}");
            return config;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Info($"[rules] Lỗi đọc file rules '{path}' ({ex.Message}) -> bỏ qua bước lọc rules.");
            return null;
        }
    }
}
