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
    public static string UsageFile => Path.Combine(SettingsDir, "usage.json");          // v2.1 MRU
    public static string FavouritesFile => Path.Combine(SettingsDir, "favourites.json"); // v2.2 (Phase 2)
    public static string ChatsFile => Path.Combine(SettingsDir, "chats.json");           // v2.4.0-alpha.5 AI chat history
    public static string PersonasFile => Path.Combine(SettingsDir, "personas.json");     // v2.4.0-alpha.6 custom AI personas
    public static string PluginsDir => Path.Combine(SettingsDir, "plugins");             // v2.5 (Task 4.2) JSON plugins

    static AppPaths()
    {
        try { Directory.CreateDirectory(DataDir); } catch { /* ignore */ }
        try { Directory.CreateDirectory(SettingsDir); } catch { /* ignore */ }
    }
}
