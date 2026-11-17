using System.Text;
using System.Text.Json;

namespace GameScanner;

/// <summary>
/// Danh sách app của GUI, lưu thành JSON. Chọn JSON thay vì SQLite vì dữ liệu chỉ vài trăm mục,
/// không cần truy vấn, người dùng sửa tay được và không phải đóng gói thêm thư viện native vào exe.
/// </summary>
internal sealed class AppLibrary
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _path;

    private AppLibrary(string path, List<LibraryApp> apps)
    {
        _path = path;
        Apps = apps;
    }

    public List<LibraryApp> Apps { get; }

    /// <summary>
    /// Đọc file dữ liệu. File hỏng không bị ghi đè âm thầm: được đổi tên thành bản sao lưu và
    /// trả về cảnh báo để hiển thị cho người dùng.
    /// </summary>
    public static AppLibrary Load(string path, out string? warning)
    {
        warning = null;
        if (!File.Exists(path))
            return new AppLibrary(path, []);

        try
        {
            LibraryData? data = JsonSerializer.Deserialize(File.ReadAllText(path), AppDataJsonContext.Default.LibraryData);
            List<LibraryApp> apps = data?.Apps ?? [];
            apps.RemoveAll(a => a is null);
            foreach (LibraryApp app in apps)
            {
                app.CandidateExes ??= [];
                if (string.IsNullOrWhiteSpace(app.Id))
                    app.Id = Guid.NewGuid().ToString("N");
            }

            return new AppLibrary(path, apps);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            string backup = $"{path}.hong-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Move(path, backup, overwrite: true);
                warning = $"[data] Không đọc được {path} ({ex.Message}). Đã đổi tên file thành {backup} và bắt đầu với danh sách rỗng.";
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                warning = $"[data] Không đọc được {path} ({ex.Message}) và cũng không sao lưu được ({moveEx.Message}).";
            }

            return new AppLibrary(path, []);
        }
    }

    public void Save()
    {
        string json = JsonSerializer.Serialize(new LibraryData { Apps = Apps }, AppDataJsonContext.Default.LibraryData);
        AtomicFile.WriteAllText(_path, json, Utf8NoBom);
    }

    public LibraryApp? FindByExe(string? exePath) =>
        string.IsNullOrWhiteSpace(exePath) ? null : Apps.FirstOrDefault(a => SamePath(a.ExePath, exePath));

    /// <summary>App trong danh sách ứng với mục của pipeline (null nếu là app mới).</summary>
    public LibraryApp? FindMatch(AppEntry entry) => Apps.FirstOrDefault(a => IsSameApp(a, entry));

    /// <summary>
    /// Lấy trạng thái cuối cùng của pipeline (danh sách included) và chỉ thêm những app chưa có.
    /// App cũ không bị sửa hay xóa, kể cả khi lần quét này không còn thấy nó.
    /// </summary>
    public List<LibraryApp> MergeNew(IEnumerable<AppEntry> included)
    {
        var added = new List<LibraryApp>();
        foreach (AppEntry entry in included)
        {
            if (string.IsNullOrWhiteSpace(entry.ExePath) && string.IsNullOrWhiteSpace(entry.InstallDir))
                continue;

            if (FindMatch(entry) is not null)
                continue;

            LibraryApp app = LibraryApp.FromEntry(entry);
            Apps.Add(app);
            added.Add(app);
        }

        return added;
    }

    /// <summary>Packet dựng từ danh sách hiện có, dùng làm đầu vào khi chưa có kết quả pipeline nào.</summary>
    public PipelinePacket ToPacket() => new()
    {
        Included = Apps.Select(a => a.ToEntry()).ToList(),
        Excluded = []
    };

    /// <summary>
    /// Cùng một app khi trùng exe chính, hoặc cùng thư mục cài đặt mà exe chính của bên này
    /// nằm trong danh sách exe ứng viên của bên kia (người dùng / pipeline đã đổi exe chính).
    /// </summary>
    private static bool IsSameApp(LibraryApp app, AppEntry entry)
    {
        if (SamePath(app.ExePath, entry.ExePath))
            return true;

        if (!SameDirectory(app.InstallDir, entry.InstallDir))
            return false;

        if (string.IsNullOrWhiteSpace(app.ExePath) || string.IsNullOrWhiteSpace(entry.ExePath))
            return true;

        return app.CandidateExes.Any(c => SamePath(c, entry.ExePath))
            || (entry.CandidateExes ?? []).Any(c => SamePath(c, app.ExePath));
    }

    private static bool SamePath(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(PathUtils.NormalizeFilePath(a), PathUtils.NormalizeFilePath(b), StringComparison.OrdinalIgnoreCase);

    private static bool SameDirectory(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(PathUtils.NormalizeDirectoryPath(a), PathUtils.NormalizeDirectoryPath(b), StringComparison.OrdinalIgnoreCase);
}
