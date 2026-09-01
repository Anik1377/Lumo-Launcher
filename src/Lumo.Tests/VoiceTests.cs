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

// ------------------------------------------------------------------ v2.6.0-alpha.4 — record → transcribe → show audio policy

public class VoiceAudioTests
{
    /// <summary>16-bit little-endian mono PCM bytes from short samples.</summary>
    private static byte[] Pcm16(params short[] samples)
    {
        var b = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            b[i * 2] = (byte)(samples[i] & 0xFF);
            b[i * 2 + 1] = (byte)((samples[i] >> 8) & 0xFF);
        }
        return b;
    }

    private const int Rate = 16000;

    [Fact]
    public void BuildWav_Writes_Canonical_Header()
    {
        var pcm = Pcm16(0, 100, -100);
        var wav = VoiceAudio.BuildWav(pcm, Rate, 1, 16);

        Assert.Equal(44 + pcm.Length, wav.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal((uint)(36 + pcm.Length), BitConverter.ToUInt32(wav, 4));      // RIFF chunk size
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", System.Text.Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal(16, BitConverter.ToInt32(wav, 16));                   // fmt chunk length
        Assert.Equal((short)1, BitConverter.ToInt16(wav, 20));             // PCM
        Assert.Equal((short)1, BitConverter.ToInt16(wav, 22));             // channels
        Assert.Equal(Rate, BitConverter.ToInt32(wav, 24));                 // sample rate
        Assert.Equal(Rate * 2, BitConverter.ToInt32(wav, 28));             // byte rate: 16k × mono × 16 bit
        Assert.Equal((short)2, BitConverter.ToInt16(wav, 32));             // block align
        Assert.Equal((short)16, BitConverter.ToInt16(wav, 34));            // bits per sample
        Assert.Equal("data", System.Text.Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(pcm.Length, BitConverter.ToInt32(wav, 40));           // data chunk size
    }

    [Fact]
    public void BuildWav_Payload_Survives_The_Round_Trip()
    {
        var pcm = Pcm16(short.MinValue, -1, 0, 1, short.MaxValue);
        var wav = VoiceAudio.BuildWav(pcm);
        for (int i = 0; i < pcm.Length; i++)
            Assert.Equal(pcm[i], wav[44 + i]);
    }

    [Fact]
    public void TrimSilence_Cuts_Room_Tone_And_Keeps_Padding()
    {
        // 1 s silence + 0.2 s speech (amplitude 3000) + 1 s silence, 16 kHz
        var silence = new short[Rate];                       // 16000 samples
        var speech = Enumerable.Repeat((short)3000, Rate / 5).ToArray();
        var pcm = Pcm16(silence.Concat(speech).Concat(silence).ToArray());

        var range = VoiceAudio.TrimSilence(pcm, Rate);
        Assert.NotNull(range);

        int speechStartBytes = silence.Length * 2;           // 32000
        int speechEndBytes = speechStartBytes + speech.Length * 2;
        int padBytes = Rate / 1000 * 220 * 2;                // 220 ms pad = 7040 bytes
        Assert.Equal(speechStartBytes - padBytes, range!.Value.Start);   // 1 s of leading tone leaves room to pad
        Assert.Equal(speechEndBytes + padBytes, range.Value.End);
    }

    [Fact]
    public void TrimSilence_All_Speech_Returns_The_Whole_Clamp()
    {
        var pcm = Pcm16(Enumerable.Repeat((short)2000, 3200).ToArray());
        var range = VoiceAudio.TrimSilence(pcm, Rate);
        Assert.NotNull(range);
        Assert.Equal(0, range!.Value.Start);
        Assert.Equal(pcm.Length, range.Value.End);
    }

    [Fact]
    public void TrimSilence_Silence_Only_And_Empty_Return_Null()
    {
        Assert.Null(VoiceAudio.TrimSilence(Pcm16(Enumerable.Repeat((short)3, Rate).ToArray()), Rate));
        Assert.Null(VoiceAudio.TrimSilence(Array.Empty<byte>(), Rate));
    }

    [Fact]
    public void TrimSilence_Quiet_But_Audible_Speech_Is_Kept()
    {
        // amplitude 500 hums above the 320 threshold — real speech in a quiet room
        var pcm = Pcm16(new short[Rate].Concat(Enumerable.Repeat((short)500, 3200)).Concat(new short[Rate]).ToArray());
        var range = VoiceAudio.TrimSilence(pcm, Rate);
        Assert.NotNull(range);
        Assert.True(range!.Value.End - range.Value.Start >= 3200 * 2);
    }
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
