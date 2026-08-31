namespace Lumo.Core;

/// <summary>
/// v2.5 (DEV_PLAN Task 4.1) — one declarative search route.
///
/// A handler owns a route prefix (including its separator — "A/", "AI/", "?", "!")
/// and answers queries that start with it. The built-in routes register in
/// SearchEngine; the plugin system (Task 4.2) can register/replace routes too.
/// Handlers must stay synchronous, in-memory and bounded — the operating rules
/// in DEV_PLAN.md §0 apply to them unchanged.
/// </summary>
public interface IPrefixHandler
{
    /// <summary>Route prefix INCLUDING its separator — "A/", "AI/", "?", "!".</summary>
    string Prefix { get; }

    /// <summary>
    /// Whole-query aliases that route with an empty argument (the AI/ route owns
    /// the bare word "AI"). Checked after the prefix of the same handler.
    /// </summary>
    IEnumerable<string> ExactAliases => Enumerable.Empty<string>();

    /// <summary>Builds the rows for the text after the prefix (already trimmed; "" at the route root).</summary>
    List<ResultItem> Handle(string arg);
}

/// <summary>
/// Routes a query to its <see cref="IPrefixHandler"/>.
///
/// Matching rules (behavior-preserving port of the old SearchCore if-chain):
///  · prefixes match case-insensitively and the LONGEST registered prefix wins,
///    so "AI/" can never be shadowed by a shorter route that happens to fit;
///  · an exact alias routes with an empty argument;
///  · no match → the caller falls through to the default mixed view.
/// </summary>
public sealed class PrefixRouter
{
    private readonly object _gate = new();
    private readonly List<IPrefixHandler> _handlers = new();

    /// <summary>Registers (or replaces, same prefix) a handler. Longest prefix is tried first.</summary>
    public void Register(IPrefixHandler handler)
    {
        lock (_gate)
        {
            _handlers.RemoveAll(h => h.Prefix.Equals(handler.Prefix, StringComparison.OrdinalIgnoreCase));
            _handlers.Add(handler);
            _handlers.Sort((a, b) => b.Prefix.Length.CompareTo(a.Prefix.Length));
        }
    }

    /// <summary>Removes a route (e.g. a plugin uninstalled). True when something was removed.</summary>
    public bool Unregister(string prefix)
    {
        lock (_gate) return _handlers.RemoveAll(h => h.Prefix.Equals(prefix, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    public int Count { get { lock (_gate) return _handlers.Count; } }

    /// <summary>Longest-prefix, case-insensitive match; exact aliases route with an empty argument.</summary>
    public IPrefixHandler? Match(string query, out string arg)
    {
        arg = "";
        if (string.IsNullOrWhiteSpace(query)) return null;
        lock (_gate)
        {
            foreach (var h in _handlers)
            {
                if (query.StartsWith(h.Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    arg = query[h.Prefix.Length..].Trim();
                    return h;
                }
                foreach (var alias in h.ExactAliases)
                {
                    if (query.Equals(alias, StringComparison.OrdinalIgnoreCase))
                        return h;   // arg stays ""
                }
            }
        }
        return null;
    }
}
