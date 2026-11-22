using System.Text;
using System.Text.Json;

namespace GameScanner;

/// <summary>Chương trình lấy kết quả của một pipeline tùy chỉnh từ đâu.</summary>
internal enum StageResultSource
{
    /// <summary>JSON chương trình in ra stdout.</summary>
    Stdout,

    /// <summary>File tại một đường dẫn cố định do người dùng khai báo.</summary>
    Path,

    /// <summary>File chương trình ghi ra đường dẫn $out.</summary>
    OutFile
}

/// <summary>Một pipeline quét do người dùng tự định nghĩa: chạy một chương trình ngoài để xử lý packet.</summary>
internal sealed class CustomStage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string? Name { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Đường dẫn tuyệt đối tới chương trình, hoặc tên lệnh có trong PATH (python, node, ...).</summary>
    public string ExePath { get; set; } = "";

    /// <summary>Tham số dòng lệnh, có thể chứa $in và $out.</summary>
    public string Arguments { get; set; } = "";

    /// <summary>Ghi packet đầu vào vào stdin của chương trình (giống nối bằng `|`).</summary>
    public bool PipeStdin { get; set; } = true;

    public StageResultSource ResultSource { get; set; } = StageResultSource.Stdout;

    /// <summary>Đường dẫn file kết quả khi <see cref="ResultSource"/> là <see cref="StageResultSource.Path"/>.</summary>
    public string? ResultPath { get; set; }

    /// <summary>Quá số giây này thì coi là lỗi và dừng chương trình; 0 = không giới hạn.</summary>
    public int TimeoutSeconds { get; set; } = 600;

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name) ? Name.Trim()
        : !string.IsNullOrWhiteSpace(ExePath) ? Path.GetFileName(ExePath.Trim().Trim('"'))
        : "(chưa đặt tên)";

    public CustomStage Clone() => (CustomStage)MemberwiseClone();
}

/// <summary>Nội dung file settings.json.</summary>
internal sealed class ScannerSettings
{
    // Bước Collect luôn bật nên không có cờ.

    public bool HeuristicStageEnabled { get; set; } = true;

    /// <summary>Bước cuối "Danh sách nhớ" (rules.json trong thư mục dữ liệu).</summary>
    public bool RememberStageEnabled { get; set; } = true;

    /// <summary>Pipeline tùy chỉnh, chạy giữa Heuristic và Danh sách nhớ theo đúng thứ tự này.</summary>
    public List<CustomStage> CustomStages { get; set; } = [];

    /// <summary>Khi xuất cho GameProcessChecker có kèm candidateExes hay chỉ exe chính.</summary>
    public bool ExportCandidateExes { get; set; } = true;
}

internal static class SettingsStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static ScannerSettings Load(out string? warning)
    {
        warning = null;
        string path = AppPaths.SettingsFile;
        if (!File.Exists(path))
            return new ScannerSettings();

        try
        {
            ScannerSettings settings =
                JsonSerializer.Deserialize(File.ReadAllText(path), AppDataJsonContext.Default.ScannerSettings) ?? new ScannerSettings();
            settings.CustomStages ??= [];
            settings.CustomStages.RemoveAll(s => s is null);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            warning = $"[data] Không đọc được {path} ({ex.Message}) -> dùng cấu hình mặc định.";
            return new ScannerSettings();
        }
    }

    public static void Save(ScannerSettings settings)
    {
        string json = JsonSerializer.Serialize(settings, AppDataJsonContext.Default.ScannerSettings);
        AtomicFile.WriteAllText(AppPaths.SettingsFile, json, Utf8NoBom);
    }
}
