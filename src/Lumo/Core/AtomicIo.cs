using System.IO;

namespace Lumo.Core;

/// <summary>
/// v3.0.0-alpha.4 — the ONE swap every store uses: tmp → final, with the
/// reader-safety guarantee that Windows' plain Move can't give.
///
/// MoveFileEx(REPLACE_EXISTING) is delete-then-rename: a reader can observe the
/// file ABSENT between the two steps, and every tolerant loader in this app
/// turns that into "corrupt ⇒ empty store" (the ChatStoreTests "Expected 1,
/// Actual 0" and DeckTests "Expected 1, Actual 2" CI flakes — and the same
/// silent reset could hit a user's real data when a read raced a save).
/// File.Replace is an atomic metadata swap on NTFS: readers see either the old
/// or the new content, never absence. Move stays as the first-write fallback,
/// and the plain Move is the last-resort exit for filesystems that reject
/// Replace. Steps retry past the Windows AV scan that briefly holds a freshly
/// written file (the alpha.6 doctrine).
/// </summary>
public static class AtomicIo
{
    /// <summary>Atomically moves <paramref name="tmp"/> onto <paramref name="file"/>.</summary>
    public static void Swap(string tmp, string file)
    {
        for (int attempt = 1; ; attempt++)
        {
            bool last = attempt >= 4;
            try
            {
                if (!last && File.Exists(file)) File.Replace(tmp, file, null);   // atomic on NTFS
                else File.Move(tmp, file, overwrite: true);
                return;
            }
            catch (Exception ex) when (!last && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(120 * attempt);
            }
        }
    }

    /// <summary>Read with brief retries — a concurrent swap must surface as a
    /// retry, never as the loader's "corrupt ⇒ empty" fallback.</summary>
    public static string ReadWithRetry(string path, int attempts = 3)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) when (attempt < attempts) { Thread.Sleep(60 * attempt); }
        }
    }

    /// <summary>Orphan-tmp sweep that only reaps STALE files: a tmp from a
    /// concurrent in-flight save is live, and deleting it mid-write causes
    /// exactly the failure the sweep exists to prevent.</summary>
    public static void SweepStaleTmps(string file, TimeSpan? age = null)
    {
        try
        {
            string? dir = Path.GetDirectoryName(file);
            if (dir is null) return;
            string name = Path.GetFileName(file);
            var floor = age ?? TimeSpan.FromSeconds(10);
            foreach (var orphan in Directory.GetFiles(dir, name + ".*.tmp"))
            {
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(orphan) > floor)
                        File.Delete(orphan);
                }
                catch { /* best-effort sweep */ }
            }
        }
        catch { /* best-effort sweep */ }
    }
}
