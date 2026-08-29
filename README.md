# Lumo — a fast, keyboard-first launcher for Windows

<p align="center">
  <img src="https://img.shields.io/badge/version-1.1.0-7C6CFF?style=flat-square" alt="version"/>
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
> — extract `Lumo-launcher-x.y.z.zip` and run `Lumo\Lumo.exe`. No installer needed.

---

## ✨ Features

| | Feature | Detail |
|---|---------|--------|
| ⌨️ | **Keyboard-first** | Global hotkey (default `Alt+Space`) summons a centered search window anywhere in Windows |
| 🚀 | **Command prefixes** | `A/` apps · `F/` files · `C/` calculator · `W/` web · `I/` images · `U/` utilities |
| 🔍 | **Hybrid file index** | Background crawl (up to 150,000 files) with instant quick-scan fallback while indexing |
| 🧮 | **Safe calculator** | `C/(1920*1080)/3`, `sqrt(2)^10`, `log(1000)` — results copy to clipboard on Enter |
| 🌗 | **Dark / light theme** | Toggle from the tray menu, persisted in `settings.json` |
| 🧳 | **Truly portable** | One 228 KB exe, no installer, no registry changes |
| 🛡️ | **Never freezes** | Bounded in-memory search pipeline, 80 ms debounce, every handler exception-guarded |
| 📋 | **Diagnostics log** | Everything recorded to `%LOCALAPPDATA%\Lumo\log.txt` for painless troubleshooting |

## 🐞 What was fixed in v1.1

1. **Hotkey now works** — the old default `Win+Space` is reserved by Windows for input-language
   switching, so it never fired. v1.1 defaults to `Alt+Space` and auto-falls back through
   `Ctrl+Alt+Space → Ctrl+Shift+Space → Ctrl+Alt+M → Win+Q`, logging every attempt.
   The live combo is always shown in the window's status bar.
2. **Shortcut launch fixed** — the window now appears immediately at first launch, and
   double-clicking the shortcut while running pops up the existing instance via named-pipe
   activation (no more "second launch does nothing").
3. **Tray single-click** — one left-click on the tray icon opens the launcher; double-click
   and a full right-click menu (Open / Theme / Settings / Exit) are also available.
4. **No more freeze-crash on typing** — search was rebuilt as a bounded, synchronous,
   in-memory pipeline; no blocking calls, no recursion in matching, all handlers guarded.

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
| `U/lock` | utilities: `lock` · `sleep` · `empty bin` · `restart` · `shutdown` · `open settings` · `open log` |
| `↑ ↓` / `Enter` / `Esc` | select / run / hide |

## ⚙️ Settings — `%APPDATA%\Lumo\settings.json`

```json
{
  "Hotkey": "Alt+Space",
  "Theme": "dark",
  "WebEngine": "google",
  "HideOnFocusLoss": false
}
```

`Hotkey` accepts combos of `Ctrl` `Alt` `Shift` `Win` + a letter, digit, `F1`–`F24` or `Space`.

## 🛠️ Building from source

```bash
dotnet publish src/Lumo/Lumo.csproj -c Release
# → src/Lumo/bin/Release/net8.0-windows/win-x64/publish/Lumo.exe
```

CI is active (`.github/workflows/build.yml`): every push to `main` builds the portable zip
and uploads it as a build artifact, and pushing a `v*` tag
(e.g. `git tag v1.2.0 && git push --tags`) publishes a new GitHub Release
with the zip attached automatically.

## 🗺️ Roadmap

- [ ] Configurable hotkey UI (no manual JSON editing)
- [ ] Plugin API for custom commands
- [ ] Everything SDK backend for instant full-disk search
- [ ] Result icons extracted from real shortcuts

## 📄 License

[MIT](LICENSE) © Najmul Islam Anik
