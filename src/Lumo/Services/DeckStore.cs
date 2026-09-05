using Lumo.Core;
using System.Diagnostics;
using System.Text.Json;

namespace Lumo.Services;

/// <summary>
/// v3.0 — App Deck persistence. v3.0.0-alpha.6 — the store now backs PAGES:
/// every page (Main, Games, Studio, …) carries its own nine slots in the one
/// appdeck.json, and every legacy API (Slots/Slot/Assign/Clear/Launch) operates
/// on the ACTIVE page, so numpad 1–9 always fire what the user sees.
///
/// File format v2: { "v":2, "active":"main", "pages":[{id,name,icon,slots[]}] }.
/// The v1 format (a bare slot array) loads as the "Main" page — upgrades keep
/// every assignment. Follows the store doctrine (PersonaStore/ChatStore):
/// hand-parsed JSON, corrupt file ⇒ empty deck, single-flight background save
/// through a unique tmp + atomic rename, value-returned launch (never throws),
/// and the alpha.4 generation ledger so the disk can never regress.
/// </summary>
public sealed class DeckStore
{
    public static DeckStore Current { get; } = new();

    private readonly string _file;
    private readonly object _gate = new();                 // guards _pages + _activeId
    private List<DeckPages.DeckPage> _pages = new();
    private string _activeId = DeckPages.DefaultPageId;
    private int _saving;
    private readonly object _saveGate = new();   // spans claim + swap, so writes are strictly ordered
    private long _gen;
    private long _writtenGen;

    public DeckStore(string? file = null)
    {
        _file = string.IsNullOrWhiteSpace(file) ? AppPaths.DeckFile : file;
        Load();
    }

    // ------------------------------------------------------------ access (active page — the legacy API)

    /// <summary>A snapshot copy of the ACTIVE page's nine slots (definition order 1..9).</summary>
    public IReadOnlyList<DeckSlots.Slot> Slots()
    {
        lock (_gate) return (DeckSlots.Slot[])ActiveLocked().Slots.ToArray().Clone();
    }

    public DeckSlots.Slot Slot(int index)
    {
        index = Math.Clamp(index, 0, DeckSlots.Count - 1);
        lock (_gate) return ActiveLocked().Slots[index];
    }

    /// <summary>Saves the slot (null = clear it) on the ACTIVE page. Returns the stored
    /// slot, or null when the edit normalized to "nothing to save".</summary>
    public DeckSlots.Slot? Assign(DeckSlots.Slot slot)
    {
        if (slot.Index < 0 || slot.Index >= DeckSlots.Count) return null;
        lock (_gate)
        {
            ReplaceActiveSlotLocked(slot.Index, slot);
            ScheduleSave();
            return slot;
        }
    }

    public void Clear(int index)
    {
        index = Math.Clamp(index, 0, DeckSlots.Count - 1);
        lock (_gate)
        {
            ReplaceActiveSlotLocked(index, DeckSlots.Empty(index));
            ScheduleSave();
        }
    }

    /// <summary>v3.0.0-alpha.4 — synchronous save that PARTICIPATES in the generation
    /// ledger: any in-flight or later background save can never overwrite this state
    /// with something older. Used by tests and any "flush before exit" path.</summary>
    public void SaveNow()
    {
        lock (_saveGate)
        {
            List<DeckPages.DeckPage> snapshot;
            string active;
            lock (_gate)
            {
                _gen++;
                _writtenGen = _gen;
                snapshot = SnapshotLocked();
                active = _activeId;
            }
            SaveSnapshot(snapshot, active, _file);
        }
    }

    /// <summary>Assigned slots on the ACTIVE page.</summary>
    public int AssignedCount
    {
        get { lock (_gate) return ActiveLocked().Slots.Count(s => s.IsAssigned); }
    }

    // ------------------------------------------------------------ pages

    /// <summary>Deep-copy snapshot of every page, in stored order.</summary>
    public IReadOnlyList<DeckPages.DeckPage> Pages()
    {
        lock (_gate) return SnapshotLocked();
    }

