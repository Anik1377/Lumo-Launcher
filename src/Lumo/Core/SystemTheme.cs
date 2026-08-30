using Microsoft.Win32;

namespace Lumo.Core;

/// <summary>
/// v2.1 — pure, OS-level theme probe (extracted from Appearance so the test
/// harness can compile Settings without WPF types).
/// Reads the Windows personalization setting (Settings → Personalization →
/// Colors → "Choose your mode"). Returns true when Windows apps are dark.
/// </summary>
public static class SystemTheme
{
    public static bool IsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v)
                return v == 0;
        }
        catch { }
        return true; // default to dark — Lumo's signature look
    }
}
