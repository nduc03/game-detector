namespace GameScanner;

/// <summary>
/// Một game/chương trình nhận diện được. Đây là toàn bộ dữ liệu xuất ra JSON cho mỗi mục.
/// </summary>
public sealed class AppEntry
{
    public string? Name { get; set; }

    public string? Publisher { get; set; }

    /// <summary>File .exe chính được chọn để đại diện.</summary>
    public string? ExePath { get; set; }

    public string? InstallDir { get; set; }

    /// <summary>Các file .exe hợp lệ khác trong thư mục cài đặt (điểm ưu tiên thấp hơn ExePath).</summary>
    public List<string> CandidateExes { get; set; } = [];
}
