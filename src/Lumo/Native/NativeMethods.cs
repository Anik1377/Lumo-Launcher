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
}
