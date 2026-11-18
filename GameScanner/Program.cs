namespace GameScanner;

/// <summary>
/// Mặc định mở giao diện WinForms. Khi được gọi kiểu dòng lệnh / nối pipe thì chạy như một
/// Unix filter đúng như trước đây (xem <see cref="CliRunner"/>).
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (CliRunner.ShouldRun(args))
            return CliRunner.Run(args);

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }
}
