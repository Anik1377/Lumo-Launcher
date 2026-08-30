using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Lumo.Native;

/// <summary>
/// v2.0 — window chrome helper (formerly the acrylic glass backdrop).
///
/// The glassmorphism era is over: Lumo now uses solid Windows 11 Fluent surfaces, so
/// there is no acrylic to apply. This helper keeps the two pieces of DWM chrome that
/// make the launcher and settings window feel native on Windows 11:
///   • DWMWA_WINDOW_CORNER_PREFERENCE = ROUND  — the 8 px rounded window corners
///     (silently ignored on Windows 10, where the card keeps square corners).
///   • DWMWA_USE_IMMERSIVE_DARK_MODE — dark title-bar/decoration context so system
///     brushes match the active theme.
/// <see cref="Applied"/> now always reports false; remaining call sites treat that as
/// "use the solid palette", which is the only look in v2.0.
/// </summary>
internal static class GlassBackdrop
{
    /// <summary>Windows 11 or newer (rounded corners + modern chrome available).</summary>
    public static bool IsWin11 => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>Legacy flag — always false since v2.0 (no acrylic blur is applied).</summary>
    public static bool Applied { get; private set; }

    /// <summary>
    /// Applies DWM rounding + dark-mode context. Safe to call repeatedly — both windows
    /// call it on theme changes. The <c>enabled</c> parameter is kept for source
    /// compatibility with existing call sites and is ignored.
    /// </summary>
    public static void Apply(Window window, bool dark, bool enabled = true)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
            if (hwnd == IntPtr.Zero) { Applied = false; return; }

            // rounded window corners (Win11; silently ignored on Win10)
            int round = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
            UpdateDarkMode(hwnd, dark);

            // belt & braces: make sure no stale acrylic from a pre-2.0 settings file
            // (or an old running instance's window state) is left behind
            Disable(hwnd);
            Applied = false;
        }
        catch
        {
            Applied = false;
        }
    }

    /// <summary>Syncs the DWM dark-mode flag so system decoration tints with the theme.</summary>
    public static void UpdateDarkMode(IntPtr hwnd, bool dark)
    {
        try
        {
            int v = dark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, 4);
        }
        catch { /* best effort */ }
    }

    private static void Disable(IntPtr hwnd)
    {
        try
        {
            SetAccent(hwnd, new NativeMethods.ACCENT_POLICY { AccentState = NativeMethods.ACCENT_DISABLED });
            int auto = 1; // DWMSBT_AUTO — no system backdrop
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref auto, 4);
        }
        catch { }
    }

    private static bool SetAccent(IntPtr hwnd, NativeMethods.ACCENT_POLICY policy)
    {
        IntPtr ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.ACCENT_POLICY>());
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new NativeMethods.WINDOWCOMPOSITIONATTRIBDATA
            {
                Attribute = NativeMethods.WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = Marshal.SizeOf<NativeMethods.ACCENT_POLICY>(),
            };
            return NativeMethods.SetWindowCompositionAttribute(hwnd, ref data);
        }
        catch { return false; }
        finally { if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr); }
    }
}
