using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Lumo.Native;

/// <summary>
/// v1.7 — the glass (glassmorphism) backdrop behind the launcher window.
///
/// Strategy, best → fallback:
///   1. Windows 11 22H2+ (build ≥ 22621): DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW —
///      the real system acrylic, the same backdrop Spotlight-style popovers use.
///   2. Windows 10 / early Win11 (build ≥ 10240): SetWindowCompositionAttribute with
///      ACCENT_ENABLE_ACRYLICBLURBEHIND and a themed tint colour (ABGR).
///   3. Anything else (very old builds, remote sessions, drivers that refuse): no blur —
///      <see cref="Applied"/> stays false and the palette falls back to an opaque panel.
///
/// Requires the window to extend the DWM frame across its whole surface — that is done
/// in the XAML via <c>WindowChrome GlassFrameThickness="-1"</c> (window must NOT be a
/// layered window, i.e. AllowsTransparency must stay false for the blur to show).
/// </summary>
internal static class GlassBackdrop
{
    /// <summary>True when the last Apply() succeeded and blur is live behind the window.</summary>
    public static bool Applied { get; private set; }

    /// <summary>
    /// Applies (or removes) the glass backdrop. Safe to call repeatedly — Settings calls it
    /// live whenever the theme or the glass toggle changes.
    /// </summary>
    public static void Apply(Window window, bool dark, bool enabled)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
            if (hwnd == IntPtr.Zero) { Applied = false; return; }

            // rounded window corners (Win11; silently ignored on Win10)
            int round = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
            UpdateDarkMode(hwnd, dark);

            if (!enabled) { Disable(hwnd); Applied = false; return; }

            // 1) native system acrylic (Windows 11 22H2+)
            int build = Environment.OSVersion.Version.Build;
            if (build >= 22621)
            {
                int backdrop = NativeMethods.DWMSBT_TRANSIENTWINDOW;
                if (NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, 4) == 0)
                {
                    Applied = true;
                    return;
                }
            }

            // 2) composition-attribute acrylic (Windows 10 / early Win11)
            //    v1.8 — tint alpha lowered (~56%) so the blur reads much more strongly;
            //    the translucent glass palette on top keeps text readable.
            uint tint = dark ? 0x8F141414u : 0x96F7F7F9u; // ABGR — dark smoke / light frost
            Applied = SetAccent(hwnd, new NativeMethods.ACCENT_POLICY
            {
                AccentState = NativeMethods.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                GradientColor = tint,
            });
            if (!Applied) Disable(hwnd);
        }
        catch
        {
            Applied = false;
        }
    }

    /// <summary>Syncs the DWM dark-mode flag so the system acrylic tints with the theme.</summary>
    public static void UpdateDarkMode(IntPtr hwnd, bool dark)
    {
        try
        {
            int v = dark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, 4);
        }
        catch { /* best effort */ }
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

    private static void Disable(IntPtr hwnd)
    {
        SetAccent(hwnd, new NativeMethods.ACCENT_POLICY { AccentState = NativeMethods.ACCENT_DISABLED });
        try
        {
            int auto = 1; // DWMSBT_AUTO — none
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref auto, 4);
        }
        catch { }
    }
}
