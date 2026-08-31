using System.IO;
using Lumo.Core;
using Lumo.Services;
using Xunit;

namespace Lumo.Tests;

// ------------------------------------------------------------------ v2.4.0-alpha.5 — chat history + personas
//
// The sidebar and persona flyout are WPF-only, but every decision surface they
// rest on is pure and pinned here: the ChatStore persistence (round-trip,
// dedupe, delete, caps, corrupt tolerance), title derivation, the persona
// registry, and the system-prompt routing inside both request builders.

public class ChatStoreTests : IDisposable
{
    private readonly string _file;

    public ChatStoreTests()
    {
        _file = Path.Combine(Path.GetTempPath(), $"lumo-chats-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { if (File.Exists(_file)) File.Delete(_file); } catch { }
    }

    private static ChatSession MakeSession(string title, params (string role, string text)[] turns)
    {
        var s = new ChatSession { Title = title };
        foreach (var (role, text) in turns)
            s.Messages.Add(new ChatMessage(role, text, DateTime.UtcNow));
        return s;
    }

    [Fact]
    public void RoundTrip_PreservesSessionsMessagesAndPersona()
    {
        var store = new ChatStore(_file);
        var a = MakeSession("Explain git rebase",
            ("user", "what is git rebase?"),
            ("assistant", "rewriting history onto a new base…"));
        a.Persona = "developer";
        var b = MakeSession("Translate a paragraph", ("user", "translate this"));
        store.Upsert(a);
        store.Upsert(b);
        store.Save();

        var reloaded = ChatStore.Load(_file);
        Assert.Equal(2, reloaded.Count);

        var first = reloaded.Sessions[0];                 // b was upserted last → newest first
        Assert.Equal("Translate a paragraph", first.Title);
        var second = reloaded.Sessions[1];
        Assert.Equal("Explain git rebase", second.Title);
        Assert.Equal("developer", second.Persona);
        Assert.Equal(2, second.Messages.Count);
        Assert.Equal("user", second.Messages[0].Role);
        Assert.Equal("assistant", second.Messages[1].Role);
        Assert.Equal("rewriting history onto a new base…", second.Messages[1].Content);
    }

    [Fact]
    public void Upsert_UpdatesInPlace_NeverDuplicates()
    {
        var store = new ChatStore(_file);
        var s = MakeSession("t", ("user", "hi"));
        store.Upsert(s);
        s.Messages.Add(new ChatMessage("assistant", "hello", DateTime.UtcNow));
        store.Upsert(s);
        Assert.Equal(1, store.Count);
        Assert.Equal(2, store.Find(s.Id)!.Messages.Count);
    }

    [Fact]
    public void Delete_RemovesOnlyTheTarget()
    {
        var store = new ChatStore(_file);
        var a = MakeSession("a"); var b = MakeSession("b");
        store.Upsert(a); store.Upsert(b);
        Assert.True(store.Delete(a.Id));
        Assert.False(store.Delete(a.Id));                 // second delete is a no-op
        Assert.Equal(1, store.Count);
        Assert.Null(store.Find(a.Id));
        Assert.NotNull(store.Find(b.Id));
    }

    [Fact]
    public void SessionCap_KeepsTheNewestChats()
    {
        var store = new ChatStore(_file);
        for (int i = 0; i < ChatStore.MaxSessions + 5; i++)
        {
            var s = MakeSession($"chat {i}");
            store.Upsert(s);   // Upsert stamps recency; Seq breaks same-ms ties deterministically
        }
        Assert.Equal(ChatStore.MaxSessions, store.Count);
        // the newest must survive; the very first chat must have been pruned
        Assert.NotNull(store.Sessions.FirstOrDefault(s => s.Title == $"chat {ChatStore.MaxSessions + 4}"));
        Assert.Null(store.Sessions.FirstOrDefault(s => s.Title == "chat 0"));
    }

    [Fact]
    public void MessageCap_TrimsFromTheFront_KeepsTheLatestTurns()
    {
        var store = new ChatStore(_file);
        var s = new ChatSession { Title = "long" };
        for (int i = 0; i < ChatStore.MaxMessagesPerSession + 30; i++)
            s.Messages.Add(new ChatMessage(i % 2 == 0 ? "user" : "assistant", $"m{i}", DateTime.UtcNow));
        store.Upsert(s);

        var kept = store.Find(s.Id)!.Messages;
        Assert.Equal(ChatStore.MaxMessagesPerSession, kept.Count);
        Assert.Equal($"m{ChatStore.MaxMessagesPerSession + 29}", kept[^1].Content);   // newest kept
        Assert.Equal($"m30", kept[0].Content);                                        // oldest 30 dropped
    }

    [Fact]
    public void CorruptFile_LoadsAsEmpty_NeverThrows()
    {
        File.WriteAllText(_file, "{ this is not json ]]");
        var store = ChatStore.Load(_file);
        Assert.Equal(0, store.Count);
        Assert.Empty(store.Sessions);
    }

    [Fact]
    public void MissingFile_LoadsAsEmpty()
    {
        var store = ChatStore.Load(_file);
        Assert.Equal(0, store.Count);
    }

    [Theory]
    [InlineData("what is git rebase?", "what is git rebase?")]
    [InlineData("first line\nsecond line", "first line")]
    [InlineData("", "New chat")]
    [InlineData("   ", "New chat")]
    public void DeriveTitle_TakesFirstLine(string input, string expected)
    {
        Assert.Equal(expected, ChatSession.DeriveTitle(input));
    }

    [Fact]
    public void DeriveTitle_TruncatesLongFirstLines()
    {
        string longLine = new('x', 120);
        string title = ChatSession.DeriveTitle(longLine);
        Assert.True(title.Length <= 41);
        Assert.EndsWith("…", title);
    }
}

public class ChatPersonaTests
{
    [Fact]
    public void Registry_HasUniqueIds_NonEmptyPromptsAndGlyphs()
    {
        var ids = ChatPersonas.All.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(ChatPersonas.All.Length, ids.Count);
        Assert.All(ChatPersonas.All, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
            Assert.False(string.IsNullOrWhiteSpace(p.Glyph));
            Assert.True(p.Prompt.Length > 30, $"{p.Id} persona prompt is suspiciously short");
        });
    }

