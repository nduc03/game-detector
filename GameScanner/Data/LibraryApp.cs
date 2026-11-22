using System.Diagnostics;

namespace GameScanner;

internal static class AppOrigins
{
    public const string Scan = "scan";
    public const string Manual = "manual";
}

/// <summary>Một app được lưu trong dữ liệu của GUI (library.json).</summary>
internal sealed class LibraryApp
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string? Name { get; set; }

    public string? Publisher { get; set; }

    public string? ExePath { get; set; }

    public string? InstallDir { get; set; }

    public List<string> CandidateExes { get; set; } = [];

    /// <summary>Nơi pipeline phát hiện app (Steam, Registry...); null với app thêm thủ công.</summary>
    public string? Source { get; set; }

    /// <summary><see cref="AppOrigins.Scan"/> (pipeline thêm) hoặc <see cref="AppOrigins.Manual"/> (người dùng thêm).</summary>
    public string Origin { get; set; } = AppOrigins.Scan;

    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;

    public LibraryApp Clone() => new()
    {
        Id = Id,
        Name = Name,
        Publisher = Publisher,
        ExePath = ExePath,
        InstallDir = InstallDir,
        CandidateExes = [.. CandidateExes],
        Source = Source,
        Origin = Origin,
        AddedAtUtc = AddedAtUtc
    };

    public RememberedApp ToRemembered() => new()
    {
        Name = Name,
        Publisher = Publisher,
        ExePath = ExePath,
        InstallDir = InstallDir,
        CandidateExes = [.. CandidateExes],
        RememberedAtUtc = DateTime.UtcNow
    };

    /// <summary>Thư mục dùng để nhận diện app (installDir, không có thì thư mục chứa exe).</summary>
    public string? Folder => AppIdentity.FolderOf(InstallDir, ExePath);

    public AppEntry ToEntry() => new()
    {
        Name = Name,
        Publisher = Publisher,
        ExePath = ExePath,
        InstallDir = InstallDir,
        CandidateExes = [.. CandidateExes],
        Source = Source
    };

    public static LibraryApp FromEntry(AppEntry entry) => new()
    {
        Name = FirstNonEmpty(entry.Name, FileNameOf(entry.ExePath), FolderNameOf(entry.InstallDir)),
        Publisher = FirstNonEmpty(entry.Publisher),
        ExePath = FirstNonEmpty(entry.ExePath),
        InstallDir = FirstNonEmpty(entry.InstallDir),
        CandidateExes = [.. (entry.CandidateExes ?? []).Where(c => !string.IsNullOrWhiteSpace(c))],
        Source = FirstNonEmpty(entry.Source),
        Origin = AppOrigins.Scan
    };

    /// <summary>Tạo mục cho file exe người dùng tự chọn; tên/nhà phát hành lấy từ thông tin phiên bản của file nếu có.</summary>
    public static LibraryApp FromExe(string exePath)
    {
        string? name = null;
        string? publisher = null;
        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(exePath);
            name = FirstNonEmpty(info.ProductName, info.FileDescription);
            publisher = FirstNonEmpty(info.CompanyName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Không đọc được thông tin phiên bản: dùng tên file.
        }

        return new LibraryApp
        {
            Name = name ?? FileNameOf(exePath),
            Publisher = publisher,
            ExePath = exePath,
            InstallDir = Path.GetDirectoryName(exePath),
            Origin = AppOrigins.Manual
        };
    }

    private static string? FileNameOf(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFileNameWithoutExtension(path);

    private static string? FolderNameOf(string? dir) =>
        string.IsNullOrWhiteSpace(dir) ? null : Path.GetFileName(dir.TrimEnd('\\', '/'));

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
}

/// <summary>Nội dung file library.json.</summary>
internal sealed class LibraryData
{
    public int Version { get; set; } = 1;

    public List<LibraryApp> Apps { get; set; } = [];
}
