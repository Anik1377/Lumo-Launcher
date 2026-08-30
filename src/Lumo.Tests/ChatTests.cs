using Lumo.Core;
using Xunit;

namespace Lumo.Tests;

// ------------------------------------------------------------------ v2.3.0-alpha.3 — AI chat tab
//
// The chat window itself is WPF-only (runs on CI's windows runner), but every
// decision surface it relies on is pure and pinned here: the multi-turn request
// builder, both streaming line parsers, and the markdown-lite renderer.

public class AiChatRequestTests
{
    private static readonly AiProviders.AiTurn[] Turns =
    {
        new("user", "who wrote Forensic Architecture?"),
        new("assistant", "a research agency."),
        new("user", "where are they based?"),
    };

    [Fact]
    public void BuildChat_Ollama_CarriesWholeConversationWithStreaming()
    {
        var (ok, spec, err) = AiProviders.BuildChat("ollama", "http://localhost:11434", "llama3.2", "", Turns);
        Assert.True(ok, err);
        Assert.NotNull(spec);
        Assert.Equal("http://localhost:11434/api/chat", spec!.Url);
        Assert.Contains("\"stream\":true", spec.Json);
        Assert.Contains("who wrote Forensic Architecture?", spec.Json);
        Assert.Contains("a research agency.", spec.Json);
        Assert.Contains("where are they based?", spec.Json);
    }

    [Fact]
    public void BuildChat_Anthropic_RequiresKey_AndNeverPutsKeyInBody()
    {
        var (okNoKey, _, errNoKey) = AiProviders.BuildChat("anthropic", null, "claude-3-haiku", "", Turns);
        Assert.False(okNoKey);
        Assert.Contains("key", errNoKey, StringComparison.OrdinalIgnoreCase);

        var (ok, spec, _) = AiProviders.BuildChat("anthropic", null, "claude-3-haiku", "sk-ant-secret", Turns);
        Assert.True(ok);
        Assert.Equal("sk-ant-secret", spec!.Headers["x-api-key"]);
        Assert.DoesNotContain("sk-ant-secret", spec.Json);
    }

    [Fact]
    public void BuildChat_EmptyPrompt_IsRejected()
    {
        var (ok, spec, _) = AiProviders.BuildChat("ollama", null, "m", "", new[] { new AiProviders.AiTurn("user", "   ") });
        Assert.False(ok);
        Assert.Null(spec);
    }

    [Fact]
    public void BuildChat_MissingModel_IsRejected()
    {
        var (ok, spec, _) = AiProviders.BuildChat("ollama", null, "", "", Turns);
        Assert.False(ok);
        Assert.Null(spec);
    }

    // ---- Ollama NDJSON stream lines ----

    [Fact]
    public void ParseOllamaStreamLine_DeltaCarriesContent()
    {
        var c = AiProviders.ParseOllamaStreamLine("""{"model":"llama3.2","message":{"role":"assistant","content":"Hel"},"done":false}""");
        Assert.Equal("Hel", c.Delta);
        Assert.False(c.Done);
        Assert.Equal("", c.Error);
    }

    [Fact]
    public void ParseOllamaStreamLine_FinalLineIsDone()
    {
        var c = AiProviders.ParseOllamaStreamLine("""{"model":"llama3.2","message":{"role":"assistant","content":""},"done":true}""");
        Assert.True(c.Done);
        Assert.Equal("", c.Delta);
    }

