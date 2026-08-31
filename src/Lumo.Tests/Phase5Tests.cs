using System.IO;
using Lumo.Core;
using Lumo.Services;
using Xunit;

namespace Lumo.Tests;

// ------------------------------------------------------------------ v2.6 Task 5.1 — ReleaseVersion / UpdateCheck

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("2.6.0-alpha.1", "2.6.0-alpha.1")]
    [InlineData("v2.6.0-alpha.1", "2.6.0-alpha.1")]
    [InlineData("V2.6.0-ALPHA.3", "2.6.0-alpha.3")]   // case-insensitive prerelease
    [InlineData("2.6.0", "2.6.0")]                     // final (no prerelease)
    [InlineData("2.6", "2.6.0")]                       // two-part version
    [InlineData("3", "3.0.0")]
    [InlineData("2.6.0-alpha", "2.6.0-alpha.0")]       // bare -alpha → alpha.0
    [InlineData("2.6.0-alpha.1-x64", "2.6.0-alpha.1")] // extra tag junk after the number
    public void TryParse_Accepts_KnownShapes(string text, string expected)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var v));
        Assert.Equal(expected, v.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("alpha")]
    [InlineData("2..0")]
    [InlineData("x.y.z")]
    [InlineData("2.6.0.0.0")]
    public void TryParse_Rejects_Garbage(string text)
        => Assert.False(ReleaseVersion.TryParse(text, out _));

    [Fact]
    public void Ordering_AlphaN_Is_Numeric_Not_Lexicographic()
    {
        Assert.True(ReleaseVersion.TryParse("2.6.0-alpha.9", out var a9));
        Assert.True(ReleaseVersion.TryParse("2.6.0-alpha.10", out var a10));
        Assert.True(a9.CompareTo(a10) < 0, "alpha.9 must sort below alpha.10");
    }

    [Fact]
    public void Ordering_Final_Beats_Any_Prerelease()
    {
        Assert.True(ReleaseVersion.TryParse("2.6.0-alpha.9", out var pre));
        Assert.True(ReleaseVersion.TryParse("2.6.0", out var final));
        Assert.True(pre.CompareTo(final) < 0);
        Assert.True(final.CompareTo(pre) > 0);
    }

    [Fact]
    public void Ordering_Triples_First()
    {
        Assert.True(ReleaseVersion.TryParse("2.5.9", out var v259));
        Assert.True(ReleaseVersion.TryParse("2.6.0-alpha.1", out var v260a1));
        Assert.True(v259.CompareTo(v260a1) < 0, "2.5.9 < 2.6.0-alpha.1");
        Assert.True(ReleaseVersion.TryParse("2.6.1", out var v261));
        Assert.True(v261.CompareTo(v260a1) > 0, "2.6.1 > 2.6.0-alpha.1");
    }

    [Fact]
    public void Unknown_Prerelease_Tags_Sort_Below_Alphas()
    {
        Assert.True(ReleaseVersion.TryParse("2.6.0-beta.2", out var beta));
        Assert.True(ReleaseVersion.TryParse("2.6.0-alpha.1", out var alpha));
        Assert.True(beta.CompareTo(alpha) < 0);
        Assert.True(beta.CompareTo(ReleaseVersion.TryParse("2.6.0", out var f) ? f : default) < 0);
    }

    [Fact]
    public void RoundTrip_ToString()
    {
        Assert.True(ReleaseVersion.TryParse("v2.6.0-alpha.7", out var v));
        Assert.Equal("2.6.0-alpha.7", v.ToString());
        Assert.True(ReleaseVersion.TryParse("2.6.0", out var f));
        Assert.Equal("2.6.0", f.ToString());
    }

    [Fact]
    public void TagToVersion_StripsLeadingV()
    {
        Assert.Equal("2.6.0-alpha.1", UpdateCheck.TagToVersion("v2.6.0-alpha.1"));
        Assert.Equal("2.6.0-alpha.1", UpdateCheck.TagToVersion("2.6.0-alpha.1"));
        Assert.Equal("", UpdateCheck.TagToVersion(null));
        Assert.Equal("", UpdateCheck.TagToVersion("   "));
    }

    [Fact]
    public void IsNewer_Basics()
    {
        Assert.True(UpdateCheck.IsNewer("v2.6.0-alpha.2", "2.6.0-alpha.1"));
        Assert.True(UpdateCheck.IsNewer("v2.7.0", "2.6.0-alpha.9"));
        Assert.False(UpdateCheck.IsNewer("v2.6.0-alpha.1", "2.6.0-alpha.1"));
        Assert.False(UpdateCheck.IsNewer("v2.5.0", "2.6.0-alpha.1"));
        Assert.False(UpdateCheck.IsNewer(null, "2.6.0-alpha.1"));
        Assert.False(UpdateCheck.IsNewer("v2.6.0-alpha.2", "not a version"));
    }
}

