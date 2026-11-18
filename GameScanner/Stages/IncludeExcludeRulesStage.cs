namespace GameScanner;

/// <summary>
/// Bước "Danh sách nhớ" (GUI) / --rules (dòng lệnh): áp rules.json lên packet.
/// Thứ tự ưu tiên cho mỗi mục: includeApps -> excludeApps -> includePaths -> excludePaths;
/// không khớp gì thì giữ nguyên vị trí. Game nhớ được so theo thư mục (xem <see cref="AppIdentity"/>),
/// pattern được so với installDir và exePath (xem <see cref="PathPatternMatcher"/>).
/// Sau đó, game trong includeApps và đường dẫn tuyệt đối trong includePaths mà chưa mục nào khớp
/// thì được thêm mới vào included (nếu còn tồn tại trên đĩa).
/// </summary>
internal sealed class IncludeExcludeRulesStage(RulesConfig config)
{
    private readonly RulesConfig _config = config.Normalize();

    public PipelinePacket Process(PipelinePacket packet)
    {
        var included = new List<AppEntry>();
        var excluded = new List<AppEntry>();

        foreach (var entry in packet.Included ?? [])
            Route(entry, wasIncluded: true, included, excluded);

        foreach (var entry in packet.Excluded ?? [])
            Route(entry, wasIncluded: false, included, excluded);

        AddMissingRememberedApps(included);
        AddMissingExplicitPaths(included);

        packet.Included = included;
        // Giữ nguyên "không có excluded" (vd. đầu vào đã chạy --no-exclude-output) nếu không có gì bị loại.
        packet.Excluded = packet.Excluded is null && excluded.Count == 0 ? null : excluded;

        Log.Info($"[rules] Sau khi áp dụng rules: {included.Count} included, {excluded.Count} excluded.");
        return packet;
    }

    /// <summary>true = chọn, false = loại, null = không rule nào nói gì.</summary>
    public bool? Decide(AppEntry entry)
    {
        if (_config.IncludeApps.Any(a => AppIdentity.IsSame(a, entry)))
            return true;
        if (_config.ExcludeApps.Any(a => AppIdentity.IsSame(a, entry)))
            return false;
        if (MatchesPaths(entry, _config.IncludePaths))
            return true;
        if (MatchesPaths(entry, _config.ExcludePaths))
            return false;
        return null;
    }

    private void Route(AppEntry entry, bool wasIncluded, List<AppEntry> included, List<AppEntry> excluded) =>
        ((Decide(entry) ?? wasIncluded) ? included : excluded).Add(entry);

    private void AddMissingRememberedApps(List<AppEntry> included)
    {
        foreach (RememberedApp app in _config.IncludeApps)
        {
            if (included.Any(e => AppIdentity.IsSame(app, e)))
                continue;

            bool exists = (!string.IsNullOrWhiteSpace(app.ExePath) && File.Exists(app.ExePath))
                       || (!string.IsNullOrWhiteSpace(app.InstallDir) && Directory.Exists(app.InstallDir));
            if (!exists)
            {
                Log.Info($"[rules] Bỏ qua game nhớ \"{app.Name}\" vì không còn trên đĩa: {app.DisplayFolder}");
                continue;
            }

            included.Add(app.ToEntry());
            Log.Info($"[rules] Đã thêm game nhớ: {app.Name} ({app.DisplayFolder})");
        }
    }

    private void AddMissingExplicitPaths(List<AppEntry> included)
    {
        foreach (string pattern in _config.IncludePaths)
        {
            if (!ExplicitPathEntryFactory.IsLiteralAbsolutePath(pattern, out string path))
                continue;

            // Đã có mục khớp đường dẫn này (đã được giữ/cứu ở bước Route) thì không thêm trùng
            if (included.Any(e => MatchesPaths(e, [path])))
                continue;

            var entry = ExplicitPathEntryFactory.TryCreate(path);
            if (entry is null)
            {
                Log.Info($"[rules] includePaths bỏ qua (không tồn tại hoặc không phải thư mục/.exe): {path}");
                continue;
            }

            if (entry.ExePath is null)
                Log.Info($"[rules] Cảnh báo: không tìm thấy .exe hợp lệ trong '{path}', exePath sẽ để trống.");

            entry.Source = "rules";
            included.Add(entry);
            Log.Info($"[rules] Đã thêm mới từ includePaths: {path}");
        }
    }

    private static bool MatchesPaths(AppEntry entry, List<string> patterns) =>
        PathPatternMatcher.IsMatch(entry.InstallDir, patterns) ||
        PathPatternMatcher.IsMatch(entry.ExePath, patterns);
}
