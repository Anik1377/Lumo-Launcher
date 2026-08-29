using Lumo.Native;
using Lumo.Services;

namespace Lumo.Core;

public enum WindowMode { Left, Right, Maximize, Center, Restore }

/// <summary>
/// v1.6 — Raycast-style window management for the window the user was just in.
/// ActivateLauncher remembers the foreground window right before Lumo takes focus,
/// so "Left half" snaps THAT window, never Lumo itself. Multi-monitor aware
/// (snaps into the work area of the window's own monitor).
/// </summary>
internal static class WindowManager
{
    private static IntPtr _target;

    /// <summary>Call before the launcher steals focus. Keeps the previous target
    /// if the current foreground is Lumo itself (e.g. hotkey toggle while visible).</summary>
    public static void RememberForeground(IntPtr ownHwnd)
    {
        try
        {
            var fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == ownHwnd) return;
            if (!NativeMethods.IsWindow(fg) || !NativeMethods.IsWindowVisible(fg)) return;
            if ((NativeMethods.GetWindowLong(fg, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) != 0) return;
            _target = fg;
        }
        catch { /* best effort */ }
    }

    /// <summary>Applies a window command. Returns null on success or a human-readable error.</summary>
    public static string? Apply(WindowMode mode)
    {
        try
        {
            IntPtr h = _target;
            if (h == IntPtr.Zero || !NativeMethods.IsWindow(h) || !NativeMethods.IsWindowVisible(h))
                return "No window to arrange — open Lumo while the app you want to snap is focused";

            string title = NativeMethods.GetTitle(h);
            if (title.Length > 42) title = title[..42] + "…";

            switch (mode)
            {
                case WindowMode.Maximize:
                    NativeMethods.ShowWindow(h, NativeMethods.SW_MAXIMIZE);
                    return null;

                case WindowMode.Restore:
                    NativeMethods.ShowWindow(h, NativeMethods.SW_RESTORE);
                    return null;
            }

            // moving a maximized window first restores it, otherwise SetWindowPos is ignored
            if (NativeMethods.IsZoomed(h)) NativeMethods.ShowWindow(h, NativeMethods.SW_RESTORE);

            if (!NativeMethods.TryGetWorkArea(h, out var work))
                return "Couldn't read the work area for this window";

            int waW = work.Right - work.Left;
            int waH = work.Bottom - work.Top;

            switch (mode)
            {
                case WindowMode.Left:
                    NativeMethods.SetWindowPos(h, IntPtr.Zero, work.Left, work.Top, waW / 2, waH,
                        NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                    break;

                case WindowMode.Right:
                    NativeMethods.SetWindowPos(h, IntPtr.Zero, work.Left + waW / 2, work.Top,
                        waW - waW / 2, waH, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                    break;

                case WindowMode.Center:
                    {
                        if (!NativeMethods.GetWindowRect(h, out var r)) return "Couldn't read the window size";
                        int w = Math.Min(r.Right - r.Left, waW);
                        int ht = Math.Min(r.Bottom - r.Top, waH);
                        NativeMethods.SetWindowPos(h, IntPtr.Zero,
                            work.Left + (waW - w) / 2, work.Top + (waH - ht) / 2, w, ht,
                            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                        break;
                    }
            }
            return null;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("WindowManager", ex);
            return "Window command failed — see the log for details";
        }
    }
}
