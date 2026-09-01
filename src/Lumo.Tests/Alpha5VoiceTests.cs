using Lumo.Core;
using Xunit;

namespace Lumo.Tests;

/// <summary>
/// v2.6.0-alpha.5 — the new pure policy: the Whisper model catalog + language
/// mapping, the reasoning-model splitter, the meter math, and the image payload
/// guard rails (with the provider JSON shapes they serialize into).
/// </summary>
public class Alpha5VoiceTests
{
    // ---------------------------------------------------------------- catalog

    [Fact]
    public void Catalog_is_sane_and_unique()
    {
        Assert.NotEmpty(VoiceWhisper.Catalog);
        Assert.All(VoiceWhisper.Catalog, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Id));
            Assert.False(string.IsNullOrWhiteSpace(m.FileName));
            Assert.StartsWith("ggml-", m.FileName);
            Assert.EndsWith(".bin", m.FileName);
            Assert.StartsWith("https://", m.Url);                       // https only — the installer's rule
            Assert.Contains(VoiceWhisper.CatalogHost, m.Url);           // official whisper.cpp weights
            Assert.EndsWith(m.FileName, m.Url, StringComparison.Ordinal);
            Assert.True(m.Bytes > 1_000_000);                           // every model is megabytes, not bytes
            Assert.False(string.IsNullOrWhiteSpace(m.Description));
        });
        Assert.Equal(VoiceWhisper.Catalog.Count, VoiceWhisper.Catalog.Select(m => m.Id).Distinct().Count());
        Assert.Equal(VoiceWhisper.Catalog.Count, VoiceWhisper.Catalog.Select(m => m.FileName).Distinct().Count());
    }

    [Fact]
    public void Default_model_exists_and_FromId_falls_back()
    {
        Assert.Contains(VoiceWhisper.Catalog, m => m.Id == VoiceWhisper.DefaultModelId);
        Assert.Equal(VoiceWhisper.DefaultModelId, VoiceWhisper.FromId("base.en").Id);
        Assert.Equal(VoiceWhisper.DefaultModelId, VoiceWhisper.FromId("BASE.EN").Id);      // case-tolerant
        Assert.Equal(VoiceWhisper.DefaultModelId, VoiceWhisper.FromId("  ").Id);
        Assert.Equal(VoiceWhisper.DefaultModelId, VoiceWhisper.FromId("does-not-exist").Id);
    }

    [Fact]
    public void SizeLabel_is_human()
    {
        var m = VoiceWhisper.FromId("base.en");
        Assert.Matches(@"^\d+ MB$", m.SizeLabel);
        Assert.NotEqual("?", m.SizeLabel);
    }

    [Theory]
    [InlineData("ggml-base.en.bin", true)]
    [InlineData("ggml-small.bin", true)]
    [InlineData("ggml-..bin", true)]         // dots are legal in a file name — separators are not
    [InlineData("model.bin", false)]          // not ggml-
    [InlineData("ggml-base.bin.txt", false)]  // wrong extension
    [InlineData("ggml-bad/name.bin", false)]  // path traversal
    [InlineData("ggml-bad\\name.bin", false)] // path traversal (windows)
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsKnownFileName_blocks_junk(string? name, bool expected)
    {
        Assert.Equal(expected, VoiceWhisper.IsKnownFileName(name));
    }

    // ---------------------------------------------------------------- language mapping

    [Fact]
    public void English_only_models_always_get_en()
    {
        var tiny = VoiceWhisper.Catalog.First(m => m.Id == "tiny.en");
        Assert.True(tiny.EnglishOnly);
        Assert.Equal("en", VoiceWhisper.ResolveLanguage(tiny, ""));
        Assert.Equal("en", VoiceWhisper.ResolveLanguage(tiny, "de-DE"));
        Assert.Equal("en", VoiceWhisper.ResolveLanguage(tiny, "garbage-input"));
    }

    [Theory]
    [InlineData("", "auto")]              // no pin → whisper auto-detects
    [InlineData("  ", "auto")]
    [InlineData("en-GB", "en")]
    [InlineData("de-DE", "de")]
    [InlineData("zh-Hans-CN", "zh")]
    [InlineData("fr", "fr")]
    [InlineData("not a lang", "auto")]    // junk pin must not throw or poison the request
    public void Multilingual_models_map_the_pin(string pin, string expected)
    {
        var small = VoiceWhisper.Catalog.First(m => m.Id == "small");
        Assert.False(small.EnglishOnly);
        Assert.Equal(expected, VoiceWhisper.ResolveLanguage(small, pin));
    }

    // ---------------------------------------------------------------- meter math

    [Fact]
    public void RmsToLevel_zeroes_silence_and_saturates_loudness()
    {
        Assert.Equal(0, VoiceAudio.RmsToLevel(0));
        Assert.Equal(0, VoiceAudio.RmsToLevel(VoiceAudio.SilenceRms - 1));   // room noise rests at zero
        Assert.Equal(1, VoiceAudio.RmsToLevel(VoiceAudio.LoudRms));
        Assert.Equal(1, VoiceAudio.RmsToLevel(32767));                       // clipping saturates, never overflows
        double quiet = VoiceAudio.RmsToLevel(VoiceAudio.SilenceRms + 60);
        Assert.InRange(quiet, 0.01, 0.5);                                    // quiet speech lifts off the floor
    }

    [Fact]
    public void ToFloatSamples_converts_pcm_range()
    {
        // 0x0000 → 0.0; 0x7FFF (32767) → ~1.0; 0x8000 (-32768) → -1.0
        byte[] pcm = { 0x00, 0x00, 0xFF, 0x7F, 0x00, 0x80 };
        var f = VoiceAudio.ToFloatSamples(pcm);
        Assert.Equal(3, f.Length);
        Assert.Equal(0f, f[0]);
        Assert.Equal(32767f / 32768f, f[1], 5);
        Assert.Equal(-1f, f[2], 5);
        Assert.Empty(VoiceAudio.ToFloatSamples(Array.Empty<byte>()));        // empty tails tolerated
        Assert.Empty(VoiceAudio.ToFloatSamples(new byte[] { 0x01 }));        // lone byte ignored
    }

    // ---------------------------------------------------------------- meter auto-gain

    [Fact]
    public void AutoGain_lifts_quiet_mics_and_keeps_silence_flat()
    {
        double peak = 0;
        Assert.Equal(0, VoiceAudio.AutoGain(0, ref peak));            // silence never manufactures motion
        double quiet = VoiceAudio.AutoGain(0.1, ref peak);            // a quiet mic still moves the bars
        Assert.InRange(quiet, 0.3, 1.0);
        Assert.Equal(0, VoiceAudio.AutoGain(0, ref peak));            // silence stays flat after gain adapts
    }

    [Fact]
    public void AutoGain_loud_levels_saturate_and_resensitize_gradually()
    {
        double peak = 0;
        double loud = VoiceAudio.AutoGain(0.9, ref peak);             // near-full-scale passes through
        Assert.InRange(loud, 0.99, 1.0);
        Assert.Equal(1, VoiceAudio.AutoGain(2.0, ref peak));          // out-of-range input clamps, never overflows

        // after the spike the peak decays ~0.8 %/push, so repeated quiet speech
        // gains more and more — sensitivity recovers gradually, never snaps
        double first = VoiceAudio.AutoGain(0.1, ref peak);
        double mid = VoiceAudio.AutoGain(0.1, ref peak);
        double last = VoiceAudio.AutoGain(0.1, ref peak);
        Assert.True(mid > first && last > mid);
        Assert.True(last <= 1.0);
    }

    // ---------------------------------------------------------------- think splitter

    [Fact]
    public void ThinkSplit_plain_text_has_no_reasoning()
    {
        var p = ThinkSplit.Split("Just the answer.");
        Assert.False(p.HasReasoning);
        Assert.Equal("Just the answer.", p.Answer);
        Assert.False(ThinkSplit.IsThinking("Just the answer."));
        Assert.False(ThinkSplit.IsThinking(null));
        Assert.False(ThinkSplit.Split(null).HasReasoning);
    }

    [Fact]
    public void ThinkSplit_separates_closed_block()
    {
        var p = ThinkSplit.Split("<think>Let me count. 2+2=4.</think>The answer is 4.");
        Assert.Equal("Let me count. 2+2=4.", p.Reasoning);
        Assert.Equal("The answer is 4.", p.Answer);
    }

    [Fact]
    public void ThinkSplit_handles_stream_partials()
    {
        // still inside the block: everything after the opener is reasoning
        var open = ThinkSplit.Split("<think>step one, step two, ste");
        Assert.True(open.HasReasoning);
        Assert.Equal("", open.Answer);
        Assert.True(ThinkSplit.IsThinking("<think>step one, step two"));

        // the opener itself half-arrived — it is not a tag yet, so plain answer
        var half = ThinkSplit.Split("The answer is <thi");
        Assert.False(half.HasReasoning);
    }

    [Fact]
    public void ThinkSplit_tolerates_thinking_tag_and_case()
    {
        var alt = ThinkSplit.Split("<THINKING>hmm</THINKING>ok");
        Assert.Equal("hmm", alt.Reasoning);
        Assert.Equal("ok", alt.Answer);

        var mixed = ThinkSplit.Split("<Think>deep</think>done");
        Assert.Equal("deep", mixed.Reasoning);
        Assert.Equal("done", mixed.Answer);
    }

    [Fact]
    public void ThinkSplit_text_before_the_block_is_kept()
    {
        var p = ThinkSplit.Split("preface <think>r</think> answer");
        Assert.Contains("preface", p.Reasoning);
        Assert.Equal("answer", p.Answer);
    }

    // ---------------------------------------------------------------- image payload

    [Fact]
    public void ImagePayload_accepts_supported_media_only()
    {
        Assert.NotNull(AiProviders.ImagePayload.Create(new byte[] { 1, 2, 3 }, "image/png"));
        Assert.NotNull(AiProviders.ImagePayload.Create(new byte[] { 1, 2, 3 }, "Image/JPEG"));   // case-tolerant
        Assert.Null(AiProviders.ImagePayload.Create(new byte[] { 1, 2, 3 }, "application/pdf"));
        Assert.Null(AiProviders.ImagePayload.Create(new byte[] { 1, 2, 3 }, null));
        Assert.Null(AiProviders.ImagePayload.Create(null, "image/png"));
        Assert.Null(AiProviders.ImagePayload.Create(Array.Empty<byte>(), "image/png"));

        var tooBig = new byte[AiProviders.ImagePayload.MaxBytes + 1];
        Assert.Null(AiProviders.ImagePayload.Create(tooBig, "image/png"));
    }

    [Fact]
    public void ImagePayload_decodes_byte_count()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var img = AiProviders.ImagePayload.Create(bytes, "image/png")!;
        Assert.Equal(bytes.Length, img.ByteCount);
        Assert.False(img.Base64.Contains(':'));   // raw base64 — no data: prefix ever
    }

    // ---------------------------------------------------------------- provider JSON shapes

    [Fact]
    public void Ollama_image_turn_serializes_images_array()
    {
        var img = AiProviders.ImagePayload.Create(new byte[] { 9, 9 }, "image/png")!;
        var turns = new List<AiProviders.AiTurn>
        {
            new("user", "previous", (AiProviders.ImagePayload?)null),
            new("user", "what is this?", img),
        };
        var (ok, spec, err) = AiProviders.BuildChat(AiProviders.OllamaStyle, "http://localhost:11434", "llava", null, turns);
        Assert.True(ok, err);
        Assert.NotNull(spec);
        Assert.Contains("\"images\"", spec!.Json);
        Assert.Contains($"\"{img.Base64}\"", spec.Json);
        Assert.Contains("what is this?", spec.Json);
    }

    [Fact]
    public void Anthropic_image_turn_serializes_base64_blocks()
    {
        var img = AiProviders.ImagePayload.Create(new byte[] { 7, 7 }, "image/png")!;
        var turns = new List<AiProviders.AiTurn> { new("user", "describe", img) };
        var (ok, spec, _) = AiProviders.BuildChat(AiProviders.AnthropicStyle, "https://api.anthropic.com", "claude-sonnet-4-5", "k", turns);
        Assert.True(ok);
        Assert.NotNull(spec);
        Assert.Contains("\"type\":\"image\"", spec!.Json.Replace(" ", ""));
        Assert.Contains("\"media_type\":\"image/png\"", spec.Json.Replace(" ", ""));
        Assert.Contains($"\"data\":\"{img.Base64}\"", spec.Json);
    }

    [Fact]
    public void Image_budget_strips_old_turns()
    {
        var img = AiProviders.ImagePayload.Create(new byte[] { 1 }, "image/png")!;
        var turns = new List<AiProviders.AiTurn>
        {
            new("user", "oldest image turn", img),                    // beyond the budget → text only
            new("assistant", "ok"),
            new("user", "middle image turn", img),                    // within the budget → rides
            new("assistant", "still ok"),
            new("user", "newest image turn", img),                    // within the budget → rides
        };
        var (ok, spec, _) = AiProviders.BuildChat(AiProviders.OllamaStyle, "http://localhost:11434", "llava", null, turns);
        Assert.True(ok);
        // exactly MaxImageTurns images arrays survive (the newest image turns)
        int count = 0;
        int at = 0;
        while ((at = spec!.Json.IndexOf("\"images\"", at, StringComparison.Ordinal)) >= 0) { count++; at += 8; }
        Assert.Equal(AiProviders.MaxImageTurns, count);
        Assert.Contains("middle image turn", spec.Json);
        Assert.Contains("newest image turn", spec.Json);   // the stripped turn keeps its TEXT, loses its image
    }

    // ---------------------------------------------------------------- settings round-trip

    [Fact]
    public void Settings_voice_engine_and_model_apply_tolerantly()
    {
        var s = new Lumo.Services.Settings();
        var json = System.Text.Json.JsonDocument.Parse(
            """{"VoiceEnabled": true, "VoiceLanguage": "en-GB", "VoiceEngine": "windows", "VoiceModel": "small"}""").RootElement;
        Lumo.Services.Settings.ApplyJson(s, json);
        Assert.Equal("windows", s.VoiceEngine);
        Assert.Equal("small", s.VoiceModel);

        // junk / absent values fall back to the safe defaults (whisper + catalog default)
        var back = new Lumo.Services.Settings();
        var junk = System.Text.Json.JsonDocument.Parse(
            """{"VoiceEngine": 123, "VoiceModel": null}""").RootElement;
        Lumo.Services.Settings.ApplyJson(back, junk);
        Assert.Equal("whisper", back.VoiceEngine);
        Assert.Equal(VoiceWhisper.DefaultModelId, back.VoiceModel);
    }
}
