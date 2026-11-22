namespace GameScanner;

/// <summary>
/// Hộp thoại điền các thuộc tính (không bắt buộc) của một app: dùng sau khi chọn file exe để thêm thủ công,
/// và để sửa một app đã có trong danh sách. Mỗi exe khác của game (candidateExes) là một dòng riêng,
/// có nút chọn file, promote lên exe chính và xóa.
/// </summary>
internal sealed class AppEditForm : Form
{
    private const int FieldWidth = 460;

    private readonly LibraryApp _app;
    private readonly TextBox _exeBox = FormLayout.CreateTextBox(FieldWidth);
    private readonly TextBox _nameBox = FormLayout.CreateTextBox(FieldWidth);
    private readonly TextBox _publisherBox = FormLayout.CreateTextBox(FieldWidth);
    private readonly TextBox _installDirBox = FormLayout.CreateTextBox(FieldWidth);
    private readonly List<TextBox> _candidateBoxes = [];

    private const int MaxVisibleCandidateRows = 6;

    private readonly ToolTip _toolTip = new();

    // Không dock trong panel cuộn (control dock không làm hiện thanh cuộn đúng); kích thước được tính tay.
    private readonly TableLayoutPanel _candidateRows = new()
    {
        ColumnCount = 4,
        Location = Point.Empty,
        Margin = Padding.Empty
    };

    /// <summary>Danh sách dòng candidate cuộn được khi game có nhiều exe, để hộp thoại không cao quá màn hình.</summary>
    private readonly Panel _candidateScroll = new()
    {
        AutoScroll = true,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        Margin = new Padding(3, 3, 3, 0),
        MinimumSize = new Size(FieldWidth, 0)
    };

    /// <param name="app">Bản sao của app cần sửa; chỉ được ghi lại khi người dùng bấm OK.</param>
    public AppEditForm(LibraryApp app, bool isNew)
    {
        _app = app;
        FormLayout.ApplyDialogStyle(this, isNew ? "Thêm game thủ công" : "Sửa thông tin game");

        _exeBox.Text = app.ExePath ?? "";
        _nameBox.Text = app.Name ?? "";
        _publisherBox.Text = app.Publisher ?? "";
        _installDirBox.Text = app.InstallDir ?? "";

        _candidateRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (int i = 0; i < 3; i++)
            _candidateRows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _candidateScroll.Controls.Add(_candidateRows);
        _candidateScroll.Resize += (_, _) => UpdateCandidateScrollHeight();

        TableLayoutPanel grid = FormLayout.CreateGrid(3);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        int row = 0;
        grid.Controls.Add(FormLayout.CreateLabel("File exe chính:"), 0, row);
        grid.Controls.Add(_exeBox, 1, row);
        grid.Controls.Add(FormLayout.CreateButton("Chọn…", (_, _) => BrowseMainExe()), 2, row++);

        grid.Controls.Add(FormLayout.CreateLabel("Tên:"), 0, row);
        grid.Controls.Add(_nameBox, 1, row++);

        grid.Controls.Add(FormLayout.CreateLabel("Nhà phát hành:"), 0, row);
        grid.Controls.Add(_publisherBox, 1, row++);

        grid.Controls.Add(FormLayout.CreateLabel("Thư mục cài đặt:"), 0, row);
        grid.Controls.Add(_installDirBox, 1, row);
        grid.Controls.Add(FormLayout.CreateButton("Chọn…", (_, _) => BrowseInstallDir()), 2, row++);

        Label candidatesLabel = FormLayout.CreateLabel("Exe khác của game:");
        candidatesLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        grid.Controls.Add(candidatesLabel, 0, row);
        grid.Controls.Add(_candidateScroll, 1, row);
        grid.SetColumnSpan(_candidateScroll, 2);
        row++;

        Button addCandidate = FormLayout.CreateButton("+ Thêm exe khác", (_, _) => AddCandidateRow("", focus: true));
        addCandidate.Anchor = AnchorStyles.Left;
        grid.Controls.Add(addCandidate, 1, row++);

        grid.Controls.Add(FormLayout.CreateHint(
            "Chỉ cần file exe chính; các trường còn lại không bắt buộc. \"Exe khác của game\" (candidateExes) là những file exe "
            + "cũng được coi là game này đang chạy. ⬆ đưa một exe khác lên làm exe chính, ✕ xóa dòng đó. "
            + "Game được nhận diện theo thư mục cài đặt (không có thì theo thư mục chứa exe chính), "
            + "nên đổi exe chính không làm game bị thêm trùng khi quét lại.", FieldWidth), 1, row++);

        FlowLayoutPanel buttons = FormLayout.CreateDialogButtons(this, (_, _) => Accept());
        grid.Controls.Add(buttons, 0, row);
        grid.SetColumnSpan(buttons, 3);

        Controls.Add(grid);

        foreach (string candidate in app.CandidateExes)
            AddCandidateRow(candidate, focus: false);
        UpdateCandidateScrollHeight();
    }

