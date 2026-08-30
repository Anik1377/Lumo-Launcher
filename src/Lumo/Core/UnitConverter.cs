using System.Globalization;

namespace Lumo.Core;

/// <summary>
/// v2.1 (DEV_PLAN Task 1.2) — inline unit + currency conversion for the C/ prefix.
///
/// Parses "{amount} {fromUnit} (to|in|as) {toUnit}" — e.g. "10 ft in cm",
/// "5kg in lbs", "50 usd to eur" — and returns a formatted result string.
/// Pure, synchronous, bounded: one parse attempt, one dictionary lookup pair,
/// zero I/O on the keystroke path (currency rates are pre-fetched in the
/// background by Services.ExchangeRateService with a static offline fallback).
/// </summary>
public static class UnitConverter
{
    private static readonly Dictionary<string, double> Length = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mm"] = 0.001, ["cm"] = 0.01, ["m"] = 1.0, ["km"] = 1000.0,
        ["in"] = 0.0254, ["inch"] = 0.0254, ["ft"] = 0.3048, ["foot"] = 0.3048,
        ["yd"] = 0.9144, ["mi"] = 1609.344, ["mile"] = 1609.344,
    };

    private static readonly Dictionary<string, double> Mass = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mg"] = 0.000001, ["g"] = 0.001, ["kg"] = 1.0, ["t"] = 1000.0, ["tonne"] = 1000.0,
        ["oz"] = 0.028349523125, ["lb"] = 0.45359237, ["lbs"] = 0.45359237, ["pound"] = 0.45359237,
        ["st"] = 6.35029318,
    };

    private static readonly Dictionary<string, double> Volume = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ml"] = 0.001, ["l"] = 1.0, ["liter"] = 1.0, ["litre"] = 1.0,
        ["gal"] = 3.785411784, ["gallon"] = 3.785411784, ["qt"] = 0.946352946,
        ["cup"] = 0.2365882365, ["floz"] = 0.0295735295625,
    };

    private static readonly Dictionary<string, double> Data = new(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = 1.0, ["kb"] = 1_000.0, ["mb"] = 1_000_000.0, ["gb"] = 1_000_000_000.0,
        ["tb"] = 1_000_000_000_000.0, ["kib"] = 1024.0, ["mib"] = 1048576.0, ["gib"] = 1073741824.0,
    };

    // temperature is special-cased (affine, not a factor)
    private static readonly HashSet<string> Temp = new(StringComparer.OrdinalIgnoreCase) { "c", "f", "k", "celsius", "fahrenheit", "kelvin" };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Families =
        new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.OrdinalIgnoreCase)
        {
            ["length"] = Length, ["mass"] = Mass, ["volume"] = Volume, ["data"] = Data,
        };

    private static readonly string[] Separators = { " to ", " in ", " as ", "→", "->", "=" };

    /// <summary>Try to parse "{amount} {unit} to {unit}" and convert. Never throws.</summary>
    public static bool TryConvert(string? input, out string result)
    {
        result = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(input)) return false;
            string s = input.Trim();
            if (s.Length > 80) return false;

            // split on the LAST separator so units like "in" inside the left side
            // ("2 in in cm" — silly but legal) still parse predictably
            int cut = -1, sepLen = 0;
            foreach (var sep in Separators)
            {
                int i = s.LastIndexOf(sep, StringComparison.OrdinalIgnoreCase);
                if (i > cut) { cut = i; sepLen = sep.Length; }
            }
            if (cut <= 0) return false;

            string left = s[..cut].Trim();
            string right = s[(cut + sepLen)..].Trim();
            if (left.Length == 0 || right.Length == 0) return false;

            // left = amount + unit
            int sp = 0;
            while (sp < left.Length && (char.IsDigit(left[sp]) || left[sp] is '.' or ',' or '-' or '+')) sp++;
            if (sp == 0) return false;
            if (!double.TryParse(left[..sp].Replace(',', '.'), NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out double amount)) return false;
            string from = left[sp..].Trim();

            // right may carry a stray amount-less unit only
            string to = right;
            if (to.Contains(' '))
            {
                var parts = to.Split(' ', 2, StringSplitOptions.TrimEntries);
                if (parts[0].All(char.IsDigit) || double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    to = parts[1];
            }

            if (from.Length == 0 || to.Length == 0) return false;

            // temperatures first (affine conversion)
            if (Temp.Contains(from) && Temp.Contains(to))
                return TryTemperature(amount, from, to, out result);

            // same-family linear units
            foreach (var fam in Families.Values)
            {
                if (fam.ContainsKey(from) && fam.ContainsKey(to))
                {
                    double v = amount * fam[from] / fam[to];
                    result = Format(v) + " " + NormalizeUnit(to);
                    return true;
                }
            }

            // currencies (rate table, refreshed in the background)
            if (ExchangeRateService.IsCurrency(from) && ExchangeRateService.IsCurrency(to))
            {
                double? rate = ExchangeRateService.Rate(from, to);
                if (rate is { } r)
                {
                    double v = amount * r;
                    result = Format(v) + " " + to.ToUpperInvariant();
                    return true;
                }
                return false;   // no rate yet → fall through to the calculator hint rows
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryTemperature(double amount, string from, string to, out string result)
    {
        result = string.Empty;
        string f = from[..1].ToLowerInvariant(), t = to[..1].ToLowerInvariant();

        // to Celsius first
        double c = f switch
        {
            "f" => (amount - 32) * 5 / 9,
            "k" => amount - 273.15,
            _ => amount,
        };
        double v = t switch
        {
            "f" => c * 9 / 5 + 32,
            "k" => c + 273.15,
            _ => c,
        };
        result = Format(v) + " °" + char.ToUpperInvariant(t == "c" ? 'C' : t == "f" ? 'F' : 'K');
        return true;
    }

    private static string NormalizeUnit(string u) => u.ToLowerInvariant() switch
    {
        "inch" => "in", "foot" => "ft", "mile" => "mi",
        "pound" => "lb", "lbs" => "lb",
        "liter" or "litre" => "l", "gallon" => "gal",
        "tonne" => "t",
        var x => x,
    };

    private static string Format(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return "—";
        // up to 6 significant decimals, no trailing zeros — "30.48", "0.9144", "45.93"
        var s = v.ToString("0.######", CultureInfo.InvariantCulture);
        return s.Length == 0 ? "0" : s;
    }
}
