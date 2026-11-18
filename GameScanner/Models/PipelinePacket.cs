using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameScanner;

/// <summary>
/// Dữ liệu trao đổi giữa các bước pipeline (cả stdin lẫn stdout đều dùng cấu trúc này).
/// </summary>
public sealed class PipelinePacket
{
    /// <summary>Các mục được chọn sau khi lọc.</summary>
    public List<AppEntry> Included { get; set; } = [];

    /// <summary>
    /// Các mục bị loại. Bằng null (ẩn khỏi JSON) khi chạy với cờ --no-exclude-output.
    /// </summary>
    public List<AppEntry>? Excluded { get; set; } = [];

    /// <summary>Trường lạ ở gốc packet do pipeline tùy chỉnh thêm vào, được giữ nguyên khi chuyển tiếp.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
