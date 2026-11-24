namespace GameScanner;

/// <summary>
/// Cửa sổ chính: bên trái là các bước pipeline, bên phải là danh sách app đã lưu và nhật ký chạy.
/// </summary>
internal sealed class MainForm : Form
{
    private const string AppTitle = "GameScanner";
    private const int MaxLogLength = 400_000;

    private static readonly Color NewAppColor = Color.FromArgb(226, 244, 229);

    private readonly AppLibrary _library;
    private readonly ScannerSettings _settings;
    private RulesConfig _rules;
    private readonly IconCache _icons = new();
    private readonly HashSet<string> _newAppIds = [];

    private List<PipelineStep> _steps = [];
    private CancellationTokenSource? _cts;
    private bool _refreshingSteps;
    private bool _suppressNextCheck;
    private bool _closeWhenIdle;

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
    private Button _toggleStepButton = null!;
    private Button _toggleAllButton = null!;
    private Button _deleteStepButton = null!;
    private Button _moveUpButton = null!;
    private Button _moveDownButton = null!;

    public MainForm()
    {
        Text = AppTitle;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1120, 660);
        MinimumSize = new Size(760, 440);
        StartPosition = FormStartPosition.CenterScreen;
        if (ShellHelper.LoadAppIcon() is { } icon)
            Icon = icon;

        _library = AppLibrary.Load(AppPaths.LibraryFile, out string? libraryWarning);
        _settings = SettingsStore.Load(out string? settingsWarning);
        _rules = RulesStore.Load(out string? rulesWarning);

        BuildUi();
        Log.SetSink(AppendLog);

        RefreshSteps(selectIndex: 0);

