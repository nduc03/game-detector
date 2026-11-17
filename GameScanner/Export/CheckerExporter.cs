using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameScanner;

/// <summary>Một mục trong games.json của GameProcessChecker: chỉ gồm các trường mà checker thực sự đọc.</summary>
internal sealed class CheckerExportEntry
{
    public string ExePath { get; set; } = "";

    public List<string>? CandidateExes { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<CheckerExportEntry>))]
internal partial class CheckerExportJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Xuất danh sách app thành games.json cho GameProcessChecker. File được tối giản: mảng ở gốc,
/// không thụt dòng, mỗi mục chỉ có exePath (+ candidateExes nếu có), bỏ tên/nhà phát hành/thư mục
/// và bỏ các đường dẫn trùng nhau.
/// </summary>
internal static class CheckerExporter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Không escape ký tự ngoài ASCII (tên thư mục tiếng Việt, tiếng Trung...) để file ngắn và dễ đọc.
    private static readonly CheckerExportJsonContext Json = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });

    public static List<CheckerExportEntry> Build(IEnumerable<LibraryApp> apps, bool includeCandidates)
    {
        var entries = new List<CheckerExportEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (LibraryApp app in apps)
        {
            var paths = new List<string>();
            if (!string.IsNullOrWhiteSpace(app.ExePath))
                paths.Add(app.ExePath);
            if (includeCandidates)
                paths.AddRange(app.CandidateExes.Where(c => !string.IsNullOrWhiteSpace(c)));

            // Bỏ đường dẫn đã có ở mục trước (hoặc lặp lại trong chính mục này).
            List<string> unique = paths.Select(p => p.Trim()).Where(seen.Add).ToList();
            if (unique.Count == 0)
                continue;

            entries.Add(new CheckerExportEntry
            {
                ExePath = unique[0],
                CandidateExes = unique.Count > 1 ? unique.GetRange(1, unique.Count - 1) : null
            });
        }

        return entries;
    }

    public static string Serialize(List<CheckerExportEntry> entries) =>
        JsonSerializer.Serialize(entries, Json.ListCheckerExportEntry);

    /// <summary>Ghi file và trả về số mục đã xuất.</summary>
    public static int Export(IEnumerable<LibraryApp> apps, string filePath, bool includeCandidates)
    {
        List<CheckerExportEntry> entries = Build(apps, includeCandidates);
        AtomicFile.WriteAllText(filePath, Serialize(entries), Utf8NoBom);
        return entries.Count;
    }
}