    /// <summary>Deep-copy snapshot of the active page.</summary>
    public DeckPages.DeckPage ActivePage()
    {
        lock (_gate) return DeckPages.Clone(ActiveLocked());
    }

    public string ActivePageId
    {
        get { lock (_gate) return _activeId; }
    }

    /// <summary>Switches the active page (numpad 1–9 now fire ITS slots). False when unknown.</summary>
    public bool SwitchPage(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        lock (_gate)
        {
            if (!_pages.Any(p => string.Equals(p.Id, id, StringComparison.Ordinal))) return false;
            if (!string.Equals(_activeId, id, StringComparison.Ordinal))
            {
                _activeId = id;
                ScheduleSave();
            }
            return true;
        }
    }

    /// <summary>Adds a page (name auto-deduped, id fresh) and makes it active.
    /// Null when the deck already holds the page cap.</summary>
    public DeckPages.DeckPage? AddPage(string? name, string icon)
    {
        lock (_gate)
        {
            if (_pages.Count >= DeckPages.MaxPages) return null;
            var taken = _pages.Select(p => p.Name).ToList();
            var page = new DeckPages.DeckPage(
                DeckPages.NewId(() => Guid.NewGuid().ToString("N")),
                DeckPages.UniqueName(DeckPages.NormalizeName(name), taken),
                icon ?? "",
                DeckPages.EmptySlots());
            _pages.Add(page);
            _activeId = page.Id;
            ScheduleSave();
            return DeckPages.Clone(page);
        }
    }

    public bool RenamePage(string id, string? name)
    {
        lock (_gate)
        {
            int i = IndexOfLocked(id);
            if (i < 0) return false;
            var taken = _pages.Where((_, n) => n != i).Select(p => p.Name).ToList();
            _pages[i] = _pages[i] with { Name = DeckPages.UniqueName(DeckPages.NormalizeName(name), taken) };
            ScheduleSave();
            return true;
        }
    }

    /// <summary>Deletes a page. The LAST page can never be deleted; deleting the
    /// active page activates the first survivor.</summary>
    public bool DeletePage(string id)
    {
        lock (_gate)
        {
            if (_pages.Count <= 1) return false;
            int i = IndexOfLocked(id);
            if (i < 0) return false;
            _pages.RemoveAt(i);
            if (string.Equals(_activeId, id, StringComparison.Ordinal))
                _activeId = _pages[0].Id;
            ScheduleSave();
            return true;
        }
    }

    /// <summary>Clears every slot on the ACTIVE page (the page itself survives).</summary>
    public void ClearPage()
    {
        lock (_gate)
        {
            var page = ActiveLocked();
            _pages[IndexOfLocked(_activeId)] = new DeckPages.DeckPage(
                page.Id, page.Name, page.Icon, DeckPages.EmptySlots());
            ScheduleSave();
        }
    }

    /// <summary>Swaps two slots (drag-to-reorder). The records are renumbered so
    /// every slot's Index always equals its position — the grid, the editor and
    /// the launch counter all rely on that invariant. False for bad indexes or a==b.</summary>
    public bool SwapSlots(int a, int b)
    {
        if (a < 0 || a >= DeckSlots.Count || b < 0 || b >= DeckSlots.Count || a == b) return false;
        lock (_gate)
        {
            var slots = (DeckSlots.Slot[])ActiveLocked().Slots.ToArray().Clone();
            (slots[a], slots[b]) = (slots[b] with { Index = a }, slots[a] with { Index = b });
            ReplaceActiveSlotsLocked(slots);
            ScheduleSave();
            return true;
        }
    }

    /// <summary>Orders the ACTIVE page's assigned slots A→Z (DeckPages.SortSlots policy).</summary>
    public void SortPage()
    {
        lock (_gate)
        {
            ReplaceActiveSlotsLocked(DeckPages.SortSlots(ActiveLocked().Slots));
            ScheduleSave();
        }
    }

