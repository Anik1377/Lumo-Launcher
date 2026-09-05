using Lumo.Core;
using Lumo.Services;
using System.Diagnostics;
using Xunit;

namespace Lumo.Tests;

/// <summary>
/// v3.0.0-alpha.6 — the App Deck usability round: pages (multi-mode layouts),
/// the .lumodeck import/export format, the new slot launch options (admin,
/// window mode, counters), the page operations (swap/sort/duplicate/clear) and
/// the app-picker ranking. Pure cores only — no WPF, no real processes.
/// </summary>
public class DeckPagesTests
{
    // ------------------------------------------------------------ DeckPages policy

    [Fact]
    public void NormalizeName_CollapsesCapsAndFallsBack()
    {
        Assert.Equal("my games", DeckPages.NormalizeName("  my   games "));
        Assert.Equal(new string('x', DeckPages.MaxNameChars), DeckPages.NormalizeName(new string('x', 100)));
        Assert.Equal("Page", DeckPages.NormalizeName("   "));
        Assert.Equal("Page", DeckPages.NormalizeName(null));
    }

    [Fact]
    public void UniqueName_KeepsFreshNamesAndSuffixesCollisions()
    {
        Assert.Equal("Games", DeckPages.UniqueName("Games", ["Main"]));
        Assert.Equal("Games (2)", DeckPages.UniqueName("Games", ["Main", "Games"]));
        Assert.Equal("Games (3)", DeckPages.UniqueName("Games", ["Main", "Games", "Games (2)"]));
        // case-insensitive dedupe
        Assert.Equal("GAMES (2)", DeckPages.UniqueName("GAMES", ["games"]));
    }

    [Fact]
    public void NewId_IsBoundedAndGuidShaped()
    {
        int n = 0;
        var id = DeckPages.NewId(() => $"guid{n++}");
        Assert.StartsWith("p", id);
        Assert.True(id.Length <= 24);
    }

    [Fact]
    public void SortSlots_OrdersAssignedByNameAndRenumbers()
    {
        var slots = DeckPages.EmptySlots().ToArray();
        slots[4] = DeckSlots.Normalize(4, "Zebra", "C:\\z.exe", "", "")!;
        slots[0] = DeckSlots.Normalize(0, "alpha", "C:\\a.exe", "", "")!;
        slots[8] = DeckSlots.Normalize(8, "Mango", "C:\\m.exe", "", "")!;

        var sorted = DeckPages.SortSlots(slots);
        Assert.Equal("alpha", sorted[0].Name);
        Assert.Equal(0, sorted[0].Index);
        Assert.Equal("Mango", sorted[1].Name);
        Assert.Equal("Zebra", sorted[2].Name);
        Assert.False(sorted[3].IsAssigned);
        Assert.False(sorted[8].IsAssigned);
    }

    [Fact]
    public void SortSlots_IsStableOnTiesByTarget()
    {
        var slots = DeckPages.EmptySlots().ToArray();
        slots[0] = DeckSlots.Normalize(0, "Same", "C:\\b.exe", "", "")!;
        slots[1] = DeckSlots.Normalize(1, "Same", "C:\\a.exe", "", "")!;
        var sorted = DeckPages.SortSlots(slots);
        Assert.Equal("C:\\a.exe", sorted[0].Target);
        Assert.Equal("C:\\b.exe", sorted[1].Target);
    }

    [Fact]
    public void Templates_ContainTheFourShippedModes()
    {
        var names = DeckPages.Templates.Select(t => t.Name).ToList();
        Assert.Contains("Games", names);
        Assert.Contains("Studio", names);
        Assert.Contains("Office", names);
        Assert.Contains("Entertainment", names);
        Assert.All(DeckPages.Templates, t => Assert.NotEmpty(t.Icon));
    }

    // ------------------------------------------------------------ slot launch options

    [Fact]
    public void Normalize_HonoursAdminWindowAndLaunches()
    {
        var slot = DeckSlots.Normalize(2, "Game", "C:\\g.exe", "", "", admin: true, windowMode: "max", launches: 7)!;
        Assert.True(slot.Admin);
        Assert.Equal("max", slot.WindowMode);
        Assert.Equal(7, slot.Launches);
    }

