using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>Nơi phát hiện mục này (Steam, EpicGames, Registry, rules...).</summary>
    public string? Source { get; set; }

    /// <summary>true khi phát hiện từ manifest/khóa registry của game launcher; heuristic coi là game.</summary>
    public bool? LauncherGame { get; set; }

    /// <summary>true với gói redistributable (vd. Steamworks Common Redistributables); heuristic không bao giờ chọn.</summary>
    public bool? Redistributable { get; set; }

    /// <summary>
    /// Trường lạ do pipeline tùy chỉnh thêm vào: không dùng tới nhưng được giữ nguyên
    /// để bước phía sau trong pipeline vẫn đọc được.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
