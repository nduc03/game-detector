namespace GameProcessChecker;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Chỉ cho chạy 1 instance.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\GameProcessChecker.SingleInstance", out bool isNew);
        if (!isNew)
            return;

        try
        {
            NativeMethods.EnableEfficiencyMode();
        }
        catch
        {
            // Không critical, bỏ qua.
        }

        using var tray = new NativeTray("Game Process Checker: đang khởi động");
        using var watcher = new GameWatcher(new TargetConfig(args));

        // Được gọi từ thread nền; SetTooltip tự chuyển việc về thread của tray.
        watcher.StateChanged += running => tray.SetTooltip(
            running ? "Game Process Checker: đang có game" : "Game Process Checker: không có game");

        watcher.Start();
        tray.Run(); // Chặn tới khi người dùng chọn "Thoát"
    }
}