    [Fact]
    public void Normalize_WindowModeJunk_FallsBackToNormal()
    {
        Assert.Equal("", DeckSlots.CleanWindowMode("weird"));
        Assert.Equal("", DeckSlots.CleanWindowMode(null));
        Assert.Equal("min", DeckSlots.CleanWindowMode("MIN"));
        var slot = DeckSlots.Normalize(0, "X", "C:\\x.exe", "", "", windowMode: "sideways")!;
        Assert.Equal("", slot.WindowMode);
        Assert.Equal(0, slot.Launches < 0 ? 1 : slot.Launches);   // negative launches clamped below
    }

    [Fact]
    public void Normalize_NegativeLaunches_ClampedToZero()
    {
        var slot = DeckSlots.Normalize(0, "X", "C:\\x.exe", "", "", launches: -5)!;
        Assert.Equal(0, slot.Launches);
    }

    [Fact]
    public void BuildStartInfo_AdminVerb_AndWindowStyles()
    {
        var normal = DeckSlots.BuildStartInfo(DeckSlots.Normalize(0, "A", "C:\\a.exe", "", "")!)!;
        Assert.Equal(ProcessWindowStyle.Normal, normal.WindowStyle);
        Assert.Equal("", normal.Verb);

        var admin = DeckSlots.BuildStartInfo(DeckSlots.Normalize(1, "B", "C:\\b.exe", "", "", admin: true)!)!;
        Assert.Equal("runas", admin.Verb);

        var min = DeckSlots.BuildStartInfo(DeckSlots.Normalize(2, "C", "C:\\c.exe", "", "", windowMode: "min")!)!;
        Assert.Equal(ProcessWindowStyle.Minimized, min.WindowStyle);

        var max = DeckSlots.BuildStartInfo(DeckSlots.Normalize(3, "D", "C:\\d.exe", "", "", windowMode: "max")!)!;
        Assert.Equal(ProcessWindowStyle.Maximized, max.WindowStyle);
    }

    // ------------------------------------------------------------ .lumodeck layout JSON

    [Fact]
    public void Layout_WriteRead_RoundTripsPagesAndOptions()
    {
        var slots = DeckPages.EmptySlots().ToArray();
        slots[0] = DeckSlots.Normalize(0, "Blender", "C:\\Tools\\blender.lnk", "-b", "C:\\Tools", admin: true, windowMode: "max")!;
        slots[3] = DeckSlots.Normalize(3, "VS", "C:\\VS\\vs.exe", "", "")!;
        var pages = new List<DeckPages.DeckPage>
        {
            new("p1", "Studio", "\uE722", slots),
            new("p2", "Games", "\uE7FC", DeckPages.EmptySlots()),
        };

        var json = DeckLayout.Write(pages);
        var read = DeckLayout.Read(json);

        Assert.Equal(2, read.Count);
        Assert.Equal("Studio", read[0].Name);
        Assert.Equal("\uE722", read[0].Icon);
        Assert.True(read[0].Slots[0].Admin);
        Assert.Equal("max", read[0].Slots[0].WindowMode);
        Assert.Equal("-b", read[0].Slots[0].Args);
        Assert.Equal("C:\\Tools", read[0].Slots[0].WorkDir);
        Assert.Equal("VS", read[0].Slots[3].Name);
        Assert.False(read[1].Slots[2].IsAssigned);
        // launch counters are session-local — they never travel with an export
        Assert.Equal(0, read[0].Slots[0].Launches);
    }

    [Fact]
    public void Layout_Read_JunkDocuments_AreTolerated()
    {
        Assert.Empty(DeckLayout.Read(""));
        Assert.Empty(DeckLayout.Read("{ not json ]"));
        Assert.Empty(DeckLayout.Read("{\"kind\":\"lumo-deck\",\"pages\":{}}"));     // wrong shape
        Assert.Empty(DeckLayout.Read("[]"));                                        // not an object
        Assert.Empty(DeckLayout.Read("{\"pages\":[{\"name\":\"NoSlots\"}]}"));      // page without slots array
    }

