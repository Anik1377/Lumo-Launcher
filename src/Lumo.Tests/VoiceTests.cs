using System.Text.Json;
using Lumo.Core;
using Lumo.Services;
using Xunit;

namespace Lumo.Tests;

// ------------------------------------------------------------------ v2.6.0-alpha.3 — voice typing pure policy

public class VoiceLanguageTests
{
    private static (string Id, string Culture) R(string id, string culture) => (id, culture);

    [Fact]
    public void Pick_Empty_Installed_Returns_Null()
    {
        Assert.Null(VoiceLanguage.Pick("en-US", Array.Empty<(string, string)>()));
    }

    [Fact]
    public void Pick_Exact_Culture_Wins()
    {
        var installed = new[] { R("rec-a", "en-US"), R("rec-b", "en-GB") };
        Assert.Equal("rec-b", VoiceLanguage.Pick("en-GB", installed));
    }

    [Fact]
    public void Pick_Exact_Recognizer_Id_Wins_Too()
    {
        var installed = new[] { R("rec-a", "en-US"), R("rec-b", "de-DE") };
        Assert.Equal("rec-b", VoiceLanguage.Pick("REC-B", installed));
    }

    [Fact]
    public void Pick_Language_Part_Matches_When_Exact_Missing()
    {
        var installed = new[] { R("rec-a", "de-DE"), R("rec-b", "en-US") };
        // a stale "en-GB" preference on an en-US-only machine still gives the user English
        Assert.Equal("rec-b", VoiceLanguage.Pick("en-GB", installed));
        // a bare "en" preference matches by language part as well
        Assert.Equal("rec-b", VoiceLanguage.Pick("en", installed));
    }

    [Fact]
    public void Pick_Empty_Preferred_Follows_Ui_Culture_Language_Part()
    {
        var installed = new[] { R("rec-a", "de-DE"), R("rec-b", "fr-CA") };
        Assert.Equal("rec-b", VoiceLanguage.Pick("", installed, "fr-FR"));
    }

    [Fact]
    public void Pick_Unmatched_Everything_Falls_Back_To_First()
    {
        var installed = new[] { R("rec-a", "de-DE"), R("rec-b", "fr-FR") };
        Assert.Equal("rec-a", VoiceLanguage.Pick("xx-XX", installed, "yy-YY"));
        Assert.Equal("rec-a", VoiceLanguage.Pick(null, installed, null));
    }

    [Fact]
    public void Pick_Case_Is_Irrelevant_And_Whitespace_Tolerated()
    {
        var installed = new[] { R("rec-a", "en-US") };
        Assert.Equal("rec-a", VoiceLanguage.Pick("  EN-US  ", installed));
    }
}

public class VoiceTextTests
{
    [Theory]
    [InlineData("", "hello world", "hello world")]
    [InlineData(null, "hello", "hello")]
    [InlineData("?", "hello", "? hello")]            // separator inserted
    [InlineData("? ", "hello", "? hello")]           // trailing space respected, not doubled
    [InlineData("summarize this:", " ok ", "summarize this: ok")]
    [InlineData("base", "", "base")]                 // empty spoken → untouched
    [InlineData("base", "   ", "base")]              // whitespace-only spoken → untouched
    [InlineData("base ", "  ", "base ")]             // …and a trailing space is preserved
    [InlineData("  ", "hi", "hi")]                   // whitespace-only base behaves as empty
    public void Compose_Joins_With_One_Space(string baseText, string spoken, string expected)
        => Assert.Equal(expected, VoiceText.Compose(baseText, spoken));
}

public class VoiceSettingsTests
{
    /// <summary>Tolerant read: proper values land, junk falls back to the current value.</summary>
    [Fact]
    public void ApplyJson_Reads_Voice_Keys()
    {
        var s = new Settings();
        var json = JsonDocument.Parse("""{"VoiceEnabled": false, "VoiceLanguage": "en-GB"}""").RootElement;
        Settings.ApplyJson(s, json);
        Assert.False(s.VoiceEnabled);
        Assert.Equal("en-GB", s.VoiceLanguage);
    }

    [Fact]
    public void ApplyJson_Junk_Voice_Values_Fall_Back()
    {
        var s = new Settings();   // defaults: enabled, ""
        var json = JsonDocument.Parse("""{"VoiceEnabled": "yes-please", "VoiceLanguage": 42}""").RootElement;
        Settings.ApplyJson(s, json);
        Assert.True(s.VoiceEnabled);
        Assert.Equal("", s.VoiceLanguage);
    }

    [Fact]
    public void RestoreFrom_Copies_Voice_Keys()
    {
        var a = new Settings { VoiceEnabled = false, VoiceLanguage = "de-DE" };
        var b = new Settings();
        b.RestoreFrom(a);
        Assert.False(b.VoiceEnabled);
        Assert.Equal("de-DE", b.VoiceLanguage);
    }

    [Fact]
    public void Clone_RoundTrips_Voice_Keys()
    {
        var s = new Settings { VoiceEnabled = false, VoiceLanguage = "en-GB" };
        var c = s.Clone();
        Assert.False(c.VoiceEnabled);
        Assert.Equal("en-GB", c.VoiceLanguage);
    }
}
