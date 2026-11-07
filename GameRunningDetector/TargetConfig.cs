using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameProcessChecker;

public sealed class PipelinePacket
{
    [JsonPropertyName("included")]
    public List<AppEntry> Included { get; set; } = [];
}

public sealed class AppEntry
{
    [JsonPropertyName("exePath")]
    public string? ExePath { get; set; }

    // Giả định: mảng chuỗi, mỗi phần tử là đường dẫn đầy đủ tới 1 exe có thể là game này.
    [JsonPropertyName("candidateExes")]
    public List<string>? CandidateExes { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PipelinePacket))]
[JsonSerializable(typeof(List<AppEntry>))]
internal partial class CheckerJsonContext : JsonSerializerContext
{
}

/// <summary>Danh sách game cần theo dõi: đường dẫn đầy đủ + tên process (để lọc nhanh).</summary>
internal sealed record TargetSet(HashSet<string> Paths, HashSet<string> Names)
{
    public static TargetSet Empty => new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public bool IsEmpty => Paths.Count == 0;

    public void Add(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return;

        string fullPath = Path.GetFullPath(exePath.Trim());
        Paths.Add(fullPath);
        Names.Add(Path.GetFileNameWithoutExtension(fullPath));
    }
}

/// <summary>Đọc games.json và tự nạp lại khi file thay đổi.</summary>
internal sealed class TargetConfig
{
    private readonly string _path;
    private DateTime _lastWriteUtc = DateTime.MinValue;
    private TargetSet _current = TargetSet.Empty;

    public TargetConfig(string[] args)
    {
        string? fileArg = args.FirstOrDefault();
        _path = !string.IsNullOrWhiteSpace(fileArg)
            ? fileArg
            : Path.Combine(AppContext.BaseDirectory, "games.json");
    }

    /// <summary>Trả về danh sách hiện tại; chỉ đọc lại file khi thời gian sửa đổi đổi.</summary>
    public TargetSet GetCurrent()
    {
        try
        {
            if (!File.Exists(_path))
            {
                _lastWriteUtc = DateTime.MinValue;
                return _current = TargetSet.Empty;
            }

            DateTime writeUtc = File.GetLastWriteTimeUtc(_path);
            if (writeUtc != _lastWriteUtc)
            {
                _current = Parse(File.ReadAllText(_path));
                _lastWriteUtc = writeUtc;
            }
        }
        catch
        {
            // File đang bị ghi dở / JSON lỗi: giữ danh sách cũ, lần sau thử lại.
        }

        return _current;
    }

    private static TargetSet Parse(string json)
    {
        TargetSet result = TargetSet.Empty;
        if (string.IsNullOrWhiteSpace(json))
            return result;

        List<AppEntry> items = json.TrimStart().StartsWith('[')
            ? JsonSerializer.Deserialize(json, CheckerJsonContext.Default.ListAppEntry) ?? []
            : JsonSerializer.Deserialize(json, CheckerJsonContext.Default.PipelinePacket)?.Included ?? [];

        foreach (AppEntry item in items)
        {
            result.Add(item.ExePath);

            // hiện tại candidate exes đang chưa chuẩn xác, lẫn launcher vào, mà exePath cũng nhầm sang launcher
            // cách giải quyết trước mắt: chơi xong tắt launcher, cho đến khi cải thiện được thuật toán GameScanner, hoặc sửa thủ công file games.json
            if (item.CandidateExes is null)
                continue;

            foreach (string candidate in item.CandidateExes)
                result.Add(candidate);

        }

        return result;
    }
}