# Lumo — a fast, keyboard-first launcher for Windows

<p align="center">
  <img src="https://img.shields.io/badge/version-2.1.0--alpha.1-CA5010?style=flat-square" alt="version"/>
  <img src="https://img.shields.io/badge/status-ALPHA%20·%20UNSTABLE-red?style=flat-square" alt="alpha unstable"/>
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square" alt=".NET 8"/>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?style=flat-square" alt="platform"/>
  <img src="https://img.shields.io/badge/license-MIT-green?style=flat-square" alt="license"/>
  <a href="https://github.com/Anik1377/Lumo-Launcher/actions/workflows/build.yml"><img src="https://github.com/Anik1377/Lumo-Launcher/actions/workflows/build.yml/badge.svg" alt="build"/></a>
</p>

> ⚠️ **ALPHA SOFTWARE — UNSTABLE.** Every release of Lumo is currently an alpha
> build: features may change without notice, bugs are expected, and there may be
> crashes. Use at your own risk and please report anything broken in Issues.

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
| 🚀 | **Command prefixes** | `A/` apps · `F/` files · `C/` calculator · `W/` web · `I/` images · `U/` utilities · `/sc` your shortcuts |
| ⚡ | **Shortcuts & macros** | Save your own one-tap launches — a URL, a file, a folder, or a multi-step macro — and run them with `/sc name` |
| 🔍 | **Hybrid file index** | Background crawl with a tunable cap (10k–300k files) and instant quick-scan fallback while indexing |
| 🧮 | **Safe calculator** | `C/(1920*1080)/3`, `sqrt(2)^10`, `log(1000)` — results copy to clipboard on Enter |
| 🪟 | **Windows 11 design** | Solid Fluent surfaces following the Windows 11 design language: `#202020`/`#F3F3F3` panels, hairline strokes, 4 px control geometry, native DWM rounded corners, Segoe UI — a full-window Settings app in the same style |
| 🖆️ | **Clean vector icons** | A coherent Fluent-style outline icon set (24×24 stroke paths) for every row, hint and button — razor sharp at any DPI, tinted by your accent |
| 🌈 | **Rim glow effect** | A soft comet of light that orbits the **true window perimeter, inside the rim** — never outside — 5 presets, solid accent, 3 speeds, pauses when hidden |
| 📋 | **Clipboard history** | `H/` — your last 50 copies with timestamps, searchable, in memory only |
| ▣ | **Window management** | `S/` — snap the last window left/right, maximize, center or restore; multi-monitor aware |
| 📝 | **Snippets** | Save paste-anywhere texts as shortcuts and trigger with `!name` — multi-line supported |
| 🎬 | **Fluid motion** | Spring-in window, cascading result rows, smooth hover transitions — with a reduced-motion master switch |
| 🎨 | **Accent theming** | 9 accent presets + custom hex, driving the whole highlight system (accent-tinted selection) |
| 🌗 | **Dark / light / auto theme** | Follows the Windows colour mode in Auto, or force Dark/Light — persisted in `settings.json` |
| ⏰ | **Start with Windows** | One toggle in Settings (per-user Run key, no admin rights) |
| 🧳 | **Truly portable** | One ~350 KB exe, no installer |
| 🛡️ | **Never freezes** | Bounded in-memory search pipeline, 60 ms debounce, every handler exception-guarded |
| 📋 | **Diagnostics log** | Everything recorded to `%LOCALAPPDATA%\Lumo\log.txt` for painless troubleshooting |

## 🆕 What's new in v2.1.0-alpha.1 — Smarter (DEV_PLAN Phases 0 + 1)

The first slice of the [development plan](DEV_PLAN.md): a regression-safe test
harness, then three "make every search feel better" features.

1. **🧪 Test harness (Phase 0).** New `src/Lumo.Tests` (xUnit) covers the pure
   core — fuzzy scoring, the safe calculator, settings JSON round-trips, tolerant
   hand-edited settings, the usage store — and CI now runs `dotnet test` **before**
   every publish, so a red test blocks a release. The harness immediately caught
   and fixed a real bug: bare `pi` / `e` (and `pi*2`, `2*e`) never evaluated — the
   parser consumed them as unknown functions.
2. **🔥 MRU ranking (Task 1.1).** Every launch is counted in
   `%APPDATA%\Lumo\usage.json` (written off-thread). Equal-match results are
   boosted up to ×2 + a small recency nudge for the last 7 days, so the apps and
   files you actually open float to the top of A/, F/ and mixed results.
