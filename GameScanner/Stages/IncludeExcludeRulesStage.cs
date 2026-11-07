namespace GameScanner;

/// <summary>
/// Stage 3: áp dụng includePaths / excludePaths từ rules.json lên installDir và exePath.
/// Thứ tự ưu tiên: khớp includePaths -> included; khớp excludePaths -> excluded;
/// không khớp rule nào -> giữ nguyên vị trí hiện tại.
/// Ngoài ra, includePaths là đường dẫn tuyệt đối (file .exe hoặc thư mục) mà chưa mục nào khớp
/// thì sẽ được thêm mới vào included.
/// </summary>
internal sealed class IncludeExcludeRulesStage(RulesConfig config)
{
    public PipelinePacket Process(PipelinePacket packet)
    {
        var included = new List<AppEntry>();
        var excluded = new List<AppEntry>();

        foreach (var entry in packet.Included)
            Route(entry, wasIncluded: true, included, excluded);

        foreach (var entry in packet.Excluded ?? [])
            Route(entry, wasIncluded: false, included, excluded);

        AddMissingExplicitPaths(included);

        packet.Included = included;
        packet.Excluded = excluded;

        Log.Info($"[rules] Sau khi áp dụng rules: {included.Count} included, {excluded.Count} excluded.");
        return packet;
    }

    private void Route(AppEntry entry, bool wasIncluded, List<AppEntry> included, List<AppEntry> excluded)
    {
        bool keep;
        if (MatchesPaths(entry, config.IncludePaths))
            keep = true;
        else if (MatchesPaths(entry, config.ExcludePaths))
            keep = false;
        else
            keep = wasIncluded;

        (keep ? included : excluded).Add(entry);
    }

    private void AddMissingExplicitPaths(List<AppEntry> included)
    {
        foreach (string pattern in config.IncludePaths)
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

            included.Add(entry);
            Log.Info($"[rules] Đã thêm mới từ includePaths: {path}");
        }
    }

    private static bool MatchesPaths(AppEntry entry, List<string> patterns) =>
        PathPatternMatcher.IsMatch(entry.InstallDir, patterns) ||
        PathPatternMatcher.IsMatch(entry.ExePath, patterns);
}
