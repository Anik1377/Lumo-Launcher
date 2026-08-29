# Lumo — a fast, keyboard-first launcher for Windows

<p align="center">
  <img src="https://img.shields.io/badge/version-1.3.1-7C6CFF?style=flat-square" alt="version"/>
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square" alt=".NET 8"/>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?style=flat-square" alt="platform"/>
  <img src="https://img.shields.io/badge/license-MIT-green?style=flat-square" alt="license"/>
  <a href="https://github.com/Anik1377/Lumo-Launcher/actions/workflows/build.yml"><img src="https://github.com/Anik1377/Lumo-Launcher/actions/workflows/build.yml/badge.svg" alt="build"/></a>
</p>

**Lumo** is a lightweight, portable, universal launcher built with **C# / WPF / .NET 8**.
Press a global hotkey from anywhere, type a few characters, and launch apps, files,
calculations, web searches and system utilities — entirely from the keyboard.

> 💾 **Download the ready-to-run exe from the
> [Releases page](https://github.com/Anik1377/Lumo-Launcher/releases/latest)**
> — extract the zip and run `Lumo.exe`. No installer needed.

---

## ✨ Features

| | Feature | Detail |
|---|---------|--------|
| ⌨️ | **Keyboard-first** | Global hotkey (default `Alt+Space`) summons a centered search window anywhere in Windows |
| 🚀 | **Command prefixes** | `A/` apps · `F/` files · `C/` calculator · `W/` web · `I/` images · `U/` utilities |
| 🔍 | **Hybrid file index** | Background crawl with a tunable cap (10k–300k files) and instant quick-scan fallback while indexing |
| 🧮 | **Safe calculator** | `C/(1920*1080)/3`, `sqrt(2)^10`, `log(1000)` — results copy to clipboard on Enter |
| 🎛️ | **Advanced Settings UI** | macOS-style sidebar settings window: General · Appearance · Hotkey · Search · About — all live-apply |
| 🌈 | **Glow border effect** | Chat-bubble style animated gradient border + halo: 5 colour presets, solid accent, 3 speeds — pauses when hidden |
| 🎬 | **Fluid motion** | Spring-in window, cascading result rows, smooth hover transitions — with a reduced-motion master switch |
| 🎨 | **Accent theming** | 9 accent presets + custom hex, driving the whole highlight system (accent-tinted selection) |
| 🌗 | **Dark / light / auto theme** | Follows the Windows colour mode in Auto, or force Dark/Light — persisted in `settings.json` |
| ⏰ | **Start with Windows** | One toggle in Settings (per-user Run key, no admin rights) |
| 🧳 | **Truly portable** | One ~260 KB exe, no installer |
| 🛡️ | **Never freezes** | Bounded in-memory search pipeline, 80 ms debounce, every handler exception-guarded |
| 📋 | **Diagnostics log** | Everything recorded to `%LOCALAPPDATA%\Lumo\log.txt` for painless troubleshooting |

## 🆕 What's new in v1.3 — Apple-clean UI & fluid motion

1. **Redesigned launcher** — iOS system-grey palettes, larger result rows with
   **kind chips** (App / File / Web / Tool), a search-row magnifier + placeholder +
   clear button (`Ctrl+Backspace`), and keyboard-style hint chips in the status bar.
2. **Accent-tinted highlights** — hover and selection colours are now derived from
   your accent colour (the macOS way), so every accent choice stays coherent.
3. **Motion everywhere** — the window springs in (scale + fade + slide), results
   cascade in with a 22 ms stagger, hover/selection colours cross-fade, and hiding
   fades out. The glow border now **pauses whenever the window is hidden or inactive**
   — zero idle CPU.
4. **Reduced-motion switch** — *Settings → Appearance → UI animations* turns off
   every animation for a snappier experience.
5. **Auto theme** — *Light / Dark / Auto*; Auto follows the Windows personalization
   colour mode.
6. **Settings, macOS-style** — coloured sidebar tiles, segmented controls,
   iOS-style animated switches, and gentle page transitions.

## ✨ What landed in v1.2 — Advanced settings & customization

1. **Full settings window** — open it from the launcher's `Settings` row, the
   gear icon in the status bar, the tray menu (`Settings…`), or type `U/settings`.
   Five sections: **General** (start with Windows, hide on focus loss, web engine),
   **Appearance** (theme, accent, glow border), **Hotkey**, **Search & Index**, **About**.
2. **Glow border effect** — the animated multi-colour border around the launcher
   (like modern chat bubbles): rotating gradient stroke + soft halo. Choose
   Aurora / Sunset / Ocean / Ember / Mint / Solid accent, Fast / Normal / Slow,
   or switch it off — everything applies **live** with a preview strip in Settings.
3. **Hotkey recorder** — click the box, press your combo, hit *Apply*. No manual JSON
   editing. Lumo tests the registration instantly and tells you the combo that won.
4. **Accent colour** — 8 presets plus custom `#RRGGBB`, tinting badges, caret,
   highlights and buttons across both windows.
5. **Start with Windows** toggle and a **tunable index cap** (10k–300k) with
   *Rebuild index now*.
6. The launcher now fades/slides in when summoned.

> ℹ️ **Why can't I use `Win+Space`?** It is reserved by Windows itself for switching
> keyboard layouts — no application can register it (this was the v1.0 "hotkey never
> works" report). Use Settings → Hotkey to pick any other combo, e.g. `Alt+Space`,
> `Ctrl+Alt+Space` or `Win+Q`.

## 🐞 What was fixed in v1.1

1. **Hotkey now works** — the old default `Win+Space` is reserved by Windows; v1.1
   defaults to `Alt+Space` and auto-falls back through
   `Ctrl+Alt+Space → Ctrl+Shift+Space → Ctrl+Alt+M → Win+Q`, logging every attempt.
2. **Shortcut launch fixed** — the window appears immediately, and re-running the
   shortcut while Lumo runs pops up the existing instance via named-pipe activation.
3. **Tray single-click** — one left-click opens the launcher; full right-click menu.
4. **No more freeze-crash on typing** — bounded, synchronous, in-memory search pipeline.

## 📦 Installation

1. Grab the latest zip from **[Releases](https://github.com/Anik1377/Lumo-Launcher/releases/latest)**.
2. Extract anywhere (e.g. `D:\Tools\Lumo`).
3. Run `Lumo.exe`.
4. Requires the [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0/runtime).

## 🚀 Usage

| Keys | Action |
|------|--------|
| `Alt+Space` | Open / hide Lumo (or single-click the tray icon) |
| type | search apps + files + web at once |
| `A/chrome` | applications only |
| `F/report` | file search |
| `C/(1920*1080)/3` | calculator (Enter copies the result) |
| `W/weather tomorrow` | web search — `W/example.com` opens a URL |
| `I/aurora borealis` | image search |
| `U/lock` | utilities: `lock` · `sleep` · `empty bin` · `restart` · `shutdown` · `settings` · `log` |
| `↑ ↓` / `Enter` / `Esc` | select / run / hide |

## ⚙️ Settings — `%APPDATA%\Lumo\settings.json`

The Settings window writes this file for you; every key can still be edited by hand:

```json
{
  "Hotkey": "Alt+Space",
  "Theme": "dark",
  "WebEngine": "google",
  "HideOnFocusLoss": false,
  "AccentColor": "#7C6CFF",
  "BorderEffect": true,
  "BorderStyle": "Aurora",
  "BorderSpeedSec": 3.5,
  "AnimationsEnabled": true,
  "StartWithWindows": false,
  "MaxIndexedFiles": 150000
}
```

`Theme` accepts `dark`, `light` or `auto` (follows the Windows colour mode).
`Hotkey` accepts combos of `Ctrl` `Alt` `Shift` `Win` + a letter, digit, `F1`–`F24`, `Space` or `` ` ``.

## 🛠️ Building from source

```bash
dotnet publish src/Lumo/Lumo.csproj -c Release
# → src/Lumo/bin/Release/net8.0-windows/win-x64/publish/Lumo.exe
```

CI is active (`.github/workflows/build.yml`): every push to `main` builds the portable zip
and uploads it as a build artifact, and pushing a `v*` tag
(e.g. `git tag v1.3.0 && git push --tags`) publishes a new GitHub Release
with the zip attached automatically.

## 🗺️ Roadmap

- [x] Configurable hotkey UI (v1.2 — hotkey recorder in Settings)
- [x] Glow border / visual customization (v1.2)
- [x] Apple-clean UI + fluid motion + auto theme (v1.3)
- [ ] Plugin API for custom commands
- [ ] Everything SDK backend for instant full-disk search
- [ ] Result icons extracted from real shortcuts

## 📄 License

[MIT](LICENSE) © Najmul Islam Anik
