using System.Runtime.InteropServices;

namespace Lumo.Native;

/// <summary>All Win32 P/Invoke entry points used by Lumo.</summary>
internal static class NativeMethods
{
    // ---------------- hotkeys ----------------
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint VK_SPACE = 0x20;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ---------------- foreground helpers ----------------
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();

    public const int SW_RESTORE = 9;

    /// <summary>Forcefully brings a window to the foreground (Windows blocks focus steal — this works around it).</summary>
    public static void ForceForeground(IntPtr hWnd)
    {
        try
        {
            var fg = GetForegroundWindow();
            uint foreThread = GetWindowThreadProcessId(fg, out _);
            uint curThread = GetCurrentThreadId();
            if (foreThread != curThread)
            {
                AttachThreadInput(curThread, foreThread, true);
                SetForegroundWindow(hWnd);
                AttachThreadInput(curThread, foreThread, false);
            }
            else
            {
                SetForegroundWindow(hWnd);
            }
        }
        catch { /* best effort */ }
    }

    // ---------------- tools ----------------
    [DllImport("user32.dll")] public static extern void LockWorkStation();

    [DllImport("powrprof.dll")] private static extern uint SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
    public static void SleepComputer() => SetSuspendState(false, false, false);

    private const int SHERB_NOCONFIRMATION = 0x1;
    private const int SHERB_NOPROGRESSUI = 0x2;
    private const int SHERB_NOSOUND = 0x4;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    public static void EmptyRecycleBin() => SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);

    // ---------------- window management (v1.6) ----------------
    public const int SW_MAXIMIZE = 3;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    public static bool TryGetWorkArea(IntPtr hWnd, out RECT work)
    {
        work = default;
        try
        {
            var mon = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero) return false;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi)) return false;
            work = mi.rcWork;
            return true;
        }
        catch { return false; }
    }

    // ---------------- glass backdrop (v1.7) ----------------
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMWCP_ROUND = 2;             // Win11 rounded window corners
    public const int DWMSBT_TRANSIENTWINDOW = 3;   // system acrylic (transient surfaces)

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public const int WCA_ACCENT_POLICY = 19;
    public const int ACCENT_DISABLED = 0;
    public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct ACCENT_POLICY
    {
        public int AccentState;
        public uint AccentFlags;
        public uint GradientColor;   // ABGR tint
        public uint AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    public static extern bool SetWindowCompositionAttribute(IntPtr hWnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    /// <summary>Best-effort window title for feedback messages.</summary>
    public static string GetTitle(IntPtr hWnd)
    {
        try
        {
            int len = GetWindowTextLength(hWnd);
            if (len <= 0 || len > 200) return "window";
            var sb = new System.Text.StringBuilder(len + 1);
            return GetWindowText(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : "window";
        }
        catch { return "window"; }
    }
}
