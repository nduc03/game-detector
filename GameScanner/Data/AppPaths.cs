namespace GameScanner;

/// <summary>Nơi GUI lưu dữ liệu: %LOCALAPPDATA%\GameScanner.</summary>
internal static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameScanner");

    /// <summary>Danh sách app đã quét / thêm thủ công.</summary>
    public static string LibraryFile => Path.Combine(DataDir, "library.json");

    /// <summary>Cấu hình pipeline và các tùy chọn của GUI.</summary>
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    /// <summary>Packet cuối cùng của lần chạy pipeline thành công gần nhất (đầu vào khi chạy lẻ một bước).</summary>
    public static string LastPacketFile => Path.Combine(DataDir, "last-packet.json");

    /// <summary>Thư mục tạm riêng cho một lần chạy pipeline (chứa file $in / $out).</summary>
    public static string CreateRunDirectory() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "GameScanner", "run-" + Guid.NewGuid().ToString("N"))).FullName;
}