    [Fact]
    public void ParseOllamaStreamLine_ErrorFieldSurfaces()
    {
        var c = AiProviders.ParseOllamaStreamLine("""{"error":"model 'x' not found"}""");
        Assert.True(c.Done);
        Assert.Contains("not found", c.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("junk")]
    [InlineData("[1,2]")]
    public void ParseOllamaStreamLine_GarbageIsEmptyChunkNeverThrows(string line)
    {
        var c = AiProviders.ParseOllamaStreamLine(line);
        Assert.Equal("", c.Delta);
        Assert.False(c.Done);
        Assert.Equal("", c.Error);
    }

    // ---- Anthropic SSE lines ----

    [Fact]
    public void ParseAnthropicSseLine_DeltaCarriesText()
    {
        var c = AiProviders.ParseAnthropicSseLine("data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"Hel\"}}");
        Assert.Equal("Hel", c.Delta);
        Assert.False(c.Done);
    }

    [Fact]
    public void ParseAnthropicSseLine_MessageStopIsDone()
    {
        var c = AiProviders.ParseAnthropicSseLine("data: {\"type\":\"message_stop\"}");
        Assert.True(c.Done);
    }

    [Fact]
    public void ParseAnthropicSseLine_ErrorEventSurfaces()
    {
        var c = AiProviders.ParseAnthropicSseLine("data: {\"type\":\"error\",\"error\":{\"type\":\"overloaded\",\"message\":\"overloaded\"}}");
        Assert.True(c.Done);
        Assert.Contains("overloaded", c.Error);
    }

    [Theory]
    [InlineData("event: content_block_delta")]
    [InlineData("")]
    [InlineData("data: [DONE]")]
    [InlineData("data: not json")]
    public void ParseAnthropicSseLine_NonDataLinesAreEmpty(string line)
    {
        var c = AiProviders.ParseAnthropicSseLine(line);
        Assert.Equal("", c.Delta);
        Assert.False(c.Done);
        Assert.Equal("", c.Error);
    }
}

public class MarkdownLiteTests
{
    [Fact]
    public void Parse_FencedBlockIsVerbatimWithLanguage()
    {
        var blocks = MarkdownLite.Parse("before\n```powershell\nGet-ChildItem  -Recurse\n  indented\n```\nafter");
        Assert.Equal(3, blocks.Count);
        var code = Assert.IsType<MarkdownLite.CodeBlock>(blocks[1]);
        Assert.Equal("powershell", code.Lang);
        Assert.Contains("Get-ChildItem  -Recurse", code.Text);
        Assert.Contains("  indented", code.Text);       // verbatim, indentation preserved
        Assert.IsType<MarkdownLite.Para>(blocks[0]);
        Assert.IsType<MarkdownLite.Para>(blocks[2]);
    }

    [Fact]
    public void Parse_UnterminatedFence_KeepsWhatArrived()
    {
        var blocks = MarkdownLite.Parse("```\nline one\nline two");
        var code = Assert.IsType<MarkdownLite.CodeBlock>(Assert.Single(blocks));
        Assert.Contains("line one", code.Text);
        Assert.Contains("line two", code.Text);
    }

    [Fact]
    public void Parse_HeadingsBulletsAndParagraphs()
    {
        string md = "# Title\n## Sub\n- first bullet\n* second bullet\n1. ordered\npara line one\npara line two\n\nnew para";
        var blocks = MarkdownLite.Parse(md);

        var h1 = Assert.IsType<MarkdownLite.Heading>(blocks[0]);
        Assert.Equal(1, h1.Level);
        Assert.Equal("Title", h1.Text);
        var h2 = Assert.IsType<MarkdownLite.Heading>(blocks[1]);
        Assert.Equal(2, h2.Level);

        var b1 = Assert.IsType<MarkdownLite.Bullet>(blocks[2]);
        Assert.Equal("first bullet", b1.Text);
        Assert.IsType<MarkdownLite.Bullet>(blocks[3]);
        var ordered = Assert.IsType<MarkdownLite.Bullet>(blocks[4]);
        Assert.StartsWith("1.", ordered.Text);           // ordered marker kept verbatim

        var para = Assert.IsType<MarkdownLite.Para>(blocks[5]);
        Assert.Equal("para line one para line two", para.Text);   // joined, blank line split
        Assert.IsType<MarkdownLite.Para>(blocks[6]);
    }

    [Fact]
    public void Parse_HashTagIsNotAHeading()
    {
        var blocks = MarkdownLite.Parse("#hashtag stays literal");
        var para = Assert.IsType<MarkdownLite.Para>(Assert.Single(blocks));
        Assert.Equal("#hashtag stays literal", para.Text);
    }

    [Fact]
    public void Parse_GarbageNeverThrows_NeverLosesContent()
    {
        Assert.Empty(MarkdownLite.Parse(null));
        Assert.Empty(MarkdownLite.Parse("   "));
        var blocks = MarkdownLite.Parse("plain text only");
        Assert.Single(blocks);
    }

    [Fact]
    public void Inline_BoldCodeAndPlainRuns()
    {
        var runs = MarkdownLite.Inline("use **bold** and `code` here");
        Assert.Equal(5, runs.Count);
        Assert.Equal("use ", runs[0].Text);
        Assert.True(runs[1].Bold);
        Assert.Equal("bold", runs[1].Text);
        Assert.True(runs[3].Code);
        Assert.Equal("code", runs[3].Text);
        Assert.Equal(" here", runs[4].Text);
    }

    [Fact]
    public void Inline_BoldInsideCodeStaysLiteral()
    {
        var runs = MarkdownLite.Inline("`a **b** c`");
        var code = Assert.Single(runs);
        Assert.True(code.Code);
        Assert.Equal("a **b** c", code.Text);
    }

    [Fact]
    public void Inline_UnbalancedMarkersStayVisible()
    {
        var runs = MarkdownLite.Inline("a ** b ` c");
        Assert.Equal("a ** b ` c", string.Concat(runs.Select(r => r.Text)));
    }
}