    [Fact]
    public void Resolve_KnownId_ReturnsPersona()
    {
        Assert.Equal("developer", ChatPersonas.Resolve("developer").Id);
        Assert.Equal("developer", ChatPersonas.Resolve("DEVELOPER").Id);   // lookup is case-insensitive
    }

    [Fact]
    public void Resolve_UnknownOrEmpty_FallsBackToDefault()
    {
        Assert.Same(ChatPersonas.Default, ChatPersonas.Resolve("no-such-persona"));
        Assert.Same(ChatPersonas.Default, ChatPersonas.Resolve(""));
        Assert.Same(ChatPersonas.Default, ChatPersonas.Resolve(null));
    }
}

public class PersonaRequestTests
{
    private static readonly AiProviders.AiTurn[] Turns =
    {
        new("user", "how do I split a string in C#?"),
    };

    private const string PersonaPrompt = "You are a senior software engineer. Answer with code.";

    [Fact]
    public void BuildChat_Ollama_SystemPrompt_BecomesLeadingSystemMessage()
    {
        var (ok, spec, err) = AiProviders.BuildChat("ollama", "http://localhost:11434", "llama3.2", "", Turns, PersonaPrompt);
        Assert.True(ok, err);
        Assert.Contains("\"stream\":true", spec!.Json);
        Assert.Contains("\"role\":\"system\"", spec.Json);
        Assert.Contains(PersonaPrompt, spec.Json);
        // the system message must lead the conversation
        Assert.True(spec.Json.IndexOf("\"role\":\"system\"", StringComparison.Ordinal) <
                    spec.Json.IndexOf("\"role\":\"user\"", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildChat_Ollama_NoSystemPrompt_OmitsSystemRole()
    {
        var (ok, spec, _) = AiProviders.BuildChat("ollama", null, "llama3.2", "", Turns);
        Assert.True(ok);
        Assert.DoesNotContain("\"role\":\"system\"", spec!.Json);
    }

    [Fact]
    public void BuildChat_Anthropic_SystemPrompt_IsTopLevelField_NeverAMessageRole()
    {
        var (ok, spec, err) = AiProviders.BuildChat("anthropic", null, "claude-haiku-4-5", "sk-ant-secret", Turns, PersonaPrompt);
        Assert.True(ok, err);
        Assert.Contains("\"system\":\"" + PersonaPrompt + "\"", spec!.Json);
        Assert.DoesNotContain("\"role\":\"system\"", spec.Json);
        Assert.DoesNotContain("sk-ant-secret", spec.Json);   // key stays in headers only
        Assert.Contains("\"role\":\"user\"", spec.Json);
    }

    [Fact]
    public void BuildChat_Anthropic_NoSystemPrompt_OmitsSystemField()
    {
        var (ok, spec, _) = AiProviders.BuildChat("anthropic", null, "claude-haiku-4-5", "k", Turns);
        Assert.True(ok);
        Assert.DoesNotContain("\"system\":", spec!.Json);
    }
}