    /// <summary>Copies a slot of the ACTIVE page into its first empty slot.
    /// Returns the new index, or -1 when the page is full.</summary>
    public int DuplicateSlot(int index)
    {
        if (index < 0 || index >= DeckSlots.Count) return -1;
        lock (_gate)
        {
            var slots = ActiveLocked().Slots;
            if (!slots[index].IsAssigned) return -1;
            int free = Enumerable.Range(0, DeckSlots.Count).FirstOrDefault(i => !slots[i].IsAssigned, -1);
            if (free < 0) return -1;                       // page full — nothing to duplicate into
            var copy = slots[index] with { Index = free, Launches = 0 };
            ReplaceActiveSlotLocked(free, copy);
            ScheduleSave();
            return free;
        }
    }

    /// <summary>
    /// Merges pages from an imported layout: every page lands as a NEW page
    /// (fresh id, name deduped "Games (2)" style) so nothing existing is ever
    /// overwritten. Respects the page cap. Returns how many pages were added.
    /// </summary>
    public int ImportPages(IEnumerable<DeckPages.DeckPage> pages)
    {
        int added = 0;
        lock (_gate)
        {
            foreach (var page in pages)
            {
                if (_pages.Count >= DeckPages.MaxPages) break;
                var slots = new DeckSlots.Slot[DeckSlots.Count];
                for (int i = 0; i < DeckSlots.Count; i++)
                    slots[i] = i < page.Slots.Count ? DeckSlots.Normalize(i, page.Slots[i].Name, page.Slots[i].Target,
                        page.Slots[i].Args, page.Slots[i].WorkDir, page.Slots[i].Admin, page.Slots[i].WindowMode,
                        page.Slots[i].Launches) ?? DeckSlots.Empty(i) : DeckSlots.Empty(i);
                var taken = _pages.Select(p => p.Name).ToList();
                _pages.Add(new DeckPages.DeckPage(
                    DeckPages.NewId(() => Guid.NewGuid().ToString("N")),
                    DeckPages.UniqueName(page.Name, taken),
                    page.Icon ?? "",
                    slots));
                added++;
            }
            if (added > 0) ScheduleSave();
            return added;
        }
    }

    // ------------------------------------------------------------ launch

    /// <summary>
    /// Launches slot <paramref name="index"/> (0-based) of the ACTIVE page. Returns a
    /// readable error string on failure, null on success (or when the slot is empty —
    /// nothing to do is not an error). A successful launch bumps the slot's persisted
    /// counter (shown in the editor + tooltips). Logs result codes only; never throws.
    /// </summary>
    public string? Launch(int index, Action<string>? report = null)
    {
        try
        {
            DeckSlots.Slot slot;
            lock (_gate) slot = ActiveLocked().Slots[Math.Clamp(index, 0, DeckSlots.Count - 1)];
            if (!slot.IsAssigned) return null;

            var problem = DeckSlots.ValidateForLaunch(slot,
                File.Exists, Directory.Exists);
            if (problem is not null) return problem;

            var start = DeckSlots.BuildStartInfo(slot);
            if (start is null) return null;

            using var process = Process.Start(start);
            lock (_gate)
            {
                ReplaceActiveSlotLocked(slot.Index, slot with { Launches = slot.Launches + 1 });
            }
            ScheduleSave();
            DiagnosticLogger.Log("Deck", $"{_activeId} slot {index + 1} → '{slot.Target}'");
            report?.Invoke($"Launched {slot.DisplayName}");
            return null;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Deck.Launch", ex);
            return $"Couldn't launch slot {index + 1} — {ex.GetType().Name}";
        }
    }

    // ------------------------------------------------------------ internals

    /// <summary>Caller holds _gate. The active id is guaranteed valid by Load().</summary>
    private DeckPages.DeckPage ActiveLocked() =>
        _pages.First(p => string.Equals(p.Id, _activeId, StringComparison.Ordinal));

