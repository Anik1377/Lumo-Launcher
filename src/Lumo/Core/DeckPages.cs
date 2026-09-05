namespace Lumo.Core;

/// <summary>
/// v3.0.0-alpha.6 — App Deck PAGES (the multi-mode layouts): every page is a
/// full set of nine slots, so "Games", "Studio", "Office" and "Entertainment"
/// can each hold their own apps while the numpad 1–9 mapping stays intact —
/// the keys always fire the ACTIVE page.
///
/// Pure model + policy (no WPF, no store I/O) exactly like DeckSlots: the
/// normalization rules, the page-name dedupe used by both "+ new page" and
/// Import, the alphabetical sort policy, and the starter template catalog.
/// </summary>
public static class DeckPages
{
    public const int MaxPages = 12;              // generous but bounded
    public const int MaxNameChars = 24;
    public const string DefaultPageId = "main";
    public const string DefaultPageName = "Main";
    public const string DefaultPageIcon = "\uE8A9";   // MDL2 AllApps

    /// <summary>One deck page: identity + its own nine slots (indexes 0..8 fixed).</summary>
    public sealed record DeckPage(string Id, string Name, string Icon, IReadOnlyList<DeckSlots.Slot> Slots);

    /// <summary>A one-click starter page offered by the "+" chip.</summary>
    public sealed record PageTemplate(string Name, string Icon);

    /// <summary>
    /// The four modes the deck shipped with — one click each from the "+" chip.
    /// Glyphs are Segoe MDL2 (Game / Camera / Document / Play).
    /// </summary>
    public static readonly IReadOnlyList<PageTemplate> Templates = new PageTemplate[]
    {
        new("Games", "\uE7FC"),
        new("Studio", "\uE722"),
        new("Office", "\uE8A5"),
        new("Entertainment", "\uE768"),
    };

    /// <summary>Page-name whitespace rules identical to slot names; empty falls back
    /// to a generic "Page" (the caller appends a number via UniquePageName).</summary>
    public static string NormalizeName(string? name)
    {
        var n = DeckSlots.Collapse(name ?? "");
        if (n.Length > MaxNameChars) n = n[..MaxNameChars];
        return n.Length > 0 ? n : "Page";
    }

    /// <summary>
    /// First id for a new page: never collides with the ones already taken.
    /// Pure — the id source is passed in so tests stay deterministic.
    /// </summary>
    public static string NewId(Func<string> guidSource)
    {
        string id;
        do { id = "p" + guidSource(); } while (id.Length < 3);   // a broken source can't win the loop
        return id.Length <= 24 ? id : id[..24];
    }

