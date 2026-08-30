using System.Text.Json;

namespace Lumo.Core;

/// <summary>One bookmark row (v2.3, DEV_PLAN Task 3.2).</summary>
public sealed record BookmarkEntry(
    string Name,
    string Url,
    string Folder,
    long AddedAtMicros);   // Chrome's date_added: microseconds since 1601-01-01 (0 = unknown)

/// <summary>
/// v2.3 (DEV_PLAN Task 3.2) — pure parser for Chrome/Edge "Bookmarks" JSON files
/// (%LOCALAPPDATA%\&lt;browser&gt;\User Data\&lt;profile&gt;\Bookmarks).
///
/// The file shape is stable and well known:
///   { "roots": { "bookmark_bar": {children:[…]}, "other": {…}, "synced": {…} } }
/// with nodes of type "url" (name + url + date_added) and "folder" (children).
/// Parsing is depth-first over all three roots, keeps only url nodes, and is
/// hard-capped so a pathological 100 MB bookmark file can never blow up memory
/// or the search pipeline (agent rule 3).
/// </summary>
public static class Bookmarks
{
    public const int MaxEntries = 3000;

    /// <summary>Parses a Bookmarks JSON document. Never throws — bad input yields an empty list.</summary>
    public static List<BookmarkEntry> Parse(string json)
    {
        var list = new List<BookmarkEntry>();
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return list;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;
            if (!doc.RootElement.TryGetProperty("roots", out var roots) || roots.ValueKind != JsonValueKind.Object)
                return list;

            foreach (var rootName in new[] { "bookmark_bar", "other", "synced" })
            {
                if (!roots.TryGetProperty(rootName, out var root) || root.ValueKind != JsonValueKind.Object)
                    continue;

                // Walk the root's CHILDREN with the base folder — the root node's own
                // display name ("Bookmarks bar") never belongs in a row's path; nested
                // folders still accumulate underneath it.
                string baseFolder = rootName is "bookmark_bar" ? "" : RootLabel(rootName);
                if (root.TryGetProperty("children", out var kids) && kids.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in kids.EnumerateArray())
                    {
                        Walk(child, baseFolder, list);
                        if (list.Count >= MaxEntries) break;
                    }
                }
                else
                {
                    Walk(root, baseFolder, list);   // malformed root without children — still tolerant
                }
                if (list.Count >= MaxEntries) break;
            }
        }
        catch { /* tolerant: a corrupt file simply means no bookmarks */ }
        return list;
    }

    private static string RootLabel(string rootName) => rootName switch
    {
        "other" => "Other bookmarks",
        "synced" => "Mobile bookmarks",
        _ => "",
    };

    private static void Walk(JsonElement node, string folder, List<BookmarkEntry> list)
    {
        if (list.Count >= MaxEntries) return;
        try
        {
            if (node.ValueKind != JsonValueKind.Object) return;

            string type = node.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? "" : "";

            if (type.Equals("url", StringComparison.OrdinalIgnoreCase))
            {
                string url = node.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String
                    ? u.GetString() ?? "" : "";
                string name = node.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString() ?? "" : "";
                long added = node.TryGetProperty("date_added", out var d) && d.ValueKind == JsonValueKind.String &&
                             long.TryParse(d.GetString(), out var micros) ? micros : 0;

                if (url.Length > 0 && name.Length > 0)
                    list.Add(new BookmarkEntry(name, url, folder, added));
                return;
            }

            if (type.Equals("folder", StringComparison.OrdinalIgnoreCase) &&
                node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                string self = node.TryGetProperty("name", out var fn) && fn.ValueKind == JsonValueKind.String
                    ? fn.GetString() ?? "" : "";
                string childFolder = self.Length == 0
                    ? folder
                    : folder.Length == 0 ? self : folder + " / " + self;

                foreach (var child in children.EnumerateArray())
                {
                    Walk(child, childFolder, list);
                    if (list.Count >= MaxEntries) return;
                }
            }
        }
        catch { /* one broken node must not kill the walk */ }
    }
}
