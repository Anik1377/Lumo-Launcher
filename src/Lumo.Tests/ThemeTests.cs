using Lumo.Core;
using Xunit;

namespace Lumo.Tests;

/// <summary>
/// v3.0 — the theme system: file format (import/export), the preset catalog and
/// the resolution chain (custom import &gt; preset &gt; legacy pair). All pure.
/// </summary>
public class ThemeTests
{
    // ------------------------------------------------------------ ThemeFile parse

    [Fact]
    public void Parse_ValidTheme_WithColors()
    {
        var json = """
        {
          "schema": "lumo.theme/1",
          "name": "My Theme",
          "mode": "light",
          "accent": "#D97757",
          "colors": { "panel": "#FAF9F5", "border": "#E5E2D9", "junk": "nope", "title": "not-a-color" }
        }
        """;
        var ok = ThemeFile.TryParse(json, out var theme, out var error);
        Assert.True(ok, error);
        Assert.NotNull(theme);
        Assert.Equal("My Theme", theme!.Name);
        Assert.False(theme.Dark);
        Assert.Equal("#D97757", theme.Accent);
        Assert.Equal(2, theme.Colors.Count);          // unknown keys + bad hex dropped
        Assert.Equal("#FAF9F5", theme.Colors["panel"]);
    }

    [Fact]
    public void Parse_StructuralGarbage_FailsWithReadableError()
    {
        Assert.False(ThemeFile.TryParse("not json at all", out _, out var error1));
        Assert.Contains("JSON", error1);

        Assert.False(ThemeFile.TryParse("{\"name\":\"no schema\"}", out _, out var error2));
        Assert.Contains("lumo.theme/1", error2);

        Assert.False(ThemeFile.TryParse("", out _, out var error3));
        Assert.NotNull(error3);
    }

    [Fact]
    public void Parse_Defaults_ModeDark_AccentRaycast()
    {
        var ok = ThemeFile.TryParse("""{"schema":"lumo.theme/1"}""", out var theme, out _);
        Assert.True(ok);
        Assert.NotNull(theme);
        Assert.True(theme!.Dark);
        Assert.Equal("#FF6363", theme.Accent);
        Assert.Equal("Imported theme", theme.Name);
    }

    [Fact]
    public void Serialize_RoundTrip_KeepsEverything()
    {
        var theme = new ThemeFile("Round Trip", false, "#88C0D0",
            new Dictionary<string, string> { ["panel"] = "#2E3440", ["title"] = "#ECEFF4" });
        var ok = ThemeFile.TryParse(theme.Serialize(), out var parsed, out _);
        Assert.True(ok);
        Assert.NotNull(parsed);
        Assert.Equal(theme.Name, parsed!.Name);
        Assert.Equal(theme.Dark, parsed.Dark);
        Assert.Equal(theme.Accent, parsed.Accent);
        Assert.Equal(theme.Colors.Count, parsed.Colors.Count);
        Assert.Equal(theme.Colors["panel"], parsed.Colors["panel"]);
    }

    [Fact]
    public void Name_Truncated_To40Chars()
    {
        var longName = new string('x', 120);
        var ok = ThemeFile.TryParse($$"""{"schema":"lumo.theme/1","name":"{{longName}}"}""", out var theme, out _);
        Assert.True(ok);
        Assert.Equal(40, theme!.Name.Length);
    }

    [Fact]
    public void HexValidation_Strict()
    {
        Assert.True(ThemeFile.IsValidHex("#FF6363"));
        Assert.True(ThemeFile.IsValidHex("#88c0d0"));
        Assert.True(ThemeFile.IsValidHex("#AAFF6363"));   // 8-digit accepted
        Assert.False(ThemeFile.IsValidHex("FF6363"));     // no #
        Assert.False(ThemeFile.IsValidHex("#FFF"));       // short form rejected
        Assert.False(ThemeFile.IsValidHex("#GG6363"));
        Assert.False(ThemeFile.IsValidHex(""));
        Assert.False(ThemeFile.IsValidHex(null));
    }

    [Fact]
    public void Slug_FilesystemSafe()
    {
        Assert.Equal("claude-dusk", ThemeFile.Slug("Claude Dusk"));
        Assert.Equal("my-theme-v2", ThemeFile.Slug("My  Theme / v2?"));
        Assert.Equal("theme", ThemeFile.Slug("!!!"));
        Assert.True(ThemeFile.Slug(new string('z', 100)).Length <= 40);
    }

    [Fact]
    public void ColorKeys_CoverTheTokenSet()
    {
        // The painter's override surface — pinned so a token rename is a conscious act.
        // All lowercase: the parser folds incoming JSON keys before this check.
        Assert.Contains("panel", ThemeFile.ColorKeys);
        Assert.Contains("field", ThemeFile.ColorKeys);
        Assert.Contains("selstroke", ThemeFile.ColorKeys);
        Assert.Contains("userbubble", ThemeFile.ColorKeys);
        Assert.Equal(ThemeFile.ColorKeys.Length, ThemeFile.ColorKeys.Select(k => k.ToLowerInvariant()).Distinct().Count());
        Assert.All(ThemeFile.ColorKeys, k => Assert.Equal(k, k.ToLowerInvariant()));
        Assert.All(ThemeFile.ColorKeys, k => Assert.False(string.IsNullOrWhiteSpace(k)));
    }