    /// <summary>App sau khi sửa (cùng đối tượng đã truyền vào), hợp lệ khi DialogResult là OK.</summary>
    public LibraryApp Result => _app;

    #region Dòng candidate

    private void AddCandidateRow(string path, bool focus)
    {
        var box = FormLayout.CreateTextBox(FieldWidth - 120);
        box.Text = path;
        box.Dock = DockStyle.Fill;
        box.Margin = new Padding(0, 3, 3, 3);

        Button browse = SmallButton("…", "Chọn file exe cho dòng này", (_, _) => BrowseCandidate(box));
        Button promote = SmallButton("⬆", "Đưa exe này lên làm exe chính", (_, _) => Promote(box));
        Button remove = SmallButton("✕", "Xóa dòng này", (_, _) => RemoveCandidateRow(box));

        int row = _candidateRows.RowCount++;
        _candidateRows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _candidateRows.Controls.Add(box, 0, row);
        _candidateRows.Controls.Add(browse, 1, row);
        _candidateRows.Controls.Add(promote, 2, row);
        _candidateRows.Controls.Add(remove, 3, row);
        _candidateBoxes.Add(box);

        UpdateCandidateScrollHeight();
        if (focus)
        {
            _candidateScroll.ScrollControlIntoView(box);
            box.Focus();
        }
    }

    private Button SmallButton(string text, string tooltip, EventHandler onClick)
    {
        Button button = FormLayout.CreateButton(text, onClick);
        button.Padding = new Padding(2, 0, 2, 0);
        button.Margin = new Padding(0, 2, 2, 2);
        button.AccessibleName = tooltip;
        _toolTip.SetToolTip(button, tooltip);
        return button;
    }

    /// <summary>Dựng lại toàn bộ dòng theo danh sách đường dẫn (sau khi xóa / promote).</summary>
    private void SetCandidates(IEnumerable<string> paths)
    {
        List<string> list = paths.ToList();
        _candidateRows.SuspendLayout();
        foreach (Control control in _candidateRows.Controls.Cast<Control>().ToList())
            control.Dispose();
        _candidateRows.Controls.Clear();
        _candidateRows.RowStyles.Clear();
        _candidateRows.RowCount = 0;
        _candidateBoxes.Clear();
        _candidateRows.ResumeLayout();

        foreach (string path in list)
            AddCandidateRow(path, focus: false);
        UpdateCandidateScrollHeight();
    }