public class UpdateCheckTests
{
    /// <summary>A trimmed /releases payload: an old draft, the running release,
    /// a newer release whose zip is buried under other assets, and a newer
    /// release with NO usable asset.</summary>
    private const string FixtureJson = """
    [
      {
        "tag_name": "v2.9.0-alpha.1",
        "name": "Lumo v2.9.0-alpha.1 — draft",
        "draft": true,
        "html_url": "https://github.com/Anik1377/Lumo-Launcher/releases/tag/v2.9.0-alpha.1",
        "assets": [
          { "name": "Lumo-launcher-v2.9.0-alpha.1.zip", "size": 1300000,
            "browser_download_url": "https://github.com/Anik1377/Lumo-Launcher/releases/download/v2.9.0-alpha.1/Lumo-launcher-v2.9.0-alpha.1.zip" }
        ]
      },
      {
        "tag_name": "v2.6.0-alpha.2",
        "name": "Lumo v2.6.0-alpha.2 — ALPHA (unstable)",
        "draft": false,
        "html_url": "https://github.com/Anik1377/Lumo-Launcher/releases/tag/v2.6.0-alpha.2",
        "assets": [
          { "name": "README.md", "size": 900, "browser_download_url": "https://example.com/README.md" },
          { "name": "Source code (zip)", "size": 999, "browser_download_url": "https://github.com/archive.zip" },
          { "name": "Lumo-launcher-v2.6.0-alpha.2.zip", "size": 1340000,
            "browser_download_url": "https://github.com/Anik1377/Lumo-Launcher/releases/download/v2.6.0-alpha.2/Lumo-launcher-v2.6.0-alpha.2.zip" }
        ]
      },
      {
        "tag_name": "v2.7.0-alpha.1",
        "name": "Lumo v2.7.0-alpha.1 — no zip yet",
        "draft": false,
        "html_url": "https://github.com/Anik1377/Lumo-Launcher/releases/tag/v2.7.0-alpha.1",
        "assets": [
          { "name": "notes.txt", "size": 10, "browser_download_url": "https://example.com/notes.txt" }
        ]
      },
      {
        "tag_name": "v2.6.0-alpha.1",
        "name": "Lumo v2.6.0-alpha.1 — running",
        "draft": false,
        "html_url": "https://github.com/Anik1377/Lumo-Launcher/releases/tag/v2.6.0-alpha.1",
        "assets": [
          { "name": "Lumo-launcher-v2.6.0-alpha.1.zip", "size": 1339000,
            "browser_download_url": "https://github.com/Anik1377/Lumo-Launcher/releases/download/v2.6.0-alpha.1/Lumo-launcher-v2.6.0-alpha.1.zip" }
        ]
      }
    ]
    """;

    [Fact]
    public void SelectNewest_Picks_Newest_NonDraft_WithZipAsset()
    {
        var info = UpdateCheck.SelectNewest(FixtureJson, "2.6.0-alpha.1");
        Assert.NotNull(info);
        Assert.Equal("2.6.0-alpha.2", info!.Version);          // draft skipped; 2.7 skipped (no zip)
        Assert.EndsWith("Lumo-launcher-v2.6.0-alpha.2.zip", info.ZipUrl);
        Assert.Equal(1_340_000, info.ZipBytes);
        Assert.Contains("v2.6.0-alpha.2", info.HtmlUrl);
        Assert.Equal("Lumo v2.6.0-alpha.2 — ALPHA (unstable)", info.ReleaseName);
    }

