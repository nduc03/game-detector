namespace GameScanner;

/// <summary>Hộp thoại xác nhận kèm một ô tick (MessageBox không có ô tick).</summary>
internal sealed class CheckConfirmForm : Form
{
    private readonly CheckBox _check;

    public CheckConfirmForm(string title, string message, string checkText, bool isChecked)
    {
        FormLayout.ApplyDialogStyle(this, title);

        _check = new CheckBox { Text = checkText, Checked = isChecked, AutoSize = true, Margin = new Padding(3, 10, 3, 3) };

        TableLayoutPanel grid = FormLayout.CreateGrid(1);
        grid.Controls.Add(new Label { Text = message, AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(3) }, 0, 0);
        grid.Controls.Add(_check, 0, 1);
        grid.Controls.Add(FormLayout.CreateDialogButtons(this, (_, _) => DialogResult = DialogResult.OK), 0, 2);
        Controls.Add(grid);
    }

    public bool IsChecked => _check.Checked;
}

internal enum FieldBrowse
{
    None,
    Folder,
    Exe
}

/// <summary>Một ô nhập trong <see cref="FieldsForm"/>.</summary>
internal sealed record FieldSpec(string Label, string Value, FieldBrowse Browse = FieldBrowse.None);

/// <summary>Hộp thoại nhập vài trường văn bản, có nút chọn thư mục / file nếu cần và hàm kiểm tra trước khi OK.</summary>
internal sealed class FieldsForm : Form
{
    private const int FieldWidth = 460;

    private readonly List<TextBox> _boxes = [];
    private readonly Func<string[], (string Message, int FieldIndex)?> _validate;

    /// <param name="validate">Trả về (thông báo lỗi, chỉ số ô cần sửa), hoặc null nếu hợp lệ.</param>
    public FieldsForm(string title, IReadOnlyList<FieldSpec> fields, string? hint,
        Func<string[], (string Message, int FieldIndex)?> validate)
    {
        _validate = validate;
        FormLayout.ApplyDialogStyle(this, title);

        TableLayoutPanel grid = FormLayout.CreateGrid(3);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        int row = 0;
        foreach (FieldSpec field in fields)
        {
            TextBox box = FormLayout.CreateTextBox(FieldWidth);
            box.Text = field.Value;
            _boxes.Add(box);

            grid.Controls.Add(FormLayout.CreateLabel(field.Label), 0, row);
            grid.Controls.Add(box, 1, row);
            if (field.Browse != FieldBrowse.None)
                grid.Controls.Add(FormLayout.CreateButton("Chọn…", (_, _) => Browse(box, field.Browse)), 2, row);
            row++;
        }

        if (!string.IsNullOrWhiteSpace(hint))
            grid.Controls.Add(FormLayout.CreateHint(hint, FieldWidth), 1, row++);

        FlowLayoutPanel buttons = FormLayout.CreateDialogButtons(this, (_, _) => Accept());
        grid.Controls.Add(buttons, 0, row);
        grid.SetColumnSpan(buttons, 3);
        Controls.Add(grid);
    }

    /// <summary>Giá trị các ô (đã cắt khoảng trắng), hợp lệ khi DialogResult là OK.</summary>
    public string[] Values => _boxes.Select(b => b.Text.Trim()).ToArray();

    private void Accept()
    {
        if (_validate(Values) is { } error)
        {
            MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _boxes[Math.Clamp(error.FieldIndex, 0, _boxes.Count - 1)].Focus();
            return;
        }

        DialogResult = DialogResult.OK;
    }

    private void Browse(TextBox box, FieldBrowse kind)
    {
        if (kind == FieldBrowse.Folder)
        {
            using var dialog = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = "Chọn thư mục" };
            if (Directory.Exists(box.Text.Trim()))
                dialog.SelectedPath = box.Text.Trim();
            if (dialog.ShowDialog(this) == DialogResult.OK)
                box.Text = dialog.SelectedPath;
            return;
        }

        using var fileDialog = new OpenFileDialog { Filter = "Chương trình (*.exe)|*.exe", CheckFileExists = true };
        if (fileDialog.ShowDialog(this) == DialogResult.OK)
            box.Text = fileDialog.FileName;
    }
}