    // ------------------------------------------------------------ preset catalog

    [Fact]
    public void Presets_UniqueIds_KnownModes_ParseableAccents()
    {
        var ids = ThemeSelect.Presets.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(ids.Count >= 6);
        Assert.True(ThemeSelect.Presets.Any(p => p.Dark), "at least one dark preset");
        Assert.True(ThemeSelect.Presets.Any(p => !p.Dark), "at least one light preset");
        Assert.All(ThemeSelect.Presets, p => Assert.True(ThemeFile.IsValidHex(p.Accent)));
        Assert.All(ThemeSelect.Presets, p => Assert.NotNull(ThemeSelect.FindPreset(p.Id)));
    }

    [Fact]
    public void FindPreset_CaseInsensitive_MissReturnsNull()
    {
        Assert.NotNull(ThemeSelect.FindPreset("NORD"));
        Assert.NotNull(ThemeSelect.FindPreset("lumo-dark"));
        Assert.Null(ThemeSelect.FindPreset("does-not-exist"));
        Assert.Null(ThemeSelect.FindPreset(null));
        Assert.Null(ThemeSelect.FindPreset(""));
    }

    // ------------------------------------------------------------ resolution chain

    [Fact]
    public void Resolve_CustomImport_WinsOverEverything()
    {
        var json = """{"schema":"lumo.theme/1","name":"Custom","mode":"light","accent":"#123456","colors":{"panel":"#010101"}}""";
        var spec = ThemeSelect.Resolve("nord", json, "dark", "#FF6363", systemDark: true);
        Assert.False(spec.Dark);                          // mode comes from the file
        Assert.Equal("#123456", spec.AccentHex);
        Assert.Equal("#010101", spec.Overrides["panel"]);
        Assert.StartsWith("user:", spec.Id);
        Assert.Equal("Custom", spec.Name);
    }

    [Fact]
    public void Resolve_Preset_Second()
    {
        var spec = ThemeSelect.Resolve("dusk", null, "light", "#FF6363", systemDark: false);
        Assert.True(spec.Dark);                           // preset's own mode beats legacy
        Assert.Equal("dusk", spec.Id);
        Assert.Equal("Claude Dusk", spec.Name);
        Assert.Equal("#D97757", spec.AccentHex);
        Assert.Equal("#1D1C1A", spec.Overrides["panel"]);
    }

    [Fact]
    public void Resolve_UnknownPreset_FallsBackToLegacy()
    {
        var spec = ThemeSelect.Resolve("bogus", null, "light", "#57C1FF", systemDark: true);
        Assert.Equal("legacy", spec.Id);
        Assert.False(spec.Dark);
        Assert.Equal("#57C1FF", spec.AccentHex);
        Assert.Empty(spec.Overrides);
    }

    [Fact]
    public void Resolve_LegacyAuto_FollowsSystem()
    {
        Assert.True(ThemeSelect.Resolve("", null, "auto", null, systemDark: true).Dark);
        Assert.False(ThemeSelect.Resolve("", null, "auto", null, systemDark: false).Dark);
        Assert.True(ThemeSelect.Resolve(null, null, "dark", null, systemDark: false).Dark);
        Assert.False(ThemeSelect.Resolve(null, null, "LIGHT", null, systemDark: true).Dark);
    }

    [Fact]
    public void Resolve_BrokenCustomJson_FallsThroughToPreset()
    {
        var spec = ThemeSelect.Resolve("nord", "{ this is not json", "dark", null, systemDark: true);
        Assert.Equal("nord", spec.Id);
    }

    [Fact]
    public void Resolve_EmptyEverything_DefaultsToDarkLumo()
    {
        var spec = ThemeSelect.Resolve(null, null, null, null, systemDark: false);
        Assert.True(spec.Dark);   // the legacy resolver's default — dark
        Assert.Equal("legacy", spec.Id);
    }

    [Fact]
    public void ExportFile_PresetRoundTrip_IsFaithful()
    {
        // ThemeService.ExportFile builds a ThemeFile from a spec; simulate via ThemeFile
        // round-trip on a preset's colors (the WPF side feeds it the same data).
        var preset = ThemeSelect.FindPreset("parchment")!;
        var file = new ThemeFile(preset.Name, preset.Dark, preset.Accent,
            preset.Colors.ToDictionary(kv => kv.Key, kv => kv.Value));
        var ok = ThemeFile.TryParse(file.Serialize(), out var parsed, out _);
        Assert.True(ok);
        Assert.NotNull(parsed);
        Assert.Equal("Parchment", parsed!.Name);
        Assert.False(parsed.Dark);
        Assert.Equal(preset.Colors.Count, parsed.Colors.Count);
    }
}
