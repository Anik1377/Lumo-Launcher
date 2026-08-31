namespace Lumo.Core;

/// <summary>
/// One newer release, normalized out of the GitHub Releases API (v2.6 — Task 5.1).
/// </summary>
public sealed record UpdateInfo(
    string Version,        // "2.6.0-alpha.2" — tag with a leading v stripped
    string ZipUrl,         // browser_download_url of the Lumo zip asset
    long ZipBytes,         // asset size (also the sanity cap during download)
    string HtmlUrl,        // release page (shown as "read the notes")
    string ReleaseName);   // GitHub release title ("Lumo v2.6.0-alpha.2 — ALPHA (unstable)")

/// <summary>
/// SemVer-ish version with an optional Lumo prerelease suffix, fully comparable.
/// "2.6.0-alpha.1" · "v2.6.0-alpha.1" · "2.6.0" · "2.6" all parse; ordering is
/// numeric triples first, then: final release &gt; any prerelease, and
/// "-alpha.N" compares numerically (alpha.9 &lt; alpha.10 — never lexicographically).
/// Unknown prerelease tags (beta, rc, anything) sort below every alpha.N.
/// </summary>
/// <remarks>
/// <para>Pre is a sortable ordinal, not the raw alpha number: int.MaxValue = final
/// release, N+1 = alpha.N (so alpha.0 ≠ the final sentinel), 0 = an unrecognized
/// prerelease tag. Comparison is then a plain integer compare, giving
/// final &gt; alpha.N &gt; unknown-prerelease for free.</para>
/// </remarks>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, int Pre) : IComparable<ReleaseVersion>
{
    private const int Final = int.MaxValue;
    private const int UnknownPre = 0;

    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().TrimStart('v', 'V');

        int pre = Final;
        var dash = t.IndexOf('-');
        if (dash >= 0)
        {
            var suffix = t[(dash + 1)..];
            t = t[..dash];
            // "alpha.1" / "alpha1" / "alpha.1-x64" → the first digit run; any other
            // tag (beta/rc/…) is still a prerelease, just with an unknown ordinal.
            if (suffix.StartsWith("alpha", StringComparison.OrdinalIgnoreCase))
            {
                var digits = suffix["alpha".Length..].TrimStart('.', '_', '-');
                int i = 0;
                while (i < digits.Length && char.IsAsciiDigit(digits[i])) i++;
                pre = i > 0 && int.TryParse(digits[..i], out var n)
                    ? Math.Min(n, int.MaxValue - 2) + 1      // never collide with Final (int.MaxValue)
                    : 1;                                     // bare "-alpha" → alpha.0
            }
            else pre = UnknownPre;
        }

        var parts = t.Split('.');
        if (parts.Length is < 1 or > 3) return false;
        if (parts.Any(p => p.Length == 0)) return false;   // "2..0" style garbage
        if (!int.TryParse(parts[0], out var major) || major < 0) return false;

        int minor = 0, patch = 0;
        if (parts.Length >= 2 && (!int.TryParse(parts[1], out minor) || minor < 0)) return false;
        if (parts.Length >= 3 && (!int.TryParse(parts[2], out patch) || patch < 0)) return false;

        version = new ReleaseVersion(major, minor, patch, pre);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        if (Patch != other.Patch) return Patch.CompareTo(other.Patch);
        return Pre.CompareTo(other.Pre);   // final(int.MaxValue) > alpha.N ≥ 1 > unknown(0)
    }

    public override string ToString()
    {
        var core = $"{Major}.{Minor}.{Patch}";
        return Pre switch
        {
            Final => core,
            UnknownPre => core + "-pre",
            var p => $"{core}-alpha.{p - 1}",
        };
    }
}

/// <summary>
/// Pure GitHub-Releases logic for the auto-update service (v2.6 — Task 5.1).
/// Every /releases entry is a prerelease on this project (ALPHA policy), so the
/// plain "latest" endpoint — which excludes prereleases — can never be used;
/// instead a page of releases is fetched and the NEWEST parseable, non-draft
/// entry that carries a Lumo zip asset wins. All functions are tolerant: bad
/// JSON, missing fields and alien assets yield null, never exceptions.
/// </summary>
public static class UpdateCheck
{
    /// <summary>List endpoint (NOT /latest — every Lumo release is a prerelease).</summary>
    public const string ReleasesApiUrl =
        "https://api.github.com/repos/Anik1377/Lumo-Launcher/releases?per_page=8";

