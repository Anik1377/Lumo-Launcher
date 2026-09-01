using Lumo.Core;
using Lumo.Services;
using Xunit;

namespace Lumo.Tests;

/// <summary>
/// v3.0 — the App Deck: slot normalization, the launch policy, and the store's
/// persistence round-trip (against a temp file, never the real appdeck.json).
/// </summary>
public class DeckTests
{
    // ------------------------------------------------------------ normalization

    [Fact]
    public void Normalize_AssignsTrimmedAndCapped()
    {
        var slot = DeckSlots.Normalize(4, "  my   game ", @"C:\Games\MyGame\game.exe", "  -fullscreen  ", "")!;
        Assert.Equal(4, slot.Index);
        Assert.Equal("my game", slot.Name);           // whitespace collapsed
        Assert.Equal(@"C:\Games\MyGame\game.exe", slot.Target);
        Assert.Equal("-fullscreen", slot.Args);
        Assert.True(slot.IsAssigned);
    }

    [Fact]
    public void Normalize_Caps_Applied()
    {
        var slot = DeckSlots.Normalize(0, new string('n', 100), "C:\\x.exe", new string('a', 500), new string('w', 500))!;
        Assert.Equal(DeckSlots.MaxNameChars, slot.Name.Length);
        Assert.Equal(DeckSlots.MaxArgsChars, slot.Args.Length);
        Assert.Equal(DeckSlots.MaxTargetChars, slot.WorkDir.Length);
    }

    [Fact]
    public void Normalize_EmptyTarget_ClearsSlot()
    {
        var cleared = DeckSlots.Normalize(2, "Whatever", "   ", "", "");
        Assert.NotNull(cleared);
        Assert.False(cleared!.IsAssigned);
        Assert.Equal(2, cleared.Index);
    }

    [Fact]
    public void Normalize_NothingAtAll_ReturnsNull()
    {
        Assert.Null(DeckSlots.Normalize(2, "", "", "", ""));
        Assert.Null(DeckSlots.Normalize(-1, "x", "y", "", ""));
        Assert.Null(DeckSlots.Normalize(9, "x", "y", "", ""));
    }

    [Fact]
    public void DisplayName_FallsBackToSlotNumber()
    {
        var slot = DeckSlots.Normalize(7, "", "C:\\x.exe", "", "")!;
        Assert.Equal("Slot 8", slot.DisplayName);   // UI is 1-based
    }

    // ------------------------------------------------------------ launch policy

    [Fact]
    public void ValidateForLaunch_EmptySlot_TellsTheUserHow()
    {
        var error = DeckSlots.ValidateForLaunch(DeckSlots.Empty(0), _ => true, _ => true);
        Assert.NotNull(error);
        Assert.Contains("assign", error);
    }

    [Fact]
    public void ValidateForLaunch_MissingTarget_PointsAtReassign()
    {
        var slot = DeckSlots.Normalize(0, "Gone", @"C:\Nope\Gone.exe", "", "")!;
        var error = DeckSlots.ValidateForLaunch(slot, _ => false, _ => false);
        Assert.NotNull(error);
        Assert.Contains("Reassign", error);
    }

    [Fact]
    public void ValidateForLaunch_ExistingTargetAndWorkDir_Ok()
    {
        var slot = DeckSlots.Normalize(0, "Ok", @"C:\Apps\app.exe", "", @"C:\Apps")!;
        Assert.Null(DeckSlots.ValidateForLaunch(slot, p => p == @"C:\Apps\app.exe", d => d == @"C:\Apps"));
    }

    [Fact]
    public void ValidateForLaunch_EnvVarTarget_SkipsTheProbe()
    {
        var slot = DeckSlots.Normalize(0, "Env", "%WINDIR%\\notepad.exe", "", "")!;
        Assert.Null(DeckSlots.ValidateForLaunch(slot, _ => false, _ => false));
    }

    [Fact]
    public void BuildStartInfo_EmptySlot_ReturnsNull()
    {
        Assert.Null(DeckSlots.BuildStartInfo(DeckSlots.Empty(3)));
        var slot = DeckSlots.Normalize(3, "X", "C:\\x.exe", "-a -b", "C:\\")!;
        var start = DeckSlots.BuildStartInfo(slot)!;
        Assert.Equal("C:\\x.exe", start.FileName);
        Assert.Equal("-a -b", start.Arguments);
        Assert.True(start.UseShellExecute);
    }

    // ------------------------------------------------------------ store round-trip

    [Fact]
    public void Store_AssignPersistReload_RoundTrips()
    {
        var file = Path.Combine(Path.GetTempPath(), "lumo-tests-deck-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new DeckStore(file);
            store.Assign(DeckSlots.Normalize(0, "Editor", @"C:\Tools\edit.exe", "-n", "")!);
            store.Assign(DeckSlots.Normalize(8, "Terminal", @"C:\Tools\term.exe", "", @"C:\Tools")!);

            // v3.0.0-alpha.4 — synchronous flush that joins the generation ledger:
            // no in-flight background save can overwrite this state with something older
            store.SaveNow();

            var reloaded = new DeckStore(file);
            Assert.Equal(2, reloaded.AssignedCount);
            Assert.Equal("Editor", reloaded.Slot(0).Name);
            Assert.Equal(@"C:\Tools\edit.exe", reloaded.Slot(0).Target);
            Assert.Equal("Terminal", reloaded.Slot(8).Name);
            Assert.False(reloaded.Slot(5).IsAssigned);

            reloaded.Clear(0);
            reloaded.SaveNow();
            Assert.Equal(1, new DeckStore(file).AssignedCount);
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    [Fact]
    public void Store_CorruptFile_LoadsEmpty()
    {
        var file = Path.Combine(Path.GetTempPath(), "lumo-tests-deck-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, "{ not an array ]");
            var store = new DeckStore(file);
            Assert.Equal(0, store.AssignedCount);
            Assert.Equal(9, store.Slots().Count);
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    [Fact]
    public void Store_MissingFile_EmptyDeck()
    {
        var store = new DeckStore(Path.Combine(Path.GetTempPath(), "lumo-tests-deck-none-" + Guid.NewGuid().ToString("N") + ".json"));
        Assert.Equal(0, store.AssignedCount);
    }
}