    [Fact]
    public void Layout_Read_BadSlotEntries_AreSkipped()
    {
        var json = """
        { "kind": "lumo-deck", "version": 1, "pages": [
          { "name": "Mixed", "icon": "", "slots": [
            { "i": 0, "t": "C:\\ok.exe", "n": "Ok" },
            { "i": 99, "t": "C:\\out.exe" },
            { "t": "C:\\noindex.exe" },
            { "i": 2, "n": "NoTarget" }
          ] }
        ] }
        """;
        var pages = DeckLayout.Read(json);
        var page = Assert.Single(pages);
        Assert.True(page.Slots[0].IsAssigned);
        Assert.False(page.Slots[2].IsAssigned);   // name-only entry clears
        Assert.Equal("Page", DeckPages.NormalizeName(null));   // name normalize contract
    }

    // ------------------------------------------------------------ store: pages

    [Fact]
    public void Store_LegacyArrayFile_MigratesIntoMainPage()
    {
        var file = TempFile();
        try
        {
            File.WriteAllText(file,
                """[ { "i": 2, "n": "Editor", "t": "C:\\Tools\\edit.exe", "a": "", "w": "" } ]""");
            var store = new DeckStore(file);
            var page = store.ActivePage();
            Assert.Equal(DeckPages.DefaultPageName, page.Name);
            Assert.Equal("Editor", store.Slot(2).Name);
            Assert.Equal(1, store.AssignedCount);
            Assert.Single(store.Pages());
        }
        finally { TryDelete(file); }
    }

    [Fact]
    public void Store_V2RoundTrip_PreservesPagesActiveAndOptions()
    {
        var file = TempFile();
        try
        {
            var store = new DeckStore(file);
            store.Assign(DeckSlots.Normalize(0, "Word", "C:\\Office\\word.exe", "", "", admin: false, windowMode: "min")!);
            var games = store.AddPage("Games", "\uE7FC")!;
            var gamesId = games.Id;
            store.SwitchPage(gamesId);
            store.Assign(DeckSlots.Normalize(4, "Quake", "C:\\Games\\quake.exe", "", "", admin: true, launches: 3)!);
            store.SaveNow();

            var reloaded = new DeckStore(file);
            Assert.Equal(gamesId, reloaded.ActivePageId);
            Assert.Equal("Quake", reloaded.Slot(4).Name);
            Assert.True(reloaded.Slot(4).Admin);
            Assert.Equal(3, reloaded.Slot(4).Launches);
            Assert.True(reloaded.SwitchPage(reloaded.Pages()[0].Id));
            Assert.Equal("Word", reloaded.Slot(0).Name);
            Assert.Equal("min", reloaded.Slot(0).WindowMode);
        }
        finally { TryDelete(file); }
    }

    [Fact]
    public void Store_AddPage_DedupesNames_AndCapsTheDeck()
    {
        var file = TempFile();
        try
        {
            var store = new DeckStore(file);
            var a = store.AddPage("Games", "")!;
            var b = store.AddPage("Games", "")!;
            Assert.NotEqual(a.Name, b.Name);
            Assert.Equal("Games (2)", b.Name);
            Assert.Equal(b.Id, store.ActivePageId);          // new page becomes active

            for (int i = 0; i < DeckPages.MaxPages; i++) store.AddPage($"Fill {i}", "");
            Assert.Null(store.AddPage("One Too Many", ""));
            Assert.Equal(DeckPages.MaxPages, store.Pages().Count);
        }
        finally { TryDelete(file); }
    }

    [Fact]
    public void Store_Rename_DedupesAgainstOtherPages()
    {
        var file = TempFile();
        try
        {
            var store = new DeckStore(file);
            var games = store.AddPage("Games", "")!;
            store.RenamePage(games.Id, "Office");            // collides with nothing
            Assert.Equal("Office", store.Pages().First(p => p.Id == games.Id).Name);

            var other = store.AddPage("Studio", "")!;
            store.RenamePage(other.Id, "Office");            // collides → suffix
            Assert.Equal("Office (2)", store.Pages().First(p => p.Id == other.Id).Name);

            Assert.False(store.RenamePage("missing-id", "X"));
            store.RenamePage(games.Id, "   ");               // blank → generic name, still unique
            Assert.Equal("Page", store.Pages().First(p => p.Id == games.Id).Name);
        }
        finally { TryDelete(file); }
    }

