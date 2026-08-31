using System.IO;
using Lumo.Core;
using Lumo.Services;
using Xunit;

namespace Lumo.Tests;

// ============================================================================
// v2.6.0-alpha.2 — the FIRST-PARTY plugin catalog: registry parsing, install
// state comparison, the AI authoring prompt, the manifest write path, and a
// consistency check over the repo's own plugins/ folder when it is present.
// ============================================================================

public class FirstPartyCatalogTests
{
    private const string ValidRegistry = """
        {
          "version": 1,
          "plugins": [
            { "id": "dev-search", "name": "Developer Search", "description": "dev lookups",
              "author": "Lumo", "version": "1.0.0",
              "url": "https://raw.githubusercontent.com/Anik1377/Lumo-Launcher/main/plugins/dev-search/plugin.json" },
            { "id": "copy-kit", "name": "Copy Kit", "description": "text templates",
              "author": "Lumo", "version": "1.0.0",
              "url": "https://raw.githubusercontent.com/Anik1377/Lumo-Launcher/main/plugins/copy-kit/plugin.json" }
          ]
        }
        """;

    [Fact]
    public void Parse_Valid_Registry_Returns_Entries()
    {
        Assert.True(FirstParty.TryParseCatalog(ValidRegistry, out var entries, out var error));
        Assert.Null(error);
        Assert.Equal(2, entries.Count);
        Assert.Equal("dev-search", entries[0].Id);
        Assert.Equal("Developer Search", entries[0].Name);
        Assert.Equal("1.0.0", entries[0].Version);
        Assert.StartsWith("https://", entries[0].Url);
    }

    [Fact]
    public void Parse_Bad_Json_And_Wrong_Shape_Report_Errors()
    {
        Assert.False(FirstParty.TryParseCatalog("{ nope", out _, out var e1));
        Assert.Contains("invalid JSON", e1);

        Assert.False(FirstParty.TryParseCatalog("[1,2,3]", out _, out var e2));
        Assert.Contains("plugins", e2);

        Assert.False(FirstParty.TryParseCatalog("""{"updated":"2026-08-31"}""", out _, out var e3));
        Assert.Contains("plugins", e3);
    }

    [Fact]
    public void Parse_Tolerates_Junk_Rows_And_Strictly_Requires_Id_And_Https()
    {
        var json = """
        {
          "plugins": [
            { "name": "no id — skipped" },
            { "id": "http-only", "url": "http://insecure.example/plugin.json" },
            { "id": "ok-1", "url": "https://example.com/p.json", "name": "One", "version": "1.0" },
            "not an object",
            { "id": "OK-1", "url": "https://example.com/dup.json", "name": "Duplicate — id compare is case-insensitive" },
            { "id": "ok-2", "url": "https://example.com/q.json" }
          ]
        }
        """;
        Assert.True(FirstParty.TryParseCatalog(json, out var entries, out _));
        Assert.Equal(2, entries.Count);          // no-id, http:// and the junk row skipped; dup id first-wins
        Assert.Equal("One", entries[0].Name);    // the FIRST occurrence kept the id
        Assert.Equal("ok-2", entries[1].Id);     // name/version default to "" when absent
    }

    [Fact]
    public void Parse_Caps_Entries_At_MaxEntries()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"plugins\":[");
        for (int i = 0; i < FirstParty.MaxEntries + 20; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":\"p").Append(i).Append("\",\"url\":\"https://example.com/").Append(i).Append(".json\"}");
        }
        sb.Append("]}");
        Assert.True(FirstParty.TryParseCatalog(sb.ToString(), out var entries, out var err));
        Assert.Null(err);
        Assert.Equal(FirstParty.MaxEntries, entries.Count);
    }

    [Fact]
    public void Parse_Clips_Long_Fields()
    {
        string longId = new string('x', 100);
        string longName = new string('n', 100);
        var json = "{ \"plugins\": [ { \"id\": \"" + longId + "\", \"url\": \"https://e.com/p.json\", \"name\": \"" + longName + "\" } ] }";
        Assert.True(FirstParty.TryParseCatalog(json, out var entries, out var err));
        Assert.Null(err);
        Assert.Single(entries);
        Assert.Equal(40, entries[0].Id.Length);   // id is sanitized to the manifest cap (40)
        Assert.Equal(61, entries[0].Name.Length); // 60 chars + the '…' clip marker (same as Plugins.Clip)
        Assert.EndsWith("…", entries[0].Name);
    }
}

