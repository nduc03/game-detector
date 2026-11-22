using System.Text.RegularExpressions;

namespace GameScanner;

/// <summary>
/// Cửa sổ xem và sửa "Danh sách nhớ" (rules.json trong thư mục dữ liệu): game luôn loại, game luôn chọn,
/// và các mẫu đường dẫn. Thay cho việc viết rules.json bằng tay.
/// </summary>
internal sealed class RulesEditForm : Form
{
    private sealed record PatternRef(string Pattern);

    private readonly RulesConfig _rules;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly ListView _excludeList = CreateList();
    private readonly ListView _includeList = CreateList();
    private readonly Button _moveButton;

    /// <param name="rules">Bản sao danh sách nhớ; chỉ được ghi lại khi người dùng bấm OK.</param>
    public RulesEditForm(RulesConfig rules)
    {
        _rules = rules.Normalize();

        Text = "Danh sách nhớ";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(860, 500);
        MinimumSize = new Size(640, 380);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        _tabs.TabPages.Add(new TabPage("Luôn loại (exclude)") { Controls = { _excludeList } });
        _tabs.TabPages.Add(new TabPage("Luôn chọn (include)") { Controls = { _includeList } });
        _tabs.SelectedIndexChanged += (_, _) => UpdateButtons();
        foreach (ListView list in new[] { _excludeList, _includeList })
        {
            list.SelectedIndexChanged += (_, _) => UpdateButtons();
            list.DoubleClick += (_, _) => EditSelected();
            list.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Delete)
                {
                    e.Handled = true;
                    DeleteSelected();
                }
            };
        }

        _moveButton = FormLayout.CreateButton("Chuyển sang Luôn chọn", (_, _) => MoveSelected());
        var side = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            WrapContents = false
        };
        side.Controls.AddRange(
        [
            WideButton("Thêm thư mục game…", (_, _) => AddFolder()),
            WideButton("Thêm file exe…", (_, _) => AddExe()),
            WideButton("Thêm mẫu đường dẫn…", (_, _) => AddPattern()),
            WideButton("Sửa…", (_, _) => EditSelected()),
            WideButton("Xóa (quên)", (_, _) => DeleteSelected()),
            _moveButton
        ]);
        _moveButton.MinimumSize = new Size(190, 0);

        Label hint = FormLayout.CreateHint(
            "Bước \"Danh sách nhớ\" chạy cuối pipeline. Game nhớ được so theo thư mục cài đặt (không phân biệt hoa thường): "
            + "game xóa khỏi danh sách chính được tự thêm vào \"Luôn loại\", game thêm thủ công vào \"Luôn chọn\". "
            + "Game luôn chọn mà lần quét không thấy sẽ được thêm lại nếu còn trên đĩa. "
            + "Mẫu đường dẫn: đường dẫn tuyệt đối (khớp chính nó và mọi thứ bên trong), có * là wildcard, "
            + "\"regex:...\" là biểu thức chính quy, còn lại là chứa chuỗi con. "
            + "Ưu tiên: game luôn chọn → game luôn loại → mẫu luôn chọn → mẫu luôn loại.", 800);
        hint.Dock = DockStyle.Fill;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_tabs, 0, 0);
        layout.Controls.Add(side, 1, 0);
        layout.Controls.Add(hint, 0, 1);
        layout.SetColumnSpan(hint, 2);
        FlowLayoutPanel buttons = FormLayout.CreateDialogButtons(this, (_, _) => DialogResult = DialogResult.OK);
        layout.Controls.Add(buttons, 0, 2);
        layout.SetColumnSpan(buttons, 2);
        layout.Resize += (_, _) => hint.MaximumSize = new Size(Math.Max(200, layout.ClientSize.Width - 30), 0);
        Controls.Add(layout);

        RefreshLists();
    }

    /// <summary>Danh sách nhớ sau khi sửa, hợp lệ khi DialogResult là OK.</summary>
    public RulesConfig Result => _rules;

    private bool OnIncludeTab => _tabs.SelectedIndex == 1;

    private ListView CurrentList => OnIncludeTab ? _includeList : _excludeList;

    private List<RememberedApp> CurrentApps => OnIncludeTab ? _rules.IncludeApps : _rules.ExcludeApps;

    private List<RememberedApp> OtherApps => OnIncludeTab ? _rules.ExcludeApps : _rules.IncludeApps;

    private List<string> CurrentPatterns => OnIncludeTab ? _rules.IncludePaths : _rules.ExcludePaths;

    private List<string> OtherPatterns => OnIncludeTab ? _rules.ExcludePaths : _rules.IncludePaths;

    private static ListView CreateList()
    {
        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = true,
            ShowItemToolTips = true
        };
        list.Columns.Add("Loại", 70);
        list.Columns.Add("Tên", 200);
        list.Columns.Add("Thư mục / mẫu", 380);
        list.Columns.Add("Ngày nhớ", 110);
        return list;
    }

    private static Button WideButton(string text, EventHandler onClick)
    {
        Button button = FormLayout.CreateButton(text, onClick);
        button.MinimumSize = new Size(190, 0);
        return button;
    }

    private void RefreshLists()
    {
        Fill(_excludeList, _rules.ExcludeApps, _rules.ExcludePaths);
        Fill(_includeList, _rules.IncludeApps, _rules.IncludePaths);
        _tabs.TabPages[0].Text = $"Luôn loại (exclude) — {_rules.ExcludeApps.Count + _rules.ExcludePaths.Count}";
        _tabs.TabPages[1].Text = $"Luôn chọn (include) — {_rules.IncludeApps.Count + _rules.IncludePaths.Count}";
        UpdateButtons();
    }

    private static void Fill(ListView list, List<RememberedApp> apps, List<string> patterns)
    {
        list.BeginUpdate();
        list.Items.Clear();
        foreach (RememberedApp app in apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            list.Items.Add(new ListViewItem(["Game", app.Name ?? "", app.DisplayFolder, app.RememberedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")])
            {
                Tag = app,
                ToolTipText = app.ExePath
            });
        }

        foreach (string pattern in patterns)
            list.Items.Add(new ListViewItem(["Mẫu", "", pattern, ""]) { Tag = new PatternRef(pattern) });

        list.EndUpdate();
    }

    private void UpdateButtons() =>
        _moveButton.Text = OnIncludeTab ? "Chuyển sang Luôn loại" : "Chuyển sang Luôn chọn";

    private List<object> SelectedTags => CurrentList.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag!).ToList();

    #region Thêm

    private void AddFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Chọn thư mục cài đặt của game", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string folder = PathUtils.NormalizeDirectoryPath(dialog.SelectedPath);
        AddApp(new RememberedApp { Name = Path.GetFileName(folder), InstallDir = folder });
    }

    private void AddExe()
    {
        using var dialog = new OpenFileDialog { Title = "Chọn file exe của game", Filter = "Chương trình (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        AddApp(LibraryApp.FromExe(PathUtils.NormalizeFilePath(dialog.FileName)).ToRemembered());
    }

    /// <summary>Thêm game vào tab hiện tại; game trùng thư mục ở tab kia được chuyển sang (một game không thể vừa chọn vừa loại).</summary>
    private void AddApp(RememberedApp app)
    {
        if (CurrentApps.FirstOrDefault(a => SameApp(a, app)) is { } existing)
        {
            MessageBox.Show(this, $"Đã có game trùng thư mục trong danh sách này: {existing.Name}\n{existing.DisplayFolder}",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        OtherApps.RemoveAll(a => SameApp(a, app));
        CurrentApps.Add(app);
        RefreshLists();
    }

    private void AddPattern()
    {
        string? pattern = PromptPattern("Thêm mẫu đường dẫn", "");
        if (pattern is null)
            return;

        if (CurrentPatterns.Contains(pattern, StringComparer.OrdinalIgnoreCase))
            return;

        OtherPatterns.RemoveAll(p => string.Equals(p, pattern, StringComparison.OrdinalIgnoreCase));
        CurrentPatterns.Add(pattern);
        RefreshLists();
    }

    #endregion

    #region Sửa / xóa / chuyển

    private void EditSelected()
    {
        if (SelectedTags is not [var tag])
        {
            MessageBox.Show(this, "Hãy chọn đúng một dòng để sửa.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (tag is PatternRef patternRef)
        {
            string? edited = PromptPattern("Sửa mẫu đường dẫn", patternRef.Pattern);
            if (edited is null)
                return;

            int index = CurrentPatterns.FindIndex(p => p == patternRef.Pattern);
            if (index >= 0)
                CurrentPatterns[index] = edited;

            // Không để mẫu trùng trong cùng danh sách hay nằm ở cả hai danh sách.
            for (int i = CurrentPatterns.Count - 1; i >= 0; i--)
            {
                if (i != index && string.Equals(CurrentPatterns[i], edited, StringComparison.OrdinalIgnoreCase))
                    CurrentPatterns.RemoveAt(i);
            }
            OtherPatterns.RemoveAll(p => string.Equals(p, edited, StringComparison.OrdinalIgnoreCase));
            RefreshLists();
            return;
        }

        var app = (RememberedApp)tag;
        using var dialog = new FieldsForm("Sửa game đã nhớ",
            [
                new FieldSpec("Tên:", app.Name ?? ""),
                new FieldSpec("Thư mục cài đặt:", app.InstallDir ?? "", FieldBrowse.Folder),
                new FieldSpec("File exe chính:", app.ExePath ?? "", FieldBrowse.Exe)
            ],
            "Game được nhận diện theo thư mục cài đặt; để trống thì dùng thư mục chứa file exe.",
            values =>
            {
                if (values[1].Length == 0 && values[2].Length == 0)
                    return ("Cần thư mục cài đặt hoặc file exe.", 1);
                if (values[1].Length > 0 && !Path.IsPathFullyQualified(values[1]))
                    return ("Thư mục cài đặt phải là đường dẫn tuyệt đối.", 1);
                if (values[2].Length > 0 && !Path.IsPathFullyQualified(values[2]))
                    return ("File exe phải là đường dẫn tuyệt đối.", 2);
                return null;
            });

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string[] v = dialog.Values;
        app.Name = v[0].Length > 0 ? v[0] : null;
        app.InstallDir = v[1].Length > 0 ? PathUtils.NormalizeDirectoryPath(v[1]) : null;
        app.ExePath = v[2].Length > 0 ? PathUtils.NormalizeFilePath(v[2]) : null;

        // Đổi thư mục có thể làm game trùng với mục khác: gộp trong cùng danh sách, bỏ khỏi danh sách kia.
        int merged = CurrentApps.RemoveAll(a => !ReferenceEquals(a, app) && SameApp(a, app));
        int moved = OtherApps.RemoveAll(a => SameApp(a, app));
        if (merged + moved > 0)
        {
            MessageBox.Show(this,
                $"Đã gộp {merged} mục trùng thư mục trong danh sách này và bỏ {moved} mục trùng khỏi danh sách kia.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        RefreshLists();
    }

    private void DeleteSelected()
    {
        List<object> tags = SelectedTags;
        if (tags.Count == 0)
            return;

        string what = tags.Count == 1 ? "dòng đã chọn" : $"{tags.Count} dòng đã chọn";
        string effect = OnIncludeTab
            ? "Game sẽ không còn được tự thêm lại, nhưng vẫn nằm trong danh sách game chính nếu đang có."
            : "Game có thể được thêm lại vào danh sách ở lần quét sau.";
        if (MessageBox.Show(this, $"Quên {what}?\n\n{effect}", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        foreach (object tag in tags)
        {
            if (tag is RememberedApp app)
                CurrentApps.Remove(app);
            else if (tag is PatternRef patternRef)
                CurrentPatterns.Remove(patternRef.Pattern);
        }

        RefreshLists();
    }

    private void MoveSelected()
    {
        List<object> tags = SelectedTags;
        if (tags.Count == 0)
            return;

        List<RememberedApp> from = CurrentApps, to = OtherApps;
        List<string> fromPatterns = CurrentPatterns, toPatterns = OtherPatterns;

        foreach (object tag in tags)
        {
            if (tag is RememberedApp app)
            {
                from.Remove(app);
                to.RemoveAll(a => SameApp(a, app));
                app.RememberedAtUtc = DateTime.UtcNow;
                to.Add(app);
            }
            else if (tag is PatternRef patternRef)
            {
                fromPatterns.Remove(patternRef.Pattern);
                if (!toPatterns.Contains(patternRef.Pattern, StringComparer.OrdinalIgnoreCase))
                    toPatterns.Add(patternRef.Pattern);
            }
        }

        RefreshLists();
    }

    #endregion

    private string? PromptPattern(string title, string initial)
    {
        using var dialog = new FieldsForm(title,
            [new FieldSpec("Mẫu:", initial, FieldBrowse.Folder)],
            "Ví dụ: D:\\Games\\MyGame (thư mục và mọi thứ bên trong), *\\Games\\*, *\\Common Files\\*, regex:\\\\Ubisoft\\\\.*\\.exe$",
            values =>
            {
                if (values[0].Length == 0)
                    return ("Mẫu đang trống.", 0);

                if (values[0].StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        _ = new Regex(values[0][6..]);
                    }
                    catch (ArgumentException ex)
                    {
                        return ($"Biểu thức chính quy không hợp lệ: {ex.Message}", 0);
                    }
                }

                return null;
            });

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Values[0] : null;
    }

    private static bool SameApp(RememberedApp a, RememberedApp b) =>
        AppIdentity.IsSame(a.InstallDir, a.ExePath, b.InstallDir, b.ExePath);
}
