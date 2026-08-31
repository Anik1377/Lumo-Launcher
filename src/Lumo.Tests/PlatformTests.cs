using System.IO;
using Lumo.Core;
using Lumo.Native;
using Lumo.Services;
using Xunit;

namespace Lumo.Tests;

// ============================================================================
// v2.5 (DEV_PLAN Phase 4 — "Platform") — PrefixRouter (4.1), the JSON plugin
// system (4.2) and the per-shortcut hotkey parsing (4.3).
// ============================================================================

// ---------------------------------------------------------------- Task 4.1 — router

public class PrefixRouterTests
{
    private sealed class Fake : IPrefixHandler
    {
        private readonly string _prefix;
        private readonly string[] _exact;
        public Fake(string prefix, params string[] exact) { _prefix = prefix; _exact = exact; }
        public string Prefix => _prefix;
        public IEnumerable<string> ExactAliases => _exact;
        public List<ResultItem> Handle(string arg) => new() { new ResultItem { Title = arg } };
    }

    [Fact]
    public void Match_Longest_Prefix_Wins_Regardless_Of_Registration_Order()
    {
        var r = new PrefixRouter();
        r.Register(new Fake("A/"));
        r.Register(new Fake("AI/"));          // registered AFTER "A/" — must still win
        Assert.Equal("AI/", r.Match("ai/hello", out var arg)?.Prefix);
        Assert.Equal("hello", arg);
        Assert.Equal("A/", r.Match("A/chrome", out _)?.Prefix);
    }

    [Fact]
    public void Match_Is_Case_Insensitive_And_Trims_The_Arg()
    {
        var r = new PrefixRouter();
        r.Register(new Fake("W/"));
        Assert.NotNull(r.Match("w/  dotnet 8 ", out var arg));
        Assert.Equal("dotnet 8", arg);
    }

    [Fact]
    public void Match_Exact_Alias_Routes_With_Empty_Arg()
    {
        var r = new PrefixRouter();
        r.Register(new Fake("AI/", "AI"));
        Assert.NotNull(r.Match("ai", out var arg));
        Assert.Equal("", arg);
        Assert.Null(r.Match("air", out _));   // alias is whole-query, not a prefix
    }

    [Fact]
    public void Match_No_Route_And_Empty_Query_Return_Null()
    {
        var r = new PrefixRouter();
        r.Register(new Fake("A/"));
        Assert.Null(r.Match("hello", out _));
        Assert.Null(r.Match("", out _));
        Assert.Null(r.Match("   ", out _));
    }

    [Fact]
    public void Register_Same_Prefix_Replaces_And_Unregister_Removes()
    {
        var r = new PrefixRouter();
        var first = new Fake("P/");
        r.Register(first);
        r.Register(new Fake("P/"));           // replacement, not a duplicate
        Assert.Equal(1, r.Count);
        Assert.True(r.Unregister("p/"));      // case-insensitive removal
        Assert.Equal(0, r.Count);
        Assert.Null(r.Match("P/x", out _));
        Assert.False(r.Unregister("P/"));
    }

    [Fact]
    public void Register_Handles_The_Plugin_Style_Lifecycle()
    {
        // The plugin rescan replaces its handler wholesale — the router must
        // surface the NEW handler's rows after re-registration.
        var r = new PrefixRouter();
        IPrefixHandler current = new Fake("X/");
        r.Register(current);
        current = new Fake("X/");
        r.Register(current);
        Assert.Same(current, r.Match("X/q", out _));
    }
}

// ---------------------------------------------------------------- Task 4.2 — plugin parsing

public class PluginParseTests
{
    private const string ValidJson = """
        {
          "name": "Emoji tools",
          "author": "someone",
          "version": "1.2",
          "commands": [
            { "keyword": "Emo", "name": "Emoji search", "subtitle": "find emojis", "type": "web",
              "template": "https://emojipedia.org/search?q={query}" },
            { "keyword": "time", "type": "open", "template": "https://time.is", "argOptional": true }
          ]
        }
        """;

