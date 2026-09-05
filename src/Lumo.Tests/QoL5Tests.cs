using Lumo.Core;
using Lumo.Services;
using Xunit;

namespace Lumo.Tests;

/// <summary>
/// v3.0.0-alpha.5 — the quality-of-life round: Ollama install/model locations
/// and Lumo's own cache cleanup. Everything here is pure or bounded I/O against
/// a temp folder — the suite runs on any OS, no Ollama, no WPF.
/// </summary>
public class QoL5Tests
{
    // ------------------------------------------------------------ Ollama install args

    [Fact]
    public void BuildInstallArgs_EmptyDir_KeepsClassicFlags()
    {
        string args = OllamaManager.BuildInstallArgs("");
        Assert.Equal("/VERYSILENT /NORESTART /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS", args);
        Assert.DoesNotContain("/DIR", args);
    }

    [Fact]
    public void BuildInstallArgs_NullDir_KeepsClassicFlags()
    {
        Assert.DoesNotContain("/DIR", OllamaManager.BuildInstallArgs(null));
    }

    [Fact]
    public void BuildInstallArgs_CustomDir_AppendsQuotedDIR()
    {
        string args = OllamaManager.BuildInstallArgs(@"D:\AI Tools\Ollama ");
        Assert.Contains("/DIR=\"D:\\AI Tools\\Ollama\"", args);   // trimmed + quoted (spaces are legal)
        Assert.StartsWith("/VERYSILENT", args);
    }

    // ------------------------------------------------------------ Ollama model storage resolution

    [Fact]
    public void ResolveModelsDir_FallsBackToStockLocation()
    {
        string resolved = OllamaManager.ResolveModelsDir(null, null, @"C:\Users\u\AppData\Local");
        Assert.Equal(@"C:\Users\u\AppData\Local" + Path.DirectorySeparatorChar + "Ollama"
            + Path.DirectorySeparatorChar + "models", resolved);
    }

    [Fact]
    public void ResolveModelsDir_UserEnv_Wins()
    {
        string resolved = OllamaManager.ResolveModelsDir(@"D:\models", @"E:\machine", @"C:\L");
        Assert.Equal(@"D:\models", resolved);
    }

    [Fact]
    public void ResolveModelsDir_BlankUserEnv_FallsToMachine()
    {
        string resolved = OllamaManager.ResolveModelsDir("   ", @"E:\machine", @"C:\L");
        Assert.Equal(@"E:\machine", resolved);
    }

    [Fact]
    public void ResolveModelsDir_TreatsNullLikeBlank()
    {
        Assert.Equal(
            OllamaManager.ResolveModelsDir(null, null, @"C:\L"),
            OllamaManager.ResolveModelsDir("", "  ", @"C:\L"));
    }

    // ------------------------------------------------------------ bounded folder sizing

    [Fact]
    public void FolderBytes_MissingFolder_IsZeroAndNotExisting()
    {
        long bytes = OllamaManager.FolderBytes(Path.Combine(Path.GetTempPath(), "lumo-tests-does-not-exist-9f2a"));
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void FolderBytes_SumsRecursively()
    {
        string root = Directory.CreateTempSubdirectory("lumo-size-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "a.bin"), new string('a', 1000));
            string sub = Directory.CreateDirectory(Path.Combine(root, "sub")).FullName;
            File.WriteAllText(Path.Combine(sub, "b.bin"), new string('b', 500));

            Assert.Equal(1500, OllamaManager.FolderBytes(root));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void FolderBytes_RespectsTheFileCap()
    {
        string root = Directory.CreateTempSubdirectory("lumo-cap-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "a.bin"), new string('a', 300));
            File.WriteAllText(Path.Combine(root, "b.bin"), new string('b', 700));
            // cap 1 → the first file found (order is stable per run) and no more
            long bytes = OllamaManager.FolderBytes(root, maxFiles: 1);
            Assert.True(bytes is 300 or 700, $"expected one file's bytes, got {bytes}");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    // ------------------------------------------------------------ Lumo cleanup scan/clear

    [Fact]
    public void Scan_ReturnsTheFourKnownLocations_WithStableIds()
    {
        var items = AppCleanup.Scan();
        Assert.Equal(4, items.Count);
        Assert.Equal(new[] { "log", "updates", "temp", "whisper" }, items.Select(i => i.Id).ToArray());
        Assert.All(items, i =>
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Label));
            Assert.False(string.IsNullOrWhiteSpace(i.Path));
            Assert.False(string.IsNullOrWhiteSpace(i.Hint));
        });
    }

    [Fact]
    public void Clear_UnknownId_FailsGracefully()
    {
        var (ok, err, freed) = AppCleanup.Clear("not-a-location");
        Assert.False(ok);
        Assert.Contains("unknown", err, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, freed);
    }

    [Fact]
    public void Clear_NullId_FailsGracefully()
    {
        var (ok, err, _) = AppCleanup.Clear(null);
        Assert.False(ok);
    }

    [Fact]
    public void Clear_TempLocation_RemovesTheScratchFolder()
    {
        // stage a file exactly where the "temp" cleanup looks
        string dir = Path.Combine(Path.GetTempPath(), "Lumo");
        Directory.CreateDirectory(dir);
        string marker = Path.Combine(dir, $"marker-{Guid.NewGuid():N}.bin");
        File.WriteAllText(marker, new string('x', 1234));

        var (ok, err, freed) = AppCleanup.Clear("temp");
        Assert.True(ok, err);
        Assert.True(freed >= 1234, $"expected ≥1234 freed, got {freed}");
        Assert.False(File.Exists(marker), "the temp marker must be gone");
    }

    [Fact]
    public void Clear_UpdatesLocation_EmptiesButKeepsTheFolder()
    {
        string dir = AppPaths.UpdatesDir;
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"Lumo-launcher-v0.0.0-test-{Guid.NewGuid():N}.zip");
        File.WriteAllText(file, "fake zip");

        var (ok, err, freed) = AppCleanup.Clear("updates");
        Assert.True(ok, err);
        Assert.True(freed >= 8, $"expected the fake zip's bytes freed, got {freed}");
        Assert.False(File.Exists(file));
        Assert.True(Directory.Exists(dir), "the updates folder itself must survive (it is the staging root)");
    }

    [Fact]
    public void CleanupItem_Clearable_RequiresBothExistenceAndBytes()
    {
        var missing = new AppCleanup.CleanupItem("x", "X", "path", 0, Exists: false, Hint: "h");
        var empty = new AppCleanup.CleanupItem("x", "X", "path", 0, Exists: true, Hint: "h");
        var full = new AppCleanup.CleanupItem("x", "X", "path", 10, Exists: true, Hint: "h");
        Assert.False(missing.Clearable);
        Assert.False(empty.Clearable);
        Assert.True(full.Clearable);
    }
}