        Load += (_, _) =>
        {
            // Kích thước theo DPI chỉ chính xác sau khi cửa sổ đã có handle và đã được scale.
            FitToScreen();
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
            if (rulesWarning is not null)
                Log.Info(rulesWarning);
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

    /// <summary>Ở DPI cao, kích thước mặc định sau khi scale có thể lớn hơn màn hình: thu lại cho vừa vùng làm việc.</summary>
    private void FitToScreen()
    {
        Rectangle area = Screen.FromControl(this).WorkingArea;
        int width = Math.Min(Width, area.Width);
        int height = Math.Min(Height, area.Height);
        if (width == Width && height == Height)
            return;

        MinimumSize = new Size(Math.Min(MinimumSize.Width, width), Math.Min(MinimumSize.Height, height));
        Bounds = new Rectangle(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
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
        _exportCandidatesItem = new ToolStripMenuItem("Xuất kèm exe khác của game")
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
        pipeline.DropDownItems.Add(StopItem(MenuItem("Dừng", StopPipeline, Keys.Shift | Keys.F5)));
        pipeline.DropDownItems.Add(new ToolStripSeparator());
        pipeline.DropDownItems.Add(IdleOnly(MenuItem("Thêm pipeline tùy chỉnh…", AddCustomStage)));
        pipeline.DropDownItems.Add(IdleOnly(MenuItem("Sửa bước đã chọn…", EditSelectedStep)));
        pipeline.DropDownItems.Add(IdleOnly(MenuItem("Bật / tắt bước đã chọn", ToggleSelectedStep)));
        pipeline.DropDownItems.Add(IdleOnly(MenuItem("Bật / tắt tất cả các bước", ToggleAllSteps)));
        pipeline.DropDownItems.Add(IdleOnly(MenuItem("Xóa pipeline tùy chỉnh đã chọn", DeleteSelectedStep)));
        pipeline.DropDownItems.Add(new ToolStripSeparator());
        pipeline.DropDownItems.Add(IdleOnly(MenuItem("Danh sách nhớ (game đã loại / luôn chọn)…", EditRememberList)));
        pipeline.DropDownItems.Add(new ToolStripSeparator());
        pipeline.DropDownItems.Add(MenuItem("Tài liệu viết pipeline tùy chỉnh", () => ShellHelper.OpenPipelineDocs(this)));

        menu.Items.AddRange([file, pipeline]);
        return menu;
    }

    private ToolStrip BuildToolStrip()
    {
        var strip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };

        strip.Items.Add(IdleOnly(ToolButton("▶ Chạy toàn bộ pipeline", RunAll)));
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
        _stepList.ItemCheck += OnStepItemCheck;
        _stepList.ItemChecked += OnStepChecked;
        _stepList.MouseDown += (_, e) => _suppressNextCheck = e.Clicks > 1;
        _stepList.MouseUp += (_, _) => _suppressNextCheck = false;
        _stepList.SelectedIndexChanged += (_, _) => UpdateStepButtons();
        _stepList.DoubleClick += (_, _) =>
        {
            // Hộp thoại sửa là modal nên MouseUp có thể không tới ListView: xóa cờ ở đây cho chắc.
            _suppressNextCheck = false;
            EditSelectedStep();
        };

        var stepButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        _editStepButton = FormLayout.CreateButton("Sửa…", (_, _) => EditSelectedStep());
        _toggleStepButton = FormLayout.CreateButton("Tắt", (_, _) => ToggleSelectedStep());
        _deleteStepButton = FormLayout.CreateButton("Xóa", (_, _) => DeleteSelectedStep());
        _moveUpButton = FormLayout.CreateButton("▲", (_, _) => MoveSelectedStep(-1));
        _moveDownButton = FormLayout.CreateButton("▼", (_, _) => MoveSelectedStep(+1));
        stepButtons.Controls.AddRange(
        [
            FormLayout.CreateButton("Thêm…", (_, _) => AddCustomStage()),
            _editStepButton, _toggleStepButton, _deleteStepButton, _moveUpButton, _moveDownButton
        ]);

        _toggleAllButton = FormLayout.CreateButton("Tắt tất cả", (_, _) => ToggleAllSteps());
        var runButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        runButtons.Controls.AddRange(
        [
            FormLayout.CreateButton("Chạy toàn bộ", (_, _) => RunAll()),
            _toggleAllButton
        ]);

        Label hint = FormLayout.CreateHint(
            "Quét xong chỉ thêm game mới vào danh sách, không xóa game đã có. "
            + "Nếu một bước lỗi, cả lần quét bị hủy và danh sách giữ nguyên.",
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

    private int SelectedStepIndex => _stepList.SelectedIndices.Count == 1 ? _stepList.SelectedIndices[0] : -1;

    private PipelineStep? SelectedStep => SelectedStepIndex >= 0 ? _steps[SelectedStepIndex] : null;

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
                    ForeColor = step.Enabled ? SystemColors.WindowText : SystemColors.GrayText,
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
        StepKind.Collect => "Thu thập ứng dụng từ Registry và các launcher (luôn chạy đầu tiên).",
        StepKind.Heuristic => "Tự động nhận diện game trong các ứng dụng vừa thu thập. "
                              + "Tắt bước này thì chỉ pipeline tùy chỉnh và Danh sách nhớ quyết định game nào được chọn.",
        StepKind.Remember => "Áp dụng Luôn chọn / Luôn loại ở bước cuối. "
                             + $"Đang nhớ {_rules.IncludeApps.Count} game luôn chọn, {_rules.ExcludeApps.Count} game luôn loại, "
                             + $"{_rules.IncludePaths.Count + _rules.ExcludePaths.Count} mẫu đường dẫn. Bấm Sửa để xem và chỉnh.",
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

        _editStepButton.Enabled = step is { Kind: StepKind.Custom or StepKind.Remember };
        _toggleStepButton.Enabled = step?.CanDisable == true;
        _toggleStepButton.Text = step is { Enabled: false } ? "Bật" : "Tắt";
        _deleteStepButton.Enabled = customIndex >= 0;
        _moveUpButton.Enabled = customIndex > 0;
        _moveDownButton.Enabled = customIndex >= 0 && customIndex < _settings.CustomStages.Count - 1;
        _toggleAllButton.Text = AllOptionalStepsEnabled ? "Tắt tất cả" : "Bật tất cả";
    }

    /// <summary>Mọi bước tắt được (tức trừ Collect) đều đang bật.</summary>
    private bool AllOptionalStepsEnabled => _steps.Where(s => s.CanDisable).All(s => s.Enabled);

    /// <summary>Chặn bỏ tick bước Collect, và chặn việc ListView tự đảo ô tick khi double-click (double-click là để sửa).</summary>
    private void OnStepItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_refreshingSteps)
            return;

        if (_suppressNextCheck || !_steps[e.Index].CanDisable)
            e.NewValue = e.CurrentValue;

        _suppressNextCheck = false;
    }