3. **💱 Inline unit + currency conversion (Task 1.2).** `C/10 ft in cm`,
   `C/5kg in lbs`, `C/100f to c`, `C/50 usd to eur` now answer directly in the
   list. Length/mass/volume/data/temperature are offline; FX rates ship with a
   static fallback and quietly refresh from open.er-api.com every 12 h — the
   keystroke path never touches the network.
4. **🌐 Per-query web quick-switch (Task 1.3).** `W/github lumo`, `W/youtube
   cats`, `W/ddg …`, `W/wiki …`, `W/maps …`, `W/so …` route that query to the
   named provider without touching your default engine. Add your own via
   `CustomWebProviders` in settings.json (`"keyword": "https://site.com/?q={0}"`)
   — custom entries win over built-ins.

## 🆕 What's new in v2.0.0-alpha.5 — the Apple-craft pass

A full UI refinement pass guided by Apple's *Designing Fluid Interfaces* (WWDC 2018)
principles, applied to a Windows launcher:

1. **⚡ Respond on pointer-down, not on release.** Every result row, the clear button
   and the gear now dip to 98.5 % the instant you press them (60 ms ease-out, always
   starting from the current value) and spring back on release — the feedback you
   feel before the click even commits.
2. **↕️ The exit mirrors the entrance.** Hiding used to be a plain fade; now the
   launcher scales back down and settles 10 px while fading, along the same path it
   arrived, with the mirrored easing curve. Spatial consistency, the Apple way.
3. **🌀 Slim overlay scrollbar.** The stock chunky WPF scrollbar is replaced by an
   8 px invisible lane with a small rounded thumb that deepens on hover —
   Spotlight/Raycast style.
4. **🌫️ Edge-fade dividers.** The hairlines under the search field and above the
   status bar now dissolve at both ends instead of hard-cutting the surface
   ("scroll edge effects, not hard dividers").
5. **✨ The light-catch.** A 1 px bright edge along the top of the card in dark
   mode — the way light grazes the top of a material. Clipped to the rounded window
   mask so it can never paint the corner cutouts.
6. **🐢 Respects Windows "show animations".** All motion (entrance, exit, stagger)
   gracefully degrades when the OS-level animation setting is off, in addition to
   Lumo's own animation toggle. The stagger was also tightened (22 → 20 ms, 8 → 6 px)
   for a calmer cascade.

## 🆕 What's new in v2.0.0-alpha.4 — the comet never stops again

The alpha.2/3 comet was driven by a WPF **timeline storyboard** (path animations
with a negative begin-time tail and in-place path mutation on resize) — a fragile
combination that could stop after exactly one lap. The engine is rewritten:

1. **♾️ Loop is now mathematical, not clocked.** The perimeter is sampled once into
   720 points; every frame the head/tail positions are computed from
   `elapsed time % lap`. Seamless by construction — there is no repeat behavior
   left to fail.
2. **📉 Zero idle CPU, same as before** — the render hook detaches whenever the
   window hides or loses focus, and re-attaches on show/activate.
3. **📐 Resize + live-settings proof** — dragging the width/thickness sliders or
   typing (window height changes) re-samples the outline in place; the comet
   keeps its time-based position with no restart, no snap, no stall.

## 🆕 What's new in v2.0.0-alpha.3 — accent fixes + advanced customization

1. **🎯 Search-field accent overlap fixed.** While typing, the field used to paint
   a full accent border **and** the 2 px accent bar — two accent layers stacked on
   one box. The Win11-correct treatment now: hairline stroke always, accent only
   in the 2 px bottom focus bar.
2. **💊 Win11 selection pill.** The selected result row gets a 3 px accent bar on
   its left edge (like Win11 list/nav selection), and the accent row tint was
   softened from 21 % → 18 % (dark) / 15 % (light) so content stays readable.
3. **🎛️ Advanced customization** (Settings → Appearance, all applied live):
   - **Glow brightness** — 40–100 % rim comet opacity
   - **Rim thickness** — 2–6 px glowing band
   - **Launcher width** — 560–900 px window width
   - **Corner radius** — Win11 rounded (8 px) or square
   - **Result density** — comfortable or compact rows

## 🆕 What's new in v2.0.0-alpha.2 — the rim comet, rebuilt

The alpha.1 glow used a **spinning gradient brush on the 1 px window border**. On a
720 px-wide rectangle that never looks right: the bright head slides diagonally
across the edges at uneven speed, stalls in the corners, and a 1 px stroke makes
it flicker. alpha.2 replaces the mechanism with the way modern AI chat boxes
actually do it:

1. **🌠 True perimeter comet.** Two soft radial light blobs — a bright head and a
   larger, fainter tail — now travel the **real window outline** (a rounded-rect
   path animation), so the light rounds every corner at constant speed instead of
   sweeping diagonally across the frame.
2. **🔒 Inside only — guaranteed.** The orbit layer is geometrically clipped to the
   window, and an opaque surface patch covers everything but the outer 3 px band.
   The glow physically cannot bleed outside the window or wash over content.
3. **🪶 Calmer & minimal.** The loop slowed from a frantic 3.5 s to a silky 9 s
   (Fast 6 / Normal 9 / Slow 14). No more static top wash — the comet is the only
   glow, exactly like the z.ai chat box.
4. **📐 Resize-proof.** The perimeter path rebuilds in place as the result list
   changes the window height — the comet never snaps back to its start.
5. **⚙️ Settings preview fixed** to show the style's actual palette (the old
   animated-border preview no longer matches the real effect).

## 🆕 What's new in v2.0.0-alpha.1 — the Windows 11 overhaul

1. **🪟 The glass era is over — hello, Windows 11.** Lumo now follows the Microsoft
   Fluent 2 design language: solid `#202020` (dark) / `#F3F3F3` (light) surfaces,
   hairline strokes, 4 px-class control geometry, native DWM rounded corners and
   drop shadows, Segoe UI typography. No acrylic dependency, no fallback surprises —
   it looks the same crisp native way everywhere.
2. **🌈 A whole new glow — the rim comet.** The old rainbow border + outer halo are
   gone. The glow is now a single minimal accent light that **orbits inside the
   window border** (like modern AI chat boxes): a bright head with a soft tail
   chasing the rim, nothing ever bleeding outside the window.
3. **🔍 Win11 search field.** The search row is now a proper Fluent text field:
   4 px rounded box, hairline stroke, and the 2 px accent focus bar along the
   bottom edge while you type.
4. **🖥️ Full-window Settings app.** Settings now opens filling your whole work
   area like a real Windows 11 system app: a 264 px nav sidebar with monochrome
   glyphs and the Win11 selection pill (accent bar + tinted row), large page
   titles, white/dark cards with hairline borders, and Win11-style 40×20 toggles.
   Includes a proper minimize button and an **ALPHA · UNSTABLE** badge in the
   title bar so there's never any doubt.
5. **⚠️ Everything is labelled alpha.** All past and future GitHub releases are
   marked **[ALPHA — unstable]**, the README carries an alpha badge and warning,
   and the exe itself reports `2.0.0-alpha.1 (ALPHA — unstable build)`.

## 🆕 What's new in v1.7.2 — critical launch fix

1. **🛑 Fixed the startup crash in v1.7.0 / v1.7.1** — Lumo died at launch with
   `InvalidCastException: Unable to cast 'System.String' to 'System.Windows.Media.Geometry'`
   (the footer gear button's icon path was fed to WPF as raw text; WPF refuses to
   convert `x:Static` strings into geometries at runtime). The gear and clear-button
   icons are now proper frozen `Geometry` objects and Lumo starts cleanly again.
   A new static lint rule now guards against this whole class of crash.
2. Settings loader is now tolerant of hand-edited boolean values (`1`/`0`) in
   `settings.json` — no more "unexpected JSON type" warning for those.

## 🆕 What's new in v1.7.1 — Enter-key fixes

1. **Enter on a clipboard-history entry now actually copies** — the `H/` list
   promised "Enter to copy again" but the Enter key had no route for clipboard
   rows (silent no-op since v1.6). Picking an entry now copies it and hides the
   launcher, ready to paste.
2. **Enter works on the first result again** — typed searches open with a Raycast-style
   section header ("APPS"…) as row zero, and the launcher pre-selected *it*, so
   pressing Enter did nothing until you arrowed down once. The first actionable
   row is now selected automatically.
3. **Arrow keys skip section headers** — Up/Down no longer land the selection on
   "APPS" / "FILES" / "CLIPBOARD HISTORY" title rows.
4. **Fixed empty icon tiles** — rows using the "✕" glyph (e.g. *Clear clipboard
   history*) mapped to a vector-icon key that didn't exist, rendering a blank tile.

## 🆕 What's new in v1.7 — the Glass update

