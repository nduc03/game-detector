using System.Diagnostics;

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

        LowerProcessPriority();

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(args));
    }

    private static void LowerProcessPriority()
    {
        try
        {
            // Background mode: CPU Idle + I/O priority thấp + memory priority thấp.
            if (!NativeMethods.SetPriorityClass(NativeMethods.GetCurrentProcess(), NativeMethods.PROCESS_MODE_BACKGROUND_BEGIN))
            {
                using Process self = Process.GetCurrentProcess();
                self.PriorityClass = ProcessPriorityClass.Idle;
            }
        }
        catch
        {
            // Không critical, bỏ qua.
        }
    }
}
