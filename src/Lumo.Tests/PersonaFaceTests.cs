using Lumo.Core;
using Lumo.Services;
using Xunit;

namespace Lumo.Tests;

/// <summary>
/// v3.0 — persona faces: the catalog invariants, normalization rules, and the
/// PersonaStore round-trip of the new Face/Color fields.
/// </summary>
public class PersonaFaceTests
{
    [Fact]
    public void Catalog_UniqueIds_ValidShapes()
    {
        var ids = PersonaFaces.All.Select(f => f.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(ids.Count >= 8);
        Assert.All(PersonaFaces.All, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Body));
            Assert.False(string.IsNullOrWhiteSpace(f.Eyes));
            Assert.False(string.IsNullOrWhiteSpace(f.Mouth));
            Assert.False(string.IsNullOrWhiteSpace(f.Name));
        });
    }

    [Fact]
    public void Resolve_UnknownOrEmpty_FallsBackToDefault()
    {
        Assert.Equal(PersonaFaces.DefaultFace, PersonaFaces.Resolve(null).Id);
        Assert.Equal(PersonaFaces.DefaultFace, PersonaFaces.Resolve("").Id);
        Assert.Equal(PersonaFaces.DefaultFace, PersonaFaces.Resolve("no-such-face").Id);
        Assert.Equal("spark", PersonaFaces.Resolve("SPARK").Id);   // case-insensitive
    }

    [Fact]
    public void NormalizeId_UnknownPersistsAsEmpty()
    {
        Assert.Equal("", PersonaFaces.NormalizeId(null));
        Assert.Equal("", PersonaFaces.NormalizeId("junk"));
        Assert.Equal("bot", PersonaFaces.NormalizeId("  BOT "));
    }

    [Fact]
    public void NormalizeColor_StrictHexOrEmpty()
    {
        Assert.Equal("#57C1FF", PersonaFaces.NormalizeColor("#57c1ff"));
        Assert.Equal("#57C1FF", PersonaFaces.NormalizeColor("#AA57C1FF"));   // alpha folded off
        Assert.Equal("", PersonaFaces.NormalizeColor("red"));
        Assert.Equal("", PersonaFaces.NormalizeColor("#12345"));
        Assert.Equal("", PersonaFaces.NormalizeColor(null));
    }

    [Fact]
    public void Builtins_CarryFaces()
    {
        // every built-in persona has a known face id and a valid (possibly empty) color
        Assert.All(ChatPersonas.All, p =>
        {
            Assert.True(PersonaFaces.Find(p.Face) is not null, p.Id + " face");
            Assert.True(p.Color.Length == 0 || ThemeFile.IsValidHex(p.Color), p.Id + " color");
        });
        Assert.Equal("spark", ChatPersonas.Default.Face);
    }

    [Fact]
    public void PersonaStore_RoundTripsFaceAndColor()
    {
        var file = Path.Combine(Path.GetTempPath(), "lumo-tests-personas-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new PersonaStore(file);
            var created = store.Add("Nauti", "\uE99A", "You are Nauti, a salty sea navigator.", "Sailing-flavored answers", "cat", "#57C1FF");
            Assert.NotNull(created);
            Assert.Equal("cat", created!.Face);
            Assert.Equal("#57C1FF", created.Color);
            store.Save();   // synchronous — deterministic for tests

            var reloaded = PersonaStore.Load(file).All.Single();
            Assert.Equal("cat", reloaded.Face);
            Assert.Equal("#57C1FF", reloaded.Color);
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    [Fact]
    public void PersonaStore_JunkFaceAndColor_NormalizeToEmpty()
    {
        var file = Path.Combine(Path.GetTempPath(), "lumo-tests-personas-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new PersonaStore(file);
            var created = store.Add("Junk", "", "A prompt.", "", "no-such-face", "not-a-color")!;
            Assert.Equal("", created.Face);
            Assert.Equal("", created.Color);
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }
}
