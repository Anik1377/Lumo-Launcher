using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Lumo.Native;

/// <summary>
/// v2.4.0-alpha.2 — window chrome helper, frosted-glass edition.
///
/// The launcher regains the Raycast material: a real DWM acrylic blur-behind
/// (SetWindowCompositionAttribute → ACCENT_ENABLE_ACRYLICBLURBEHIND) under a
/// translucent panel brush, so the frosted desktop shows through the card —
/// the signature Raycast depth, natively. Settings and AI chat stay on solid
/// Fluent surfaces (typing-heavy windows read better without live blur).
///
/// The helper keeps the two pieces of DWM chrome every window needs:
///   • DWMWA_WINDOW_CORNER_PREFERENCE = ROUND  — rounded window corners
///     (silently ignored on Windows 10, where the card keeps square corners).
///   • DWMWA_USE_IMMERSIVE_DARK_MODE — dark title-bar/decoration context so
///     system brushes match the active theme.
/// </summary>
internal static class GlassBackdrop
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_REMOTESESSION = 0x1000;

    private static bool IsRemoteSession
    {
        get { try { return GetSystemMetrics(SM_REMOTESESSION) != 0; } catch { return false; } }
    }

    /// <summary>Windows 11 or newer (rounded corners + modern chrome available).</summary>
    public static bool IsWin11 => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>
    /// Minimum build for trustworthy acrylic via SetWindowCompositionAttribute
    /// (Windows 10 April 2018 Update). Older builds render black edges on
    /// chromeless windows, so they keep the solid palette.
    /// </summary>
    public static bool IsAcrylicSafeBuild => Environment.OSVersion.Version.Build >= 17134;

    /// <summary>True when the LAST Apply call enabled acrylic on that window.</summary>
    public static bool Applied { get; private set; }

    /// <summary>
    /// Applies DWM rounding + dark-mode context, and — when <paramref name="acrylic"/>
    /// is requested and the platform allows it — the acrylic blur-behind.
    /// Returns true when acrylic is actually active (the caller then paints the
    /// window surface translucent so the frosted desktop shows through).
    /// Safe to call repeatedly — windows call it on theme changes.
    /// </summary>
    public static bool Apply(Window window, bool dark, bool acrylic = false)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
            if (hwnd == IntPtr.Zero) { Applied = false; return false; }

            // rounded window corners (Win11; silently ignored on Win10)
            int round = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
            UpdateDarkMode(hwnd, dark);

            bool enabled = acrylic && IsAcrylicSafeBuild && !IsRemoteSession
                           && EnableAcrylic(hwnd, dark);
            if (!enabled)
                Disable(hwnd);
            Applied = enabled;
            return enabled;
        }
        catch
        {
            Applied = false;
            return false;
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

    /// <summary>
    /// Enables the acrylic blur-behind with a barely-there tint of the panel colour —
    /// the frost itself. The window's WPF surface paints the readable part on top
    /// (translucent panel), so the combined material lands around Raycast's
    /// rgba(28,28,30,0.9) over a live desktop blur.
    /// GradientColor is 0xAABBGGRR (ABGR) — pack from the WPF colour accordingly.
    /// </summary>
    private static bool EnableAcrylic(IntPtr hwnd, bool dark)
    {
        try
        {
            // #0E0F12 dark / #FAFAFB light — the panel tier of the ladder
            Color tint = dark ? Color.FromRgb(0x0E, 0x0F, 0x12) : Color.FromRgb(0xFA, 0xFA, 0xFB);
            uint abgr = (uint)(0x40u << 24 | (uint)tint.B << 16 | (uint)tint.G << 8 | tint.R);
            var policy = new NativeMethods.ACCENT_POLICY
            {
                AccentState = NativeMethods.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,          // draw all borders off — the card paints its own hairline
                GradientColor = abgr,
                AnimationId = 0,
            };
            return SetAccent(hwnd, policy);
        }
        catch { return false; }
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