    private void OnStepChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (!_refreshingSteps)
            SetStepEnabled(e.Item.Index, e.Item.Checked);
    }

    private void ToggleSelectedStep()
    {
        if (!IsRunning && SelectedStep is { CanDisable: true } step)
            SetStepEnabled(SelectedStepIndex, !step.Enabled);
    }

    /// <summary>
    /// Đang bật hết thì tắt hết, còn lại thì bật hết. Bước Collect không tắt được nên không bị đụng tới;
    /// cấu hình của các bước vẫn được giữ.
    /// </summary>
    private void ToggleAllSteps()
    {
        if (IsRunning)
            return;

        bool enabled = !AllOptionalStepsEnabled;
        _settings.HeuristicStageEnabled = enabled;
        _settings.RememberStageEnabled = enabled;
        foreach (CustomStage stage in _settings.CustomStages)
            stage.Enabled = enabled;

        SaveSettings();
        Log.Info($"[pipeline] {(enabled ? "Bật" : "Tắt")} tất cả các bước (trừ bước 1).");
        RefreshSteps(selectIndex: SelectedStepIndex);
    }

    /// <summary>Tắt chỉ bỏ qua bước khi chạy toàn bộ; cấu hình của bước vẫn được giữ.</summary>
    private void SetStepEnabled(int index, bool enabled)
    {
        PipelineStep step = _steps[index];
        if (!step.CanDisable || step.Enabled == enabled)
            return;

        switch (step.Kind)
        {
            case StepKind.Heuristic:
                _settings.HeuristicStageEnabled = enabled;
                break;
            case StepKind.Remember:
                _settings.RememberStageEnabled = enabled;
                break;
            case StepKind.Custom:
                CustomStage? stage = _settings.CustomStages.Find(s => s.Id == step.Custom!.Id);
                if (stage is null)
                    return;
                stage.Enabled = enabled;
                break;
        }

        _steps[index] = step with { Enabled = enabled };
        SaveSettings();
        Log.Info($"[pipeline] {(enabled ? "Bật" : "Tắt")} bước: {step.DisplayName}");

        // Dựng lại danh sách sau khi sự kiện của ListView xử lý xong.
        BeginInvoke(() => RefreshSteps(selectIndex: index));
    }

    private void AddCustomStage()
    {
        if (IsRunning)
            return;

        using var dialog = new StageEditForm(null);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        // Bước mới nằm sau các pipeline tùy chỉnh khác; Danh sách nhớ tự xuống cuối.
        _settings.CustomStages.Add(dialog.Result);
        SaveSettings();
        RefreshSteps(selectIndex: 1 + _settings.CustomStages.Count); // Collect, Heuristic rồi tới các bước tùy chỉnh
    }

    private void EditSelectedStep()
    {
        if (IsRunning || SelectedStep is not { } step)
            return;

        switch (step.Kind)
        {
            case StepKind.Remember:
                EditRememberList();
                return;

            case StepKind.Custom:
                break;

            default:
                return; // Collect và Heuristic không có gì để chỉnh.
        }

        int index = _settings.CustomStages.FindIndex(s => s.Id == step.Custom!.Id);
        if (index < 0)
            return;

        using var dialog = new StageEditForm(_settings.CustomStages[index].Clone());
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _settings.CustomStages[index] = dialog.Result;
        SaveSettings();
        RefreshSteps(selectIndex: SelectedStepIndex);
    }

    private void EditRememberList()
    {
        if (IsRunning)
            return;

        using var dialog = new RulesEditForm(RulesStore.Clone(_rules));
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _rules = dialog.Result;
        SaveRules();
        RefreshSteps(selectIndex: _steps.FindIndex(s => s.Kind == StepKind.Remember));
    }

    private void DeleteSelectedStep()
    {
        if (IsRunning || SelectedStep?.Custom is not { } custom)
            return;

        if (!Confirm($"Xóa hẳn pipeline tùy chỉnh \"{custom.DisplayName}\"?\n\n"
                     + "Nếu chỉ muốn tạm bỏ qua bước này, hãy bấm \"Tắt\" để giữ lại cấu hình."))
        {
            return;
        }

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
        RefreshSteps(selectIndex: SelectedStepIndex + offset);
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
            // Bước Collect luôn bật nên luôn chạy đầu tiên và tự sinh dữ liệu: đầu vào là packet rỗng.
            var input = new PipelinePacket();
            RulesConfig rules = RulesStore.Clone(_rules);

            PipelinePacket result = await Task.Run(
                () => PipelineRunner.RunAsync(steps, input, rules, cts.Token), cts.Token);

            // Chỉ tới đây khi mọi bước đều thành công: trạng thái cuối cùng quyết định app nào được thêm.
            List<LibraryApp> added = _library.MergeNew(result.Included);
            _newAppIds.Clear();
            foreach (LibraryApp app in added)
                _newAppIds.Add(app.Id);

            // Lỗi ghi file chỉ được báo riêng; app mới đã nằm trong danh sách đang hiển thị.
            if (added.Count > 0)
                SaveLibrary();

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

            // Người dùng đã bấm đóng cửa sổ trong lúc chạy: giờ chương trình ngoài đã dừng hẳn, đóng được rồi.
            if (_closeWhenIdle && !IsDisposed)
                Close();
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
            row.Cells[4].Value = app.Origin == AppOrigins.Manual ? "Thủ công" : app.Source ?? "Quét";
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

        LibraryApp candidate = LibraryApp.FromExe(PathUtils.NormalizeFilePath(fileDialog.FileName));
        if (WarnIfDuplicate(candidate))
            return;

        using var dialog = new AppEditForm(candidate, isNew: true);
        if (dialog.ShowDialog(this) != DialogResult.OK || WarnIfDuplicate(dialog.Result))
            return;

        LibraryApp app = dialog.Result;
        _library.Apps.Add(app);
        SaveLibrary();

        // Nhớ là "luôn chọn" (và bỏ khỏi danh sách đã loại nếu trước đó từng xóa game này).
        RulesStore.RememberIncluded(_rules, app);
        SaveRules();
        RefreshSteps(selectIndex: SelectedStepIndex);

        _searchBox.Clear();
        RefreshGrid(selectId: app.Id);
        Log.Info($"[data] Đã thêm thủ công: {app.Name} ({app.ExePath})");
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
        if (index < 0 || WarnIfDuplicate(dialog.Result))
            return;

        _library.Apps[index] = dialog.Result;
        SaveLibrary();

        if (RulesStore.UpdateRemembered(_rules, original, dialog.Result))
            SaveRules();

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
                     + "Game sẽ được ghi vào \"Danh sách nhớ\" (theo thư mục) để tự động bị loại ở các lần quét sau. "
                     + "Muốn quét lại game này thì bỏ nó khỏi danh sách nhớ."))
        {
            return;
        }

        foreach (LibraryApp app in selected)
        {
            _library.Apps.Remove(app);
            RulesStore.RememberExcluded(_rules, app);
        }

        SaveLibrary();
        SaveRules();
        RefreshSteps(selectIndex: SelectedStepIndex);
        RefreshGrid();
    }

    /// <summary>Báo và chọn sẵn game đã có trong danh sách nếu <paramref name="app"/> trùng thư mục với nó.</summary>
    private bool WarnIfDuplicate(LibraryApp app)
    {
        if (_library.FindSame(app) is not { } existing)
            return false;

        ShowWarning($"Game này trùng thư mục với \"{existing.Name}\" đã có trong danh sách:\n{existing.Folder}");
        _searchBox.Clear();
        RefreshGrid(selectId: existing.Id);
        return true;
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

    private void SaveRules()
    {
        try
        {
            RulesStore.Save(_rules);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Không lưu được danh sách nhớ:\n{ex.Message}");
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
        // Đóng cửa sổ giữa lúc chạy: hủy pipeline và chờ chương trình ngoài bị dừng hẳn rồi mới đóng,
        // để không bỏ lại tiến trình con chạy ngầm. Kết quả dở dang không được ghi.
        if (IsRunning && e.CloseReason != CloseReason.WindowsShutDown)
        {
            e.Cancel = true;
            _closeWhenIdle = true;
            StopPipeline();
            _statusLabel.Text = "Đang dừng pipeline để đóng...";
        }

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
