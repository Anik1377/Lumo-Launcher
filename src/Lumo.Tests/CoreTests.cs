using System.Text.Json;
using Lumo.Core;
using Lumo.Services;
using Xunit;

namespace Lumo.Tests;

// ============================================================================
// Phase 0 (DEV_PLAN Task 0.2) — the pure core, regression-guarded.
// ============================================================================

public class FuzzyTests
{
    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("ABC", "abc")]          // exact match, case-insensitive
    public void Exact_Match_Scores_1000(string q, string t) =>
        Assert.Equal(1000, Fuzzy.Score(q, t));

    [Fact]
    public void Prefix_At_Word_Start_Is_900_Class()
    {
        // "def" starts at a word boundary inside "abc def" → 800 + 100 − idx(4) = 896,
        // i.e. the 900 class (above generic substrings, below exact/prefix-at-0).
        int s = Fuzzy.Score("def", "abc def");
        Assert.InRange(s, 801, 899);
    }

    [Fact]
    public void Subsequence_Match_Is_Positive()
    {
        Assert.True(Fuzzy.Score("abc", "abcdef") > 0);
        Assert.True(Fuzzy.Score("nt", "dotnet") > 0);
    }

    [Fact]
    public void No_Match_Is_Zero()
    {
        Assert.Equal(0, Fuzzy.Score("zzz", "abc"));
        Assert.Equal(0, Fuzzy.Score("xyz", "abc def"));
    }

    [Fact]
    public void Case_Insensitive()
    {
        Assert.True(Fuzzy.Score("ABC", "abc") > 0);
        Assert.Equal(Fuzzy.Score("abc", "ABC"), Fuzzy.Score("ABC", "abc"));
    }

    [Fact]
    public void Empty_Or_Null_Is_Safe()
    {
        Assert.Equal(1, Fuzzy.Score("", "anything"));   // empty query = neutral match
        Assert.Equal(0, Fuzzy.Score("abc", ""));
        Assert.Equal(1, Fuzzy.Score(null, "abc"));      // null query = neutral match
        Assert.Equal(1, Fuzzy.Score(null, null));
    }

    // ---- v2.1 usage blending (DEV_PLAN Task 1.1) -----------------------------

    [Fact]
    public void Usage_Boost_Ranks_Frequent_And_Recent_Higher()
    {
        int fuzzy = 800;
        var often = new UsageEntry(Count: 40, LastUsed: DateTime.UtcNow.AddDays(-1));
        var never = (UsageEntry?)null;

        int boosted = Fuzzy.ScoreWithUsage(fuzzy, often);
        int plain = Fuzzy.ScoreWithUsage(fuzzy, never);

        Assert.True(boosted > plain);
        Assert.Equal(fuzzy, plain);
    }

    [Fact]
    public void Usage_Boost_Is_Capped_And_Never_Negative()
    {
        // A count far above the cap must not run away: ×2 frequency + 0.25 recency max.
        var huge = new UsageEntry(Count: 100_000, LastUsed: DateTime.UtcNow);
        int boosted = Fuzzy.ScoreWithUsage(800, huge);
        Assert.InRange(boosted, 800, 1801);

        // Zero/negative fuzzy scores stay zero/negative — no resurrecting dead rows.
        Assert.Equal(0, Fuzzy.ScoreWithUsage(0, huge));
    }

    [Fact]
    public void Recency_Nudge_Applies_Within_7_Days_Only()
    {
        var recent = new UsageEntry(Count: 0, LastUsed: DateTime.UtcNow.AddDays(-1));
        var stale = new UsageEntry(Count: 0, LastUsed: DateTime.UtcNow.AddDays(-30));

        // Count 0 → boost 1.0; recent gets +0.25 recency nudge, stale does not.
        Assert.True(Fuzzy.ScoreWithUsage(800, recent) > Fuzzy.ScoreWithUsage(800, stale));
    }
}