    [Fact]
    public void Store_DeletePage_NeverEmptiesTheDeck()
    {
        var file = TempFile();
        try
        {
            var store = new DeckStore(file);
            string mainId = store.ActivePageId;
            Assert.False(store.DeletePage(mainId));          // the last page is protected
            var games = store.AddPage("Games", "")!;
            Assert.True(store.DeletePage(games.Id));
            Assert.Single(store.Pages());
            Assert.Equal(mainId, store.ActivePageId);

            // deleting the ACTIVE page re-activates the first survivor
            var second = store.AddPage("Studio", "")!;
            Assert.True(store.DeletePage(second.Id));
            Assert.Equal(mainId, store.ActivePageId);
        }
        finally { TryDelete(file); }
    }

    [Fact]
    public void Store_ClearPage_OnlyTouchesTheActivePage()
    {
        var file = TempFile();
        try
        {
            var store = new DeckStore(file);
            store.Assign(DeckSlots.Normalize(0, "Word", "C:\\w.exe", "", "")!);
            var games = store.AddPage("Games", "")!;
            store.Assign(DeckSlots.Normalize(1, "Quake", "C:\\q.exe", "", "")!);
            store.ClearPage();
            Assert.Equal(0, store.AssignedCount);
            Assert.True(store.SwitchPage(store.Pages()[0].Id));
            Assert.Equal(1, store.AssignedCount);
            Assert.Equal("Word", store.Slot(0).Name);
            Assert.False(store.SwitchPage("no-such-page"));
        }
        finally { TryDelete(file); }
    }

    [Fact]
    public void Store_SwapSlots_ExchangesFullSlots()
    {
        var file = TempFile();
        try
        {
            var store = new DeckStore(file);
            store.Assign(DeckSlots.Normalize(0, "A", "C:\\a.exe", "-x", "")!);
            store.Assign(DeckSlots.Normalize(2, "B", "C:\\b.exe", "", "")!);

            Assert.False(store.SwapSlots(0, 0));
            Assert.False(store.SwapSlots(-1, 2));
            Assert.False(store.SwapSlots(0, 9));
            Assert.True(store.SwapSlots(0, 2));

            Assert.Equal("B", store.Slot(0).Name);
            Assert.Equal(0, store.Slot(0).Index);
            Assert.Equal("A", store.Slot(2).Name);
            Assert.Equal("-x", store.Slot(2).Args);
        }
        finally { TryDelete(file); }
    }

    [Fact]
    public void Store_DuplicateSlot_CopiesIntoTheFirstEmptySlot()
    {
        var file = TempFile();
        try
        {
            var store = new DeckStore(file);
            store.Assign(DeckSlots.Normalize(0, "A", "C:\\a.exe", "", "")!);
            store.Assign(DeckSlots.Normalize(1, "B", "C:\\b.exe", "", "")!);

            Assert.Equal(-1, store.DuplicateSlot(5));        // duplicating an empty slot
            int free = store.DuplicateSlot(0);
            Assert.Equal(2, free);
            Assert.Equal("A", store.Slot(2).Name);
            Assert.Equal(0, store.Slot(2).Launches);          // the copy starts fresh

            // fill the page → duplicate reports full
            for (int i = 0; i < DeckSlots.Count; i++)
                store.Assign(DeckSlots.Normalize(i, $"S{i}", $"C:\\s{i}.exe", "", "")!);
            Assert.Equal(-1, store.DuplicateSlot(0));
        }
        finally { TryDelete(file); }
    }

