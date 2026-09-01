using Lumo.Core;
using System.Diagnostics;
using System.Text.Json;

namespace Lumo.Services;

/// <summary>
/// v3.0 — App Deck persistence: nine quick-launch slots in appdeck.json.
/// Follows the store doctrine (PersonaStore/ChatStore): hand-parsed JSON,
/// corrupt file ⇒ empty deck, single-flight background save through a unique
/// tmp + atomic rename, value-returned launch (never throws to the UI).
/// </summary>
public sealed class DeckStore
{
    public static DeckStore Current { get; } = new();

    private readonly string _file;
    private readonly DeckSlots.Slot[] _slots = new DeckSlots.Slot[DeckSlots.Count];
    private int _saving;

    public DeckStore(string? file = null)
    {
        _file = string.IsNullOrWhiteSpace(file) ? AppPaths.DeckFile : file;
        for (int i = 0; i < DeckSlots.Count; i++) _slots[i] = DeckSlots.Empty(i);
        LoadInto(_slots, _file);
    }

    // ------------------------------------------------------------ access

    /// <summary>A snapshot copy of all nine slots (definition order 1..9).</summary>
    public IReadOnlyList<DeckSlots.Slot> Slots()
    {
        var copy = new DeckSlots.Slot[DeckSlots.Count];
        lock (_slots) Array.Copy(_slots, copy, DeckSlots.Count);
        return copy;
    }

    public DeckSlots.Slot Slot(int index)
    {
        index = Math.Clamp(index, 0, DeckSlots.Count - 1);
        lock (_slots) return _slots[index];
    }

    /// <summary>Saves the slot (null = clear it). Returns the stored slot, or null
    /// when the edit normalized to "nothing to save".</summary>
    public DeckSlots.Slot? Assign(DeckSlots.Slot slot)
    {
        if (slot.Index < 0 || slot.Index >= DeckSlots.Count) return null;
        lock (_slots)
        {
            _slots[slot.Index] = slot;
            ScheduleSave();
            return slot;
        }
    }

    public void Clear(int index)
    {
        index = Math.Clamp(index, 0, DeckSlots.Count - 1);
        lock (_slots)
        {
            _slots[index] = DeckSlots.Empty(index);
            ScheduleSave();
        }
    }

    public int AssignedCount
    {
        get { lock (_slots) return _slots.Count(s => s.IsAssigned); }
    }

    // ------------------------------------------------------------ launch

    /// <summary>
    /// Launches slot <paramref name="index"/> (0-based). Returns a readable error
    /// string on failure, null on success (or when the slot is empty — nothing to do
    /// is not an error). Logs result codes only; never throws to the caller.
    /// </summary>
    public string? Launch(int index, Action<string>? report = null)
    {
        try
        {
            var slot = Slot(index);
            if (!slot.IsAssigned) return null;

            var problem = DeckSlots.ValidateForLaunch(slot,
                File.Exists, Directory.Exists);
            if (problem is not null) return problem;

            var start = DeckSlots.BuildStartInfo(slot);
            if (start is null) return null;

            using var process = Process.Start(start);
            DiagnosticLogger.Log("Deck", $"slot {index + 1} → '{slot.Target}'");
            report?.Invoke($"Launched {slot.DisplayName}");
            return null;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Deck.Launch", ex);
            return $"Couldn't launch slot {index + 1} — {ex.GetType().Name}";
        }
    }

    // ------------------------------------------------------------ persistence

    private void LoadInto(DeckSlots.Slot[] slots, string file)
    {
        try
        {
            if (!File.Exists(file)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                int idx = el.TryGetProperty("i", out var iEl) && iEl.TryGetInt32(out var i) ? i : -1;
                if (idx < 0 || idx >= DeckSlots.Count) continue;
                string target = el.TryGetProperty("t", out var tEl) ? tEl.GetString() ?? "" : "";
                string name = el.TryGetProperty("n", out var nEl) ? nEl.GetString() ?? "" : "";
                string args = el.TryGetProperty("a", out var aEl) ? aEl.GetString() ?? "" : "";
                string work = el.TryGetProperty("w", out var wEl) ? wEl.GetString() ?? "" : "";

                var slot = DeckSlots.Normalize(idx, name, target, args, work);
                slots[idx] = slot ?? DeckSlots.Empty(idx);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("DeckStore.Load", ex);   // corrupt ⇒ empty deck
        }
    }

    private void ScheduleSave()
    {
        if (Interlocked.Exchange(ref _saving, 1) == 1) return;
        var snapshot = Slots();
        Task.Run(() =>
        {
            try { SaveSnapshot(snapshot, _file); }
            catch (Exception ex) { DiagnosticLogger.LogException("DeckStore.Save", ex); }
            finally { Interlocked.Exchange(ref _saving, 0); }
        });
    }

    /// <summary>Serialize → unique tmp → atomic move. Sweeps orphan tmps.</summary>
    internal static void SaveSnapshot(IReadOnlyList<DeckSlots.Slot> slots, string file)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var s in slots)
            {
                if (!s.IsAssigned) continue;
                writer.WriteStartObject();
                writer.WriteNumber("i", s.Index);
                writer.WriteString("n", s.Name);
                writer.WriteString("t", s.Target);
                writer.WriteString("a", s.Args);
                writer.WriteString("w", s.WorkDir);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file) ?? ".");
        var tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(tmp, stream.ToArray());

        // Windows truth (the alpha.6 lesson, now doctrine): a fresh write can be
        // briefly held by an antivirus scan, and MoveFileEx(REPLACE_EXISTING) then
        // fails with Access Denied. Retry a few times before giving up.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tmp, file, overwrite: true);
                break;
            }
            catch (Exception ex) when (attempt < 4 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(120 * attempt);
            }
        }

        foreach (var stale in Directory.GetFiles(Path.GetDirectoryName(file) ?? ".", Path.GetFileName(file) + ".*.tmp"))
        {
            try { File.Delete(stale); } catch { /* best-effort sweep */ }
        }
    }
}