public class FirstPartyStateTests
{
    private static FirstPartyEntry Entry(string version = "1.0.0") =>
        new() { Id = "p", Url = "https://e.com/p.json", Version = version };

    [Fact]
    public void Missing_When_Not_Installed()
    {
        Assert.Equal(FirstPartyState.Missing, FirstParty.StateFor(Entry(), null));
    }

    [Fact]
    public void Same_Older_Newer_Compare_As_Versions()
    {
        PluginDefinition Def(string v) => new() { Id = "p", Version = v };
        Assert.Equal(FirstPartyState.Same, FirstParty.StateFor(Entry("1.2.0"), Def("1.2.0")));
        Assert.Equal(FirstPartyState.Older, FirstParty.StateFor(Entry("1.2.0"), Def("1.1.0")));   // catalog newer → update available
        Assert.Equal(FirstPartyState.Newer, FirstParty.StateFor(Entry("1.0.0"), Def("2.0")));     // local edit ahead of catalog
        Assert.Equal(FirstPartyState.Older, FirstParty.StateFor(Entry("1.10.0"), Def("1.9.0")));  // numeric, not lexicographic
    }

    [Fact]
    public void Non_Version_Strings_Fall_Back_To_Same_Or_Different()
    {
        PluginDefinition Def(string v) => new() { Id = "p", Version = v };
        Assert.Equal(FirstPartyState.Same, FirstParty.StateFor(Entry("alpha"), Def("alpha")));
        Assert.Equal(FirstPartyState.Different, FirstParty.StateFor(Entry("alpha"), Def("beta")));
    }

    [Fact]
    public void Button_Label_Policy()
    {
        Assert.Equal("Install", FirstParty.ButtonLabel(FirstPartyState.Missing));
        foreach (var s in new[] { FirstPartyState.Same, FirstPartyState.Older, FirstPartyState.Newer, FirstPartyState.Different })
            Assert.Equal("Reinstall", FirstParty.ButtonLabel(s));
    }
}

