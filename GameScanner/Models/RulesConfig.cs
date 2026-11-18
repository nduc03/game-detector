namespace GameScanner;

/// <summary>
/// Nội dung rules.json (GUI lưu ở GameScanner_Data\rules.json; dòng lệnh đọc qua --rules).
/// Thứ tự ưu tiên khi quyết định một mục: includeApps -> excludeApps -> includePaths -> excludePaths;
/// không khớp gì thì giữ nguyên vị trí.
/// </summary>
public sealed class RulesConfig
{
    /// <summary>Game được nhớ là luôn chọn (vd. thêm thủ công). So khớp theo thư mục, xem <see cref="AppIdentity"/>.</summary>
    public List<RememberedApp> IncludeApps { get; set; } = [];

    /// <summary>Game được nhớ là luôn loại (vd. đã xóa khỏi danh sách). So khớp theo thư mục.</summary>
    public List<RememberedApp> ExcludeApps { get; set; } = [];

    /// <summary>Pattern đường dẫn (xem <see cref="PathPatternMatcher"/>) so với installDir và exePath: khớp -> included.</summary>
    public List<string> IncludePaths { get; set; } = [];

    /// <summary>Pattern đường dẫn: khớp -> excluded.</summary>
    public List<string> ExcludePaths { get; set; } = [];

    /// <summary>Bảo đảm không còn danh sách null (file viết tay có thể để null hoặc thiếu trường).</summary>
    public RulesConfig Normalize()
    {
        IncludeApps ??= [];
        ExcludeApps ??= [];
        IncludePaths ??= [];
        ExcludePaths ??= [];
        IncludeApps.RemoveAll(a => a is null);
        ExcludeApps.RemoveAll(a => a is null);
        IncludePaths.RemoveAll(string.IsNullOrWhiteSpace);
        ExcludePaths.RemoveAll(string.IsNullOrWhiteSpace);
        foreach (RememberedApp app in IncludeApps.Concat(ExcludeApps))
            app.CandidateExes ??= [];
        return this;
    }
}

/// <summary>Một game được nhớ trong rules.json.</summary>
public sealed class RememberedApp
{
    public string? Name { get; set; }

    public string? Publisher { get; set; }

    public string? ExePath { get; set; }

    public string? InstallDir { get; set; }

    public List<string> CandidateExes { get; set; } = [];

    public DateTime RememberedAtUtc { get; set; } = DateTime.UtcNow;

    public string DisplayFolder => AppIdentity.FolderOf(InstallDir, ExePath) ?? "";

    public RememberedApp Clone() => new()
    {
        Name = Name,
        Publisher = Publisher,
        ExePath = ExePath,
        InstallDir = InstallDir,
        CandidateExes = [.. CandidateExes ?? []],
        RememberedAtUtc = RememberedAtUtc
    };

    public AppEntry ToEntry() => new()
    {
        Name = Name,
        Publisher = Publisher,
        ExePath = ExePath,
        InstallDir = InstallDir,
        CandidateExes = [.. CandidateExes ?? []],
        Source = "remembered"
    };
}