1. **🪟 Glassmorphism, the real thing** — the launcher now sits on a live acrylic
   backdrop: on **Windows 11 22H2+** it uses the actual system acrylic
   (`DWMWA_SYSTEMBACKDROP_TYPE`, the same transient backdrop Spotlight-style popovers
   get), on **Windows 10 / early Win11** it falls back to composition-attribute acrylic
   with a themed tint. The card paints translucent smoked-glass / light-frost panels on
   top, so your desktop shows through with a beautiful blur. Unsupported systems (remote
   sessions, old GPUs) automatically get the opaque panel — and the new
   **Settings → Appearance → Glass backdrop** toggle lets you switch it off any time.
2. **Native rounded corners + real shadow** — on Win11 the window itself is rounded by
   DWM (`DWMWCP_ROUND`) with the genuine drop shadow, instead of a hand-painted halo
   bleed. The card now *is* the window — cleaner edges, no more shadow banding.
3. **🔆 Modern vector icon set** — every glyph tile, hint row and footer button now
   draws from a coherent **Fluent-style outline icon set** (24×24 stroke paths with
   round caps — the Lucide/Fluent look): app grid, file, percent, globe, image, sliders,
   clipboard, window snap, snippet, zap, gear, record, plus, alert… rendered as frozen
   `StreamGeometry`, razor sharp at any DPI, tinted by your accent colour, no fonts or
   emoji involved. Real shell icons (app logos, file-type icons) are unchanged.
4. **Ambient accent wash** — the old outer glow halo is now a soft accent-coloured
   light source inside the top of the glass card (it fades down into the panel), still
   driven by the 5 border-style presets and still pausing when the window is inactive.
5. **Polish** — the search **clear button** finally appears when you type (a v1.3 style
   trigger bug kept it hidden) and gets an accent hover state; the footer gear is a
   crisp vector icon; glass-aware translucent chips, tiles and separators everywhere.

## 🆕 What's new in v1.6 — the Raycast update

Built after studying [Raycast for Windows](https://www.raycast.com/windows) — same flagship features, same feel, still one ~340 KB portable exe.

1. **⧉ Clipboard History** — type `H/` and see everything you've copied (last 50, with
   "copied 3m ago" stamps). Pick one → it's on your clipboard again, ready to paste.
   Search within history, or clear it in one row. **In memory only** — never written to
   disk, gone when Lumo exits.
