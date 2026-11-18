namespace GameScanner;

/// <summary>
/// Hộp thoại điền các thuộc tính (không bắt buộc) của một app: dùng sau khi chọn file exe để thêm thủ công,
/// và để sửa một app đã có trong danh sách.
/// </summary>
internal sealed class AppEditForm : Form
{
    private readonly LibraryApp _app;
    private readonly TextBox _exeBox = FormLayout.CreateTextBox(460);
    private readonly TextBox _nameBox = FormLayout.CreateTextBox(460);
    private readonly TextBox _publisherBox = FormLayout.CreateTextBox(460);
    private readonly TextBox _installDirBox = FormLayout.CreateTextBox(460);
    private readonly TextBox _candidatesBox = new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        AcceptsReturn = true,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        MinimumSize = new Size(460, 96),
        Height = 96
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
        _candidatesBox.Text = string.Join(Environment.NewLine, app.CandidateExes);

        TableLayoutPanel grid = FormLayout.CreateGrid(3);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        int row = 0;
        grid.Controls.Add(FormLayout.CreateLabel("File exe:"), 0, row);
        grid.Controls.Add(_exeBox, 1, row);
        grid.Controls.Add(FormLayout.CreateButton("Chọn…", (_, _) => BrowseExe()), 2, row++);

        grid.Controls.Add(FormLayout.CreateLabel("Tên:"), 0, row);
        grid.Controls.Add(_nameBox, 1, row++);

        grid.Controls.Add(FormLayout.CreateLabel("Nhà phát hành:"), 0, row);
        grid.Controls.Add(_publisherBox, 1, row++);

        grid.Controls.Add(FormLayout.CreateLabel("Thư mục cài đặt:"), 0, row);
        grid.Controls.Add(_installDirBox, 1, row);
        grid.Controls.Add(FormLayout.CreateButton("Chọn…", (_, _) => BrowseInstallDir()), 2, row++);

        grid.Controls.Add(FormLayout.CreateLabel("Exe khác của game:"), 0, row);
        grid.Controls.Add(_candidatesBox, 1, row);
        grid.Controls.Add(FormLayout.CreateButton("Thêm…", (_, _) => AddCandidates()), 2, row++);

        Label hint = FormLayout.CreateHint(
            "Chỉ cần file exe; các trường còn lại không bắt buộc. \"Exe khác của game\" (candidateExes) là những file exe "
            + "cũng được coi là game này đang chạy, mỗi dòng một đường dẫn.", 460);
        grid.Controls.Add(hint, 1, row++);

        FlowLayoutPanel buttons = FormLayout.CreateDialogButtons(this, (_, _) => Accept());
        grid.Controls.Add(buttons, 0, row);
        grid.SetColumnSpan(buttons, 3);

        Controls.Add(grid);
    }

    /// <summary>App sau khi sửa (cùng đối tượng đã truyền vào), hợp lệ khi DialogResult là OK.</summary>
    public LibraryApp Result => _app;

    private void BrowseExe()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Chọn file exe của game",
            Filter = "Chương trình (*.exe)|*.exe",
            CheckFileExists = true
        };
        TrySetInitialDirectory(dialog, _exeBox.Text);

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _exeBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(_installDirBox.Text))
            _installDirBox.Text = Path.GetDirectoryName(dialog.FileName) ?? "";
    }

    private void BrowseInstallDir()
    {
        using var dialog = new FolderBrowserDialog { Description = "Chọn thư mục cài đặt của game", UseDescriptionForTitle = true };
        if (Directory.Exists(_installDirBox.Text.Trim()))
            dialog.SelectedPath = _installDirBox.Text.Trim();

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _installDirBox.Text = dialog.SelectedPath;
    }

    private void AddCandidates()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Chọn thêm file exe của game",
            Filter = "Chương trình (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = true
        };
        TrySetInitialDirectory(dialog, _exeBox.Text);

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        List<string> lines = ReadCandidates();
        lines.AddRange(dialog.FileNames);
        _candidatesBox.Text = string.Join(Environment.NewLine, lines.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void Accept()
    {
        string? exe = PathUtils.Unquote(_exeBox.Text);
        string? installDir = PathUtils.Unquote(_installDirBox.Text);

        if (exe is null && installDir is null)
        {
            ShowError("Cần có file exe hoặc thư mục cài đặt.", _exeBox);
            return;
        }

        if (exe is not null)
        {
            if (!Path.IsPathFullyQualified(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                ShowError("File exe phải là đường dẫn tuyệt đối tới một file .exe.", _exeBox);
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

        List<string> candidates = ReadCandidates();
        string? badCandidate = candidates.FirstOrDefault(c => !Path.IsPathFullyQualified(c));
        if (badCandidate is not null)
        {
            ShowError($"Đường dẫn exe khác không hợp lệ (cần đường dẫn tuyệt đối):\n{badCandidate}", _candidatesBox);
            return;
        }

        _app.ExePath = exe;
        _app.InstallDir = installDir ?? (exe is null ? null : Path.GetDirectoryName(exe));
        _app.Name = PathUtils.Unquote(_nameBox.Text)
            ?? (exe is not null ? Path.GetFileNameWithoutExtension(exe) : Path.GetFileName(installDir));
        _app.Publisher = PathUtils.Unquote(_publisherBox.Text);
        _app.CandidateExes = candidates
            .Select(PathUtils.NormalizeFilePath)
            .Where(c => !string.Equals(c, exe, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        DialogResult = DialogResult.OK;
    }

    private List<string> ReadCandidates() =>
        _candidatesBox.Lines
            .Select(PathUtils.Unquote)
            .Where(line => line is not null)
            .Select(line => line!)
            .ToList();

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

    private void ShowError(string message, Control focus)
    {
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        focus.Focus();
    }

    private bool Confirm(string message) =>
        MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
}