    private void RemoveCandidateRow(TextBox box)
    {
        if (box.Text.Trim().Length > 0
            && MessageBox.Show(this, $"Xóa exe này khỏi danh sách exe khác của game?\n{box.Text.Trim()}", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        SetCandidates(_candidateBoxes.Where(b => b != box).Select(b => b.Text));
    }

    private void Promote(TextBox box)
    {
        string? promoted = PathUtils.Unquote(box.Text);
        if (promoted is null)
        {
            MessageBox.Show(this, "Dòng này đang trống.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            box.Focus();
            return;
        }

        string? currentMain = PathUtils.Unquote(_exeBox.Text);
        string message = $"Đưa exe này lên làm exe chính?\n{promoted}"
                         + (currentMain is null ? "" : $"\n\nExe chính hiện tại sẽ chuyển xuống danh sách exe khác:\n{currentMain}");

        using var confirm = new CheckConfirmForm(Text, message,
            "Xóa toàn bộ exe khác sau khi promote (kể cả exe chính hiện tại)", isChecked: false);
        if (confirm.ShowDialog(this) != DialogResult.OK)
            return;

        _exeBox.Text = promoted;
        if (confirm.IsChecked)
        {
            SetCandidates([]);
            return;
        }

        // Exe chính cũ thế vào đúng vị trí của dòng vừa được promote.
        SetCandidates(_candidateBoxes.Select(b => b == box ? currentMain ?? "" : b.Text)
            .Where(p => p.Trim().Length > 0));
    }

    /// <summary>
    /// Panel cao vừa đủ số dòng, tối đa <see cref="MaxVisibleCandidateRows"/> dòng rồi cuộn. Tính theo chiều cao thật
    /// của ô nhập để đúng ở mọi mức DPI.
    /// </summary>
    private void UpdateCandidateScrollHeight()
    {
        _candidateScroll.Visible = _candidateBoxes.Count > 0;

        int rowHeight = _exeBox.Height + 6;
        int maxHeight = rowHeight * MaxVisibleCandidateRows;
        int rowsHeight = _candidateRows.GetPreferredSize(new Size(_candidateScroll.ClientSize.Width, 0)).Height;
        bool needsScroll = rowsHeight > maxHeight;

        // Panel không có viền nên Width là bề rộng đầy đủ; ClientSize đã trừ thanh cuộn khi nó đang hiện nên không dùng.
        int scrollBarWidth = SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi);
        int width = Math.Max(0, _candidateScroll.Width - (needsScroll ? scrollBarWidth : 0));
        _candidateRows.Size = new Size(width, rowsHeight);
        _candidateScroll.Height = Math.Min(rowsHeight, maxHeight) + 2;
    }

    #endregion

    #region Chọn file / thư mục

    private void BrowseMainExe()
    {
        string? file = PickExe(_exeBox.Text, "Chọn file exe chính của game");
        if (file is null)
            return;

        _exeBox.Text = file;
        if (string.IsNullOrWhiteSpace(_installDirBox.Text))
            _installDirBox.Text = Path.GetDirectoryName(file) ?? "";
    }

    private void BrowseCandidate(TextBox box)
    {
        string? file = PickExe(box.Text.Length > 0 ? box.Text : _exeBox.Text, "Chọn exe khác của game");
        if (file is not null)
            box.Text = file;
    }

    private string? PickExe(string currentPath, string title)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Chương trình (*.exe)|*.exe",
            CheckFileExists = true
        };
        TrySetInitialDirectory(dialog, currentPath);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private void BrowseInstallDir()
    {
        using var dialog = new FolderBrowserDialog { Description = "Chọn thư mục cài đặt của game", UseDescriptionForTitle = true };
        if (Directory.Exists(_installDirBox.Text.Trim()))
            dialog.SelectedPath = _installDirBox.Text.Trim();

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _installDirBox.Text = dialog.SelectedPath;
    }

    private static void TrySetInitialDirectory(FileDialog dialog, string currentPath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(currentPath.Trim().Trim('"'));
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                dialog.InitialDirectory = dir;
        }
        catch (ArgumentException)
        {
            // Ô đang chứa chuỗi không phải đường dẫn: để hộp thoại tự chọn thư mục.
        }
    }

    #endregion

    private void Accept()
    {
        string? exe = PathUtils.Unquote(_exeBox.Text);
        string? installDir = PathUtils.Unquote(_installDirBox.Text);

        if (exe is null && installDir is null)
        {
            ShowError("Cần có file exe chính hoặc thư mục cài đặt.", _exeBox);
            return;
        }

        if (exe is not null)
        {
            if (!Path.IsPathFullyQualified(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                ShowError("File exe chính phải là đường dẫn tuyệt đối tới một file .exe.", _exeBox);
                return;
            }

            exe = PathUtils.NormalizeFilePath(exe);
            if (!File.Exists(exe) && !Confirm($"File này hiện không tồn tại:\n{exe}\n\nVẫn lưu?"))
                return;
        }

        if (installDir is not null)
        {
            if (!Path.IsPathFullyQualified(installDir))
            {
                ShowError("Thư mục cài đặt phải là đường dẫn tuyệt đối.", _installDirBox);
                return;
            }

            installDir = PathUtils.NormalizeDirectoryPath(installDir);
        }

        var candidates = new List<string>();
        foreach (TextBox box in _candidateBoxes)
        {
            string? path = PathUtils.Unquote(box.Text);
            if (path is null)
                continue; // Dòng trống: bỏ qua.

            if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                _candidateScroll.ScrollControlIntoView(box);
                ShowError($"Exe khác không hợp lệ (cần đường dẫn tuyệt đối tới file .exe):\n{path}", box);
                return;
            }

            candidates.Add(PathUtils.NormalizeFilePath(path));
        }

        _app.ExePath = exe;
        _app.InstallDir = installDir ?? (exe is null ? null : Path.GetDirectoryName(exe));
        _app.Name = PathUtils.Unquote(_nameBox.Text)
            ?? (exe is not null ? Path.GetFileNameWithoutExtension(exe) : Path.GetFileName(installDir));
        _app.Publisher = PathUtils.Unquote(_publisherBox.Text);
        _app.CandidateExes = candidates
            .Where(c => !AppIdentity.SamePath(c, exe))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        DialogResult = DialogResult.OK;
    }

    private void ShowError(string message, Control focus)
    {
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        focus.Focus();
    }

    private bool Confirm(string message) =>
        MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    // Lần tính trong constructor là trước khi form được scale theo DPI: tính lại khi đã hiện và khi đổi màn hình.
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateCandidateScrollHeight();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(UpdateCandidateScrollHeight);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _toolTip.Dispose();

        base.Dispose(disposing);
    }
}
