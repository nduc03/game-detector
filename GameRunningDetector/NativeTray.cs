using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GameProcessChecker;

/// <summary>
/// Tray icon thuần Win32: 1 cửa sổ ẩn + Shell_NotifyIcon + message loop.
/// Không phụ thuộc WinForms. Mọi thao tác lên icon đều chạy trên thread tạo cửa sổ.
/// </summary>
internal sealed class NativeTray : IDisposable
{
    private const uint WM_NULL = 0x0000;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_APP = 0x8000;
    private const uint WM_TRAYICON = WM_APP + 1;
    private const uint WM_UPDATE_TIP = WM_APP + 2;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint MF_STRING = 0x00000000;

    private const int IDI_APPLICATION = 32512;
    private const uint CMD_EXIT = 1001;
    private const string ClassName = "GameProcessChecker_TrayClass";

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // Layout NOTIFYICONDATAW "V1" (kết thúc ở szTip) - Shell chấp nhận, đủ cho icon + tooltip.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, nuint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lpTPMParams);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

    private readonly WndProcDelegate _wndProc; // Giữ tham chiếu để GC không thu hồi
    private readonly uint _taskbarCreatedMsg;
    private readonly IntPtr _hwnd;
    private readonly IntPtr _icon;
    private volatile string _tooltip;
    private bool _disposed;

    public NativeTray(string initialTooltip)
    {
        _tooltip = initialTooltip;
        _wndProc = WndProc;
        _taskbarCreatedMsg = RegisterWindowMessageW("TaskbarCreated");
        _icon = LoadIconW(IntPtr.Zero, (IntPtr)IDI_APPLICATION);

        IntPtr hInstance = GetModuleHandleW(null);
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = ClassName
        };

        if (RegisterClassExW(ref wc) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        // Cửa sổ top-level nhưng KHÔNG bao giờ hiển thị (không ShowWindow): vô hình, không có nút taskbar.
        // Không dùng message-only window (HWND_MESSAGE) vì loại đó không nhận broadcast "TaskbarCreated",
        // dẫn tới mất icon khi explorer.exe khởi động lại (hoặc khi app tự chạy lúc đăng nhập quá sớm).
        _hwnd = CreateWindowExW(0, ClassName, "GameProcessChecker.TrayHost", 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        AddIcon();
    }

    /// <summary>Message loop. Chạy trên thread tạo NativeTray, trả về khi người dùng chọn Thoát.</summary>
    public void Run()
    {
        while (GetMessageW(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            DispatchMessageW(ref msg);
        }
    }

    /// <summary>Gọi được từ bất kỳ thread nào: chỉ lưu text rồi post message, việc cập nhật icon chạy trên thread UI.</summary>
    public void SetTooltip(string text)
    {
        _tooltip = text;
        if (!_disposed)
            PostMessageW(_hwnd, WM_UPDATE_TIP, IntPtr.Zero, IntPtr.Zero);
    }

    private NOTIFYICONDATA BuildData(uint flags)
    {
        string tip = _tooltip;
        return new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _icon,
            szTip = tip.Length > 127 ? tip[..127] : tip
        };
    }

    private void AddIcon()
    {
        NOTIFYICONDATA nid = BuildData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        Shell_NotifyIconW(NIM_ADD, ref nid);
    }

    private void UpdateTipOnUiThread()
    {
        NOTIFYICONDATA nid = BuildData(NIF_TIP);
        Shell_NotifyIconW(NIM_MODIFY, ref nid);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_TRAYICON:
                if (((uint)lParam.ToInt64() & 0xFFFF) == WM_RBUTTONUP)
                    ShowContextMenu(hWnd);
                return IntPtr.Zero;

            case WM_UPDATE_TIP:
                UpdateTipOnUiThread();
                return IntPtr.Zero;
        }

        if (_taskbarCreatedMsg != 0 && msg == _taskbarCreatedMsg)
        {
            AddIcon(); // Explorer vừa khởi động lại: đăng ký lại icon
            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu(IntPtr hWnd)
    {
        GetCursorPos(out POINT pt);
        IntPtr hMenu = CreatePopupMenu();
        AppendMenuW(hMenu, MF_STRING, CMD_EXIT, "Thoát");

        // Bắt buộc để menu tự đóng khi bấm ra ngoài.
        SetForegroundWindow(hWnd);
        uint selected = TrackPopupMenuEx(
            hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_LEFTALIGN, pt.X, pt.Y, hWnd, IntPtr.Zero);
        PostMessageW(hWnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(hMenu);

        if (selected == CMD_EXIT)
            PostQuitMessage(0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        NOTIFYICONDATA nid = BuildData(0);
        Shell_NotifyIconW(NIM_DELETE, ref nid);
        DestroyWindow(_hwnd);
    }
}