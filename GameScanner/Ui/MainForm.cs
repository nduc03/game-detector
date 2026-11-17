using System.Text;
using System.Text.Json;

namespace GameScanner;

/// <summary>
/// Cửa sổ chính: bên trái là các bước pipeline, bên phải là danh sách app đã lưu và nhật ký chạy.
/// </summary>
internal sealed class MainForm : Form
{
    private const string AppTitle = "GameScanner";
    private const int MaxLogLength = 400_000;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly Color NewAppColor = Color.FromArgb(226, 244, 229);

    private readonly AppLibrary _library;
    private readonly ScannerSettings _settings;
    private readonly IconCache _icons = new();
    private readonly HashSet<string> _newAppIds = [];

    private List<PipelineStep> _steps = [];
    private PipelinePacket? _lastPacket;
    private CancellationTokenSource? _cts;
    private bool _refreshingSteps;

    private readonly ListView _stepList = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        CheckBoxes = true,
        FullRowSelect = true,
        HideSelection = false,
        MultiSelect = false,
        HeaderStyle = ColumnHeaderStyle.None,
        ShowItemToolTips = true
    };

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        MultiSelect = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.None
    };

    private readonly TextBox _logBox = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        WordWrap = false,
        ScrollBars = ScrollBars.Both,
        BackColor = SystemColors.Window,
        Font = new Font(FontFamily.GenericMonospace, 9F)
    };

    private readonly SplitContainer _mainSplit = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
    private readonly SplitContainer _rightSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _countLabel = new();
    private readonly ToolStripProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly ToolStripTextBox _searchBox = new() { AutoSize = false, Width = 180 };

    private readonly DataGridViewImageColumn _iconColumn = new()
    {
        HeaderText = "",
        ImageLayout = DataGridViewImageCellLayout.Zoom,
        AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
        Resizable = DataGridViewTriState.False,
        SortMode = DataGridViewColumnSortMode.NotSortable
    };

    private readonly DataGridViewButtonColumn _folderColumn = new()
    {
        HeaderText = "Thư mục",
        Text = "Mở thư mục",
        UseColumnTextForButtonValue = true,
        AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
        SortMode = DataGridViewColumnSortMode.NotSortable
    };

    // Những điều khiển phải khóa trong lúc pipeline đang chạy.
    private readonly List<ToolStripItem> _idleOnlyItems = [];
    private readonly List<Control> _idleOnlyControls = [];
    private readonly List<ToolStripItem> _stopItems = [];
    private ToolStripMenuItem _exportCandidatesItem = null!;
    private Button _editStepButton = null!;
    private Button _deleteStepButton = null!;
    private Button _moveUpButton = null!;
    private Button _moveDownButton = null!;

    public MainForm()
    {
        Text = AppTitle;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1200, 720);
        MinimumSize = new Size(860, 500);
        StartPosition = FormStartPosition.CenterScreen;
        if (ShellHelper.LoadAppIcon() is { } icon)
            Icon = icon;

        _library = AppLibrary.Load(AppPaths.LibraryFile, out string? libraryWarning);
        _settings = SettingsStore.Load(out string? settingsWarning);

        BuildUi();
        Log.SetSink(AppendLog);

        RefreshSteps(selectIndex: 0);

        Load += (_, _) =>
        {
            // Kích thước theo DPI chỉ chính xác sau khi cửa sổ đã có handle và đã được scale.
            ApplyInitialSplit();
            _iconColumn.Width = LogicalToDeviceUnits(40);
            _folderColumn.Width = LogicalToDeviceUnits(104);
            _grid.RowTemplate.Height = LogicalToDeviceUnits(34);
            RefreshGrid();

            Log.Info($"[data] Dữ liệu lưu tại: {AppPaths.DataDir}");
            if (libraryWarning is not null)
                Log.Info(libraryWarning);
            if (settingsWarning is not null)
                Log.Info(settingsWarning);
        };
    }

    #region Dựng giao diện

    private void BuildUi()
    {
        SuspendLayout();

        BuildGrid();
        _rightSplit.Panel1.Controls.Add(_grid);
        _rightSplit.Panel2.Controls.Add(_logBox);

        _mainSplit.Panel1.Controls.Add(BuildPipelinePanel());
        _mainSplit.Panel2.Controls.Add(_rightSplit);

        ToolStrip toolStrip = BuildToolStrip();
        MenuStrip menu = BuildMenu();
        var status = new StatusStrip();
        status.Items.AddRange([_statusLabel, _progress, _countLabel]);

        // Thứ tự Add quyết định thứ tự dock: phần Fill thêm trước, menu thêm sau cùng để nằm trên cùng.
        Controls.Add(_mainSplit);
        Controls.Add(toolStrip);
        Controls.Add(menu);
        Controls.Add(status);
        MainMenuStrip = menu;

        ResumeLayout(performLayout: true);
        SetRunning(false);
    }

    private void ApplyInitialSplit()
    {
        try
        {
            _mainSplit.SplitterDistance = LogicalToDeviceUnits(320);
            _rightSplit.SplitterDistance = Math.Max(_rightSplit.Panel1MinSize, _rightSplit.Height - LogicalToDeviceUnits(190));
        }
        catch (InvalidOperationException)
        {
            // Cửa sổ quá nhỏ để đặt vị trí thanh chia: giữ mặc định.
        }
        catch (ArgumentOutOfRangeException)
        {
            // Như trên.
        }
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();

        var file = new ToolStripMenuItem("&Tệp");
        file.DropDownItems.Add(IdleOnly(MenuItem("Thêm game thủ công…", AddManually, Keys.Control | Keys.N)));
        file.DropDownItems.Add(IdleOnly(MenuItem("Sửa game đã chọn…", EditSelectedApp, Keys.F2)));
        // Không gán phím tắt Delete cho menu (sẽ nuốt phím Delete của ô tìm kiếm); phím này được bắt trên bảng.
        ToolStripMenuItem deleteItem = IdleOnly(MenuItem("Xóa game đã chọn", DeleteSelectedApps));
        deleteItem.ShortcutKeyDisplayString = "Del";
        file.DropDownItems.Add(deleteItem);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MenuItem("Xuất JSON cho GameProcessChecker…", ExportForChecker, Keys.Control | Keys.E));
        _exportCandidatesItem = new ToolStripMenuItem("Xuất kèm exe khác của game (candidateExes)")
        {
            CheckOnClick = true,
            Checked = _settings.ExportCandidateExes
        };
        _exportCandidatesItem.CheckedChanged += (_, _) =>
        {
            _settings.ExportCandidateExes = _exportCandidatesItem.Checked;
            SaveSettings();
        };
        file.DropDownItems.Add(_exportCandidatesItem);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MenuItem("Mở thư mục dữ liệu", OpenDataDirectory));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MenuItem("Thoát", Close));

        var pipeline = new ToolStripMenuItem("&Pipeline");
        pipeline.DropDownItems.Add(IdleOnly(MenuItem("Chạy toàn bộ pipeline", RunAll, Keys.F5)));
        pipeline.DropDownItems.Add(IdleOnly(MenuItem("Chạy riêng bước đã chọn", RunSelectedStep, Keys.F6)));
        pipeline.DropDownItems.Add(StopItem(MenuItem("Dừng", StopPipeline, Keys.Shift | Keys.F5)));
        pipeline.DropDownItems.Add(new ToolStripSeparator());
        pipeline.DropDownItems.Add(IdleOnly(MenuItem("Thêm pipeline tùy chỉnh…", AddCustomStage)));
        pipeline.DropDownItems.Add(IdleOnly(MenuItem("Sửa bước đã chọn…", EditSelectedStep)));
        pipeline.DropDownItems.Add(IdleOnly(MenuItem("Xóa bước đã chọn", DeleteSelectedStep)));
        pipeline.DropDownItems.Add(new ToolStripSeparator());
        pipeline.DropDownItems.Add(IdleOnly(MenuItem("Chọn file rules…", ChooseRulesFile)));
        pipeline.DropDownItems.Add(IdleOnly(MenuItem("Bỏ chọn file rules (tự tìm rules.json)", ClearRulesFile)));

        menu.Items.AddRange([file, pipeline]);
        return menu;
    }

    private ToolStrip BuildToolStrip()
    {
        var strip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };

        strip.Items.Add(IdleOnly(ToolButton("▶ Chạy toàn bộ pipeline", RunAll)));
        strip.Items.Add(IdleOnly(ToolButton("Chạy riêng bước đã chọn", RunSelectedStep)));
        strip.Items.Add(StopItem(ToolButton("■ Dừng", StopPipeline)));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(IdleOnly(ToolButton("Thêm thủ công…", AddManually)));
        strip.Items.Add(IdleOnly(ToolButton("Sửa…", EditSelectedApp)));
        strip.Items.Add(IdleOnly(ToolButton("Xóa", DeleteSelectedApps)));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(ToolButton("Xuất JSON cho GameProcessChecker…", ExportForChecker));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(new ToolStripLabel("Tìm:"));
        strip.Items.Add(_searchBox);

        _searchBox.TextChanged += (_, _) => RefreshGrid();
        return strip;
    }

    private Control BuildPipelinePanel()
    {
        _stepList.Columns.Add("Bước", 280);
        _stepList.Resize += (_, _) => FitStepColumn();
        _stepList.ItemChecked += OnStepChecked;
        _stepList.SelectedIndexChanged += (_, _) => UpdateStepButtons();
        _stepList.DoubleClick += (_, _) => EditSelectedStep();

        var stepButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        _editStepButton = FormLayout.CreateButton("Sửa…", (_, _) => EditSelectedStep());
        _deleteStepButton = FormLayout.CreateButton("Xóa", (_, _) => DeleteSelectedStep());
        _moveUpButton = FormLayout.CreateButton("▲", (_, _) => MoveSelectedStep(-1));
        _moveDownButton = FormLayout.CreateButton("▼", (_, _) => MoveSelectedStep(+1));
        stepButtons.Controls.AddRange(
        [
            FormLayout.CreateButton("Thêm…", (_, _) => AddCustomStage()),
            _editStepButton, _deleteStepButton, _moveUpButton, _moveDownButton
        ]);

        var runButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        runButtons.Controls.AddRange(
        [
            FormLayout.CreateButton("Chạy toàn bộ", (_, _) => RunAll()),
            FormLayout.CreateButton("Chạy riêng bước đã chọn", (_, _) => RunSelectedStep())
        ]);

        Label hint = FormLayout.CreateHint(
            "Chạy từ trên xuống; bỏ tick để bỏ qua một bước khi chạy toàn bộ. Một bước lỗi thì cả lần chạy bị hủy, "
            + "danh sách app giữ nguyên. Chạy xong, app mới trong \"included\" được thêm vào danh sách; app cũ không bị xóa.",
            300);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        // Chữ ghi chú tự xuống dòng theo bề rộng thật của khung pipeline.
        layout.Resize += (_, _) => hint.MaximumSize = new Size(Math.Max(120, layout.ClientSize.Width - 8), 0);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(_stepList, 0, 0);
        layout.Controls.Add(stepButtons, 0, 1);
        layout.Controls.Add(runButtons, 0, 2);
        layout.Controls.Add(hint, 0, 3);

        _idleOnlyControls.Add(_stepList);
        _idleOnlyControls.Add(stepButtons);
        _idleOnlyControls.Add(runButtons);

        var group = new GroupBox { Text = "Pipeline quét", Dock = DockStyle.Fill, Padding = new Padding(8) };
        group.Controls.Add(layout);
        return group;
    }

    private void BuildGrid()
    {
        _iconColumn.DefaultCellStyle.NullValue = null;

        _grid.Columns.Add(_iconColumn);
        _grid.Columns.Add(TextColumn("Tên", 28));
        _grid.Columns.Add(TextColumn("Nhà phát hành", 16));
        _grid.Columns.Add(TextColumn("File exe", 46));
        _grid.Columns.Add(TextColumn("Nguồn", 10));
        _grid.Columns.Add(_folderColumn);

        _grid.CellFormatting += OnGridCellFormatting;
        _grid.CellContentClick += OnGridCellContentClick;
        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
            {
                e.Handled = true;
                DeleteSelectedApps();
            }
        };
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex != _folderColumn.Index)
                EditSelectedApp();
        };
    }

    private static DataGridViewTextBoxColumn TextColumn(string header, float fillWeight) => new()
    {
        HeaderText = header,
        FillWeight = fillWeight,
        SortMode = DataGridViewColumnSortMode.Automatic
    };

    private static ToolStripMenuItem MenuItem(string text, Action onClick, Keys shortcut = Keys.None)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeys = shortcut };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static ToolStripButton ToolButton(string text, Action onClick)
    {
        var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        button.Click += (_, _) => onClick();
        return button;
    }

    private T IdleOnly<T>(T item) where T : ToolStripItem
    {
        _idleOnlyItems.Add(item);
        return item;
    }

    private T StopItem<T>(T item) where T : ToolStripItem
    {
        _stopItems.Add(item);
        return item;
    }

    #endregion

    #region Danh sách bước pipeline

    private PipelineStep? SelectedStep =>
        _stepList.SelectedIndices.Count == 1 ? _steps[_stepList.SelectedIndices[0]] : null;

    private void RefreshSteps(int? selectIndex = null)
    {
        _refreshingSteps = true;
        try
        {
            _steps = PipelineStep.BuildAll(_settings);

            _stepList.BeginUpdate();
            _stepList.Items.Clear();
            for (int i = 0; i < _steps.Count; i++)
            {
                PipelineStep step = _steps[i];
                var item = new ListViewItem($"{i + 1}. {step.DisplayName}")
                {
                    Checked = step.Enabled,
                    ToolTipText = DescribeStep(step)
                };
                _stepList.Items.Add(item);
            }
            _stepList.EndUpdate();
        }
        finally
        {
            _refreshingSteps = false;
        }

        if (selectIndex is { } index && index >= 0 && index < _stepList.Items.Count)
        {
            _stepList.Items[index].Selected = true;
            _stepList.Items[index].EnsureVisible();
        }

        FitStepColumn();
        UpdateStepButtons();
    }

    private string DescribeStep(PipelineStep step) => step.Kind switch
    {
        StepKind.Scan => "Có sẵn: quét Registry và các launcher rồi lọc bằng heuristic. Là nguồn dữ liệu, không dùng packet đầu vào.",
        StepKind.Rules => string.IsNullOrWhiteSpace(_settings.RulesPath)
            ? "Có sẵn: lọc theo rules.json cạnh GameScanner.exe hoặc ở thư mục hiện tại; không có file thì bỏ qua."
            : $"Có sẵn: lọc theo file rules {_settings.RulesPath}",
        _ => $"{step.Custom!.ExePath} {step.Custom.Arguments}".Trim()
    };

    private void FitStepColumn()
    {
        if (_stepList.Columns.Count > 0 && _stepList.ClientSize.Width > 8)
            _stepList.Columns[0].Width = _stepList.ClientSize.Width - 4;
    }

    private void UpdateStepButtons()
    {
        PipelineStep? step = SelectedStep;
        int customIndex = step?.Custom is null ? -1 : _settings.CustomStages.FindIndex(s => s.Id == step.Custom.Id);

        _editStepButton.Enabled = step is not null && step.Kind != StepKind.Scan;
        _deleteStepButton.Enabled = customIndex >= 0;
        _moveUpButton.Enabled = customIndex > 0;
        _moveDownButton.Enabled = customIndex >= 0 && customIndex < _settings.CustomStages.Count - 1;
    }

    private void OnStepChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_refreshingSteps)
            return;

        PipelineStep step = _steps[e.Item.Index];
        bool enabled = e.Item.Checked;
        if (step.Enabled == enabled)
            return; // ListView cũng bắn sự kiện này lúc mới tạo, khi chưa có gì thay đổi.

        switch (step.Kind)
        {
            case StepKind.Scan:
                _settings.ScanStageEnabled = enabled;
                break;
            case StepKind.Rules:
                _settings.RulesStageEnabled = enabled;
                break;
            default:
                CustomStage? stage = _settings.CustomStages.Find(s => s.Id == step.Custom!.Id);
                if (stage is not null)
                    stage.Enabled = enabled;
                break;
        }

        _steps[e.Item.Index] = step with { Enabled = enabled };
        SaveSettings();
    }

    private void AddCustomStage()
    {
        if (IsRunning)
            return;

        using var dialog = new StageEditForm(null);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _settings.CustomStages.Add(dialog.Result);
        SaveSettings();
        RefreshSteps(selectIndex: _steps.Count);
    }

    private void EditSelectedStep()
    {
        if (IsRunning || SelectedStep is not { } step)
            return;

        switch (step.Kind)
        {
            case StepKind.Scan:
                return;

            case StepKind.Rules:
                ChooseRulesFile();
                return;
        }

        int index = _settings.CustomStages.FindIndex(s => s.Id == step.Custom!.Id);
        if (index < 0)
            return;

        using var dialog = new StageEditForm(_settings.CustomStages[index].Clone());
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _settings.CustomStages[index] = dialog.Result;
        SaveSettings();
        RefreshSteps(selectIndex: _stepList.SelectedIndices.Count == 1 ? _stepList.SelectedIndices[0] : null);
    }

    private void DeleteSelectedStep()
    {
        if (IsRunning || SelectedStep?.Custom is not { } custom)
            return;

        if (!Confirm($"Xóa pipeline tùy chỉnh \"{custom.DisplayName}\"?"))
            return;

        _settings.CustomStages.RemoveAll(s => s.Id == custom.Id);
        SaveSettings();
        RefreshSteps(selectIndex: 0);
    }

    private void MoveSelectedStep(int offset)
    {
        if (IsRunning || SelectedStep?.Custom is not { } custom)
            return;

        List<CustomStage> stages = _settings.CustomStages;
        int index = stages.FindIndex(s => s.Id == custom.Id);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= stages.Count)
            return;

        (stages[index], stages[target]) = (stages[target], stages[index]);
        SaveSettings();
        RefreshSteps(selectIndex: _stepList.SelectedIndices[0] + offset);
    }

    private void ChooseRulesFile()
    {
        if (IsRunning)
            return;

        using var dialog = new OpenFileDialog
        {
            Title = "Chọn file rules",
            Filter = "JSON (*.json)|*.json|Tất cả file (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _settings.RulesPath = dialog.FileName;
        SaveSettings();
        RefreshSteps(selectIndex: 1);
    }

    private void ClearRulesFile()
    {
        if (IsRunning)
            return;

        _settings.RulesPath = null;
        SaveSettings();
        RefreshSteps(selectIndex: 1);
    }

    #endregion

    #region Chạy pipeline

    private bool IsRunning => _cts is not null;

    private void RunAll()
    {
        List<PipelineStep> steps = _steps.Where(s => s.Enabled).ToList();
        if (steps.Count == 0)
        {
            ShowWarning("Chưa có bước nào được tick để chạy.");
            return;
        }

        _ = RunPipelineAsync(steps);
    }

    private void RunSelectedStep()
    {
        if (SelectedStep is not { } step)
        {
            ShowWarning("Hãy chọn một bước trong danh sách pipeline.");
            return;
        }

        _ = RunPipelineAsync([step]);
    }

    private void StopPipeline()
    {
        if (_cts is { IsCancellationRequested: false } cts)
        {
            Log.Info("[pipeline] Đang dừng...");
            cts.Cancel();
        }
    }

    private async Task RunPipelineAsync(List<PipelineStep> steps)
    {
        if (IsRunning)
            return;

        var cts = new CancellationTokenSource();
        _cts = cts;
        SetRunning(true);

        try
        {
            PipelinePacket input = BuildInputPacket(steps[0]);
            string? rulesPath = _settings.RulesPath;

            PipelinePacket result = await Task.Run(
                () => PipelineRunner.RunAsync(steps, input, rulesPath, cts.Token), cts.Token);

            // Chỉ tới đây khi mọi bước đều thành công: trạng thái cuối cùng quyết định app nào được thêm.
            _lastPacket = result;
            SaveLastPacket(result);

            List<LibraryApp> added = _library.MergeNew(result.Included);
            _newAppIds.Clear();
            foreach (LibraryApp app in added)
                _newAppIds.Add(app.Id);

            if (added.Count > 0)
                _library.Save();

            RefreshGrid();
            string summary = $"Pipeline xong: {result.Included.Count} included, {result.Excluded?.Count ?? 0} excluded; "
                             + $"thêm {added.Count} app mới (danh sách hiện có {_library.Apps.Count} app).";
            Log.Info("[pipeline] " + summary);
            _statusLabel.Text = summary;
        }
        catch (OperationCanceledException)
        {
            Log.Info("[pipeline] Đã dừng theo yêu cầu. Toàn bộ kết quả của lần chạy này bị hủy, danh sách app giữ nguyên.");
            _statusLabel.Text = "Đã dừng pipeline, không có gì thay đổi.";
        }
        catch (PipelineException ex)
        {
            Log.Info($"[pipeline] LỖI ở bước \"{ex.StepName}\": {ex.Message}");
            Log.Info("[pipeline] Đã ngắt pipeline và hủy toàn bộ kết quả từ bước đầu tiên; danh sách app giữ nguyên.");
            _statusLabel.Text = $"Pipeline lỗi ở bước \"{ex.StepName}\", không có gì thay đổi.";
            ShowError($"Pipeline lỗi ở bước \"{ex.StepName}\":\n\n{ex.Message}\n\n"
                      + "Toàn bộ kết quả của lần chạy này đã bị hủy, danh sách app giữ nguyên.");
        }
        catch (Exception ex)
        {
            Log.Info($"[pipeline] LỖI: {ex.Message}");
            _statusLabel.Text = "Pipeline lỗi, không có gì thay đổi.";
            ShowError($"Pipeline lỗi:\n\n{ex.Message}\n\nToàn bộ kết quả của lần chạy này đã bị hủy.");
        }
        finally
        {
            _cts = null;
            cts.Dispose();
            if (!IsDisposed)
                SetRunning(false);
        }
    }

    /// <summary>
    /// Bước "Quét hệ thống" tự sinh dữ liệu. Các bước khác cần packet đầu vào: lấy kết quả của lần chạy
    /// pipeline thành công gần nhất (có cả included lẫn excluded); chưa có thì dùng danh sách app hiện tại.
    /// </summary>
    private PipelinePacket BuildInputPacket(PipelineStep firstStep)
    {
        if (firstStep.Kind == StepKind.Scan)
            return new PipelinePacket();

        _lastPacket ??= LoadLastPacket();
        if (_lastPacket is not null)
        {
            Log.Info($"[pipeline] Đầu vào: kết quả lần chạy pipeline gần nhất ({_lastPacket.Included.Count} included, "
                     + $"{_lastPacket.Excluded?.Count ?? 0} excluded).");
            return _lastPacket;
        }

        Log.Info($"[pipeline] Đầu vào: danh sách app hiện có ({_library.Apps.Count} app), vì chưa có kết quả pipeline nào.");
        return _library.ToPacket();
    }

    private static PipelinePacket? LoadLastPacket()
    {
        try
        {
            return File.Exists(AppPaths.LastPacketFile)
                ? PacketValidator.Parse(File.ReadAllText(AppPaths.LastPacketFile))
                : null;
        }
        catch (Exception ex) when (ex is PacketFormatException or IOException or UnauthorizedAccessException)
        {
            Log.Info($"[data] Bỏ qua {AppPaths.LastPacketFile} vì không đọc được: {ex.Message}");
            return null;
        }
    }

    private static void SaveLastPacket(PipelinePacket packet)
    {
        try
        {
            string json = JsonSerializer.Serialize(packet, AppJsonContext.Default.PipelinePacket);
            AtomicFile.WriteAllText(AppPaths.LastPacketFile, json, Utf8NoBom);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"[data] Không lưu được {AppPaths.LastPacketFile}: {ex.Message}");
        }
    }

    private void SetRunning(bool running)
    {
        foreach (ToolStripItem item in _idleOnlyItems)
            item.Enabled = !running;
        foreach (Control control in _idleOnlyControls)
            control.Enabled = !running;
        foreach (ToolStripItem item in _stopItems)
            item.Enabled = running;

        _progress.Visible = running;
        if (running)
            _statusLabel.Text = "Đang chạy pipeline...";
    }

    #endregion

    #region Danh sách app

    private List<LibraryApp> SelectedApps =>
        _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<LibraryApp>().ToList();

    private void RefreshGrid(string? selectId = null)
    {
        string filter = _searchBox.Text.Trim();
        List<LibraryApp> apps = _library.Apps
            .Where(a => MatchesFilter(a, filter))
            .OrderByDescending(a => _newAppIds.Contains(a.Id))
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (LibraryApp app in apps)
        {
            DataGridViewRow row = _grid.Rows[_grid.Rows.Add()];
            row.Tag = app;
            row.Cells[1].Value = app.Name ?? "";
            row.Cells[2].Value = app.Publisher ?? "";
            row.Cells[3].Value = app.ExePath ?? app.InstallDir ?? "";
            row.Cells[4].Value = app.Origin == AppOrigins.Manual ? "Thủ công" : "Quét";
            if (_newAppIds.Contains(app.Id))
                row.DefaultCellStyle.BackColor = NewAppColor;
            if (app.Id == selectId)
            {
                _grid.ClearSelection();
                row.Selected = true;
                _grid.CurrentCell = row.Cells[1];
            }
        }
        _grid.ResumeLayout();

        _countLabel.Text = filter.Length == 0
            ? $"{_library.Apps.Count} app"
            : $"{apps.Count} / {_library.Apps.Count} app";
    }

    private static bool MatchesFilter(LibraryApp app, string filter) =>
        filter.Length == 0
        || Contains(app.Name, filter) || Contains(app.Publisher, filter)
        || Contains(app.ExePath, filter) || Contains(app.InstallDir, filter);

    private static bool Contains(string? value, string filter) =>
        value is not null && value.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

    private void OnGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != _iconColumn.Index)
            return;

        if (_grid.Rows[e.RowIndex].Tag is LibraryApp app)
        {
            e.Value = _icons.Get(app.ExePath);
            e.FormattingApplied = true;
        }
    }

    private void OnGridCellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 && e.ColumnIndex == _folderColumn.Index && _grid.Rows[e.RowIndex].Tag is LibraryApp app)
            ShellHelper.RevealInExplorer(this, app);
    }

    private void AddManually()
    {
        if (IsRunning)
            return;

        using var fileDialog = new OpenFileDialog
        {
            Title = "Chọn file exe của game",
            Filter = "Chương trình (*.exe)|*.exe",
            CheckFileExists = true
        };

        if (fileDialog.ShowDialog(this) != DialogResult.OK)
            return;

        string exe = PathUtils.NormalizeFilePath(fileDialog.FileName);
        if (_library.FindByExe(exe) is { } existing)
        {
            ShowWarning($"File này đã có trong danh sách với tên \"{existing.Name}\".");
            RefreshGrid(selectId: existing.Id);
            return;
        }

        using var dialog = new AppEditForm(LibraryApp.FromExe(exe), isNew: true);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _library.Apps.Add(dialog.Result);
        SaveLibrary();
        _searchBox.Clear();
        RefreshGrid(selectId: dialog.Result.Id);
        Log.Info($"[data] Đã thêm thủ công: {dialog.Result.Name} ({dialog.Result.ExePath})");
    }

    private void EditSelectedApp()
    {
        if (IsRunning)
            return;

        List<LibraryApp> selected = SelectedApps;
        if (selected.Count != 1)
        {
            ShowWarning("Hãy chọn đúng một game để sửa.");
            return;
        }

        LibraryApp original = selected[0];
        using var dialog = new AppEditForm(original.Clone(), isNew: false);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        int index = _library.Apps.IndexOf(original);
        if (index < 0)
            return;

        _library.Apps[index] = dialog.Result;
        SaveLibrary();
        RefreshGrid(selectId: dialog.Result.Id);
    }

    private void DeleteSelectedApps()
    {
        if (IsRunning)
            return;

        List<LibraryApp> selected = SelectedApps;
        if (selected.Count == 0)
            return;

        string what = selected.Count == 1 ? $"\"{selected[0].Name}\"" : $"{selected.Count} game";
        if (!Confirm($"Xóa {what} khỏi danh sách? File trên đĩa không bị đụng tới.\n\n"
                     + "Lưu ý: lần chạy pipeline sau có thể thêm lại game này; muốn loại hẳn thì thêm vào excludePaths của file rules."))
        {
            return;
        }

        foreach (LibraryApp app in selected)
            _library.Apps.Remove(app);

        SaveLibrary();
        RefreshGrid();
    }

    private void ExportForChecker()
    {
        if (_library.Apps.Count == 0)
        {
            ShowWarning("Danh sách đang trống, chưa có gì để xuất.");
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Xuất games.json cho GameProcessChecker",
            Filter = "JSON (*.json)|*.json",
            FileName = "games.json",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            int count = CheckerExporter.Export(_library.Apps, dialog.FileName, _settings.ExportCandidateExes);
            Log.Info($"[export] Đã xuất {count} mục ra {dialog.FileName}");
            MessageBox.Show(this,
                $"Đã xuất {count} mục ra:\n{dialog.FileName}\n\n"
                + "Đặt file này cạnh GameProcessChecker.exe (tên games.json) hoặc truyền đường dẫn file làm tham số khi chạy.",
                AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Không ghi được file:\n{ex.Message}");
        }
    }

    private void OpenDataDirectory()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            ShellHelper.OpenFolder(AppPaths.DataDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowError($"Không mở được thư mục dữ liệu:\n{ex.Message}");
        }
    }

    #endregion

    #region Lưu dữ liệu, nhật ký, hộp thoại

    private void SaveLibrary()
    {
        try
        {
            _library.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Không lưu được danh sách app:\n{ex.Message}");
        }
    }

    private void SaveSettings()
    {
        try
        {
            SettingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Không lưu được cấu hình:\n{ex.Message}");
        }
    }

    /// <summary>Gọi được từ mọi thread: nội dung được chuyển về thread giao diện.</summary>
    private void AppendLog(string message)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        string line = $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}";
        try
        {
            BeginInvoke(() =>
            {
                if (_logBox.IsDisposed)
                    return;

                if (_logBox.TextLength > MaxLogLength)
                    _logBox.Text = _logBox.Text[(MaxLogLength / 2)..];

                _logBox.AppendText(line);
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // Cửa sổ đang đóng.
        }
    }

    private void ShowWarning(string message) =>
        MessageBox.Show(this, message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void ShowError(string message)
    {
        if (!IsDisposed)
            MessageBox.Show(this, message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private bool Confirm(string message) =>
        MessageBox.Show(this, message, AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Đóng cửa sổ giữa lúc chạy: dừng chương trình ngoài, kết quả dở dang không được ghi.
        _cts?.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Log.SetSink(null);
            _icons.Dispose();
        }

        base.Dispose(disposing);
    }

    #endregion
}