    [Fact]
    public void Store_ImportPages_MergesWithoutOverwriting()
    {
        var file = TempFile();
        try
        {
            var store = new DeckStore(file);
            store.Assign(DeckSlots.Normalize(0, "KeepMe", "C:\\keep.exe", "", "")!);

            var incoming = new List<DeckPages.DeckPage>
            {
                new("x1", "Games", "", DeckPages.EmptySlots()),
                new("x2", "Games", "", DeckPages.EmptySlots()),       // name collision → suffix
            };
            var slots = DeckPages.EmptySlots().ToArray();
            slots[1] = DeckSlots.Normalize(1, "Junk", "C:\\junk.exe", "  spaced  ", "", windowMode: "junk")!;
            incoming.Add(new("x3", "Main", "", slots));                    // Main collision → suffix

            int added = store.ImportPages(incoming);
            Assert.Equal(3, added);
            Assert.Equal(1, store.AssignedCount);                          // nothing was overwritten
            Assert.Equal("KeepMe", store.Slot(0).Name);

            var names = store.Pages().Select(p => p.Name).ToList();
            Assert.Equal(2, names.Count(n => n.StartsWith("Games", StringComparison.Ordinal)));
            Assert.Contains(names, n => n.StartsWith("Main", StringComparison.Ordinal));
            Assert.True(store.SwitchPage(store.Pages().Last().Id));
            Assert.Equal("spaced", store.Slot(1).Args);
            Assert.Equal("", store.Slot(1).WindowMode);                    // junk window mode cleaned

            // the cap is respected
            for (int i = 0; i < DeckPages.MaxPages; i++)
                store.ImportPages([new DeckPages.DeckPage($"bulk{i}", $"Bulk {i}", "", DeckPages.EmptySlots())]);
            Assert.Equal(DeckPages.MaxPages, store.Pages().Count);
        }
        finally { TryDelete(file); }
    }

    [Fact]
    public void Store_CorruptFile_FallsBackToEmptyMainPage()
    {
        var file = TempFile();
        try
        {
            File.WriteAllText(file, "{ v: 2 nonsense ]");
            var store = new DeckStore(file);
            Assert.Equal(0, store.AssignedCount);
            var page = Assert.Single(store.Pages());
            Assert.Equal(DeckPages.DefaultPageId, page.Id);
        }
        finally { TryDelete(file); }
    }

    // ------------------------------------------------------------ app picker ranking

    private static readonly AppEntry A = new("Audacity", @"C:\A\audacity.lnk");
    private static readonly AppEntry B = new("Battle.net", @"C:\B\battle.lnk");
    private static readonly AppEntry C = new("Code", @"C:\C\code.lnk");

    [Fact]
    public void Picker_EmptyQuery_RanksByUsageThenName()
    {
        UsageEntry? Usage(string key) => key switch
        {
            _ when key == B.Path => new UsageEntry(9, DateTime.UtcNow),
            _ when key == C.Path => new UsageEntry(2, DateTime.UtcNow),
            _ => null,
        };
        var result = AppPicker.Filter([A, B, C], "", Usage);
        Assert.Equal([B.Path, C.Path, A.Path], result.Select(r => r.Path));
    }

    [Fact]
    public void Picker_Query_RanksFuzzyMatchesAndDropsTheRest()
    {
        var result = AppPicker.Filter([A, B, C], "code", null);
        var row = Assert.Single(result);
        Assert.Equal("Code", row.Name);

        Assert.Empty(AppPicker.Filter([A, B, C], "zzzznothing", null));
        Assert.Equal(3, AppPicker.Filter([A, B, C], "", null).Count);   // browse shows all
    }

    [Fact]
    public void Picker_UsageBoostsButNeverResurrects()
    {
        UsageEntry? Usage(string key) => key == B.Path ? new UsageEntry(50, DateTime.UtcNow) : null;
        // "bat" prefix-matches Battle.net strongly; Audacity is a weak subsequence — usage must not flip that
        var result = AppPicker.Filter([A, B], "bat", Usage);
        Assert.Equal("Battle.net", result[0].Name);
    }

    [Fact]
    public void Picker_RespectsTheBrowseCap()
    {
        var many = Enumerable.Range(0, 900).Select(i => new AppEntry($"App{i:0000}", $"C:\\A\\app{i:0000}.lnk")).ToList();
        Assert.Equal(AppPicker.BrowseLimit, AppPicker.Filter(many, "", null).Count);
        Assert.Equal(5, AppPicker.Filter(many, "", null, 5).Count);   // the cap applies in browse mode too
        Assert.Single(AppPicker.Filter(many, "app0249", null));       // a precise query matches once
    }

    // ------------------------------------------------------------ helpers

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "lumo-tests-deckpages-" + Guid.NewGuid().ToString("N") + ".json");

    private static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); } catch { }
    }
}