2. **▣ Window Management** — type `S/` to snap the window you were just using:
   **Left half / Right half / Maximize / Center / Restore down**. Multi-monitor aware
   (snaps into the work area of the window's own monitor) and it never touches Lumo
   itself — Lumo remembers which app had focus before it popped up.
3. **S Snippets** — your paste-anywhere texts (email drafts, addresses, promo codes).
   Create via `/` → New shortcut → type **Snippet**. Run with `/sc name` or type
   `!name` — Enter copies the text, `Ctrl+V` pastes it anywhere. Multi-line supported.
4. **Raycast-style UI** — the launcher now sits **top-center** like Raycast (no longer
   follows the cursor), the results list has **section headers** (APPS / FILES /
   CLIPBOARD HISTORY / WINDOW MANAGEMENT / SNIPPETS), the search box reads
   *"Search apps and commands…"*, and the footer got a proper divider. Wider panel,
   same Apple-clean aesthetic and glow border.

## 🆕 What's new in v1.5.1 — recorder usability fixes

1. **Keyboard always works** — clicking *Record a macro* (or any row) used to leave
   keyboard focus on the list, so typing went nowhere and the recorder felt broken.
   Lumo now hands focus back to the input box after every recording action and every
   captured launch, and clears the used-up query so the next launch is one keystroke away.
2. **Guidance banner while recording** — the result list now leads with a clear
   "● Recording — type to open an app, file or URL" banner above *Stop & save* / *Cancel*.
3. **Escape cancels the recording** — before, Esc hid the window but the recorder kept
   running invisibly. Now it stops cleanly and tells you.
4. **Settings button tells the truth** — the button reads *"⏹ Recording live — finish in
   the Lumo bar"* while a recording runs, and clicking it again no longer silently wipes
   the steps captured so far.

## 🆕 What's new in v1.5 — macro recorder & visual builder

1. **⏺ Record macros** — hit *Record a macro* (empty view, `/` view, or Settings →
   Shortcuts), then just launch apps, files and URLs through Lumo. Every launch is
   captured as a step. *Stop & save* opens the builder with everything you did —
   no key logging, no global hooks, only Lumo's own launches.
2. **Visual builder (Apple Shortcuts style)** — macros are now editable action
   cards: tap to edit, reorder with ▲▼, remove with ✕, add actions from a type
   palette. **▶ Test run** executes the draft instantly.
3. **New action types** — beyond Open App / URL / File / Folder, macros now
   support **Wait** (100–60000 ms pause) and **Clipboard** (copy text). Up to
   **30 steps** per macro (was 12).
4. **Safer runs** — macros validate before running and execute on a background
   thread, so waits never freeze the launcher; one failing step doesn't stop the rest.

## 🆕 What's new in v1.4.1 — real app icons in results

1. **Real logos beside names** — app results now show the application's actual
   icon (extracted from the Start Menu shortcut's target) instead of the "A"
   letter tile. File rows show their file-type icon, folders show the folder
   icon, and file/folder shortcuts do too.
2. **Fast & light** — icons are resolved once per path on the search's
   background thread and cached in memory; rows without an icon fall back to
   the familiar letter glyph.

## 🆕 What's new in v1.4 — Shortcuts, macros & a smoother ride

1. **Shortcuts & macros (the big one)** — create named one-tap launches and run them
   by typing `/sc <name>` (or just `/` to browse them all). Four kinds:
   **URL** (`/sc mail` → opens gmail.com), **File**, **Folder**, and **Macro** —
   up to 12 targets that open one after another ("morning": work mail + docs + team board).
2. **Create anywhere** — type `/sc` then anything: press Enter on *“Create shortcut …”*
   and the editor opens with the name pre-filled. A friendly editor window handles
   name, type, target (with a **Browse** button), optional extra keywords, validation,
   and `Ctrl+Enter` to save. Shortcuts live in `%APPDATA%\Lumo\shortcuts.json`.
3. **Manage in Settings** — a new **Shortcuts** page lists everything with
   Edit / Delete, and the launcher picks up changes live (no restart).
4. **Quick hits without the prefix** — type a shortcut's name in the default view and
   it appears right under matching apps; the empty view also lists your three
   most-used shortcuts. A `⚡` prefix badge shows when you're in shortcut mode.
5. **Smoother motion** — result rows no longer re-animate on every keystroke: the
   full cascade now plays when the launcher opens or the view changes shape, while
   typing updates bind **instantly** for a snappier feel. Debounce tightened to 60 ms.

## ✨ What landed in v1.3 — Apple-clean UI & fluid motion

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
| `/sc` or `/sc name` | **shortcuts & macros** — run a saved launch, or press Enter on *New shortcut* to create one |
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

## ⚡ Shortcuts & macros — `%APPDATA%\Lumo\shortcuts.json`

Saved shortcuts live next to `settings.json` and are managed from the launcher
(`/sc` → *New shortcut* / *Manage shortcuts*) or **Settings → Shortcuts**. Each entry:

```json
{
  "Id": "8f4c1d2b6a09",
  "Name": "mail",
  "Type": "url",
  "Target": "https://mail.google.com",
  "Steps": [],
  "Keywords": "gmail work"
}
```

`Type` is `url`, `file`, `folder` or `macro` — for a macro, put one target per line in
`Steps` (URLs and paths, up to 12); they all open when the shortcut runs.
`Keywords` are optional extra terms that help `/sc` find it.

## 🛠️ Building from source

```bash
dotnet publish src/Lumo/Lumo.csproj -c Release
# → src/Lumo/bin/Release/net8.0-windows/win-x64/publish/Lumo.exe
```

CI is active (`.github/workflows/build.yml`): every push to `main` builds the portable zip
and uploads it as a build artifact, and pushing a `v*` tag
(e.g. `git tag v1.4.0 && git push --tags`) publishes a new GitHub Release
with the zip attached automatically.

## 🗺️ Roadmap

- [x] Configurable hotkey UI (v1.2 — hotkey recorder in Settings)
- [x] Glow border / visual customization (v1.2)
- [x] Apple-clean UI + fluid motion + auto theme (v1.3)
- [x] User shortcuts & macros with `/sc` (v1.4)
- [x] Windows 11 Fluent UI overhaul + full-window Settings app + rim glow (v2.0)
- [ ] Plugin API for custom commands
- [ ] Everything SDK backend for instant full-disk search
- [ ] Result icons extracted from real shortcuts
- [ ] Pin results / usage-frequency ranking

## 📄 License

[MIT](LICENSE) © Najmul Islam Anik
