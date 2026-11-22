namespace GameScanner;

/// <summary>
/// Dữ liệu của app nằm trong thư mục GameScanner_Data cạnh GameScanner.exe (portable: chép cả thư mục
/// sang máy khác là mang theo được). Thư mục chứa exe vì thế phải ghi được (đừng đặt trong Program Files).
/// </summary>
internal static class AppPaths
{
    public const string DataFolderName = "GameScanner_Data";

    public static string DataDir { get; } = Path.Combine(AppContext.BaseDirectory, DataFolderName);

    /// <summary>Danh sách app đã quét / thêm thủ công.</summary>
    public static string LibraryFile => Path.Combine(DataDir, "library.json");

    /// <summary>Cấu hình pipeline và các tùy chọn của GUI.</summary>
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    /// <summary>Danh sách nhớ (include/exclude) của bước cuối pipeline, cùng định dạng với --rules.</summary>
    public static string RulesFile => Path.Combine(DataDir, "rules.json");

    /// <summary>Packet cuối cùng của lần chạy pipeline thành công gần nhất (đầu vào khi chạy lẻ một bước).</summary>
    public static string LastPacketFile => Path.Combine(DataDir, "last-packet.json");

    /// <summary>Thư mục tạm riêng cho một lần chạy pipeline (chứa file $in / $out).</summary>
    public static string CreateRunDirectory() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GameScanner", "run-" + Guid.NewGuid().ToString("N"))).FullName;
}
