using System.Text;
using System.Text.Json;

namespace GameScanner;

/// <summary>Đọc/ghi danh sách nhớ của GUI (GameScanner_Data\rules.json) và các thao tác nhớ/quên game.</summary>
internal static class RulesStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>File hỏng được đổi tên thành bản sao lưu và trả về cảnh báo, không bị ghi đè âm thầm.</summary>
    public static RulesConfig Load(out string? warning)
    {
        warning = null;
        string path = AppPaths.RulesFile;
        if (!File.Exists(path))
            return new RulesConfig();

        try
        {
            return (JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.RulesConfig) ?? new RulesConfig()).Normalize();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            string backup = $"{path}.hong-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Move(path, backup, overwrite: true);
                warning = $"[data] Không đọc được {path} ({ex.Message}). Đã đổi tên thành {backup}, danh sách nhớ bắt đầu lại từ rỗng.";
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                warning = $"[data] Không đọc được {path} ({ex.Message}) và cũng không sao lưu được ({moveEx.Message}).";
            }

            return new RulesConfig();
        }
    }

    public static void Save(RulesConfig rules)
    {
        string json = JsonSerializer.Serialize(rules.Normalize(), AppJsonContext.Default.RulesConfig);
        AtomicFile.WriteAllText(AppPaths.RulesFile, json, Utf8NoBom);
    }

    public static RulesConfig Clone(RulesConfig rules) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(rules, AppJsonContext.Default.RulesConfig), AppJsonContext.Default.RulesConfig)!
            .Normalize();

    /// <summary>Nhớ game là luôn loại (khi người dùng xóa khỏi danh sách): bỏ khỏi includeApps, thêm vào excludeApps.</summary>
    public static void RememberExcluded(RulesConfig rules, LibraryApp app)
    {
        rules.IncludeApps.RemoveAll(r => AppIdentity.IsSame(r, app));
        if (!rules.ExcludeApps.Any(r => AppIdentity.IsSame(r, app)))
            rules.ExcludeApps.Add(app.ToRemembered());
    }

    /// <summary>Nhớ game là luôn chọn (khi người dùng thêm thủ công): bỏ khỏi excludeApps, thêm vào includeApps.</summary>
    public static void RememberIncluded(RulesConfig rules, LibraryApp app)
    {
        rules.ExcludeApps.RemoveAll(r => AppIdentity.IsSame(r, app));
        if (!rules.IncludeApps.Any(r => AppIdentity.IsSame(r, app)))
            rules.IncludeApps.Add(app.ToRemembered());
    }

    /// <summary>Game trong danh sách vừa được sửa: cập nhật bản nhớ của nó (nếu có) theo dữ liệu mới.</summary>
    public static bool UpdateRemembered(RulesConfig rules, LibraryApp before, LibraryApp after)
    {
        bool changed = false;
        foreach (List<RememberedApp> list in new[] { rules.IncludeApps, rules.ExcludeApps })
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (!AppIdentity.IsSame(list[i], before))
                    continue;

                RememberedApp updated = after.ToRemembered();
                updated.RememberedAtUtc = list[i].RememberedAtUtc;
                list[i] = updated;
                changed = true;
            }
        }

        return changed;
    }
}
