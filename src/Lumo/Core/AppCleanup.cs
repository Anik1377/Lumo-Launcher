using System.IO;
using Lumo.Services;

namespace Lumo.Core;

/// <summary>
/// v3.0.0-alpha.5 — "Storage &amp; maintenance": the Lumo-owned files that quietly
/// grow on disk and deserve a one-click mop. Four locations, every one of them
/// safe to clear while Lumo runs:
///
///   · log      — the diagnostics log (AppPaths.LogFile). Cleared in place
///                (truncated, file kept) so an open writer never sees a hole.
///   · updates  — AppPaths.UpdatesDir: staged "Lumo-launcher-v*.zip" downloads
///                plus their download-*.tmp leftovers. A staged-but-not-yet-
///                installed zip is re-downloadable, so clearing costs nothing
///                but bandwidth.
///   · temp     — %TEMP%\Lumo: the Ollama installer downloads and every other
///                scratch file Lumo stages in the system temp.
///   · whisper  — WhisperEngine's voice model cache (AppPaths.DataDir\models):
///                the one-time ggml downloads behind voice typing. Re-downloaded
///                on demand, but the user should know the next voice session
///                pays for it again.
///
/// Pure policy + bounded I/O: sizes are computed with a hard file cap, every
/// clear returns (ok, error, freed) instead of throwing, and nothing here ever
/// touches the UI thread requirement — callers Task.Run the scan.
/// </summary>
public static class AppCleanup
{
    public sealed record CleanupItem(string Id, string Label, string Path, long Bytes, bool Exists, string Hint)
    {
        /// <summary>True when there is something on disk worth clearing.</summary>
        public bool Clearable => Exists && Bytes > 0;
    }

    /// <summary>Hard cap on files counted per location — a runaway folder can't hang the scan.</summary>
    public const int MaxScannedFiles = 20_000;

    /// <summary>
    /// Scans every known location and returns the rows for the maintenance
    /// card, always in a stable display order. Never throws; unreadable
    /// locations report 0 bytes rather than killing the list.
    /// </summary>
    public static List<CleanupItem> Scan()
    {
        var items = new List<CleanupItem>(4);

        // diagnostics log — a single file
        string logFile = AppPaths.LogFile;
        long logBytes = 0;
        try { if (File.Exists(logFile)) logBytes = new FileInfo(logFile).Length; } catch { }
        items.Add(new CleanupItem("log", "Diagnostics log", logFile, logBytes, File.Exists(logFile),
            "Everything Lumo writes while debugging itself. Clearing it keeps the file, drops the contents."));

        // staged update downloads (the real zips + their download-*.tmp)
        string updatesDir = AppPaths.UpdatesDir;
        long updatesBytes = FolderBytes(updatesDir, out bool updatesExists);
        items.Add(new CleanupItem("updates", "Staged update downloads", updatesDir, updatesBytes, updatesExists,
            "Update zips Lumo downloaded from GitHub Releases. Installed or not, they can always be re-downloaded."));

        // the system-temp scratch folder
        string tempDir = Path.Combine(Path.GetTempPath(), "Lumo");
        long tempBytes = FolderBytes(tempDir, out bool tempExists);
        items.Add(new CleanupItem("temp", "Temp files", tempDir, tempBytes, tempExists,
            "Installers and scratch files Lumo stages in %TEMP%\\Lumo. Safe to clear at any time."));

        // the Whisper voice model cache (re-downloadable, but say so)
        string whisperDir = Path.Combine(AppPaths.DataDir, "models");
        long whisperBytes = FolderBytes(whisperDir, out bool whisperExists);
        items.Add(new CleanupItem("whisper", "Voice model cache", whisperDir, whisperBytes, whisperExists,
            "Downloaded Whisper models for offline voice typing. Clearing frees the space; the next voice session re-downloads the model."));

        return items;
    }

    /// <summary>Bounded recursive byte count of a folder, with an existence flag out.</summary>
    public static long FolderBytes(string? path, out bool exists)
    {
        exists = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        if (!exists) return 0;
        long total = 0;
        int seen = 0;
        var stack = new Stack<string>();
        stack.Push(path!);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    if (++seen > MaxScannedFiles) return total;
                    try { total += new FileInfo(f).Length; } catch { /* unreadable file — skip */ }
                }
                foreach (var d in Directory.EnumerateDirectories(dir)) stack.Push(d);
            }
            catch { /* unreadable dir — skip */ }
        }
        return total;
    }

    /// <summary>
    /// Clears one location by id (as returned by <see cref="Scan"/>). Returns
    /// the freed byte count on success. Never throws; the error string is a
    /// short human reason. Deleting runs file-by-file so a single locked file
    /// costs its own bytes, not the whole operation.
    /// </summary>
    public static (bool Ok, string Error, long Freed) Clear(string? id)
    {
        try
        {
            switch ((id ?? "").Trim().ToLowerInvariant())
            {
                case "log":
                {
                    string file = AppPaths.LogFile;
                    if (!File.Exists(file)) return (true, "", 0);
                    long bytes;
                    try { bytes = new FileInfo(file).Length; } catch { bytes = 0; }
                    File.WriteAllText(file, "");   // truncate in place — an open writer keeps its handle
                    DiagnosticLogger.Log("Cleanup", $"diagnostics log truncated ({bytes} bytes freed)");
                    return (true, "", bytes);
                }

                case "updates":
                {
                    string dir = AppPaths.UpdatesDir;
                    return ClearFolderContents(dir, keepDir: true, label: "staged updates");
                }

                case "temp":
                {
                    string dir = Path.Combine(Path.GetTempPath(), "Lumo");
                    return ClearFolderContents(dir, keepDir: false, label: "temp files");
                }

                case "whisper":
                {
                    string dir = Path.Combine(AppPaths.DataDir, "models");
                    return ClearFolderContents(dir, keepDir: true, label: "voice model cache");
                }

                default:
                    return (false, $"unknown location '{id}'", 0);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Cleanup.Clear", ex);
            return (false, ex.Message, 0);
        }
    }

    /// <summary>Deletes every file (and subfolder) inside <paramref name="dir"/>, tolerating locked files.</summary>
    private static (bool Ok, string Error, long Freed) ClearFolderContents(string dir, bool keepDir, string label)
    {
        try
        {
            if (!Directory.Exists(dir)) return (true, "", 0);

            long freed = 0;
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                long len = 0;
                try { len = new FileInfo(f).Length; } catch { }
                try { File.Delete(f); freed += len; }
                catch { /* locked or in use — leave it, keep the rest */ }
            }
            foreach (var d in Directory.EnumerateDirectories(dir))
            {
                long sub = FolderBytes(d, out _);
                try { Directory.Delete(d, recursive: true); freed += sub; }
                catch { /* partially locked tree — leave it */ }
            }

            if (!keepDir)
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }

            DiagnosticLogger.Log("Cleanup", $"{label} cleared ({freed} bytes freed)");
            return (true, "", freed);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, 0);
        }
    }
}