    [Fact]
    public void SelectNewest_ReturnsNull_WhenNothingNewer()
    {
        Assert.Null(UpdateCheck.SelectNewest(FixtureJson, "2.6.0-alpha.2"));
        Assert.Null(UpdateCheck.SelectNewest(FixtureJson, "3.0.0"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html>rate limited</html>")]
    [InlineData("{\"message\":\"Not Found\"}")]      // object, not array
    [InlineData("[{\"garbage\":true}]")]              // no tag_name at all
    public void SelectNewest_Tolerates_BadPayloads(string? json)
        => Assert.Null(UpdateCheck.SelectNewest(json, "2.6.0-alpha.1"));

    [Fact]
    public void SelectNewest_ComparesFullVersions_AcrossMixedTags()
    {
        // 2.7.1-alpha.9 outranks the final 2.7.0 because 2.7.1 > 2.7.0 —
        // the triple compare wins BEFORE any prerelease logic kicks in.
        const string json = """
        [
          { "tag_name": "v2.7.1-alpha.9", "draft": false, "assets": [ { "name": "Lumo-launcher-v2.7.1-alpha.9.zip", "size": 1,
              "browser_download_url": "https://x/Lumo-launcher-v2.7.1-alpha.9.zip" } ] },
          { "tag_name": "v2.7.0", "draft": false, "assets": [ { "name": "Lumo-launcher-v2.7.0.zip", "size": 2,
              "browser_download_url": "https://x/Lumo-launcher-v2.7.0.zip" } ] }
        ]
        """;
        var info = UpdateCheck.SelectNewest(json, "2.6.0-alpha.1");
        Assert.NotNull(info);
        Assert.Equal("2.7.1-alpha.9", info!.Version);
    }
}

// ------------------------------------------------------------------ v2.6 Task 5.1 — AutoCheckDue + Settings round-trip

public class UpdateServicePolicyTests
{
    private static Settings Enabled() => new() { UpdatesEnabled = true, LastUpdateCheckUtc = "" };

    [Fact]
    public void AutoCheckDue_FirstRun_AlwaysTrue()
    {
        Assert.True(UpdateService.AutoCheckDue(Enabled(), DateTimeOffset.UtcNow));
        Assert.True(UpdateService.AutoCheckDue(Enabled(), DateTimeOffset.UtcNow.AddDays(30)));
    }

    [Fact]
    public void AutoCheckDue_Disabled_NeverTrue()
    {
        var s = Enabled();
        s.UpdatesEnabled = false;
        Assert.False(UpdateService.AutoCheckDue(s, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AutoCheckDue_Within24h_False_After24h_True()
    {
        var now = DateTimeOffset.Parse("2026-08-31T10:00:00Z");
        var s = Enabled();
        s.LastUpdateCheckUtc = "2026-08-30T10:00:01Z";
        Assert.False(UpdateService.AutoCheckDue(s, now));
        s.LastUpdateCheckUtc = "2026-08-30T09:59:59Z";
        Assert.True(UpdateService.AutoCheckDue(s, now));
    }

    [Fact]
    public void AutoCheckDue_CorruptStamp_FallsBackToCheck()
    {
        var s = Enabled();
        s.LastUpdateCheckUtc = "not-a-date";
        Assert.True(UpdateService.AutoCheckDue(s, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Settings_NewFields_JsonRoundTrip()
    {
        var s = new Settings
        {
            FirstRunDone = true,
            UpdatesEnabled = false,
            LastUpdateCheckUtc = "2026-08-31T09:00:00.0000000Z",
        };
        var clone = s.Clone();
        Assert.True(clone.FirstRunDone);
        Assert.False(clone.UpdatesEnabled);
        Assert.Equal(s.LastUpdateCheckUtc, clone.LastUpdateCheckUtc);

        var restored = new Settings();
        restored.RestoreFrom(s);
        Assert.True(restored.FirstRunDone);
        Assert.False(restored.UpdatesEnabled);
        Assert.Equal(s.LastUpdateCheckUtc, restored.LastUpdateCheckUtc);
    }

    [Fact]
    public void Settings_TolerantRead_BadTypes_FallBack()
    {
        const string json = """
        {
          "FirstRunDone": 17,
          "UpdatesEnabled": "yes",
          "LastUpdateCheckUtc": { "oops": true }
        }
        """;
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var s = new Settings();
        Settings.ApplyJson(s, doc.RootElement);

        Assert.True(s.FirstRunDone);      // GetBool tolerates hand-edited 1/0 numbers → 17 is truthy
        Assert.True(s.UpdatesEnabled);    // "yes" is not a parseable bool string → default (true) kept
        Assert.Equal("", s.LastUpdateCheckUtc);   // object where a string belongs → default kept
    }
}

// ------------------------------------------------------------------ v2.6 Task 5.2 — portable data mode

public class PortableDataTests : IDisposable
{
    private readonly string _root;

    public PortableDataTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "lumo-p5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void ResolveRoots_WithoutDataDir_FallsBackToAppData()
    {
        var (portable, data, settings) = AppPaths.ResolveRoots(_root);
        Assert.False(portable);
        Assert.NotEqual(Path.Combine(_root, "data"), data);
        Assert.EndsWith("Lumo", data);
        Assert.EndsWith("Lumo", settings);
    }

    [Fact]
    public void ResolveRoots_WithDataDir_IsPortable_AndAllStoresCollapseIntoIt()
    {
        var dataDir = Path.Combine(_root, "data");
        Directory.CreateDirectory(dataDir);

        var (portable, data, settings) = AppPaths.ResolveRoots(_root);
        Assert.True(portable);
        Assert.Equal(dataDir, data);
        Assert.Equal(dataDir, settings);   // one root — everything collapses into data/
        Assert.Equal(Path.Combine(dataDir, "settings.json"), Path.Combine(settings, "settings.json"));
    }

    [Fact]
    public void UpdatesDir_HangsOffDataDir_SoPortableCarriesIt()
    {
        // static-shape sanity: the staged-update home derives from DataDir,
        // so portable mode (DataDir == exe\data) carries it automatically
        Assert.StartsWith(AppPaths.DataDir, AppPaths.UpdatesDir);
        Assert.EndsWith("updates", AppPaths.UpdatesDir);
    }

    [Fact]
    public void ResolveRoots_WeirdExeDir_NeverThrows()
    {
        var (portable, _, _) = AppPaths.ResolveRoots("Z:\\does\\not\\exist\\at\\all");
        Assert.False(portable);   // missing dir → classic AppData roots, no exception
    }
}
