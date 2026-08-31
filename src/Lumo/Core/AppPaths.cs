using System.IO;

namespace Lumo.Core;

/// <summary>
/// Central location for all on-disk paths used by Lumo.
///
/// v2.6 (DEV_PLAN Task 5.2) — PORTABLE DATA MODE: when a folder named "data"
/// exists next to Lumo.exe, every store (settings, shortcuts, chats, plugins,
/// usage, favourites, the log and staged update downloads) lives inside it and
/// travels with the exe — drop Lumo.exe + data/ on a USB stick and the whole
/// setup follows. Without that folder the classic %LOCALAPPDATA% (log) +
/// %APPDATA% (settings) locations are used, exactly as before. The data folder
/// is the opt-in switch: nothing is created next to the exe unless the user
/// (or the zip packager) put it there.
/// </summary>
public static class AppPaths
{
    // Static-initialization order matters: Roots is the FIRST field initializer,
    // so every path below (LogFile, SettingsFile, …) resolves against real roots.
    // (DataDir as a field assigned in the static ctor would leave it null while
    //  the later field initializers run.)
    private static readonly (bool Portable, string DataDir, string SettingsDir) Roots = ResolveRoots();

    /// <summary>True when portable mode is active (a "data" folder sits next to the exe).</summary>
    public static bool IsPortable => Roots.Portable;

    /// <summary>Diagnostics log root (also hosts staged update downloads).</summary>
    public static string DataDir => Roots.DataDir;

    /// <summary>JSON store root (settings.json, shortcuts.json, plugins\, …).</summary>
    public static string SettingsDir => Roots.SettingsDir;

    public static readonly string LogFile = Path.Combine(DataDir, "log.txt");
    public static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");
    public static readonly string ShortcutsFile = Path.Combine(SettingsDir, "shortcuts.json");
    public static string UsageFile => Path.Combine(SettingsDir, "usage.json");          // v2.1 MRU
    public static string FavouritesFile => Path.Combine(SettingsDir, "favourites.json"); // v2.2 (Phase 2)
    public static string ChatsFile => Path.Combine(SettingsDir, "chats.json");           // v2.4.0-alpha.5 AI chat history
    public static string PersonasFile => Path.Combine(SettingsDir, "personas.json");     // v2.4.0-alpha.6 custom AI personas
    public static string PluginsDir => Path.Combine(SettingsDir, "plugins");             // v2.5 (Task 4.2) JSON plugins

    /// <summary>Staged update downloads land here (v2.6 — Task 5.1).</summary>
    public static string UpdatesDir => Path.Combine(DataDir, "updates");

    static AppPaths()
    {
        try { Directory.CreateDirectory(DataDir); } catch { /* ignore */ }
        try { Directory.CreateDirectory(SettingsDir); } catch { /* ignore */ }
    }

    /// <summary>
    /// The portable decision, extracted for the test harness (which never has a
    /// "data" folder next to the testhost): a data dir next to the exe wins,
    /// otherwise the classic per-user AppData roots. Never throws.
    /// </summary>
    internal static (bool Portable, string DataDir, string SettingsDir) ResolveRoots(string? exeDir = null)
    {
        // Environment.ProcessPath is the real single-file exe path (AppContext.BaseDirectory
        // also works for framework-dependent bundles, but ProcessPath is the honest exe).
        try
        {
            exeDir ??= Environment.ProcessPath is { Length: > 0 } exe ? Path.GetDirectoryName(exe) : null;
            if (!string.IsNullOrWhiteSpace(exeDir))
            {
                var portable = Path.Combine(exeDir, "data");
                if (Directory.Exists(portable))
                    return (true, portable, portable);
            }
        }
        catch { /* fall through to AppData */ }

        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumo");
        var roaming = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lumo");
        return (false, local, roaming);
    }
}