public class CalculatorTests
{
    private static double EvalToNumber(string expr)
    {
        Assert.True(Calculator.TryEvaluate(expr, out string result),
            $"expected '{expr}' to evaluate, got '{result}'");
        return double.Parse(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Arithmetic()
    {
        Assert.Equal(691200, EvalToNumber("(1920*1080)/3"), 5);
        Assert.Equal(691200, EvalToNumber("C/(1920*1080)/3"), 5);   // C/ prefix stripped
        Assert.Equal(5, EvalToNumber("2+3"), 5);
        Assert.Equal(8, EvalToNumber("2^3"), 5);
    }

    [Fact]
    public void Functions_And_Constants()
    {
        Assert.Equal(32, EvalToNumber("sqrt(2)^10"), 4);
        Assert.Equal(3, EvalToNumber("log(1000)"), 9);
        Assert.Equal(Math.PI, EvalToNumber("pi"), 12);
        Assert.Equal(Math.E, EvalToNumber("e"), 12);
        Assert.Equal(-4, EvalToNumber("abs(2-6)*-1"), 5);           // unary minus composition
    }

    [Fact]
    public void Division_By_Zero_Returns_True_With_Text_Result()
    {
        bool ok = Calculator.TryEvaluate("5/0", out string result);
        Assert.True(ok);
        Assert.Equal("Cannot divide by zero", result);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("2+")]
    [InlineData("sqrt(")]
    [InlineData("")]
    [InlineData("1 2")]
    public void Garbage_Returns_False(string expr)
    {
        Assert.False(Calculator.TryEvaluate(expr, out _));
    }

    [Fact]
    public void Overflowing_Literal_Is_Rejected_Not_Crashed()
    {
        // 320 nines parse past double.MaxValue (~1.8e308) → PositiveInfinity —
        // TryEvaluate must refuse, not crash.
        Assert.False(Calculator.TryEvaluate("sqrt(" + new string('9', 320) + ")", out _));
    }

    [Fact]
    public void Deeply_Nested_Does_Not_Overflow()
    {
        string expr = new string('(', 500) + "1" + new string(')', 500);
        Assert.False(Calculator.TryEvaluate(expr, out _));   // depth guard, not a crash
    }
}

public class SettingsRoundTripTests
{
    private static Settings RoundTrip(Settings s)
    {
        string json = JsonSerializer.Serialize(s);
        return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
    }

    [Fact]
    public void Values_Survive_A_Json_Round_Trip()
    {
        var s = new Settings
        {
            Hotkey = "Ctrl+Shift+L",
            Theme = "light",
            WebEngine = "bing",
            AccentColor = "#FF8800",
            BorderStyle = "Ocean",
            BorderSpeedSec = 6.5,
            GlowOpacity = 0.55,
            RimThickness = 5,
            WindowWidth = 800,
            CornerStyle = "square",
            RowDensity = "compact",
            MaxIndexedFiles = 200_000,
            AnimationsEnabled = false,
        };

        var back = RoundTrip(s);
        Assert.Equal(s.Hotkey, back.Hotkey);
        Assert.Equal(s.Theme, back.Theme);
        Assert.Equal(s.WebEngine, back.WebEngine);
        Assert.Equal(s.AccentColor, back.AccentColor);
        Assert.Equal(s.BorderStyle, back.BorderStyle);
        Assert.Equal(s.BorderSpeedSec, back.BorderSpeedSec);
        Assert.Equal(s.GlowOpacity, back.GlowOpacity);
        Assert.Equal(s.RimThickness, back.RimThickness);
        Assert.Equal(s.WindowWidth, back.WindowWidth);
        Assert.Equal(s.CornerStyle, back.CornerStyle);
        Assert.Equal(s.RowDensity, back.RowDensity);
        Assert.Equal(s.MaxIndexedFiles, back.MaxIndexedFiles);
        Assert.Equal(s.AnimationsEnabled, back.AnimationsEnabled);
    }

    [Fact]
    public void RestoreFrom_Copies_Every_Value()
    {
        var src = new Settings
        {
            Hotkey = "Ctrl+Alt+K", Theme = "auto", AccentColor = "#123456",
            BorderEffect = false, BorderSpeedSec = 12, GlowOpacity = 0.4, RimThickness = 2,
            WindowWidth = 600, CornerStyle = "square", RowDensity = "compact",
        };
        var dst = new Settings();
        dst.RestoreFrom(src);

        Assert.Equal(src.Hotkey, dst.Hotkey);
        Assert.Equal(src.Theme, dst.Theme);
        Assert.Equal(src.AccentColor, dst.AccentColor);
        Assert.Equal(src.BorderEffect, dst.BorderEffect);
        Assert.Equal(src.BorderSpeedSec, dst.BorderSpeedSec);
        Assert.Equal(src.GlowOpacity, dst.GlowOpacity);
        Assert.Equal(src.RimThickness, dst.RimThickness);
        Assert.Equal(src.WindowWidth, dst.WindowWidth);
        Assert.Equal(src.CornerStyle, dst.CornerStyle);
        Assert.Equal(src.RowDensity, dst.RowDensity);
    }

    // ---- tolerant read (the real settings.json survives hand-editing) --------

    [Fact]
    public void Tolerant_Read_Applies_Good_Values_And_Ignores_Bad_Types()
    {
        const string json = """
            {
              "Hotkey": "Ctrl+Shift+Space",
              "Theme": { "oops": true },
              "BorderSpeedSec": "11.5",
              "GlowOpacity": 0.7,
              "MaxIndexedFiles": 999999999,
              "RimThickness": [1, 2],
              "WindowWidth": 640,
              "UnknownFutureKey": 42
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var s = new Settings();
        Settings.ApplyJson(s, doc.RootElement);

        Assert.Equal("Ctrl+Shift+Space", s.Hotkey);       // good string applied
        Assert.Equal("dark", s.Theme);                     // wrong JSON type → default
        Assert.Equal(11.5, s.BorderSpeedSec);              // numeric string tolerated
        Assert.Equal(0.7, s.GlowOpacity);
        Assert.Equal(500_000, s.MaxIndexedFiles);          // clamped to the sane cap
        Assert.Equal(3.0, s.RimThickness);                 // wrong JSON type → default
        Assert.Equal(640, s.WindowWidth);
    }

    [Fact]
    public void Tolerant_Read_Never_Throws_On_Non_Object()
    {
        using var doc = JsonDocument.Parse("[1,2,3]");
        var s = new Settings();
        Settings.ApplyJson(s, doc.RootElement);            // must be a silent no-op
        Assert.Equal("Alt+Space", s.Hotkey);
    }
}

// ---- v2.1 UsageStore (DEV_PLAN Task 1.1) -------------------------------------

public class UsageStoreTests
{
    private static string TempFile()
    {
        string p = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"lumo-usage-{Guid.NewGuid():N}.json");
        return p;
    }

    [Fact]
    public void Record_Increments_And_Persists()
    {
        string file = TempFile();
        try
        {
            var store = new UsageStore(file);
            store.Record(@"C:\Apps\Chrome.lnk");
            store.Record(@"C:\Apps\Chrome.lnk");
            store.Record(@"C:\Apps\Chrome.lnk");
            store.Save();

            var reloaded = new UsageStore(file);
            reloaded.Load();
            var e = reloaded.Get(@"C:\Apps\Chrome.lnk");
            Assert.NotNull(e);
            Assert.Equal(3, e!.Count);

            // case-insensitive key
            Assert.Equal(3, reloaded.Get(@"c:\apps\CHROME.LNK")!.Count);
        }
        finally { try { System.IO.File.Delete(file); } catch { } }
    }

    [Fact]
    public void Corrupt_File_Is_Tolerated()
    {
        string file = TempFile();
        try
        {
            System.IO.File.WriteAllText(file, "{ this is not json !!!");
            var store = new UsageStore(file);
            store.Load();                                   // must not throw
            Assert.Null(store.Get("anything"));
            store.Record("x"); store.Save();                // and the store still works
        }
        finally { try { System.IO.File.Delete(file); } catch { } }
    }

    [Fact]
    public void Higher_Usage_Outranks_Lower_At_Equal_Fuzzy()
    {
        // The ranking property the whole feature rests on.
        int baseScore = Fuzzy.Score("chr", "chrome");
        var high = new UsageEntry(Count: 30, LastUsed: DateTime.UtcNow);
        var low = new UsageEntry(Count: 1, LastUsed: DateTime.UtcNow.AddDays(-20));
        Assert.True(Fuzzy.ScoreWithUsage(baseScore, high) > Fuzzy.ScoreWithUsage(baseScore, low));
    }
}

// ---- v2.2 Favourites (DEV_PLAN Task 2.2) --------------------------------------

public class FavouritesTests
{
    private static string TempFile() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lumo-favs-{Guid.NewGuid():N}.json");

    [Fact]
    public void Add_Remove_Toggle_Semantics()
    {
        var favs = new Favourites(TempFile());
        Assert.True(favs.Add(@"C:\Apps\Chrome.lnk", "Chrome", "Application", "A", "App"));
        Assert.True(favs.IsPinned(@"C:\APPS\CHROME.LNK"));   // OrdinalIgnoreCase keys
        Assert.False(favs.Add(@"C:\Apps\Chrome.lnk", "Chrome", "Application", "A", "App")); // idempotent
        Assert.Equal(1, favs.Count);

        Assert.True(favs.Remove(@"c:\apps\chrome.lnk"));
        Assert.False(favs.IsPinned(@"C:\Apps\Chrome.lnk"));
        Assert.False(favs.Remove(@"C:\Apps\Chrome.lnk"));    // second remove is a no-op
    }

    [Fact]
    public void Empty_Or_Whitespace_Keys_Are_Ignored()
    {
        var favs = new Favourites(TempFile());
        Assert.False(favs.Add("", "x", "", "", "App"));
        Assert.False(favs.Add("   ", "x", "", "", "App"));
        Assert.False(favs.Add(null, "x", "", "", "App"));
        Assert.False(favs.IsPinned(""));
        Assert.False(favs.Remove(null));
        Assert.Equal(0, favs.Count);
    }

    [Fact]
    public void Snapshot_Preserves_Insertion_Order()
    {
        var favs = new Favourites(TempFile());
        favs.Add("a", "First", "", "★", "App");
        favs.Add("b", "Second", "", "★", "File");
        favs.Add("c", "Third", "", "★", "Web");

        var snap = favs.Snapshot();
        Assert.Equal(3, snap.Count);
        Assert.Equal("First", snap[0].Title);
        Assert.Equal("Second", snap[1].Title);
        Assert.Equal("Third", snap[2].Title);
        Assert.Equal("File", snap[1].Kind);
    }

    [Fact]
    public void Save_Load_Round_Trip_Keeps_Order_And_Data()
    {
        string file = TempFile();
        try
        {
            var favs = new Favourites(file);
            favs.Add(@"C:\Tools\build.exe", "build", @"C:\Tools", "A", "App");
            favs.Add("https://github.com/Anik1377/Lumo-Launcher", "Lumo", "web", "W", "Web");
            favs.Save();   // synchronous — no background race in tests

            var reloaded = new Favourites(file);
            reloaded.Load();
            Assert.Equal(2, reloaded.Count);
            var snap = reloaded.Snapshot();
            Assert.Equal(@"C:\Tools\build.exe", snap[0].Key);
            Assert.Equal("build", snap[0].Title);
            Assert.Equal("App", snap[0].Kind);
            Assert.Equal("Lumo", snap[1].Title);
            Assert.True(reloaded.IsPinned(@"HTTPS://GITHUB.COM/ANIK1377/LUMO-LAUNCHER"));
        }
        finally { try { System.IO.File.Delete(file); } catch { } }
    }

    [Fact]
    public void Corrupt_File_Is_Tolerated_And_Store_Still_Works()
    {
        string file = TempFile();
        try
        {
            System.IO.File.WriteAllText(file, "[{ broken json");
            var favs = new Favourites(file);
            favs.Load();                                    // must not throw
            Assert.Equal(0, favs.Count);
            Assert.True(favs.Add("k", "n", "s", "★", "App")); // and the store still works
            favs.Save();
        }
        finally { try { System.IO.File.Delete(file); } catch { } }
    }

    [Fact]
    public void Duplicate_Keys_Skipped_On_Load()
    {
        string file = TempFile();
        try
        {
            System.IO.File.WriteAllText(file,
                """[{"Key":"a","Title":"1","Subtitle":"","Glyph":"★","Kind":"App"},{"Key":"A","Title":"2","Subtitle":"","Glyph":"★","Kind":"App"}]""");
            var favs = new Favourites(file);
            favs.Load();
            Assert.Equal(1, favs.Count);                    // case-insensitive dedupe
            Assert.Equal("1", favs.Snapshot()[0].Title);    // first wins
        }
        finally { try { System.IO.File.Delete(file); } catch { } }
    }

    // v2.2.0-alpha.2 rework — Toggle + display order

    [Fact]
    public void Toggle_Flips_Pin_State()
    {
        var favs = new Favourites(TempFile());
        Assert.True(favs.Toggle("k1", "Name", "Sub", "A", "App"));    // unpinned → pinned
        Assert.True(favs.IsPinned("k1"));
        Assert.False(favs.Toggle("K1", "Name", "Sub", "A", "App"));   // case-insensitive → unpinned
        Assert.Equal(0, favs.Count);
        Assert.False(favs.Toggle(null, "x", "", "", "App"));          // empty keys stay no-ops
    }

    [Fact]
    public void DisplaySnapshot_Shows_Newest_Pin_First()
    {
        var favs = new Favourites(TempFile());
        favs.Add("a", "First", "", "★", "App");
        favs.Add("b", "Second", "", "★", "File");
        favs.Add("c", "Third", "", "★", "Web");

        // display: the pin you just made leads the section
        Assert.Equal(new[] { "Third", "Second", "First" },
                     favs.DisplaySnapshot().Select(f => f.Title).ToArray());

        // storage order is untouched — the JSON file keeps insertion order
        Assert.Equal(new[] { "First", "Second", "Third" },
                     favs.Snapshot().Select(f => f.Title).ToArray());
    }
}

// ---- v2.2.0-alpha.2 RowActions rework (DEV_PLAN Task 2.1) -----------------------

public class RowActionsTests
{
    private static ResultItem Row(ResultKind kind, string arg, string title = "row") =>
        new() { Kind = kind, RunArgument = arg, Title = title };

    [Fact]
    public void App_Menu_Leads_With_Open_And_Ends_With_Pin()
    {
        var list = RowActions.For(Row(ResultKind.App, @"C:\Apps\Chrome.lnk", "Chrome"), pinned: false);
        Assert.Equal(RowAction.Open, list[0]);                 // primary action first
        Assert.Equal(RowAction.Pin, list[^1]);                 // pin sits last, after a separator
        Assert.Contains(RowAction.OpenContainingFolder, list);
        Assert.Contains(RowAction.OpenTerminal, list);
        Assert.Contains(RowAction.CopyPath, list);
        Assert.Contains(RowAction.CopyName, list);
        Assert.Contains(RowAction.RunAsAdmin, list);           // .lnk is elevatable
    }

    [Fact]
    public void Pinned_Row_Offers_Unpin_And_Plain_Text_Files_Never_Elevate()
    {
        var list = RowActions.For(Row(ResultKind.File, @"C:\Docs\readme.txt"), pinned: true);
        Assert.Equal(RowAction.Unpin, list[^1]);
        Assert.DoesNotContain(RowAction.RunAsAdmin, list);     // .txt cannot elevate
    }

    [Fact]
    public void Batch_File_Found_By_Index_Can_Be_Elevated()    // v2.2.0-alpha.2 — File-kind elevation
    {
        var list = RowActions.For(Row(ResultKind.File, @"C:\Scripts\build.bat"), pinned: false);
        Assert.Contains(RowAction.RunAsAdmin, list);
    }

    [Fact]
    public void Tool_Rows_Are_Openable_And_Pinnable_With_No_Path_Actions()
    {
        var list = RowActions.For(Row(ResultKind.Tool, "cmd:mute"), pinned: false);
        Assert.Equal(new List<RowAction> { RowAction.Open, RowAction.Pin }, list);
    }

    [Fact]
    public void Web_Rows_Open_Copy_Their_Url_And_Pin()
    {
        var list = RowActions.For(Row(ResultKind.Web, "https://github.com"), pinned: false);
        Assert.Equal(RowAction.Open, list[0]);
        Assert.Contains(RowAction.CopyPath, list);
        Assert.Equal(RowAction.Pin, list[^1]);
    }

    [Fact]
    public void Management_Commands_Are_Never_Pinnable()
    {
        Assert.False(RowActions.Pinnable(Row(ResultKind.Tool, "cmd:record-stop")));
        Assert.False(RowActions.Pinnable(Row(ResultKind.Tool, "cmd:new-shortcut:work")));
        Assert.False(RowActions.Pinnable(Row(ResultKind.Tool, "cmd:manage-shortcuts")));
        Assert.True(RowActions.Pinnable(Row(ResultKind.Tool, "cmd:app-settings")));   // Settings is a legit favourite
        Assert.True(RowActions.Pinnable(Row(ResultKind.Tool, "cmd:mute")));
    }

    [Fact]
    public void Non_Actionable_Rows_Get_No_Menu_At_All()
    {
        Assert.Empty(RowActions.For(Row(ResultKind.Header, ""), false));
        Assert.Empty(RowActions.For(Row(ResultKind.Hint, "A/"), false));
        Assert.Empty(RowActions.For(Row(ResultKind.Error, ""), false));
        Assert.Empty(RowActions.For(Row(ResultKind.Calculator, "42"), false));
        Assert.Empty(RowActions.For(Row(ResultKind.Clipboard, "copied text"), false));
    }

    [Fact]
    public void Pinnable_Guards_Null_And_Empty_Keys()
    {
        Assert.False(RowActions.Pinnable(null));
        Assert.False(RowActions.Pinnable(new ResultItem { Kind = ResultKind.App, RunArgument = "" }));
    }
}
