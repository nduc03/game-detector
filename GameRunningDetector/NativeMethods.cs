using System.Runtime.InteropServices;

namespace GameProcessChecker;

internal static class NativeMethods
{
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint IDLE_PRIORITY_CLASS = 0x00000040;

    // Hạ CPU + I/O + memory priority xuống mức nền (Idle) cho cả process.
    public const uint PROCESS_MODE_BACKGROUND_BEGIN = 0x00100000;

    // Power throttling (EcoQoS / Efficiency mode)
    private const int ProcessPowerThrottling = 4;
    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
    private const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x4; // Win11+

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, char[] lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

    [DllImport("kernel32.dll")]
    public static extern uint GetPriorityClass(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(
        IntPtr hProcess,
        int processInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE processInformation,
        uint processInformationSize);

    /// <summary>
    /// Bật "Efficiency mode" giống Task Manager trên Win 11:
    ///   1) SetPriorityClass(IDLE_PRIORITY_CLASS)  -> đổi base priority của process
    ///   2) SetProcessInformation(ProcessPowerThrottling) -> EcoQoS (ưu tiên E-core, hạ xung)
    /// Không dùng PROCESS_MODE_BACKGROUND_BEGIN vì nó không đổi priority class của process.
    /// Trả về true nếu cả hai bước đều thành công và priority class đọc lại đúng là Idle.
    /// </summary>
    public static bool EnableEfficiencyMode()
    {
        IntPtr self = GetCurrentProcess();

        bool idleOk = SetPriorityClass(self, IDLE_PRIORITY_CLASS)
                      && GetPriorityClass(self) == IDLE_PRIORITY_CLASS;

        //bool backgroundOk = SetPriorityClass(self, PROCESS_MODE_BACKGROUND_BEGIN)
              //&& GetPriorityClass(self) == PROCESS_MODE_BACKGROUND_BEGIN;

        bool ecoQosOk = false;
        try
        {
            uint size = (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>();
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
                StateMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION
            };

            ecoQosOk = SetProcessInformation(self, ProcessPowerThrottling, ref state, size);

            if (!ecoQosOk)
            {
                // Hệ thống không nhận bit IGNORE_TIMER_RESOLUTION: thử lại chỉ với EcoQoS.
                state.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
                state.StateMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
                ecoQosOk = SetProcessInformation(self, ProcessPowerThrottling, ref state, size);
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Windows quá cũ, không có SetProcessInformation.
        }

        return idleOk && ecoQosOk;
    }
}