    [Fact]
    public void TryParse_Valid_Manifest_Round_Trips()
    {
        Assert.True(Plugins.TryParse(ValidJson, "My Emoji Tools", out var def, out var error));
        Assert.Null(error);
        Assert.NotNull(def);
        Assert.Equal("my-emoji-tools", def!.Id);
        Assert.Equal("Emoji tools", def.Name);
        Assert.Equal(2, def.Commands.Count);

        var web = def.Commands[0];
        Assert.Equal("emo", web.Keyword);        // normalized (lowercase)
        Assert.Equal("web", web.Type);
        Assert.False(web.ArgOptional);

        var open = def.Commands[1];
        Assert.Equal("time", open.Keyword);
        Assert.True(open.ArgOptional);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{ \"name\": \"x\" }")]                       // missing commands
    [InlineData("{ \"commands\": [] }")]                      // no commands
    [InlineData("{ \"commands\": [{ \"keyword\": \"x\" }] }")]                    // web without template
    [InlineData("{ \"commands\": [{ \"keyword\": \"x\", \"type\": \"copy\" }] }")] // copy without text
    [InlineData("{ \"commands\": [{ \"keyword\": \"x\", \"type\": \"macro\", \"template\": \"y\" }] }")] // bad type
    public void TryParse_Rejects_Broken_Manifests_With_An_Error_Not_A_Throw(string json)
    {
        Assert.False(Plugins.TryParse(json, "folder", out var def, out var error));
        Assert.Null(def);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_Rejects_Too_Many_Commands()
    {
        var sb = new System.Text.StringBuilder("{ \"commands\": [");
        for (int i = 0; i <= Plugins.MaxCommandsPerPlugin; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"{{ \"keyword\": \"k{i}\", \"template\": \"https://x/{i}\" }}");
        }
        sb.Append("] }");
        Assert.False(Plugins.TryParse(sb.ToString(), "f", out _, out var error));
        Assert.Contains("24", error);
    }

    [Theory]
    [InlineData("Emoji Search", "emoji-search")]
    [InlineData("  spaced  name  ", "spaced-name")]
    [InlineData("UPPER", "upper")]
    [InlineData("dots.dot", "dotsdot")]
    [InlineData("double--dash", "double-dash")]
    [InlineData("-leading-trailing-", "leading-trailing")]
    public void SanitizeId_Makes_Folder_Names_Safe_Ids(string folder, string expected)
        => Assert.Equal(expected, Plugins.SanitizeId(folder));

    [Theory]
    [InlineData("GH", "gh")]
    [InlineData("my key", "my-key")]
    [InlineData("--x--", "x")]
    [InlineData("", null)]
    [InlineData("way-too-long-keyword-over-24-chars", null)]
    [InlineData("has/slash", null)]
    public void TryNormalizeKeyword_Normalizes_Or_Rejects(string input, string? expected)
    {
        bool ok = Plugins.TryNormalizeKeyword(input, out var normalized);
        Assert.Equal(expected is not null, ok);
        if (expected is not null) Assert.Equal(expected, normalized);
    }

    [Fact]
    public void Expand_Substitutes_Query()
    {
        Assert.Equal("https://x/q/a b", Plugins.Expand("https://x/q/{query}", "a b"));
        Assert.Equal("https://x/q/", Plugins.Expand("https://x/q/{query}", ""));
        Assert.Equal("plain", Plugins.Expand("plain", "zzz"));
    }

    [Theory]
    [InlineData("emo", "emo", true)]
    [InlineData("EMO sunset", "emo", true)]
    [InlineData("emo  ", "emo", true)]      // trimmed by caller, but raw token+space also routes
    [InlineData("emot", "emo", false)]      // strict token — no partial-word stealing
    [InlineData("", "emo", false)]
    [InlineData("em o", "emo", false)]
    public void KeywordRoutes_Is_Token_Exact(string query, string keyword, bool expected)
        => Assert.Equal(expected, Plugins.KeywordRoutes(query, keyword));

    [Fact]
    public void Starter_Json_Is_A_Valid_Plugin_By_Itself()
    {
        // The "copy starter" flow must never hand the user a broken manifest.
        Assert.True(Plugins.TryParse(Plugins.StarterJson, "starter", out var def, out var error));
        Assert.Null(error);
        Assert.True(def!.Commands.Count >= 2);
    }
}

// ---------------------------------------------------------------- Task 4.2 — the store (temp dir, no real %APPDATA%)

