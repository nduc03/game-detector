using System.Text.Json;

namespace GameScanner;

internal static class RulesLoader
{
    private const string DefaultFileName = "rules.json";

    /// <summary>
    /// Có đường dẫn tường minh (--rules) thì dùng nó; nếu không thì tự tìm rules.json ở thư mục làm việc
    /// hiện tại, thư mục chứa file .exe, rồi tới danh sách nhớ của GUI (GameScanner_Data\rules.json).
    /// Trả về null nếu không có file.
    /// </summary>
    public static string? Resolve(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return File.Exists(explicitPath) ? explicitPath : null;

        return new[]
            {
                Path.Combine(Environment.CurrentDirectory, DefaultFileName),
                Path.Combine(AppContext.BaseDirectory, DefaultFileName),
                AppPaths.RulesFile
            }
            .FirstOrDefault(File.Exists);
    }

    public static RulesConfig? Load(string? path)
    {
        if (path is null)
            return null;

        try
        {
            var config = JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.RulesConfig)?.Normalize();
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