    /// <summary>
    /// "Games" taken? → "Games (2)", "Games (3)" … keeps Import and "+" from ever
    /// producing two chips with the same label (the numpad mapping is per page,
    /// but a duplicated name would be genuinely confusing).
    /// </summary>
    public static string UniqueName(string baseName, IReadOnlyCollection<string> taken)
    {
        var clean = NormalizeName(baseName);
        if (!taken.Contains(clean, StringComparer.OrdinalIgnoreCase)) return clean;
        for (int n = 2; n < 1000; n++)
        {
            var candidate = $"{clean} ({n})";
            if (!taken.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
        }
        return clean + " (" + Guid.NewGuid().ToString("N")[..4] + ")";
    }

    /// <summary>
    /// The sort policy: assigned slots land in 0..k-1 ordered by display name
    /// (ties broken by target so the order is stable), the empty slots fill the
    /// remaining indexes. Returns fresh slot records — the input is untouched.
    /// </summary>
    public static DeckSlots.Slot[] SortSlots(IReadOnlyList<DeckSlots.Slot> slots)
    {
        var ordered = slots.Where(s => s.IsAssigned)
            .OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(s => s.Target, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var result = new DeckSlots.Slot[DeckSlots.Count];
        for (int i = 0; i < DeckSlots.Count; i++)
            result[i] = i < ordered.Count
                ? ordered[i] with { Index = i }
                : DeckSlots.Empty(i);
        return result;
    }

    /// <summary>Slots for a brand-new page: nine empties.</summary>
    public static DeckSlots.Slot[] EmptySlots()
    {
        var slots = new DeckSlots.Slot[DeckSlots.Count];
        for (int i = 0; i < DeckSlots.Count; i++) slots[i] = DeckSlots.Empty(i);
        return slots;
    }

    /// <summary>Deep copy — the store hands out snapshots, never live state.</summary>
    public static DeckPage Clone(DeckPage page) =>
        new(page.Id, page.Name, page.Icon, page.Slots.ToArray());
}

/// <summary>
/// v3.0.0-alpha.6 — the shareable deck-layout file (.lumodeck): a small JSON
/// document carrying every page and its slots, so users can back a deck up or
/// hand it to a friend. Pure JSON in / JSON out (no dialogs, no disk paths) so
/// both directions are unit-testable; the UI wraps it with file dialogs.
/// Tolerant by doctrine: junk documents read as "no pages", never throw.
/// </summary>
public static class DeckLayout
{
    public const string FileExtension = ".lumodeck";
    public const string Kind = "lumo-deck";
    public const int Version = 1;

    /// <summary>Serializes pages to the .lumodeck document.</summary>
    public static string Write(IReadOnlyList<DeckPages.DeckPage> pages)
    {
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", Kind);
            writer.WriteNumber("version", Version);
            writer.WriteString("exported", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteStartArray("pages");
            foreach (var page in pages)
            {
                writer.WriteStartObject();
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
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Parses a .lumodeck document (also accepts an appdeck.json v2 export — the
    /// page shape is the same). Returns every valid page, names normalized, ids
    /// synthesized (the importer re-ids anyway). Junk ⇒ empty list.
    /// </summary>
    public static List<DeckPages.DeckPage> Read(string json)
    {
        var pages = new List<DeckPages.DeckPage>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json ?? "");
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return pages;

            var root = doc.RootElement;
            // "kind" is a courtesy marker — accepted but not enforced, so an
            // appdeck.json (v2) or a hand-edited file still imports.
            if (!root.TryGetProperty("pages", out var pagesEl) ||
                pagesEl.ValueKind != System.Text.Json.JsonValueKind.Array)
                return pages;

            int n = 0;
            foreach (var el in pagesEl.EnumerateArray())
            {
                n++;
                if (el.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                string name = DeckPages.NormalizeName(el.TryGetProperty("name", out var nEl) ? nEl.GetString() : null);
                string icon = el.TryGetProperty("icon", out var iEl) ? iEl.GetString() ?? "" : "";

                var slots = DeckPages.EmptySlots();
                bool sawSlots = false;
                if (el.TryGetProperty("slots", out var sEl) && sEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    sawSlots = true;
                    foreach (var s in sEl.EnumerateArray())
                    {
                        if (s.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                        int idx = s.TryGetProperty("i", out var iEl2) && iEl2.TryGetInt32(out var i) ? i : -1;
                        if (idx < 0 || idx >= DeckSlots.Count) continue;
                        string target = s.TryGetProperty("t", out var tEl) ? tEl.GetString() ?? "" : "";
                        string sname = s.TryGetProperty("n", out var snEl) ? snEl.GetString() ?? "" : "";
                        string args = s.TryGetProperty("a", out var aEl) ? aEl.GetString() ?? "" : "";
                        string work = s.TryGetProperty("w", out var wEl) ? wEl.GetString() ?? "" : "";
                        bool admin = s.TryGetProperty("admin", out var adEl) && adEl.ValueKind == System.Text.Json.JsonValueKind.True;
                        string win = s.TryGetProperty("win", out var winEl) ? winEl.GetString() ?? "" : "";
                        slots[idx] = DeckSlots.Normalize(idx, sname, target, args, work, admin, win) ?? DeckSlots.Empty(idx);
                    }
                }
                // a page with no slots array at all is junk, unless it's a deliberate empty
                if (!sawSlots) continue;
                pages.Add(new DeckPages.DeckPage($"import{n}", name, icon, slots));
            }
        }
        catch { /* tolerant: junk layout ⇒ no pages */ }
        return pages;
    }
}
