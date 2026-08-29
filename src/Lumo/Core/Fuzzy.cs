namespace Lumo.Core;

/// <summary>
/// Iterative (non-recursive) fuzzy subsequence scorer — cannot stack-overflow regardless
/// of input length, and never allocates per call.
/// Score of 0 means "no match". Higher is better.
/// </summary>
public static class Fuzzy
{
    public static int Score(string? query, string? target)
    {
        if (string.IsNullOrWhiteSpace(query)) return 1;
        if (string.IsNullOrEmpty(target)) return 0;

        // Exact / prefix / substring fast paths (cheap and give great ranks).
        int idx = target.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx == 0) return 1000;
        if (idx > 0)
        {
            bool wordStart = idx == 0 || IsWordBoundary(target[idx - 1]);
            return 800 + (wordStart ? 100 : 0) - Math.Min(idx, 100);
        }

        // Generic subsequence scan.
        int score = 0, streak = 0;
        bool atWordStart = true;
        int qi = 0;
        int qlen = query.Length, tlen = target.Length;

        for (int ti = 0; ti < tlen && qi < qlen; ti++)
        {
            char tc = Lower(target[ti]);
            char qc = Lower(query[qi]);

            if (tc == qc)
            {
                streak++;
                int gain = 10 + streak * 2 + (atWordStart ? 8 : 0);
                score += gain;
                qi++;
                atWordStart = false;
            }
            else
            {
                streak = 0;
                if (tc is ' ' or '-' or '_' or '.' or '\\' or '/')
                    atWordStart = true;
            }
        }

        return qi == qlen ? score : 0;
    }

    private static bool IsWordBoundary(char c) => c is ' ' or '-' or '_' or '.' or '\\' or '/' or '(';

    private static char Lower(char c) => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
}
