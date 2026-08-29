using System.Windows.Threading;

namespace Lumo.Services;

/// <summary>
/// v1.6 — Clipboard History, Raycast-style: while Lumo runs, every text copy
/// on the system lands here (polled on the UI thread; clipboard is STA-only).
/// Entries live in memory only — never written to disk, cleared on exit.
/// </summary>
public sealed class ClipboardHistory
{
    /// <summary>One captured copy.</summary>
    public sealed class Entry
    {
        public string Id { get; } = Guid.NewGuid().ToString("N")[..12];
        public string Text { get; }
        public DateTime At { get; } = DateTime.Now;
        public Entry(string text) => Text = text;
    }

    private const int MaxEntries = 50;
    private readonly object _gate = new();
    private readonly List<Entry> _items = new();
    private bool _suppress;          // ignore our own restores
    private readonly DispatcherTimer _timer;

    public ClipboardHistory()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    public int Count { get { lock (_gate) return _items.Count; } }

    public List<Entry> Snapshot()
    {
        lock (_gate) return new List<Entry>(_items);
    }

    public Entry? Find(string id)
    {
        lock (_gate) return _items.FirstOrDefault(e => e.Id == id);
    }

    public void Clear()
    {
        lock (_gate) _items.Clear();
        DiagnosticLogger.Log("Clipboard", "History cleared");
    }

    /// <summary>Writes text back to the clipboard without re-capturing it.</summary>
    public void Restore(Entry entry)
    {
        try
        {
            _suppress = true;
            System.Windows.Clipboard.SetText(entry.Text);
            DiagnosticLogger.Log("Clipboard", $"Restored {entry.Text.Length} chars");
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Clipboard.Restore", ex); }
        finally { _suppress = false; }
    }

    /// <summary>UI-thread poll: capture new text if the clipboard changed.</summary>
    private void Poll()
    {
        try
        {
            if (_suppress) return;
            if (!System.Windows.Clipboard.ContainsText()) return;
            string text = System.Windows.Clipboard.GetText();
            if (string.IsNullOrEmpty(text)) return;

            lock (_gate)
            {
                if (_items.Count > 0 && _items[0].Text == text) return;   // unchanged
                int dupe = _items.FindIndex(e => e.Text == text);
                if (dupe >= 0)
                {
                    var e = _items[dupe];
                    _items.RemoveAt(dupe);
                    _items.Insert(0, e);                                  // move to top
                    return;
                }
                _items.Insert(0, new Entry(text));
                if (_items.Count > MaxEntries) _items.RemoveAt(_items.Count - 1);
            }
        }
        catch { /* clipboard momentarily locked by another process — retry next tick */ }
    }
}
