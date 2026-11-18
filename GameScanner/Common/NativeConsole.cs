using System.Runtime.InteropServices;

namespace GameScanner;

/// <summary>
/// App là WinExe (không có console riêng) nên Console.IsInputRedirected/IsErrorRedirected không đáng tin:
/// handle rỗng cũng bị coi là "đã chuyển hướng". Ở đây kiểm tra trực tiếp loại handle.
/// </summary>
internal static class NativeConsole
{
    private const int STD_INPUT_HANDLE = -10;
    private const int STD_ERROR_HANDLE = -12;
    private const uint FILE_TYPE_DISK = 0x0001;
    private const uint FILE_TYPE_PIPE = 0x0003;
    private const uint FILE_TYPE_REMOTE = 0x8000;
    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
    private static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr hFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    /// <summary>stdin đang nối với pipe (`a | GameScanner`) hoặc file (`GameScanner &lt; a.json`).</summary>
    public static bool IsStdinRedirected => IsFileOrPipe(STD_INPUT_HANDLE);

    /// <summary>stderr đang nối với pipe hoặc file (`GameScanner 2&gt; log.txt`).</summary>
    public static bool IsStderrRedirected => IsFileOrPipe(STD_ERROR_HANDLE);

    /// <summary>Gắn vào console của tiến trình cha (nếu có) để log stderr hiện ra cửa sổ dòng lệnh đang gọi.</summary>
    public static void AttachToParentConsole()
    {
        try
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
        }
        catch
        {
            // Không có console cha: bỏ qua.
        }
    }

    private static bool IsFileOrPipe(int stdHandle)
    {
        try
        {
            IntPtr handle = GetStdHandle(stdHandle);
            if (handle == IntPtr.Zero || handle == InvalidHandle)
                return false;

            uint type = GetFileType(handle) & ~FILE_TYPE_REMOTE;
            return type is FILE_TYPE_DISK or FILE_TYPE_PIPE;
        }
        catch
        {
            return false;
        }
    }
}
