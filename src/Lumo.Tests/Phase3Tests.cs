using Lumo.Core;
using Xunit;

namespace Lumo.Tests;

// ------------------------------------------------------------------ v2.3 Task 3.1 — AI providers

public class AiProvidersTests
{
    [Fact]
    public void Build_Ollama_PutsModelAndPromptInBody_NoKeyHeader()
    {
        var (ok, spec, err) = AiProviders.Build("ollama", "http://localhost:11434", "llama3.2", "", "hello world");
        Assert.True(ok, err);
        Assert.NotNull(spec);
        Assert.Equal("http://localhost:11434/api/chat", spec!.Url);
        Assert.Contains("llama3.2", spec.Json);
        Assert.Contains("hello world", spec.Json);
        Assert.DoesNotContain("authorization", string.Join(",", spec.Headers.Keys), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_Anthropic_RequiresKey_AndSetsVersionHeader()
    {
        var (okNoKey, _, errNoKey) = AiProviders.Build("anthropic", null, "claude-3-haiku", "", "hi");
        Assert.False(okNoKey);
        Assert.Contains("key", errNoKey, StringComparison.OrdinalIgnoreCase);

        var (ok, spec, err) = AiProviders.Build("anthropic", null, "claude-3-haiku", "sk-ant-123", "hi");
        Assert.True(ok, err);
        Assert.Equal("https://api.anthropic.com/v1/messages", spec!.Url);
        Assert.Equal("sk-ant-123", spec.Headers["x-api-key"]);
        Assert.Equal(AiProviders.AnthropicVersion, spec.Headers["anthropic-version"]);
        // the key travels ONLY in headers — never in the body
        Assert.DoesNotContain("sk-ant-123", spec.Json);
    }

    [Fact]
    public void Build_EndsWithoutScheme_FallsBackToLocalHttp()
    {
        var (ok, spec, _) = AiProviders.Build("ollama", "localhost:11434", "m", "", "q");
        Assert.True(ok);
        Assert.StartsWith("http://localhost:11434", spec!.Url);
    }

    [Fact]
    public void Build_EmptyPrompt_IsRejected()
    {
        var (ok, spec, _) = AiProviders.Build("ollama", null, "m", "", "   ");
        Assert.False(ok);
        Assert.Null(spec);
    }

    [Fact]
    public void Extract_OllamaChatShape_ReturnsContent()
    {
        string json = """{"model":"llama3.2","message":{"role":"assistant","content":"  42 lines of wisdom  "}}""";
        Assert.Equal("42 lines of wisdom", AiProviders.Extract("ollama", json));
    }

    [Fact]
    public void Extract_OllamaGenerateShape_FallbackWorks()
    {
        string json = """{"response":"the answer"}""";
        Assert.Equal("the answer", AiProviders.Extract("ollama", json));
    }

    [Fact]
    public void Extract_Anthropic_JoinsAllTextBlocks()
    {
        string json = """{"content":[{"type":"text","text":"part1 "},{"type":"text","text":"part2"}]}""";
        Assert.Equal("part1 part2", AiProviders.Extract("anthropic", json));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"error":{"message":"rate limited"}}""")]
    [InlineData("[]")]
    public void Extract_Garbage_ReturnsNull(string json)
    {
        Assert.Null(AiProviders.Extract("ollama", json));
        Assert.Null(AiProviders.Extract("anthropic", json));
    }

    [Fact]
    public void Redact_RemovesSecretFromAnyLogLine()
    {
        string line = "POST failed: sk-ant-abc123 → 401";
        Assert.Equal("POST failed: *** → 401", AiProviders.Redact("sk-ant-abc123", line));
        Assert.Equal(line, AiProviders.Redact("", line));   // no secret → untouched
        Assert.Equal("", AiProviders.Redact("x", ""));      // null-safe
    }

    [Fact]
    public void IsAnthropic_StyleAndEndpointSniffing()
    {
        Assert.True(AiProviders.IsAnthropic("anthropic", null));
        Assert.True(AiProviders.IsAnthropic(null, "https://api.anthropic.com"));
        Assert.False(AiProviders.IsAnthropic("ollama", null));
        Assert.False(AiProviders.IsAnthropic(null, "http://localhost:11434"));
    }
}

// ------------------------------------------------------------------ v2.3 Task 3.2 — Bookmarks parser

public class BookmarksTests
{
    private const string SampleJson = """
    {
      "roots": {
        "bookmark_bar": {
          "type": "folder",
          "name": "Bookmarks bar",
          "children": [
            { "type": "url", "name": "GitHub", "url": "https://github.com", "date_added": "13390000000000000" },
            {
              "type": "folder",
              "name": "Dev",
              "children": [
                { "type": "url", "name": "NuGet", "url": "https://nuget.org", "date_added": "13390000000000001" }
              ]
            }
          ]
        },
        "other": {
          "type": "folder",
          "name": "Other",
          "children": [
            { "type": "url", "name": "Docs", "url": "https://learn.microsoft.com", "date_added": "13390000000000002" }
          ]
        },
        "synced": { "type": "folder", "name": "Mobile", "children": [] }
      }
    }
    """;

    [Fact]
    public void Parse_ExtractsUrls_WithFolders_AndKeepsOrder()
    {
        var list = Bookmarks.Parse(SampleJson);
        Assert.Equal(3, list.Count);

        Assert.Equal("GitHub", list[0].Name);
        Assert.Equal("https://github.com", list[0].Url);
        Assert.Equal("", list[0].Folder);                     // top of the bar → no folder prefix
        Assert.Equal(13390000000000000L, list[0].AddedAtMicros);

        Assert.Equal("Dev", list[1].Folder);                  // nested folder accumulates
        Assert.Equal("Other bookmarks", list[2].Folder);      // "other" root gets a label
    }

    [Fact]
    public void Parse_ToleratesGarbage()
    {
        Assert.Empty(Bookmarks.Parse(""));
        Assert.Empty(Bookmarks.Parse("not json at all"));
        Assert.Empty(Bookmarks.Parse("""{"roots": 42}"""));
        Assert.Empty(Bookmarks.Parse("""{"roots":{"bookmark_bar":{"type":"folder"}}}"""));
    }

    [Fact]
    public void Parse_SkipsFolderAndBrokenNodes()
    {
        string json = """
        {"roots":{"bookmark_bar":{"type":"folder","children":[
            {"type":"folder","name":"empty"},
            {"type":"url","name":"no url"},
            {"type":"url","name":"ok","url":"https://ok.example"}
        ]}}}
        """;
        var list = Bookmarks.Parse(json);
        Assert.Single(list);
        Assert.Equal("ok", list[0].Name);
    }

    [Fact]
    public void Parse_RespectsTheBoundedCap()
    {
        var sb = new System.Text.StringBuilder("""{"roots":{"bookmark_bar":{"type":"folder","children":[""");
        for (int i = 0; i < Bookmarks.MaxEntries + 500; i++)
        {
            if (i > 0) sb.Append(',');   // no trailing comma — System.Text.Json is strict
            sb.Append($$"""{"type":"url","name":"n{{i}}","url":"https://x{{i}}.example"}""");
        }
        sb.Append("]}}}");
        Assert.Equal(Bookmarks.MaxEntries, Bookmarks.Parse(sb.ToString()).Count);   // never unbounded
    }
}

// ------------------------------------------------------------------ v2.3 Task 3.3 — SnippetExpander

public class SnippetExpanderTests
{
    private static readonly DateTime Now = new(2026, 8, 30, 14, 5, 0);

    [Fact]
    public void Date_And_Time_Tokens_Expand()
    {
        Assert.Equal("2026-08-30", SnippetExpander.ExpandAll("{{date}}", () => "", Now));
        Assert.Equal("14:05", SnippetExpander.ExpandAll("{{time}}", () => "", Now));
        Assert.Equal("2026-08-30 14:05", SnippetExpander.ExpandAll("{{datetime}}", () => "", Now));
        Assert.Equal("d=2026-08-30 t=14:05", SnippetExpander.ExpandAll("d={{date}} t={{time}}", () => "", Now));
    }

    [Fact]
    public void Tokens_AreCaseInsensitive_AndWhitespaceTolerant()
    {
        Assert.Equal("2026-08-30", SnippetExpander.ExpandAll("{{ DATE }}", () => "", Now));
    }

    [Fact]
    public void Clipboard_Token_PullsFromTheCallback()
    {
        Assert.Equal("copied text", SnippetExpander.ExpandAll("{{clipboard}}", () => "copied text", Now));
        Assert.Equal("", SnippetExpander.ExpandAll("{{clipboard}}", () => null, Now));
        Assert.Equal("", SnippetExpander.ExpandAll("{{clipboard}}", () => throw new InvalidOperationException("locked"), Now));
    }

    [Fact]
    public void KeyDefault_Token_ExpandsToItsDefault()
    {
        Assert.Equal("Dear Jane", SnippetExpander.ExpandAll("Dear {{name:Jane}}", () => "", Now));
        Assert.Equal("a:b", SnippetExpander.ExpandAll("{{k:a:b}}", () => "", Now));   // first colon splits
    }

    [Fact]
    public void Unknown_Tokens_StayVerbatim()
    {
        Assert.Equal("{{what}}", SnippetExpander.ExpandAll("{{what}}", () => "", Now));
        Assert.Equal("{{unclosed", SnippetExpander.ExpandAll("{{unclosed", () => "", Now));
    }

    [Fact]
    public void Cursor_SplitsText_AndExpandAllDropsIt()
    {
        var (before, after, has) = SnippetExpander.ExpandWithCursor("head {{cursor}} tail", () => "", Now);
        Assert.True(has);
        Assert.Equal("head", before.Trim());
        Assert.Equal("tail", after.Trim());

        Assert.Equal("headtail", SnippetExpander.ExpandAll("head{{cursor}}tail", () => "", Now));
    }

    [Fact]
    public void Expansion_IsNotRecursive()
    {
        // a clipboard that CONTAINS a token must stay literal
        Assert.Equal("{{date}}", SnippetExpander.ExpandAll("{{clipboard}}", () => "{{date}}", Now));
    }

    [Fact]
    public void SnippetRecipe_EndToEnd()
    {
        string template = "Hi {{name:Jane}}, today is {{date}} at {{time}}. Notes: {{clipboard}}";
        string expanded = SnippetExpander.ExpandAll(template, () => "ship it", Now);
        Assert.Equal("Hi Jane, today is 2026-08-30 at 14:05. Notes: ship it", expanded);
    }
}
