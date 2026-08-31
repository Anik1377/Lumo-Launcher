using System.IO;
using System.Text.Json;
using Lumo.Core;
using Lumo.Services;
using Xunit;

namespace Lumo.Tests;

// ------------------------------------------------ v2.4.0-alpha.6 — custom personas
//
// The Settings editor and the chat's persona flyout are WPF-only, but the store
// under them is pure and pinned here: round-trip, the custom_ id namespace,
// add/update/delete semantics, the caps, and the corrupt-file tolerance that
// every Lumo store shares.

public class PersonaStoreTests : IDisposable
{
    private readonly string _file;

    public PersonaStoreTests()
    {
        _file = Path.Combine(Path.GetTempPath(), $"lumo-personas-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { if (File.Exists(_file)) File.Delete(_file); } catch { }
    }

    [Fact]
    public void Add_TrimsFields_MintsCustomId_AndPersists()
    {
        var store = new PersonaStore(_file);
        var p = store.Add("  Code Buddy  ", " 🤖 ", "  Answer with code only.  ", "irrelevant");
        Assert.NotNull(p);
        Assert.StartsWith(ChatPersonas.CustomPrefix, p!.Id);
        Assert.Equal("Code Buddy", p.Name);
        Assert.Equal("🤖", p.Glyph);
        Assert.Equal("Answer with code only.", p.Prompt);
        store.Save();

        var reloaded = PersonaStore.Load(_file);
        Assert.Equal(1, reloaded.Count);
        Assert.Equal(p.Id, reloaded.All[0].Id);
        Assert.Equal("Answer with code only.", reloaded.All[0].Prompt);
    }

    [Theory]
    [InlineData("", "some prompt")]
    [InlineData("   ", "some prompt")]
    [InlineData("name", "")]
    [InlineData("name", "   ")]
    public void Add_EmptyNameOrPrompt_IsRejected(string name, string prompt)
    {
        var store = new PersonaStore(_file);
        Assert.Null(store.Add(name, "", prompt, ""));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Add_EmptyGlyph_GetsTheContactDefault()
    {
        var store = new PersonaStore(_file);
        var p = store.Add("n", "", "prompt body that is long enough", "");
        Assert.Equal("\uE77B", p!.Glyph);
    }

    [Fact]
    public void Update_RewritesFields_UnknownIdReturnsFalse()
    {
        var store = new PersonaStore(_file);
        var p = store.Add("first", "", "original prompt text", "");
        Assert.NotNull(p);

        Assert.True(store.Update(p!.Id, "second", "\uE943", "rewritten prompt", "blurb"));
        Assert.Equal("second", store.Find(p.Id)!.Name);
        Assert.Equal("rewritten prompt", store.Find(p.Id)!.Prompt);
        Assert.Equal("rewritten prompt", store.Find(p.Id)!.Prompt);

        Assert.False(store.Update("custom_nope", "x", "", "y", ""));
    }

    [Fact]
    public void Update_EmptyNameOrPrompt_IsRejected()
    {
        var store = new PersonaStore(_file);
        var p = store.Add("first", "", "original prompt", "");
        Assert.False(store.Update(p!.Id, "  ", "", "still the old one", ""));
        Assert.Equal("original prompt", store.Find(p.Id)!.Prompt);   // unchanged
    }

    [Fact]
    public void Delete_RemovesOnlyTheTarget()
    {
        var store = new PersonaStore(_file);
        var a = store.Add("a", "", "prompt a", "")!;
        var b = store.Add("b", "", "prompt b", "")!;
        Assert.True(store.Delete(a.Id));
        Assert.False(store.Delete(a.Id));                 // idempotent no-op
        Assert.Null(store.Find(a.Id));
        Assert.NotNull(store.Find(b.Id));
    }

    [Fact]
    public void Cap_RejectsNewPersonasWhenFull_NeverDropsExisting()
    {
        var store = new PersonaStore(_file);
        for (int i = 0; i < PersonaStore.MaxPersonas; i++)
            Assert.NotNull(store.Add($"persona {i}", "", $"prompt {i}", ""));
        Assert.Null(store.Add("overflow", "", "prompt overflow", ""));   // full → rejected
        Assert.Equal(PersonaStore.MaxPersonas, store.Count);
        Assert.Contains(store.All, p => p.Name == "persona 0");          // nothing was dropped silently
    }

    [Fact]
    public void CorruptFile_LoadsAsEmpty_NeverThrows()
    {
        File.WriteAllText(_file, "[{ not json ]]");
        var store = PersonaStore.Load(_file);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void MissingFile_LoadsAsEmpty()
    {
        Assert.Equal(0, PersonaStore.Load(_file).Count);
    }

    [Fact]
    public void NonObjectEntries_AreSkipped_Tolerantly()
    {
        File.WriteAllText(_file, "[\"nope\", 42, null, {\"Id\":\"custom_ok1\",\"Name\":\"Ok\",\"Glyph\":\"\",\"Prompt\":\"p\",\"Blurb\":\"\"}]");
        var store = PersonaStore.Load(_file);
        Assert.Equal(1, store.Count);
        Assert.Equal("custom_ok1", store.All[0].Id);
    }

    [Fact]
    public void ResolveWith_CustomPersonasFlowThroughTheWindowContract()
    {
        // the exact call shape the AI chat window makes every time it renders a chip
        var custom = new PersonaStore(_file).Add("persona", "", "prompt", "")!;
        var customList = new List<ChatPersona> { custom };
        var resolved = ChatPersonas.ResolveWith(custom.Id, customList);
        Assert.Equal(custom.Name, resolved.Name);
        // and after a delete, chats fall back to the default, never throw
        Assert.Same(ChatPersonas.Default, ChatPersonas.ResolveWith("custom_gone", customList));
    }
}
