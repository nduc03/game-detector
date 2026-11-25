namespace GameScanner;

/// <summary>Cửa sổ con để tự định nghĩa một pipeline quét (chương trình ngoài xử lý packet).</summary>
internal sealed class StageEditForm : Form
{
    private const int FieldWidth = 520;
    // Lề trên của nhóm radio "Lấy kết quả từ" và của radio đầu tiên (Margin mặc định của control là 3).
    private const int ResultPanelTopMargin = 3;
    private const int RadioTopMargin = 3;

    private readonly CustomStage _stage;
    private readonly TextBox _nameBox = FormLayout.CreateTextBox(FieldWidth);
    private readonly TextBox _exeBox = FormLayout.CreateTextBox(FieldWidth);
    private readonly Label _exeStatus = new() { AutoSize = true, MaximumSize = new Size(FieldWidth, 0), Margin = new Padding(3, 0, 3, 8) };
    private readonly TextBox _argsBox = FormLayout.CreateTextBox(FieldWidth);
    private readonly CheckBox _pipeBox = new()
    {
        Text = "Truyền dữ liệu đầu vào (packet) qua stdin",
        AutoSize = true,
        Margin = new Padding(3, 3, 3, 8)
    };
    private readonly RadioButton _fromStdout = new() { Text = "stdout của chương trình", AutoSize = true };
    private readonly RadioButton _fromPath = new() { Text = "File tại đường dẫn:", AutoSize = true };
    private readonly RadioButton _fromOutFile = new() { Text = "File chương trình ghi ra $out", AutoSize = true };
    private readonly TextBox _resultPathBox = FormLayout.CreateTextBox(300);
    private readonly Button _resultPathButton;
    // Tìm chương trình trong PATH có thể chậm (thư mục mạng...), nên chỉ tìm khi người dùng ngừng gõ.
    private readonly System.Windows.Forms.Timer _exeCheckTimer = new() { Interval = 350 };
    private readonly NumericUpDown _timeoutBox = new() { Minimum = 0, Maximum = 86400, Width = 90, Anchor = AnchorStyles.Left };

    /// <param name="stage">Bản sao của pipeline cần sửa, hoặc null để tạo mới.</param>
    public StageEditForm(CustomStage? stage)
    {
        _stage = stage ?? new CustomStage();
        FormLayout.ApplyDialogStyle(this, stage is null ? "Thêm pipeline tùy chỉnh" : "Sửa pipeline tùy chỉnh");

        _nameBox.Text = _stage.Name ?? "";
        _exeBox.Text = _stage.ExePath;
        _argsBox.Text = _stage.Arguments;
        _pipeBox.Checked = _stage.PipeStdin;
        _resultPathBox.Text = _stage.ResultPath ?? "";
        _timeoutBox.Value = Math.Clamp(_stage.TimeoutSeconds, 0, 86400);
        _fromStdout.Checked = _stage.ResultSource == StageResultSource.Stdout;
        _fromPath.Checked = _stage.ResultSource == StageResultSource.Path;
        _fromOutFile.Checked = _stage.ResultSource == StageResultSource.OutFile;

        _resultPathButton = FormLayout.CreateButton("Chọn…", (_, _) => BrowseResultPath());
        _exeBox.TextChanged += (_, _) =>
        {
            _exeCheckTimer.Stop();
            _exeCheckTimer.Start();
        };
        _exeCheckTimer.Tick += (_, _) =>
        {
            _exeCheckTimer.Stop();
            UpdateExeStatus();
        };
        _fromPath.CheckedChanged += (_, _) => UpdateResultPathEnabled();

        TableLayoutPanel grid = FormLayout.CreateGrid(3);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        int row = 0;
        grid.Controls.Add(FormLayout.CreateLabel("Tên:"), 0, row);
        grid.Controls.Add(_nameBox, 1, row++);

        grid.Controls.Add(FormLayout.CreateLabel("Chương trình (exe):"), 0, row);
        grid.Controls.Add(_exeBox, 1, row);
        grid.Controls.Add(FormLayout.CreateButton("Chọn…", (_, _) => BrowseExe()), 2, row++);
        grid.Controls.Add(_exeStatus, 1, row++);

        grid.Controls.Add(FormLayout.CreateLabel("Tham số (args):"), 0, row);
        grid.Controls.Add(_argsBox, 1, row++);

        grid.Controls.Add(FormLayout.CreateHint(
            "Biến hỗ trợ:\n"
            + "$in — đường dẫn file JSON chứa pipeline packet đầu vào (trước khi xử lý).\n"
            + "$out — đường dẫn file để chương trình ghi pipeline packet đã xử lý.\n"
            + "Biến tự động bọc nháy kép. Các đường dẫn khác nên dùng đường dẫn tuyệt đối.",
            FieldWidth), 1, row++);

        grid.Controls.Add(_pipeBox, 1, row++);

        // Nhãn neo lên đầu ô (ngang dòng radio đầu tiên) thay vì canh giữa theo chiều cao cả nhóm radio.
        Label resultLabel = FormLayout.CreateLabel("Lấy kết quả từ:");
        resultLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        resultLabel.Margin = resultLabel.Margin with { Top = ResultPanelTopMargin + RadioTopMargin + 2 };
        grid.Controls.Add(resultLabel, 0, row);
        grid.Controls.Add(CreateResultPanel(), 1, row++);
        grid.Controls.Add(FormLayout.CreateHint(
            "Kết quả phải là JSON dạng { \"included\": [...], \"excluded\": [...] }. Chương trình thoát với mã khác 0, "
            + "quá thời gian hoặc trả kết quả sai cấu trúc sẽ làm hủy cả lần quét.",
            FieldWidth), 1, row++);

        grid.Controls.Add(FormLayout.CreateLabel("Giới hạn thời gian (giây):"), 0, row);
        grid.Controls.Add(_timeoutBox, 1, row++);
        grid.Controls.Add(FormLayout.CreateHint("0 = không giới hạn.", FieldWidth), 1, row++);

        var docsLink = new LinkLabel
        {
            Text = "Xem tài liệu viết pipeline tùy chỉnh (định dạng packet, ví dụ script)",
            AutoSize = true,
            Margin = new Padding(3, 0, 3, 8)
        };
        docsLink.LinkClicked += (_, _) => ShellHelper.OpenPipelineDocs(this);
        grid.Controls.Add(docsLink, 1, row++);

        FlowLayoutPanel buttons = FormLayout.CreateDialogButtons(this, (_, _) => Accept());
        grid.Controls.Add(buttons, 0, row);
        grid.SetColumnSpan(buttons, 3);

        Controls.Add(grid);
        UpdateExeStatus();
        UpdateResultPathEnabled();
    }

