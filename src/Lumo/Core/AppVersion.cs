using System.Reflection;

namespace Lumo.Core;

/// <summary>
/// One source of truth for the DISPLAY version (v2.4.0-alpha.7).
///
/// InformationalVersion in Lumo.csproj ("Lumo 2.4.0-alpha.7 (ALPHA — unstable build)")
/// is the only string that needs bumping per release; every user-visible version label
/// derives from it now. Before this, the tray tooltip said "Lumo v1.4", the hotkey
/// tooltip hardcoded "v2.1.0" and Settings truncated to "v2.4" — all drifted apart.
/// </summary>
public static class AppVersion
{
    /// <summary>Version label without a leading "v" — e.g. "2.4.0-alpha.7".</summary>
    public static string Label { get; } = FromInformational(
        typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>Shown when the attribute is missing (e.g. the pure net8.0 test harness).</summary>
    public const string Fallback = "2.4.0";

    /// <summary>
    /// Pure: pulls the first whitespace-separated token that starts with an ASCII digit
    /// out of "Lumo 2.4.0-alpha.7 (ALPHA — unstable build)" → "2.4.0-alpha.7".
    /// A token grab (not a regex like \d+(\.\d+)+) is deliberate — a regex would
    /// truncate the "-alpha.N" prerelease suffix at the first hyphen.
    /// </summary>
    public static string FromInformational(string? informational)
    {
        if (!string.IsNullOrWhiteSpace(informational))
        {
            foreach (var token in informational.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length > 0 && char.IsAsciiDigit(token[0]))
                    return token;
            }
        }
        return Fallback;
    }
}