public class PluginStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly Settings _settings = new();

    public PluginStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumo-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private void WritePlugin(string folder, string json)
    {
        string path = Path.Combine(_dir, folder);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, Plugins.ManifestFile), json);
    }

    [Fact]
    public void Scan_Finds_Valid_Plugins_And_Skips_Broken_Files()
    {
        WritePlugin("good", Plugins.StarterJson);
        WritePlugin("bad", "{ not json");
        Directory.CreateDirectory(Path.Combine(_dir, "empty-folder"));   // no manifest — ignored

        var store = new PluginStore(_settings, _dir);
        Assert.Equal(1, store.Count);
        Assert.Equal("good", store.All()[0].Id);   // the id comes from the FOLDER name, not the manifest
    }

    [Fact]
    public void First_Plugin_Owns_A_Duplicate_Keyword()
    {
        WritePlugin("aaa", """{ "commands": [{ "keyword": "dupe", "template": "https://a/{query}" }] }""");
        WritePlugin("bbb", """{ "commands": [{ "keyword": "DUPE", "template": "https://b/{query}" }] }""");
        var store = new PluginStore(_settings, _dir);

        Assert.Equal(1, store.Count);                        // "bbb" had ONLY the dupe keyword → skipped wholesale
        Assert.True(store.TryRoute("dupe x", out var def, out _, out _));
        Assert.Equal("aaa", def.Id);                         // "aaa" (alphabetical) owns the token
    }

    [Fact]
    public void Disabled_Plugins_Are_Invisible_To_Routing()
    {
        WritePlugin("good", """{ "commands": [{ "keyword": "kw", "template": "https://x/{query}" }] }""");
        _settings.DisabledPlugins.Add("good");

        var store = new PluginStore(_settings, _dir);
        Assert.Equal(1, store.Count);        // still listed in All()…
        Assert.Equal(0, store.Enabled().Count);
        Assert.False(store.TryRoute("kw x", out _, out _, out _));   // …but invisible to search
        Assert.False(store.FindCommand("good", "kw", out _, out _));
        Assert.False(store.TryRouteExact("plugin:good:kw hello", out _, out _, out _));
    }

    [Fact]
    public void TryRouteExact_Parses_The_RunArgument_Shape()
    {
        WritePlugin("tools", """{ "commands": [{ "keyword": "so", "name": "Stack Overflow", "template": "https://so/{query}" }] }""");
        var store = new PluginStore(_settings, _dir);

        Assert.True(store.TryRouteExact("plugin:tools:so pointer events", out var def, out var cmd, out var arg));
        Assert.Equal("tools", def.Id);
        Assert.Equal("so", cmd.Keyword);
        Assert.Equal("pointer events", arg);

        Assert.True(store.TryRouteExact("plugin:tools:so", out _, out _, out var bare));
        Assert.Equal("", bare);
        Assert.False(store.TryRouteExact("plugin:missing:so", out _, out _, out _));
        Assert.False(store.TryRouteExact("cmd:lock", out _, out _, out _));
    }

    [Fact]
    public void EnsureFresh_Rescans_When_The_Directory_Changes()
    {
        var store = new PluginStore(_settings, _dir);
        Assert.Equal(0, store.Count);

        WritePlugin("late", Plugins.StarterJson);
        Directory.SetLastWriteTimeUtc(_dir, DateTime.UtcNow);   // the probe watches the dir mtime
        store.EnsureFresh();
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void SetEnabled_Reflects_In_IsEnabled_Without_Rescan()
    {
        WritePlugin("p", """{ "commands": [{ "keyword": "k", "template": "https://x/{query}" }] }""");
        var store = new PluginStore(_settings, _dir);
        Assert.True(store.IsEnabled("p"));

        store.SetEnabled("p", enabled: false);
        Assert.False(store.IsEnabled("p"));
        Assert.Contains("p", _settings.DisabledPlugins);

        store.SetEnabled("p", enabled: true);
        Assert.True(store.IsEnabled("p"));
    }
}

// ---------------------------------------------------------------- Task 4.3 — hotkey parsing pins

public class ShortcutHotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+G", true)]
    [InlineData("Ctrl+Shift+5", true)]
    [InlineData("Win+F9", true)]
    [InlineData("Alt+Space", true)]
    [InlineData("ctrl+alt+g", true)]     // the parser is case-insensitive
    [InlineData("G", false)]             // bare key — would hijack typing
    [InlineData("", false)]
    [InlineData("Ctrl+", false)]
    [InlineData("Ctrl+Num5", false)]     // the capture UI must never produce this
    [InlineData("Ctrl+F25", false)]      // F1–F24 only
    public void TryParseCombo_Accepts_Exactly_What_The_Editor_Can_Produce(string combo, bool expected)
        => Assert.Equal(expected, HotkeyService.TryParseCombo(combo, out _, out _));

    [Theory]
    [InlineData("Ctrl+Alt+G", true)]
    [InlineData("Win+F9", true)]
    [InlineData("Alt+Space", true)]
    [InlineData("Shift+G", false)]       // parses, but registration refuses — global hijack risk
    [InlineData("G", false)]
    public void IsRegistrableCombo_Requires_Ctrl_Or_Alt_Or_Win(string combo, bool expected)
        => Assert.Equal(expected, HotkeyService.IsRegistrableCombo(combo));

    [Fact]
    public void Shortcut_Hotkey_Ids_Never_Collide_With_The_Main_Hotkey()
    {
        Assert.NotEqual(HotkeyService.HotkeyId, HotkeyService.ShortcutHotkeyBase);
        Assert.True(HotkeyService.MaxShortcutHotkeys > 0);
    }

    [Fact]
    public void ShortcutDef_Hotkey_Round_Trips_Through_Json()
    {
        // ShortcutDef is serialized by ShortcutStore verbatim — the new property
        // must survive a JSON round trip (and default to "" for old files).
        var def = new ShortcutDef { Name = "work", Type = "url", Target = "https://x", Hotkey = "Ctrl+Alt+W" };
        var json = System.Text.Json.JsonSerializer.Serialize(def);
        var back = System.Text.Json.JsonSerializer.Deserialize<ShortcutDef>(json);
        Assert.NotNull(back);
        Assert.Equal("Ctrl+Alt+W", back!.Hotkey);
        Assert.Equal("", new ShortcutDef().Hotkey);
    }
}
