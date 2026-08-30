using System.Text.Json;
using Lumo.Services;

namespace Lumo.Core;

/// <summary>
/// v2.1 (DEV_PLAN Task 1.2) — offline-first FX rates for inline currency conversion.
/// A small static snapshot table guarantees C/50 usd to eur works with no network;
/// a background refresh from open.er-api.com (every 12 h) upgrades accuracy when
/// online. Every failure is swallowed + logged — this service can never block or
/// break the keystroke path.
/// </summary>
public static class ExchangeRateService
{
    // Static fallback (approximate snapshot) — used until/unless a live refresh lands.
    private static readonly Dictionary<string, double> Fallback = new(StringComparer.OrdinalIgnoreCase)
    {
        ["usd"] = 1.0, ["eur"] = 0.92, ["gbp"] = 0.79, ["bdt"] = 117.5, ["inr"] = 83.5,
        ["jpy"] = 150.0, ["cny"] = 7.2, ["cad"] = 1.36, ["aud"] = 1.51, ["chf"] = 0.88,
    };

    private static readonly object _gate = new();
    private static Dictionary<string, double> _rates = new(Fallback, StringComparer.OrdinalIgnoreCase);
    private static int _refreshing;

    static ExchangeRateService() => KickRefresh();

    /// <summary>True when both codes are known currencies.</summary>
    public static bool IsCurrency(string code)
    {
        lock (_gate) { return _rates.ContainsKey(code); }
    }

    /// <summary>Cross rate from→to (USD-based table), or null when unknown.</summary>
    public static double? Rate(string from, string to)
    {
        try
        {
            lock (_gate)
            {
                if (!_rates.TryGetValue(from, out var f) || !_rates.TryGetValue(to, out var t))
                    return null;
                return t / f;
            }
        }
        catch { return null; }
    }

    private static void KickRefresh()
    {
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Lumo-Launcher/2.1");
                while (true)
                {
                    try
                    {
                        string json = await http.GetStringAsync("https://open.er-api.com/v6/latest/USD").ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("rates", out var rates) &&
                            rates.ValueKind == JsonValueKind.Object)
                        {
                            var fresh = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["usd"] = 1.0 };
                            foreach (var p in rates.EnumerateObject())
                                if (p.Value.ValueKind == JsonValueKind.Number)
                                    fresh[p.Name] = p.Value.GetDouble();
                            if (fresh.Count > 5)
                            {
                                lock (_gate) { _rates = fresh; }
                                DiagnosticLogger.Log("FX", $"Rates refreshed ({fresh.Count} currencies)");
                            }
                        }
                    }
                    catch (Exception ex) { DiagnosticLogger.Log("FX", "Refresh failed (offline ok): " + ex.Message); }

                    await Task.Delay(TimeSpan.FromHours(12)).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { DiagnosticLogger.LogException("FX", ex); }
            finally { Interlocked.Exchange(ref _refreshing, 0); }
        });
    }
}
