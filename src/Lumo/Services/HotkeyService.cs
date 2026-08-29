using System.Runtime.InteropServices;
using Lumo.Native;

namespace Lumo.Services;

/// <summary>
/// Global hotkey registration with automatic fallback.
///
/// FIX (v1.1): the old default Win+Space is RESERVED by Windows for switching input
/// language / keyboard layout, so RegisterHotKey fails silently and the hotkey "never works".
/// The default is now Alt+Space, and if the configured combo can't be registered we walk a
/// fallback chain automatically. Every attempt is written to the diagnostics log, and the
/// combo that actually won is reported back to the UI status bar.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    public const int HotkeyId = 0x4C4D4F31; // "LMO1"

    /// <summary>The combo that actually registered successfully (human readable).</summary>
    public string ActiveDescription { get; private set; } = "(none)";

    /// <summary>True if a fallback combo (different from the user's setting) was used.</summary>
    public bool UsedFallback { get; private set; }

    private readonly IntPtr _hwnd;
    private readonly Settings _settings;
    private bool _registered;

    public HotkeyService(IntPtr hwnd, Settings settings)
    {
        _hwnd = hwnd;
        _settings = settings;
    }

    /// <summary>Ordered fallback chain — first entry is the user's configured combo.</summary>
    private List<string> BuildChain()
    {
        var chain = new List<string>();
        var configured = string.IsNullOrWhiteSpace(_settings.Hotkey) ? "Alt+Space" : _settings.Hotkey.Trim();
        chain.Add(configured);
        foreach (var candidate in new[] { "Alt+Space", "Ctrl+Alt+Space", "Ctrl+Shift+Space", "Ctrl+Alt+M", "Win+Q" })
            if (!chain.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                chain.Add(candidate);
        return chain;
    }

    public bool TryRegister(out string activeDescription)
    {
        Unregister();

        foreach (var combo in BuildChain())
        {
            if (!TryParseCombo(combo, out uint mods, out uint vk))
            {
                DiagnosticLogger.Log("Hotkey", $"Cannot parse combo '{combo}' — skipped");
                continue;
            }

            if (NativeMethods.RegisterHotKey(_hwnd, HotkeyId, mods, vk))
            {
                _registered = true;
                UsedFallback = !combo.Equals(_settings.Hotkey, StringComparison.OrdinalIgnoreCase);
                ActiveDescription = combo;
                DiagnosticLogger.Log("Hotkey", $"Registered OK: {combo} (mods={mods}, vk={vk})");
                activeDescription = combo;
                return true;
            }

            int err = Marshal.GetLastWin32Error();
            DiagnosticLogger.Log("Hotkey", $"RegisterHotKey FAILED for '{combo}' (win32 error {err}) — trying next");
        }

        DiagnosticLogger.Log("Hotkey", "ALL hotkey combos failed. Use tray icon to open Lumo, or free the combo and restart.");
        activeDescription = ActiveDescription;
        return false;
    }

    public void Unregister()
    {
        if (!_registered) return;
        try { NativeMethods.UnregisterHotKey(_hwnd, HotkeyId); } catch { }
        _registered = false;
    }

    public void Dispose() => Unregister();

    // ---------------------------------------------------------------- parsing

    public static bool TryParseCombo(string combo, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(combo)) return false;

        var parts = combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= NativeMethods.MOD_CONTROL; break;
                case "alt": mods |= NativeMethods.MOD_ALT; break;
                case "shift": mods |= NativeMethods.MOD_SHIFT; break;
                case "win" or "windows": mods |= NativeMethods.MOD_WIN; break;
                default: return false;
            }
        }

        var key = parts[^1].ToLowerInvariant();
        vk = ParseMainKey(key);
        return vk != 0;
    }

    private static uint ParseMainKey(string key)
    {
        if (key == "space") return 0x20;
        if (key is "`" or "~") return 0xC0;
        if (key is "esc" or "escape") return 0x1B;

        // F1–F24
        if (key.Length >= 2 && key[0] == 'f' && uint.TryParse(key[1..], out var fn) && fn is >= 1 and <= 24)
            return 0x70 + fn - 1;

        if (key.Length == 1)
        {
            char c = key[0];
            if (c is >= 'a' and <= 'z') return char.ToUpperInvariant(c);
            if (c is >= '0' and <= '9') return c;
        }
        return 0;
    }
}
