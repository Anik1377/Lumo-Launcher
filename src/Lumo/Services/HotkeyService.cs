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

    /// <summary>v2.5 (DEV_PLAN Task 4.3) — base id for per-shortcut hotkeys (base + n, n ≥ 1).</summary>
    public const int ShortcutHotkeyBase = HotkeyId + 0x1000;

    /// <summary>v2.5 — sane cap on per-shortcut hotkeys (Win32 would allow more; UX would not).</summary>
    public const int MaxShortcutHotkeys = 16;

    /// <summary>The combo that actually registered successfully (human readable).</summary>
    public string ActiveDescription { get; private set; } = "(none)";

    /// <summary>True if a fallback combo (different from the user's setting) was used.</summary>
    public bool UsedFallback { get; private set; }

    private readonly IntPtr _hwnd;
    private readonly Settings _settings;
    private bool _registered;
    private readonly HashSet<int> _extraIds = new();   // v2.5 — shortcut hotkey ids currently registered

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

    // ---------------------------------------------------------------- v2.5 — per-shortcut hotkeys (Task 4.3)

    /// <summary>
    /// Registers one extra hotkey under an explicit id (ShortcutHotkeyBase + n).
    /// Unlike the main hotkey there is NO fallback chain — a taken combo simply
    /// fails and the caller (shortcut editor) surfaces that to the user.
    /// </summary>
    public bool TryRegisterId(int id, string combo)
    {
        if (id == HotkeyId) return false;                       // never collide with the main id
        if (!IsRegistrableCombo(combo))                          // also refuses bare / Shift-only combos
        {
            DiagnosticLogger.Log("Hotkey", $"Shortcut hotkey '{combo}' cannot be parsed or lacks Ctrl/Alt/Win — skipped");
            return false;
        }
        if (!TryParseCombo(combo, out uint mods, out uint vk)) return false;
        UnregisterId(id);   // idempotent re-register
        if (!NativeMethods.RegisterHotKey(_hwnd, id, mods, vk))
        {
            DiagnosticLogger.Log("Hotkey", $"Shortcut hotkey '{combo}' (id {id}) FAILED — win32 error {Marshal.GetLastWin32Error()}");
            return false;
        }
        _extraIds.Add(id);
        DiagnosticLogger.Log("Hotkey", $"Shortcut hotkey '{combo}' registered (id {id})");
        return true;
    }

    /// <summary>Unregisters one extra hotkey id. True when it was registered.</summary>
    public bool UnregisterId(int id)
    {
        if (!_extraIds.Remove(id)) return false;
        try { NativeMethods.UnregisterHotKey(_hwnd, id); } catch { }
        return true;
    }

    /// <summary>True when this extra id currently holds a registration.</summary>
    public bool IsIdRegistered(int id) => _extraIds.Contains(id);

    /// <summary>Re-checks a combo WITHOUT registering it — true when the combo is parseable.</summary>
    public static bool IsValidCombo(string combo) => TryParseCombo(combo, out _, out _);

    /// <summary>
    /// v2.5 — registration-grade check: parseable AND carries Ctrl/Alt/Win. The parser
    /// itself is permissive (Shift+G parses — the MAIN hotkey capture UI rejects bare
    /// / shift-only combos before it ever reaches a registration), but a per-shortcut
    /// hotkey can come from a hand-edited shortcuts.json — a shift-only GLOBAL hotkey
    /// would hijack normal typing system-wide, so registration refuses it.
    /// </summary>
    public static bool IsRegistrableCombo(string combo)
    {
        if (!TryParseCombo(combo, out uint mods, out _)) return false;
        return (mods & (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_WIN)) != 0;
    }

    public void Dispose()
    {
        foreach (int id in _extraIds.ToList())   // v2.5 — drop every shortcut hotkey too
        {
            try { NativeMethods.UnregisterHotKey(_hwnd, id); } catch { }
        }
        _extraIds.Clear();
        Unregister();
    }

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
