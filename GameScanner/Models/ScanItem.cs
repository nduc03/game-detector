namespace GameScanner;

/// <summary>
/// Dữ liệu làm việc nội bộ trong lúc quét (Collect -> Heuristic).
/// Kết quả cuối cùng được chuyển sang <see cref="AppEntry"/> để xuất ra JSON.
/// </summary>
internal sealed class ScanItem
{
    public string Name { get; set; } = "";

    public string? Publisher { get; set; }

    public string? InstallDir { get; set; }
    public string? ExePath { get; set; }
    public List<string> CandidateExes { get; set; } = [];

    /// <summary>Phát hiện từ launcher (Steam, Epic, GOG, ...) nên mặc định đáng tin là game.</summary>
    public bool IsLauncher { get; set; }

    /// <summary>Gói redistributable (vd. Steamworks Common Redistributables), không phải game.</summary>
    public bool IsRedistributable { get; set; }

    public AppEntry ToEntry() => new()
    {
        Name = Name,
        Publisher = Publisher,
        ExePath = ExePath,
        InstallDir = InstallDir,
        CandidateExes = CandidateExes
    };
}