    /// <summary>Pipeline sau khi sửa, hợp lệ khi DialogResult là OK.</summary>
    public CustomStage Result => _stage;

    /// <summary>Ba radio phải cùng nằm trong một container thì mới tự loại trừ nhau.</summary>
    private TableLayoutPanel CreateResultPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, ResultPanelTopMargin, 0, 8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        panel.Controls.Add(_fromStdout, 0, 0);
        panel.SetColumnSpan(_fromStdout, 3);

        _fromPath.Anchor = AnchorStyles.Left;
        panel.Controls.Add(_fromPath, 0, 1);
        panel.Controls.Add(_resultPathBox, 1, 1);
        panel.Controls.Add(_resultPathButton, 2, 1);

        panel.Controls.Add(_fromOutFile, 0, 2);
        panel.SetColumnSpan(_fromOutFile, 3);
        return panel;
    }

    private StageResultSource SelectedSource =>
        _fromPath.Checked ? StageResultSource.Path
        : _fromOutFile.Checked ? StageResultSource.OutFile
        : StageResultSource.Stdout;

    private void UpdateExeStatus()
    {
        if (string.IsNullOrWhiteSpace(_exeBox.Text))
        {
            _exeStatus.Text = "Nhập đường dẫn tuyệt đối tới chương trình, hoặc tên lệnh có trong PATH (python, node, pwsh...).";
            _exeStatus.ForeColor = SystemColors.GrayText;
            return;
        }

        string? resolved = ExecutableLocator.Resolve(_exeBox.Text);
        _exeStatus.Text = resolved is not null ? $"Hợp lệ: {resolved}" : "Không tìm thấy chương trình này.";
        _exeStatus.ForeColor = resolved is not null ? Color.FromArgb(0, 110, 50) : Color.FromArgb(190, 30, 30);
    }

    private void UpdateResultPathEnabled()
    {
        _resultPathBox.Enabled = _fromPath.Checked;
        _resultPathButton.Enabled = _fromPath.Checked;
    }

    private void BrowseExe()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Chọn chương trình xử lý packet",
            Filter = "Chương trình (*.exe;*.com;*.bat;*.cmd)|*.exe;*.com;*.bat;*.cmd|Tất cả file (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _exeBox.Text = dialog.FileName;
    }

    private void BrowseResultPath()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "File mà chương trình sẽ ghi kết quả",
            Filter = "JSON (*.json)|*.json|Tất cả file (*.*)|*.*",
            OverwritePrompt = false,
            CheckPathExists = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _resultPathBox.Text = dialog.FileName;
    }

    private void Accept()
    {
        string exe = _exeBox.Text.Trim();
        string args = _argsBox.Text.Trim();
        StageResultSource source = SelectedSource;

        if (exe.Length == 0)
        {
            ShowError("Chưa chọn chương trình.", _exeBox);
            return;
        }

        if (ExecutableLocator.Resolve(exe) is null)
        {
            ShowError("Không tìm thấy chương trình. Cần đường dẫn tuyệt đối tới file .exe/.com/.bat/.cmd đang tồn tại, "
                      + "hoặc tên lệnh có trong PATH.", _exeBox);
            return;
        }

        if (source == StageResultSource.OutFile && !StageArguments.Contains(args, StageArguments.Out))
        {
            ShowError("Đã chọn lấy kết quả từ file $out nhưng tham số chưa có $out.", _argsBox);
            return;
        }

        string? resultPath = null;
        if (source == StageResultSource.Path)
        {
            resultPath = PathUtils.Unquote(_resultPathBox.Text);
            if (resultPath is null || CustomStageRunner.TryResolveResultPath(resultPath) is null)
            {
                ShowError("Cần đường dẫn tuyệt đối tới file kết quả.", _resultPathBox);
                return;
            }
        }

        if (!_pipeBox.Checked && !StageArguments.Contains(args, StageArguments.In)
            && !Confirm("Chương trình sẽ không nhận được packet đầu vào (không truyền qua stdin, tham số cũng không có $in).\n\nVẫn lưu?"))
        {
            return;
        }

        _stage.Name = PathUtils.Unquote(_nameBox.Text);
        _stage.ExePath = exe;
        _stage.Arguments = args;
        _stage.PipeStdin = _pipeBox.Checked;
        _stage.ResultSource = source;
        _stage.ResultPath = resultPath;
        _stage.TimeoutSeconds = (int)_timeoutBox.Value;

        DialogResult = DialogResult.OK;
    }

    private void ShowError(string message, Control focus)
    {
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        focus.Focus();
    }

    private bool Confirm(string message) =>
        MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _exeCheckTimer.Dispose();

        base.Dispose(disposing);
    }
}
