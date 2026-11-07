namespace GameScanner;

/// <summary>
/// Nội dung rules.json. Mỗi pattern được so khớp với installDir và exePath của từng mục.
/// Pattern hỗ trợ: "regex:..." (regex), chứa '*' (wildcard, khớp toàn chuỗi), còn lại là chứa chuỗi con.
/// Không phân biệt hoa thường.
/// </summary>
public sealed class RulesConfig
{
    /// <summary>Khớp -> đưa vào included (ưu tiên hơn excludePaths).</summary>
    public List<string> IncludePaths { get; set; } = [];

    /// <summary>Khớp -> đưa vào excluded.</summary>
    public List<string> ExcludePaths { get; set; } = [];
}