public class FirstPartyInstallTests
{
    [Fact]
    public void WritePluginManifest_Creates_Folder_Writes_And_Overwrites()
    {
        string root = Path.Combine(Path.GetTempPath(), "lumo-fp-" + Guid.NewGuid().ToString("N"));
        try
        {
            string json1 = """{"name":"One","commands":[{"keyword":"kw1","template":"https://e.com/?q={query}"}]}""";
            FirstPartyStore.WritePluginManifest(root, "my-plugin", json1);

            string path = Path.Combine(root, "my-plugin", Plugins.ManifestFile);
            Assert.True(File.Exists(path));
            Assert.Equal(json1, File.ReadAllText(path));

            // overwrite is the update path — same folder, new payload
            string json2 = """{"name":"Two","commands":[{"keyword":"kw2","template":"https://e.com/?q={query}"}]}""";
            FirstPartyStore.WritePluginManifest(root, "my-plugin", json2);
            Assert.Equal(json2, File.ReadAllText(path));

            // the written manifest parses with the production parser (no tmp leftovers)
            Assert.True(Plugins.TryParse(File.ReadAllText(path), "my-plugin", out var def, out _));
            Assert.NotNull(def);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "my-plugin"), "*.tmp"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Store_FindInstalled_Uses_Sanitized_Ids_And_Is_Case_Insensitive()
    {
        string root = Path.Combine(Path.GetTempPath(), "lumo-fp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "dev-search"));
            File.WriteAllText(Path.Combine(root, "dev-search", Plugins.ManifestFile),
                """{"name":"Developer Search","version":"1.0.0","commands":[{"keyword":"mdn","template":"https://developer.mozilla.org/en-US/search?q={query}"}]}""");

            var settings = new Settings();
            var store = new PluginStore(settings, root);        // scans the temp dir
            var fp = new FirstPartyStore(store, root);

            Assert.True(fp.IsInstalled("dev-search"));
            Assert.True(fp.IsInstalled("DEV-SEARCH"));          // id compare is case-insensitive
            Assert.True(fp.IsInstalled("Dev Search"));          // catalog ids sanitize to the folder id
            Assert.False(fp.IsInstalled("something-else"));
            Assert.Equal("Developer Search", fp.FindInstalled("Dev Search")?.Name);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

public class FirstPartyPromptTests
{
    [Fact]
    public void AiPrompt_Carries_The_Whole_Manifest_Contract()
    {
        Assert.False(string.IsNullOrWhiteSpace(Plugins.AiPrompt));
        Assert.Contains("plugin.json", Plugins.AiPrompt);
        Assert.Contains("{query}", Plugins.AiPrompt);
        Assert.Contains("\"web\"", Plugins.AiPrompt);
        Assert.Contains("\"open\"", Plugins.AiPrompt);
        Assert.Contains("\"copy\"", Plugins.AiPrompt);
        Assert.Contains("argOptional", Plugins.AiPrompt);
        Assert.Contains("keyword", Plugins.AiPrompt);
        // the one thing a user must fill in is clearly marked
        Assert.Contains("DESCRIBE YOUR PLUGIN HERE", Plugins.AiPrompt);
    }

    [Fact]
    public void Starter_Json_Still_Parses()
    {
        Assert.True(Plugins.TryParse(Plugins.StarterJson, "starter", out var def, out _));
        Assert.NotNull(def);
        Assert.Equal(2, def!.Commands.Count);
    }
}

/// <summary>
/// When the tests run inside the repo checkout, the real first-party catalog
/// must be internally consistent: every registry entry resolves to an existing
/// folder whose manifest parses with the production rules, ids match folders,
/// and registry versions match manifest versions.
/// </summary>
public class RepoCatalogConsistencyTests
{
    public static IEnumerable<object[]> RepoPlugins()
    {
        string? dir = AppContext.BaseDirectory;
        string? root = null;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "plugins"))) { root = dir; break; }
            dir = Path.GetDirectoryName(dir);
        }
        if (root is null) yield break;   // running outside the checkout — nothing to verify

        var registry = Path.Combine(root, "plugins", "registry.json");
        Assert.True(FirstParty.TryParseCatalog(File.ReadAllText(registry), out var entries, out var error));
        Assert.Null(error);
        Assert.NotEmpty(entries);
        foreach (var e in entries)
            yield return new object[] { root, e };
    }

    [Theory]
    [MemberData(nameof(RepoPlugins))]
    public void Every_Catalog_Entry_Matches_A_Valid_Manifest(string root, FirstPartyEntry entry)
    {
        // ids are folder names
        Assert.Equal(entry.Id, Plugins.SanitizeId(entry.Id));

        // the url points at the same folder in the repo
        Assert.EndsWith($"/plugins/{entry.Id}/plugin.json", entry.Url);

        // the manifest exists, parses with the production rules, and its version matches the registry
        string manifestPath = Path.Combine(root, "plugins", entry.Id, Plugins.ManifestFile);
        Assert.True(File.Exists(manifestPath), manifestPath);
        string manifest = File.ReadAllText(manifestPath);
        Assert.True(Plugins.TryParse(manifest, entry.Id, out var def, out var parseError),
            $"{entry.Id}: {parseError}");
        Assert.NotNull(def);
        Assert.Equal(entry.Version, def!.Version);
        Assert.True(def.Commands.Count > 0);

        // keywords must be unique ACROSS the whole catalog — the scanner is
        // first-plugin-owns-keyword, so a dup would silently shadow a command
    }

    [Fact]
    public void Catalog_Keywords_Are_Globally_Unique()
    {
        string? dir = AppContext.BaseDirectory;
        string? root = null;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "plugins"))) { root = dir; break; }
            dir = Path.GetDirectoryName(dir);
        }
        if (root is null) return;   // running outside the checkout

        Assert.True(FirstParty.TryParseCatalog(File.ReadAllText(Path.Combine(root, "plugins", "registry.json")),
            out var entries, out _));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            string manifest = File.ReadAllText(Path.Combine(root, "plugins", entry.Id, Plugins.ManifestFile));
            Assert.True(Plugins.TryParse(manifest, entry.Id, out var def, out _));
            foreach (var cmd in def!.Commands)
                Assert.True(seen.Add(cmd.Keyword),
                    $"keyword '{cmd.Keyword}' is defined twice across the first-party catalog");
        }
    }
}