    /// <summary>Hard cap for a staged download — the real zip is ≈ 1.3 MB; anything
    /// past this is a proxy error page or worse and is rejected mid-stream.</summary>
    public const long MaxZipBytes = 80L * 1024 * 1024;

    /// <summary>"v2.6.0-alpha.1" → "2.6.0-alpha.1"; already-bare tags pass through.</summary>
    public static string TagToVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return "";
        return tag.Trim().StartsWith('v') || tag.Trim().StartsWith('V')
            ? tag.Trim()[1..]
            : tag.Trim();
    }

    /// <summary>True when <paramref name="candidate"/> is strictly newer than current.</summary>
    public static bool IsNewer(string? candidate, string? current) =>
        ReleaseVersion.TryParse(candidate, out var c) &&
        ReleaseVersion.TryParse(current, out var cur) &&
        c.CompareTo(cur) > 0;

    /// <summary>
    /// Parse a GitHub /releases JSON array and pick the newest release newer than
    /// <paramref name="currentVersion"/> that ships a "Lumo…zip" asset.
    /// Returns null when the payload is unusable, nothing qualifies, or nothing
    /// is newer (the overwhelmingly common case).
    /// </summary>
    public static UpdateInfo? SelectNewest(string? json, string? currentVersion)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return null;

            UpdateInfo? best = null;
            var bestVersion = new ReleaseVersion();
            ReleaseVersion.TryParse(currentVersion, out var current);

            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                if (rel.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                if (TryBool(rel, "draft")) continue;                        // drafts never ship zips users should take
                var tag = TryStr(rel, "tag_name");
                if (!ReleaseVersion.TryParse(TagToVersion(tag), out var ver)) continue;

                if (!TryAsset(rel, out var zipUrl, out var zipBytes)) continue;   // no usable zip asset

                if (ver.CompareTo(current) <= 0) continue;                  // not newer than what's running
                if (best is not null && ver.CompareTo(bestVersion) <= 0) continue;

                bestVersion = ver;
                best = new UpdateInfo(
                    Version: ver.ToString(),
                    ZipUrl: zipUrl,
                    ZipBytes: zipBytes,
                    HtmlUrl: TryStr(rel, "html_url") ?? "",
                    ReleaseName: TryStr(rel, "name") ?? TryStr(rel, "tag_name") ?? ver.ToString());
            }
            return best;
        }
        catch
        {
            return null;   // tolerant: a proxy HTML page or rate-limit body is just "no update"
        }
    }

    // ---------------------------------------------------------------- tolerant JSON helpers

    private static string? TryStr(System.Text.Json.JsonElement obj, string name)
    {
        try
        {
            if (obj.ValueKind == System.Text.Json.JsonValueKind.Object &&
                obj.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
                return v.GetString();
        }
        catch { }
        return null;
    }

    private static bool TryBool(System.Text.Json.JsonElement obj, string name)
    {
        try
        {
            return obj.ValueKind == System.Text.Json.JsonValueKind.Object &&
                   obj.TryGetProperty(name, out var v) &&
                   v.ValueKind == System.Text.Json.JsonValueKind.True;
        }
        catch { return false; }
    }

    /// <summary>Finds the release's Lumo zip asset — the CI-named
    /// "Lumo-launcher-v…zip" (case-insensitive "lumo" + ".zip"), skipping
    /// source-code auto-assets and anything else.</summary>
    private static bool TryAsset(System.Text.Json.JsonElement release, out string url, out long bytes)
    {
        url = ""; bytes = 0;
        try
        {
            if (!release.TryGetProperty("assets", out var assets) ||
                assets.ValueKind != System.Text.Json.JsonValueKind.Array)
                return false;

            foreach (var a in assets.EnumerateArray())
            {
                var name = TryStr(a, "name") ?? "";
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                if (!name.Contains("lumo", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.StartsWith("source", StringComparison.OrdinalIgnoreCase)) continue;
                var u = TryStr(a, "browser_download_url");
                if (string.IsNullOrWhiteSpace(u)) continue;
                long size = 0;
                try
                {
                    if (a.TryGetProperty("size", out var sz) && sz.ValueKind == System.Text.Json.JsonValueKind.Number)
                        size = sz.GetInt64();
                }
                catch { }
                url = u; bytes = size;
                return true;
            }
        }
        catch { }
        return false;
    }
}
