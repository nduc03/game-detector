using System.Diagnostics;

namespace GameScanner;

/// <summary>Bộ nhớ đệm icon lấy từ file exe của từng app.</summary>
internal sealed class IconCache : IDisposable
{
    private readonly Dictionary<string, Image> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Image _fallback = SystemIcons.Application.ToBitmap();

    public Image Get(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return _fallback;

        if (_icons.TryGetValue(exePath, out Image? cached))
            return cached;

        Image image = _fallback;
        try
        {
            if (File.Exists(exePath))
            {
                using Icon? icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon is not null)
                    image = icon.ToBitmap();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException
                                       or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Không lấy được icon (đường dẫn mạng, file bị khóa...): dùng icon mặc định.
        }

        _icons[exePath] = image;
        return image;
    }

    public void Dispose()
    {
        foreach (Image image in _icons.Values.Where(i => !ReferenceEquals(i, _fallback)))
            image.Dispose();
        _icons.Clear();
        _fallback.Dispose();
    }
}

internal static class ShellHelper
{
    /// <summary>Icon của chính GameScanner.exe (nhúng qua ApplicationIcon) để gắn cho cửa sổ.</summary>
    public static Icon? LoadAppIcon()
    {
        try
        {
            string? path = Environment.ProcessPath;
            return path is null ? null : Icon.ExtractAssociatedIcon(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Mở Explorer tại thư mục chứa app, chọn sẵn file exe nếu còn tồn tại.</summary>
    public static void RevealInExplorer(IWin32Window owner, LibraryApp app)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(app.ExePath) && File.Exists(app.ExePath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{app.ExePath}\"") { UseShellExecute = false })?.Dispose();
                return;
            }

            string? dir = !string.IsNullOrWhiteSpace(app.InstallDir) ? app.InstallDir
                : !string.IsNullOrWhiteSpace(app.ExePath) ? Path.GetDirectoryName(app.ExePath)
                : null;

            if (dir is not null && Directory.Exists(dir))
            {
                OpenFolder(dir);
                return;
            }

            MessageBox.Show(owner, "Thư mục của app này không còn tồn tại.", "GameScanner",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            MessageBox.Show(owner, $"Không mở được Explorer: {ex.Message}", "GameScanner",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public static void OpenFolder(string dir) =>
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true })?.Dispose();

    /// <summary>Bản online, dùng khi bản cạnh exe bị thiếu (ví dụ chỉ chép riêng GameScanner.exe).</summary>
    private const string PipelineDocsUrl = "https://github.com/nduc03/game-detector/blob/master/GameScanner/docs/pipeline.md";

    /// <summary>
    /// Mở tài liệu pipeline tùy chỉnh: file docs\pipeline.md được build chép cạnh exe. Máy chưa gán app mở .md thì mở bằng
    /// Notepad; thiếu file thì mở bản online.
    /// </summary>
    public static void OpenPipelineDocs(IWin32Window owner)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "docs", "pipeline.md");
        try
        {
            if (!File.Exists(path))
            {
                Process.Start(new ProcessStartInfo { FileName = PipelineDocsUrl, UseShellExecute = true })?.Dispose();
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true })?.Dispose();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Chưa có app nào được gán để mở file .md.
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = false })?.Dispose();
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            MessageBox.Show(owner, $"Không mở được tài liệu: {ex.Message}\n\n{path}\n{PipelineDocsUrl}", "GameScanner",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

/// <summary>Các mảnh dựng form dùng chung cho hộp thoại.</summary>
internal static class FormLayout
{
    public static TableLayoutPanel CreateGrid(int columns)
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = columns,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        return grid;
    }

    public static Label CreateLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 6, 8, 6)
    };

    public static Label CreateHint(string text, int maxWidth) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(maxWidth, 0),
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(3, 0, 3, 8)
    };

    public static TextBox CreateTextBox(int minWidth) => new()
    {
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        MinimumSize = new Size(minWidth, 0),
        Width = minWidth
    };

    public static Button CreateButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 2, 8, 2),
            Margin = new Padding(3)
        };
        button.Click += onClick;
        return button;
    }

    /// <summary>Hàng nút OK / Hủy canh phải ở cuối hộp thoại.</summary>
    public static FlowLayoutPanel CreateDialogButtons(Form form, EventHandler onAccept)
    {
        Button ok = CreateButton("OK", onAccept);
        Button cancel = CreateButton("Hủy", (_, _) => form.DialogResult = DialogResult.Cancel);
        ok.MinimumSize = cancel.MinimumSize = new Size(88, 0);

        form.AcceptButton = ok;
        form.CancelButton = cancel;

        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 0, 0)
        };
        panel.Controls.Add(cancel);
        panel.Controls.Add(ok);
        return panel;
    }

    public static void ApplyDialogStyle(Form form, string title)
    {
        form.Text = title;
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.AutoSize = true;
        form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.CenterParent;
    }
}
