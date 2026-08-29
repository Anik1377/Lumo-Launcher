using System.IO;

namespace Lumo.Core;

/// <summary>Central location for all on-disk paths used by Lumo.</summary>
public static class AppPaths
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumo");

    public static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lumo");

    public static readonly string LogFile = Path.Combine(DataDir, "log.txt");
    public static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");
    public static readonly string ShortcutsFile = Path.Combine(SettingsDir, "shortcuts.json");

    static AppPaths()
    {
        try { Directory.CreateDirectory(DataDir); } catch { /* ignore */ }
        try { Directory.CreateDirectory(SettingsDir); } catch { /* ignore */ }
    }
}
