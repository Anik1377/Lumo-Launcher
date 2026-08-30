using Lumo.Services;
using Xunit;

namespace Lumo.Tests;

// ------------------------------------------------------------------ v2.3.0-alpha.2 — one-click local AI setup
//
// OllamaManager is mostly I/O (downloads, process launches, HTTP) which can only
// be exercised for real on a Windows box — but its decision surfaces are pure and
// therefore pinned here: the /api/pull NDJSON line parser, the /api/tags parser,
// the curated model catalog and the "is this endpoint local?" guard that decides
// whether the launcher may ever offer the installer.

public class OllamaManagerTests
{
    // ---- ParsePullLine — one NDJSON line of POST /api/pull (stream:true) ----

    [Fact]
    public void ParsePullLayerLine_ExtractsDigestTotalsAndFraction()
    {
        var pl = OllamaManager.ParsePullLine(
            """{"status":"pulling sha256:a3f9","digest":"sha256:a3f9","total":2000000000,"completed":500000000}""");

        Assert.Equal("pulling sha256:a3f9", pl.Status);
        Assert.Equal("sha256:a3f9", pl.Digest);
        Assert.Equal(2_000_000_000L, pl.Total);
        Assert.Equal(500_000_000L, pl.Completed);
        Assert.Equal(0.25, pl.Fraction, 5);
        Assert.False(pl.Done);
    }

    [Fact]
    public void ParsePullSuccessLine_IsDone()
    {
        var pl = OllamaManager.ParsePullLine("""{"status":"success"}""");
        Assert.True(pl.Done);
        Assert.Equal("success", pl.Status);
    }

    [Fact]
    public void ParsePullManifestLine_HasNoDigestNoTotal()
    {
        var pl = OllamaManager.ParsePullLine("""{"status":"pulling manifest"}""");
        Assert.Equal("pulling manifest", pl.Status);
        Assert.Null(pl.Digest);
        Assert.Equal(0, pl.Total);
        Assert.False(pl.Done);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"error\":\"oom\"}")]
    public void ParsePullLine_GarbageFallsBackToEmpty_NeverThrows(string line)
    {
        var pl = OllamaManager.ParsePullLine(line);
        Assert.Equal("", pl.Status);
        Assert.Null(pl.Digest);
        Assert.False(pl.Done);
        Assert.Equal(0, pl.Fraction);
    }

    [Fact]
    public void PullLine_FractionClampsToUnitRange()
    {
        var over = OllamaManager.ParsePullLine(
            """{"status":"x","digest":"d","total":10,"completed":99}""");
        Assert.Equal(1.0, over.Fraction, 5);
    }

    // ---- ParseTags — GET /api/tags (installed models + on-disk sizes) ----

    [Fact]
    public void ParseTags_ReadsNamesAndSizes()
    {
        string json = """
        {
          "models": [
            { "name": "llama3.2:latest", "size": 2019393189, "digest": "a", "modified_at": "2026-01-01" },
            { "name": "qwen2.5:0.5b",    "size": 398000000,  "digest": "b", "modified_at": "2026-01-02" }
          ]
        }
        """;
        var list = OllamaManager.ParseTags(json);
        Assert.Equal(2, list.Count);
        Assert.Equal("llama3.2:latest", list[0].Name);
        Assert.Equal(2_019_393_189L, list[0].Bytes);
        Assert.Equal("qwen2.5:0.5b", list[1].Name);
    }

    [Fact]
    public void ParseTags_SkipsMalformedEntries_ButKeepsGoodOnes()
    {
        string json = """
        {
          "models": [
            42,
            { "size": 100 },
            { "name": "ok-model", "size": 7 }
          ]
        }
        """;
        var list = OllamaManager.ParseTags(json);
        Assert.Single(list);
        Assert.Equal("ok-model", list[0].Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("{\"models\": \"not an array\"}")]
    public void ParseTags_GarbageReturnsEmptyList(string json)
    {
        Assert.Empty(OllamaManager.ParseTags(json));
    }

    // ---- IsLocalEndpoint — the installer may only be offered for local runtimes ----

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("http://localhost:11434", true)]
    [InlineData("localhost:11434", true)]
    [InlineData("http://127.0.0.1:11434", true)]
    [InlineData("http://192.168.1.20:11434", false)]
    [InlineData("http://ai.myserver.net:11434", false)]
    public void IsLocalEndpoint_OnlyLoopbackCounts(string? endpoint, bool expected)
    {
        Assert.Equal(expected, OllamaManager.IsLocalEndpoint(endpoint));
    }

    // ---- Catalog — the curated "recommended lightweight models" list ----

    [Fact]
    public void Catalog_HasDistinctValidIds_WithSaneSizes()
    {
        Assert.NotEmpty(OllamaManager.Catalog);
        var ids = OllamaManager.Catalog.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(OllamaManager.Catalog.Count, ids.Count);

        foreach (var m in OllamaManager.Catalog)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Id));
            Assert.False(string.IsNullOrWhiteSpace(m.Blurb));
            Assert.True(m.SizeGb > 0 && m.SizeGb <= 10, $"{m.Id} size out of range");
            // every id must be a directly pullable ollama tag: "name[:tag]" —
            // "phi3.5" (implicit :latest) is as valid as "llama3.2:1b"
            Assert.Matches("""^[a-z0-9][a-z0-9._-]*(:[a-z0-9._-]+)?$""", m.Id);
        }
    }

    [Fact]
    public void Catalog_IncludesTheLlamaDefaults_AndSmallestModelIsFirst()
    {
        var ids = OllamaManager.Catalog.Select(m => m.Id).ToList();
        Assert.Contains("llama3.2:1b", ids);
        Assert.Contains("llama3.2:3b", ids);
        // ordered smallest-first so the top row is always a safe default
        Assert.Equal(ids, ids.OrderBy(i => OllamaManager.Catalog.First(m => m.Id == i).SizeGb).ToList());
    }

    // ---- Install constants ----

    [Fact]
    public void InstallUrl_IsTheOfficialWindowsInstaller()
    {
        Assert.Equal("https://ollama.com/download/OllamaSetup.exe", OllamaManager.InstallUrl);
    }
}