    private int IndexOfLocked(string id) => _pages.FindIndex(
        p => string.Equals(p.Id, id, StringComparison.Ordinal));

    private void ReplaceActiveSlotLocked(int index, DeckSlots.Slot value)
    {
        var page = ActiveLocked();
        var slots = (DeckSlots.Slot[])page.Slots.ToArray().Clone();
        slots[index] = value;
        ReplaceActiveSlotsLocked(slots);
    }

    private void ReplaceActiveSlotsLocked(DeckSlots.Slot[] slots)
    {
        int i = IndexOfLocked(_activeId);
        _pages[i] = new DeckPages.DeckPage(_pages[i].Id, _pages[i].Name, _pages[i].Icon, slots);
    }

    private List<DeckPages.DeckPage> SnapshotLocked() =>
        _pages.Select(DeckPages.Clone).ToList();

    // ------------------------------------------------------------ persistence

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) { lock (_gate) EnsureBasicsLocked(null); return; }
            using var doc = JsonDocument.Parse(AtomicIo.ReadWithRetry(_file));

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                // v1 format — a bare slot array becomes the Main page.
                var slots = ParseSlots(doc.RootElement);
                lock (_gate)
                {
                    _pages = [new DeckPages.DeckPage(DeckPages.DefaultPageId, DeckPages.DefaultPageName,
                        DeckPages.DefaultPageIcon, slots)];
                    _activeId = DeckPages.DefaultPageId;
                }
                return;
            }

            if (doc.RootElement.ValueKind != JsonValueKind.Object) { lock (_gate) EnsureBasicsLocked(null); return; }

            var root = doc.RootElement;
            var loaded = new List<DeckPages.DeckPage>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("pages", out var pagesEl) && pagesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in pagesEl.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    string id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) continue;   // junk / duplicate id → skip
                    string name = DeckPages.NormalizeName(el.TryGetProperty("name", out var nEl) ? nEl.GetString() : null);
                    string icon = el.TryGetProperty("icon", out var iEl2) ? iEl2.GetString() ?? "" : "";
                    var slots = el.TryGetProperty("slots", out var sEl) && sEl.ValueKind == JsonValueKind.Array
                        ? ParseSlots(sEl)
                        : DeckPages.EmptySlots();
                    loaded.Add(new DeckPages.DeckPage(id, name, icon, slots));
                }
            }

            string? active = root.TryGetProperty("active", out var aEl) ? aEl.GetString() : null;
            lock (_gate) EnsureBasicsLocked(loaded, active);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("DeckStore.Load", ex);   // corrupt ⇒ empty deck
            lock (_gate) EnsureBasicsLocked(null);
        }
    }

    /// <summary>Caller holds _gate. Guarantees: at least one page, unique ids,
    /// nine slots per page, a valid active id.</summary>
    private void EnsureBasicsLocked(List<DeckPages.DeckPage>? pages, string? active = null)
    {
        if (pages is not null && pages.Count > 0)
        {
            _pages = pages;
            // Defensive: every page must hold exactly Count slots.
            for (int i = 0; i < _pages.Count; i++)
            {
                var p = _pages[i];
                if (p.Slots.Count == DeckSlots.Count) continue;
                var slots = new DeckSlots.Slot[DeckSlots.Count];
                for (int s = 0; s < DeckSlots.Count; s++)
                    slots[s] = s < p.Slots.Count ? p.Slots[s] : DeckSlots.Empty(s);
                _pages[i] = new DeckPages.DeckPage(p.Id, p.Name, p.Icon, slots);
            }
        }
        else
        {
            _pages = [new DeckPages.DeckPage(DeckPages.DefaultPageId, DeckPages.DefaultPageName,
                DeckPages.DefaultPageIcon, DeckPages.EmptySlots())];
        }

        _activeId = active is not null && _pages.Any(p => string.Equals(p.Id, active, StringComparison.Ordinal))
            ? active
            : _pages[0].Id;
    }

    private static DeckSlots.Slot[] ParseSlots(JsonElement arr)
    {
        var slots = DeckPages.EmptySlots();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            int idx = el.TryGetProperty("i", out var iEl) && iEl.TryGetInt32(out var i) ? i : -1;
            if (idx < 0 || idx >= DeckSlots.Count) continue;
            string target = el.TryGetProperty("t", out var tEl) ? tEl.GetString() ?? "" : "";
            string name = el.TryGetProperty("n", out var nEl) ? nEl.GetString() ?? "" : "";
            string args = el.TryGetProperty("a", out var aEl) ? aEl.GetString() ?? "" : "";
            string work = el.TryGetProperty("w", out var wEl) ? wEl.GetString() ?? "" : "";
            bool admin = el.TryGetProperty("admin", out var adEl) && adEl.ValueKind == JsonValueKind.True;
            string win = el.TryGetProperty("win", out var winEl) ? winEl.GetString() ?? "" : "";
            int launches = el.TryGetProperty("c", out var cEl) && cEl.TryGetInt32(out var c) ? Math.Max(0, c) : 0;

            slots[idx] = DeckSlots.Normalize(idx, name, target, args, work, admin, win, launches)
                         ?? DeckSlots.Empty(idx);
        }
        return slots;
    }

    private void ScheduleSave()
    {
        lock (_gate) _gen++;
        if (Interlocked.Exchange(ref _saving, 1) == 1) return;   // a save is in flight; it loops until it has written every generation
        _ = Task.Run(() =>
        {
            try
            {
                while (true)
                {
                    lock (_saveGate)
                    {
                        List<DeckPages.DeckPage> snapshot;
                        string active;
                        lock (_gate)
                        {
                            if (_gen <= _writtenGen) return;   // disk already holds this state or newer
                            _writtenGen = _gen;                // claim + swap are ONE serialized step,
                            snapshot = SnapshotLocked();
                            active = _activeId;
                        }
                        SaveSnapshot(snapshot, active, _file); // so an older claimed write can never land last
                    }
                }
            }
            catch (Exception ex) { DiagnosticLogger.LogException("DeckStore.Save", ex); }
            finally
            {
                Interlocked.Exchange(ref _saving, 0);
                bool dirty;
                lock (_gate) dirty = _gen > _writtenGen;      // a mutation slipped past the final check
                if (dirty) ScheduleSave();                     // — re-arm so the newest state always lands
            }
        });
    }

    /// <summary>Serialize (v2) → unique tmp → atomic move. Sweeps orphan tmps.</summary>
    internal static void SaveSnapshot(IReadOnlyList<DeckPages.DeckPage> pages, string activeId, string file)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", 2);
            writer.WriteString("active", activeId);
            writer.WriteStartArray("pages");
            foreach (var page in pages)
            {
                writer.WriteStartObject();
                writer.WriteString("id", page.Id);
                writer.WriteString("name", page.Name);
                writer.WriteString("icon", page.Icon);
                writer.WriteStartArray("slots");
                foreach (var s in page.Slots)
                {
                    if (!s.IsAssigned) continue;
                    writer.WriteStartObject();
                    writer.WriteNumber("i", s.Index);
                    writer.WriteString("n", s.Name);
                    writer.WriteString("t", s.Target);
                    writer.WriteString("a", s.Args);
                    writer.WriteString("w", s.WorkDir);
                    if (s.Admin) writer.WriteBoolean("admin", true);
                    if (s.WindowMode.Length > 0) writer.WriteString("win", s.WindowMode);
                    if (s.Launches > 0) writer.WriteNumber("c", s.Launches);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file) ?? ".");
        var tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(tmp, stream.ToArray());
        AtomicIo.Swap(tmp, file);   // v3.0.0-alpha.4 — atomic replace + AV-race retries
        AtomicIo.SweepStaleTmps(file);
    }
}
