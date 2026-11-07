using System.Diagnostics;

namespace GameProcessChecker;

internal static class ProcessScanner
{
    private static readonly char[] PathBuffer = new char[1024];

    /// <summary>
    /// Liệt kê process 1 lần, chỉ lọc theo TÊN (rẻ), rồi mới OpenProcess + so đường dẫn
    /// cho những process trùng tên. Nhờ vậy gần như không phải mở handle process nào khác.
    /// </summary>
    public static bool IsAnyTargetRunning(TargetSet targets)
    {
        if (targets.IsEmpty)
            return false;

        Process[] processes = Process.GetProcesses();
        bool found = false;

        foreach (Process proc in processes)
        {
            try
            {
                if (!found && proc.Id > 4 && targets.Names.Contains(proc.ProcessName))
                {
                    string? exePath = TryGetProcessExePath(proc.Id);
                    if (exePath is not null && targets.Paths.Contains(exePath))
                        found = true;
                }
            }
            catch
            {
                // Process đã thoát giữa chừng hoặc không có quyền truy cập.
            }
            finally
            {
                proc.Dispose();
            }
        }

        return found;
    }

    private static string? TryGetProcessExePath(int pid)
    {
        IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero)
            return null;

        try
        {
            uint size = (uint)PathBuffer.Length;
            return NativeMethods.QueryFullProcessImageNameW(hProcess, 0, PathBuffer, ref size) && size > 0
                ? new string(PathBuffer, 0, (int)size)
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }
}
