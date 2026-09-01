# Lumo — a fast, keyboard-first launcher for Windows

<p align="center">
  <img src="https://img.shields.io/badge/version-3.0.0--alpha.2-FF6363?style=flat-square" alt="version"/>
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

> 💾 **Download the ready-to-run build from the
> [Releases page](https://github.com/Anik1377/Lumo-Launcher/releases)**
> — extract the **whole** zip into one folder (it contains `Lumo.exe` plus a
> `runtimes\` folder the voice engine loads from) and run `Lumo.exe` from there.
> No installer needed.

---

## ✨ Features — everything, broken down

### Search & launch

| | Feature | Detail |
|---|---------|--------|
| ⌨️ | **Keyboard-first** | Global hotkey (default `Alt+Space`) summons a centered search window anywhere in Windows; `↑↓`/`Enter`/`Esc` select/run/hide |
| 🚀 | **Command prefixes** | 14 built-in views — `A/` apps · `F/` files · `C/` calculator · `W/` web · `I/` images · `U/` utilities · `H/` clipboard · `S/` windows · `B/` bookmarks · `AI/` chat · `?` ask AI · `!` snippets · `P/` plugins · `/sc` shortcuts (full table below) |
| 🔍 | **Hybrid file index** | `F/` — background crawl with a tunable cap (10k–300k files) and instant quick-scan fallback while indexing |
| 🔥 | **MRU ranking** | Every launch is counted (`usage.json`); equal matches are boosted up to ×2 + a recency nudge, so the apps and files you actually use float to the top |
| ★ | **Pinned favourites** | A FAVOURITES section leads the empty view, newest pin first (up to 12) — pin via the right-click menu **or the hover ★** on any row (`favourites.json`) |
| ✂️ | **Row quick actions** | Right-click (or `Ctrl+→`) any row: **Open** (what Enter does), containing folder, copy path/name, open in terminal, run as administrator, pin — each row type gets only the actions that make sense for it |
| 👁️ | **Preview pane** | `Tab` previews the selection — text-file heads (binary-safe, 512 KB cap), image thumbnails, clipboard entries, snippet bodies, URLs — read off-thread with stale-read protection |

### Calculations & conversions (`C/`)

| | Feature | Detail |
|---|---------|--------|
| 🧮 | **Safe calculator** | `C/(1920*1080)/3`, `sqrt(2)^10`, `log(1000)`, bare `pi`/`e` — results copy to clipboard on Enter |
| 📏 | **Unit conversion** | `C/10 ft in cm`, `C/5kg in lbs`, `C/100f to c` — length, mass, volume, data, temperature, all offline |
| 💱 | **Currency conversion** | `C/50 usd to eur` — static fallback rates, quietly refreshed from open.er-api.com every 12 h; the keystroke path never touches the network |

### Web & knowledge

| | Feature | Detail |
|---|---------|--------|
| 🌐 | **Web search** | `W/` searches your default engine; `W/example.com` opens a URL directly |
| 🔀 | **Per-query quick-switch** | `W/github dotnet`, `W/youtube cats`, `W/ddg …`, `W/wiki …` route that one query to the named provider — 16 built-ins + your own via `CustomWebProviders` (table below) |
| 🖼️ | **Image search** | `I/aurora borealis` — Google Images results |
| 🔖 | **Browser bookmarks** | `B/` searches Chrome & Edge bookmarks (all profiles, capped at 8) read-only in the background — fuzzy over name, folder path and URL, newest first on the empty query |

### Shortcuts, snippets & windows

| | Feature | Detail |
|---|---------|--------|
| ⚡ | **Shortcuts & macros** | Save one-tap launches — a URL, a file, a folder, or a macro of up to 12 steps — run with `/sc name` or from anywhere with **a per-shortcut global hotkey** (up to 16, e.g. `Ctrl+Alt+G`) |
| 📝 | **Snippets with variables** | Save paste-anywhere texts and trigger with `!name` — Enter copies, Ctrl+V pastes. Live variables: `{{date}}`, `{{time}}`, `{{datetime}}`, `{{clipboard}}`, `{{name:Jane}}` (fallback default), `{{cursor}}`; unknown tokens stay verbatim, expansion never recurses |
| 🎙️ | **Macro recorder** | Record a launch sequence live (`/sc` → record), replay it from one row, cancel a pending restart countdown from the same list |
| ▣ | **Window management** | `S/` — snap the last-active window left/right half, maximize, center or restore; multi-monitor aware |

### AI (local or Anthropic)

| | Feature | Detail |
|---|---------|--------|
| 🤖 | **Quick AI answers** | `?` + a question — the answer lands right on the result row; Enter copies it. Requests fire off the UI thread, deduped in-flight, stale replies discarded, 8-entry scratchpad cache |
| 💬 | **AI chat** | `AI/` (or the bare word `AI`) opens a dedicated chat tab — streaming replies, markdown rendering, multi-line input, polished to the prompt-kit design language (gradient avatars, typing dots, collapsible reasoning blocks for thinking models, image attachments for vision models, timestamps, one-click code copy) |
| 🗂️ | **Chat sessions** | Full history (`chats.json`, 40 sessions / 200 messages) with a Raycast-style slide-over sidebar, pin/rename/delete curation, `Ctrl+N` new chat, last-answer-only regenerate |
| 🎭 | **Personas** | 6 built-in system-prompt personas + your own (`personas.json`, edited in Settings → AI) — pick per chat from the persona chip flyout |
| 🎤 | **Voice typing (Whisper)** | The chat's mic button (or `Ctrl+M`): click, speak the whole thought, click again — the clip is recorded in full and transcribed as **one batch** by **OpenAI Whisper** (whisper.cpp, fully offline) for dramatically better accuracy than the Windows recognizer; a one-time guided download sets the model up from the chat itself (the built-in Windows speech stays available as the fallback). A live waveform + elapsed clock prove the mic is really rendering, a **prominent pulsing red stop cap** finishes the clip, and pause/resume records one continuous sentence across interruptions. Silence is trimmed before recognition; nothing ever leaves the PC |
| 🔌 | **Providers** | **Ollama** (local, no key — one-click setup incl. model pull) or **Anthropic** Messages API (key stored only in `settings.json`, redacted from every log line) |

### Plugins (extensible keywords)

| | Feature | Detail |
|---|---------|--------|
| 🧩 | **JSON plugins** | New keyword commands as a single `plugin.json` — `web` (search URL), `open` (URL/path), `copy` (clipboard text), each with a `{query}` placeholder. No code, no DLLs, nothing leaves your PC |
| 📥 | **First-party catalog** | **Settings → Plugins → Browse catalog** — download ready-made plugins (Developer Search, Social Search, Copy Kit…) straight from the Lumo repo, one click, live immediately |
| 🤖 | **AI authoring prompt** | A copyable prompt (in the app and in the docs) that makes any AI assistant write a valid `plugin.json` for you — [guide](docs/PLUGIN_DEVELOPMENT.md) |
| ⌨️ | **Token routing** | Type the keyword alone or + a query (`emo sunset`); keywords that start with what you type surface as quick-hits; `P/` lists everything with a starter, the folder, a rescan and the AI prompt |

### Appearance & behaviour

| | Feature | Detail |
|---|---------|--------|
| 🪟 | **Raycast-grade design** | Near-black surface ladder, hairline strokes, quiet selection with an accent pill, embedded **Inter** typography, flush search header — one design system across launcher, Settings and AI chat |
| 🧊 | **Frosted glass** | Real DWM acrylic blur-behind under the launcher card (fallback to solid where unsupported; `"Acrylic": false` to opt out) |
| 🌈 | **Rim glow** | A soft comet of light orbiting the true window perimeter, inside the rim — 6 styles, 3 speeds, brightness/thickness sliders, pauses when hidden, zero idle CPU |
| 🎨 | **Accent theming** | 9 accent presets (Raycast red `#FF6363` leads) + custom `#RRGGBB`, driving the whole highlight system |
| 🌗 | **Dark / light / auto** | Follows the Windows colour mode in Auto, or force Dark/Light — persisted in `settings.json` |
| 🎬 | **Fluid motion** | Spring-in window, cascading rows, press-feedback dip, mirrored exit — a reduced-motion master switch + Windows "show animations" respected |
| 📐 | **Layout controls** | Launcher width 560–900 px, Win11 rounded or square corners, comfortable/compact row density |

### Platform

| | Feature | Detail |
|---|---------|--------|
| ⌨️ | **Hotkey choice** | Recorder in Settings; combos of `Ctrl Alt Shift Win` + letter/digit/`F1`–`F24`/`Space`/`` ` ``; auto-fallback chain if taken (why not `Win+Space`? [Reserved by Windows](#-usage--every-key)) |
| 📥 | **Auto-update** | Quietly checks GitHub Releases ~daily (opt-out); tray nudge + Settings → About card: check now, download with progress, open the staged zip. Installing stays a two-step by design — extract over `Lumo.exe` |
| 🧳 | **Portable data mode** | A `data` folder next to `Lumo.exe` redirects EVERY store (settings, shortcuts, chats, plugins, favourites, log, updates) into it — the whole setup travels on a USB stick; no folder = classic `%APPDATA%\Lumo`, byte-identical |
| 🧭 | **First-run tour** | A 3-step intro (hotkey → prefixes → data & updates) on first launch, skip-safe, replayable from Settings → About |
| 🖥️ | **System tray** | Single-click opens the launcher; right-click menu; update balloon; the real app icon everywhere (tray included) |
| 🚀 | **Start with Windows** | One toggle (per-user Run key, no admin rights) |
| 🛡️ | **Never freezes** | Bounded, synchronous, in-memory search pipeline; 60 ms debounce; every handler exception-guarded — a text change can never throw or block the UI thread |
| 📋 | **Diagnostics log** | Everything recorded to the log for painless troubleshooting — open via `U/log` |
| 📦 | **Truly portable** | One ~2.5 MB framework-dependent exe (Inter embedded), .NET 8 Desktop Runtime required |

---

## 🎹 Usage — every key, every prefix

### The prefixes

| Prefix | View | Examples |
|--------|------|----------|
| *(none)* | Mixed: apps + files + tools + plugin quick-hits | `chrome`, `report` |
| `A/` | Installed applications | `A/chrome`, `A/term` |
| `F/` | Files (hybrid index) | `F/report`, `F/tax 2025` |
| `C/` | Calculator + units + currency | `C/(1920*1080)/3` · `C/10 ft in cm` · `C/50 usd to eur` |
| `W/` | Web search (default engine; a URL opens directly) | `W/weather tomorrow` · `W/github.com` |
| `I/` | Image search | `I/aurora borealis` |
| `U/` | System utilities (list below) | `U/lock`, `U/mute`, `U/restart` |
| `H/` | Clipboard history (last 50, in-memory) | `H/api`, pick a row to copy again |
| `S/` | Window management for the last-active window | `S/left`, `S/max` |
| `B/` | Chrome & Edge bookmarks | `B/github` |
| `AI/` | AI chat tab (bare word `AI` works too) | `AI/explain quantum computing` |
| `?` | Quick AI answer on the row (enable in Settings → AI) | `?regex for an ISO date` |
| `!` | Snippets — Enter copies, Ctrl+V pastes | `!email`, `!addr` |
| `P/` | Plugins — browse, run, manage | `P/`, `emo sunset` (keyword + query) |
| `/sc` | Shortcuts & macros (`sc` optional) | `/sc work`, `/sc` → *New shortcut* |

### `W/` quick-switch keywords

The first word of a `W/` query names the provider for that one query —
`W/github dotnet 8` searches GitHub, `W/ddg news` DuckDuckGo, while your
default engine stays untouched:

| Keyword | Searches | | Keyword | Searches |
|---|---|---|---|---|
| `google` | Google | | `news` | Google News |
| `bing` | Bing | | `images` | Google Images |
| `ddg` / `duckduckgo` | DuckDuckGo | | `amazon` | Amazon |
| `github` | GitHub | | `npm` | npm |
| `youtube` | YouTube | | `nuget` | NuGet |
| `maps` | Google Maps | | `so` / `stackoverflow` | Stack Overflow |
| `wiki` | Wikipedia | | `scholar` | Google Scholar |

Add your own in `settings.json` → `CustomWebProviders`
(`"keyword": "https://site.com/?q={0}"`) — custom entries win over built-ins.

### `U/` utilities

`lock` · `sleep` · `hibernate` · `mute` (volume toggle) · `empty bin` ·
`night light` · `battery` · `restart` · `restart in 10 seconds` (cancellable —
a ✕ row floats to the top until it fires) · `shutdown` · `settings` (window) ·
`settings file` · `log`

### Snippet variables

| Token | Expands to |
|---|---|
| `{{date}}` | today, ISO (`2026-08-31`) |
| `{{time}}` | current time |
| `{{datetime}}` | date + time |
| `{{clipboard}}` | current clipboard text |
| `{{name:Jane}}` | a fill-in with a fallback default |
| `{{cursor}}` | caret marker for future paste positioning |

Unknown tokens stay **verbatim** (a visible typo beats silent loss) and
expansion is never recursive.

### Keys

| Keys | Action |
|------|--------|
| `Alt+Space` *(default)* | Open / hide Lumo (or single-click the tray icon) |
| `↑ ↓` / `Enter` / `Esc` | select / run / hide |
| `Tab` | open the preview pane for the selection |
| `Ctrl+→` | open the quick-action menu (again closes) |
| `Ctrl+N` (in AI chat) | new chat session |
| `Ctrl+M` (in AI chat) | start / stop voice recording — the overlay shows a live waveform; the red cap finishes & transcribes (Whisper, offline), pause/resume holds mid-recording, `Esc` cancels |
| `F11` | fullscreen / window toggle (AI chat) |

### Plugins in one minute

A plugin is a folder with one `plugin.json` — new keywords for the launcher,
no code:

```json
{
  "name": "My first plugin",
  "author": "you",
  "version": "1.0",
  "commands": [
    { "keyword": "so", "name": "Stack Overflow search", "type": "web",
      "template": "https://stackoverflow.com/search?q={query}" },
    { "keyword": "time", "type": "open", "template": "https://time.is", "argOptional": true }
  ]
}
```

Drop it in `%APPDATA%\Lumo\plugins\<id>\plugin.json`, rescan, and `so rust
lifetimes` / bare `time` work everywhere. Or **Settings → Plugins → Browse
catalog** to one-click install the first-party set (Developer Search, Dev
Tools, Social Search, Movies & Music, Shopping, Quick Jumps, Web Utilities,
Copy Kit). The full schema, routing rules, limits and the copyable AI
authoring prompt live in the
**[plugin development guide](docs/PLUGIN_DEVELOPMENT.md)**.


## 🆕 What's new in v3.0.0-alpha.2 — the Lumo hub: an App Deck bound to your numpad

v3 phase 2 of 3:

1. **🎛️ The window is a hub now.** A quiet nav rail lives on the left edge of
   the AI window: **AI** (the chat) and **App Deck**, plus a settings gear at
   the bottom. Switching keeps the chat's state alive underneath — the deck is
   an overlay, not a page reload.
2. **🎮 App Deck — nine one-keystroke launches.** A 3×3 grid that mirrors your
   numpad's physical layout. Click a card to launch, drop any file/shortcut
   onto a card to assign it, right-click (or click an empty card) to edit name,
   target, arguments and start-in folder. Icons are extracted from the exe/lnk
   automatically. While Lumo is focused, **numpad 1–9** (or the top-row digits)
   launch the matching slot instantly.
3. **🌍 Optional global hotkeys.** Settings → General → *Global numpad
   hotkeys* registers numpad 1–9 system-wide (MOD_NOREPEAT, per-user, no
   admin) so the deck fires from anywhere — including over full-screen games
   that don't own the keys. Off by default, and the trade is spelled out in
   the setting itself.
4. **💾 Slots persist.** `appdeck.json` in the data folder follows the same
   atomic-store doctrine as everything else; the launch policy validates the
   target and reports readable errors ("Can't find X — it may have moved").

## 🆕 What's new in v3.0.0-alpha.1 — the v3 foundation: a real design system, themes you can share, and an actual installer

v3 phase 1 of 3 — the ground everything else builds on:

1. **🎨 The WPF-UI Fluent library is in.** Lumo now builds on
   [WPF-UI](https://wpfui.lepo.co) (lepoco/wpfui, MIT) — the leading Fluent
   design library for WPF. Its modern implicit styles carry base controls
   (buttons, toggles, scrollbars, tooltips) app-wide, while Lumo's own
   `ThemeService` stays the palette authority and keeps the Fluent layer
   synced to the active mode + accent. One component library, one design
   language.
2. **🖌️ One theme engine, five windows.** Every window used to hand-build
   its own ~30 brushes (four near-identical `ApplyTheme` copies that had
   already drifted apart). They all paint from the new shared
   **ThemeService** now — which made the next item possible at all.
3. **🎭 A theme system with import/export.** Seven built-in themes in the
   gallery (Settings → Appearance): Lumo Dark/Light, **Claude Dusk**,
   **Parchment**, **Nord**, **Matcha** and **Graphite** — each card is a live
   miniature of the palette and applies instantly. **Import…** loads any
   `lumo.theme/1` JSON file, **Export…** writes the current look to share.
   Old settings keep working: with no theme picked, the classic dark/light/
   auto + accent pair rules.
4. **✨ The glow system, fixed to minimalism.** The orbiting rim comet (two
   light blobs, 720-point perimeter sampling, a rendering clock, style
   presets, speed/brightness/thickness sliders) is **gone**. What remains is
   the **edge shine**: a 1 px light catch along the launcher's top edge that
   settles into the hairline — a static brush, zero idle CPU, nothing moves.
   The AI orb halo breathes a whisper instead of a lighthouse pulse.
5. **🎈 Smooth scrolling everywhere it matters.** A new `SmoothScroll`
   behavior glides the Settings panels, the AI chat log and long lists with
   exponential easing on the composition clock instead of WPF's hard
   48 px wheel jumps — honors the animations master switch and reduced-motion.
6. **📦 Lumo is actually installable now.** Tag releases ship a real
   **`LumoSetup-<version>.exe`** (Inno Setup) next to the portable zip:
   per-user install to `%LOCALAPPDATA%\Programs\Lumo`, no admin prompt,
   start-menu + optional desktop shortcut, optional start-with-Windows,
   a real uninstaller — and the full portable layout (Lumo.exe +
   `runtimes\win-x64\`) lands intact either way.

## 🆕 What's new in v2.6.0-alpha.8 — the AI chat, cloned to the ChatGPT / Claude design language

With voice and text both working, the chat page itself now looks the part —
rebuilt to the layout grammar of chatgpt.com and claude.ai, natively in WPF:

1. **💬 ChatGPT's message rhythm.** User messages sit in the quiet elevated
   gray pill (uniform 20 px radius, no more inverted-white shout) — chatgpt.com's
   own `#2F2F2F` bubble. Assistant answers stay full-width plain text beside the
   orb avatar, with a bigger 14 px body, taller 21.5 px line height and a
   generous 22 px gap between turns — each answer reads as its own block on the
   page, exactly like the real thing.
2. **⬆️ The signature send cap.** The gradient accent button is gone: the
   composer's send is now ChatGPT's solid circle with the inverted arrow — black
   on white in dark mode, white on near-black in light — dimming while disabled
   and doubling as the stop button mid-generation. No glow, no gradient.
3. **🎛️ Borderless top pickers.** The model and persona chips dropped their
   borders for chatgpt.com's top-bar treatment: the name IS the control, a small
   chevron rides along, and hover breathes a soft fill instead of drawing a box.
4. **📝 Composer & page polish.** The prompt shell grew to a rounded-24 pill
   (matching the voice overlay), the conversation column widened to 760 px in a
   larger 860×640 window, the empty state greets with **"What can I help
   with?"**, code blocks round to 12 px, and the footer now carries ChatGPT's
   disclaimer line — *"Lumo AI can make mistakes — double-check important
   answers."* — above the quieter keyboard captions.

## 🆕 What's new in v2.6.0-alpha.7 — whisper actually starts: the "Native Library not found" fix

Root-cause fix for the field report where recording worked but every
transcription died with `FileNotFoundException: Native Library not found in
default paths…`:

1. **🎤 The whisper.cpp engine now ships where its loader actually looks.**
   alpha.5/6 embedded the native dlls (`whisper.dll` + three `ggml` dlls)
   inside the single-file exe and let .NET unpack them to a temp folder on
   first run — but Whisper.net 1.9.1's loader probes **only**
   `runtimes/win-x64/` next to Lumo.exe (verified against its source: the
   check is a plain `Directory.Exists` + `File.Exists` walk) and never sees
   the temp extraction. Dev runs worked because the build output already has
   the folder; every installed copy failed. The dlls are now real files in
   `runtimes/win-x64/` beside the exe — the exact layout the loader reads.
   Nothing to extract, nothing for an antivirus to lock mid-extract, and
   `Lumo.exe` is a lean single file again (~4.9 MB, down from ~10.4 MB).
2. **📦 The release zip carries a `runtimes/` folder — keep it together.**
   Extract the whole zip into one place and run `Lumo.exe` from there; if you
   move the exe, move the folder with it. Lumo pre-flights the layout before
   every transcription: a missing dll now yields a plain-English message —
   *"the Whisper runtime file … is missing next to Lumo.exe
   (runtimes/win-x64) — re-extract the full Lumo zip, keeping every file and
   folder together"* — in both the chat failure line and `lumo-log.txt`,
   instead of whisper's cryptic loader exception.
3. **🧪 +7 tests → 358** — the layout rule (required dll set, path
   composition, first-missing detection, message wording) is pure Core and
   unit-tested; the packaging script packs just the `win-x64` natives this
   x64-only app loads.

## 🆕 What's new in v2.6.0-alpha.6 — voice fixes: downloads that survive, a waveform that moves, a mic that never traps you

First field-report fixes for the alpha.5 voice + prompt-kit build:

1. **📥 Whisper downloads can't strand you anymore.** Broken or interrupted
   model downloads now **resume where they stopped** (partial data lands in a
   stable `.part` file; each attempt continues with an HTTP Range request) and
   transient failures **retry automatically** — one network hiccup no longer
   kills a 148–488 MB stream. Stale temp files from earlier attempts are
   swept.
2. **🔓 The "file is in use" error is gone.** Re-downloading (or repairing) a
   model used to fail because the loaded Whisper engine still held the model
   file open — the engine is now released before the fresh file moves into
   place, and the move itself retries a few times to ride out an antivirus
   scanner that's still inspecting the fresh download.
3. **📈 The waveform reacts to every mic.** The bars now **auto-gain** against
   a decaying peak: a quiet microphone gets amplified (up to ~3.6×) instead of
   drawing a near-flat line, silence stays perfectly flat, and after loud
   speech the sensitivity recovers gradually. Slightly taller bars, too.
4. **🎤 The setup card gained a mic button and a way back.** The one-time
   Whisper download card now has a **real mic button** — record right away
   with the built-in Windows speech, no download needed — and a **close (✕)
   button** (Esc still works) so a failed download can never trap you away
   from the prompt. The Windows-speech fallback is now **session-only**: one
   fallback no longer permanently demotes Whisper in settings.json.
5. **🧪 +2 tests → 351.** The auto-gain curve (quiet lifted, silence flat,
   saturation clamped, gradual re-sensitization).

## 🆕 What's new in v2.6.0-alpha.5 — Whisper voice + the prompt-kit AI chat

The two headline asks at once: a genuinely better transcription engine, and an
AI chat page rebuilt to the [prompt-kit](https://www.prompt-kit.com) design
language.

1. **🥇 OpenAI Whisper is now the default voice engine.** The chat's mic no
   longer leans on the Windows desktop recognizer — clips are transcribed by
   **Whisper via whisper.cpp** (the same weights, fully offline, no cloud, no
   key), which is dramatically more accurate on real speech. It is
   **install-on-demand**: the first mic click offers a one-time guided setup
   card — pick the model (Tiny 78 MB / Base 148 MB / Base multi / Small 488 MB
   — official whisper.cpp ggml checkpoints), watch the progress bar, and
   recording starts the moment it lands. The Windows recognizer stays one click
   away as the fallback ("Use Windows speech" on the card, or
   `"VoiceEngine": "windows"` in settings.json; `"VoiceModel"` picks a model).
2. **📈 Proof-of-life recording UI.** While you speak, the prompt row is
   replaced by a recording overlay: a **live scrolling waveform** driven by the
   actual capture buffers (10 Hz metering, silence rests flat), a **recording
   red dot**, and an elapsed clock that excludes paused time. Transcribing
   freezes the waveform — you can always tell which stage is running.
3. **🔴 The stop button is impossible to miss.** Finishing a clip is a **46 px
   pulsing red cap with a soft glow**, flanked by pause/resume (the mic keeps
   running but the paused stretch is really discarded from the clip) and
   cancel. The mic / `Ctrl+M` / `Enter` shortcuts still finish too.
4. **🎨 The AI chat gets the full prompt-kit treatment** (rebuilt natively in
   WPF — still no WebView): right-aligned accent **chat bubbles** with the
   sharp-tail corner, **gradient message avatars** with a sparkle mark, sine
   -wave **typing dots**, **message timestamps**, one-click **code copy**
   buttons, collapsible **Reasoning blocks** that stream live for thinking
   models (`deepseek-r1` & friends — the `<think>` half is split out of the
   visible answer and out of the API history), **image attachments** for vision
   models (paste a screenshot or use the attach button — Ollama and Anthropic
   vision shapes are both wired, with a payload budget that stops old turns
   from re-uploading), and the polished **prompt input** with focus ring and
   gradient send cap.
5. **🧪 +33 tests → 349.** The Whisper model catalog (https-only URLs, unique
   ids/files, junk-name rejection), language mapping (English-only pins, junk
   pins fall back to auto), the waveform meter math (silence floor, loudness
   saturation), the reasoning splitter (closed blocks, streaming partials,
   case/tag variants, preface text), the image payload guard rails (media
   types, size cap, byte counts) and both providers' image JSON shapes.

## 🆕 What's new in v2.6.0-alpha.4 — voice typing rebuilt: record → transcribe → show

The AI chat's mic no longer transcribes live — accuracy first:

1. **🎙️ The whole clip, then one recognition.** alpha.3 finalized a segment at
   every 450 ms pause, so half-thoughts were committed mid-sentence and every
   "final" word stuck. Now: click the mic (or press `Ctrl+M`) and speak — Lumo
   **records the complete clip**; click again (or press `Enter`) and the
   recording is recognized as **one batch** on the desktop speech stack that
   ships with Windows; only then does the text appear in the prompt, ready to
   edit or send. Still **no cloud, no API key, no extra install** — the AI
   conversation never leaves the PC.
2. **✂️ Silence is cut before recognition.** Room tone at the edges made SAPI
   hallucinate filler words ("the", "uh") — the clip is trimmed to the spoken
   part (with breathing-room padding) before the recognizer ever sees it.
3. **⌨️ Predictable keys.** While recording: mic / `Ctrl+M` / `Enter` finishes
   and transcribes, `Esc` cancels the clip. While transcribing: `Esc` discards
   the pending text. The transcription appends to whatever you already typed;
   what appears is exactly what a second `Enter` sends.
4. **🧪 +6 tests → 316.** WAV header layout (RIFF/fmt/data, byte-exact),
   payload round-trip, and silence trimming (edge cutting + padding,
   all-speech, silence-only, quiet-but-audible speech kept).

## 🆕 What's new in v2.6.0-alpha.3 — voice typing in the AI chat

1. **🎤 Dictate into the AI chat.** The prompt box grew a mic button — click it
   (or press `Ctrl+M`) and speak: offline Windows speech turns your voice into
   text **live**, right in the input, segments committing as you pause. `Enter`
   stops listening and sends exactly what you saw; `Esc` stops without sending;
   dictating appends to whatever you already typed. It runs on the desktop
   speech stack that ships with Windows itself — **no cloud, no API key, no
   extra install**, matching Lumo's local-first doctrine (the default Ollama
   brain + a local mic = an AI conversation that never leaves the PC).
2. **🎛️ One toggle.** **Settings → AI → Voice input** switches the feature; the
   card reports whether a recognizer is installed (English ships with English
   Windows; more languages come from Windows Settings → Time & Language →
   Speech, or pin one via `"VoiceLanguage"` in settings.json).
3. **🧪 +20 tests → 310.** Recognizer picking (exact culture, exact id,
   language-part fallback, OS UI-language default, last-resort first, empty
   machines), dictation text composition (spacing rules, whitespace-only
   bases, empty segments), and the new settings keys (tolerant read,
   `RestoreFrom`, `Clone` round-trip).

## 🆕 What's new in v2.6.0-alpha.2 — the plugin ecosystem

Phase 4's plugin system grows an ecosystem: official plugins installable from
inside the app, a full development guide, and an AI authoring shortcut.

1. **📥 First-party plugin catalog.** **Settings → Plugins → Browse catalog**
   fetches the official catalog from the Lumo repo and installs any plugin
   with one click — the manifest is downloaded, validated with the production
   parser, written to your plugins folder and rescanned; the keywords work
   immediately. The launch set: **Developer Search** (so, mdn, npm, pypi,
   crates, docker), **Developer Tools** (regex, devdocs, caniuse, jsonfmt,
   ghstatus, speedtest), **Social Search** (reddit, x, yt, twitch, pins),
   **Movies & Music** (imdb, sp, netflix, tmdb, lastfm), **Shopping** (amzn,
   ebay, ali, etsy), **Quick Jumps** (gmail, gcal, gdrive, keep, notion,
   trello, whatsweb, teleweb), **Web Utilities** (tr, weather, wayback,
   isdown, tempmail) and **Copy Kit** (lorem, greet, shrug, tableflip,
   divider…). The catalog is fetched on demand — never at startup, never on a
   keystroke — and installs are atomic (tmp-file swap) and re-runnable as the
   update path. P/ gained a *Download first-party plugins* row too.
2. **📖 Plugin development guide.** A new
   **[docs/PLUGIN_DEVELOPMENT.md](docs/PLUGIN_DEVELOPMENT.md)** — the full
   manifest schema as tables, the three command types with examples, routing
   rules (static routes win, token-exact keywords, first-plugin-owns-keyword),
   the hard limits, debugging via the log, publishing to the official catalog
   via PR, and an FAQ.
3. **🤖 AI authoring prompt.** A ready-to-paste prompt carrying the whole
   plugin contract — copy it from **Settings → Plugins → Copy AI prompt**,
   from **P/**, or from the guide, describe the plugin you want in one line,
   and any AI chat returns a valid `plugin.json`.
4. **📚 README, fully broken down.** Every feature (through v2.6), every
   prefix with examples, every `W/` quick-switch keyword, every utility, the
   snippet variables, and the plugins section now live in the README as
   reference tables.
5. **🧪 +22 tests → 290.** Catalog parsing (junk rows, https-only, id
   sanitization, dedupe, caps, clipping), install-state comparison (numeric
   version ordering, non-version fallbacks), the atomic manifest write path,
   the AI prompt contract, and a repo-consistency test that fails the build
   if a registry entry ever drifts from its manifest (or two first-party
   plugins ever claim the same keyword).

## 🆕 What's new in v2.4.0-alpha.2 — the frosted-glass material

The launcher goes from "styled like Raycast" to **made of the same material**:

1. **🧊 Real frosted glass.** The launcher card is now translucent with a genuine
   DWM acrylic blur-behind (`SetWindowCompositionAttribute` → acrylic) under a
   translucent panel brush — the frosted desktop shows through the palette, the
   signature Raycast depth, natively (no WebView, still one portable exe).
   Automatic fallback to the solid card on pre-17134 builds, remote sessions,
   or a refused compositor call; opt out with `"Acrylic": false` in
   `settings.json`.
2. **🎯 The active row wears the accent.** Following Raycast's DESIGN.md —
   *"the accent ONLY for the active row title and the caret"* — the selected
   row's title now tints to your accent while selection itself stays quiet.
3. **🔠 Uppercase section headers.** The root list groups like Raycast's home
   screen: 11 px, semibold, muted, uppercase.
4. **⌨️ Raycast keycaps.** Footer hint chips tighten to spec — 4 px radius,
   min-width 20, brighter 11 px text.
5. **📏 A 54 px search band** and 16 px input type for the flush header.

## 🆕 What's new in v2.4.0-alpha.1 — the Raycast-grade UI overhaul

The whole app was re-skinned from the ground up. Every surface, stroke, glyph
and animation now follows one design system inspired by **Raycast**'s command
palette — researched from Raycast's published design tokens and rebuilt natively
in WPF (no WebView, still one portable exe).

1. **🌊 The Raycast surface ladder.** The flat Win11 grays (`#202020`) are gone.
   Dark mode is now a near-black, faintly blue-tinted canvas (`#0E0F12`) with one
   elevation step up for fields and tiles (`#17181C`), hairline strokes
   (`#26282D`), ink `#F4F4F6` and mute `#9B9CA1` — the same tiering Raycast uses
   (`canvas → surface → surface-elevated → card`). Light mode mirrors the
   structure (`#FAFAFB` canvas, white fields, `#E6E7EA` hairlines).
2. **🔤 Inter, embedded.** Lumo ships the Inter type family (Regular / Medium /
   SemiBold / Bold, SIL OFL) inside the exe — the same type system Raycast builds
   on. Name tables are flattened so `FontWeight` picks the exact face; every
   window, list row, card and caption now renders in Inter.
3. **⌨️ Command-palette search header.** The boxed Win11 text field is replaced by
   a flush search row — the input IS the header, like Raycast/Spotlight — with a
   full-bleed hairline beneath, a larger quiet magnifier, and no focus chrome
   (the caret is the affordance). Rows are quieter too: medium-weight titles,
   8 px-class icon tiles, Raycast-style section headers, and a **quiet neutral
   selection** with the accent demoted to a 3 px pill.
4. **🪟 Settings & AI chat, same system.** Settings gets 8 px cards, a deeper
   sidebar tier, 6 px controls, a richer window shadow and Inter headings; the AI
   chat inherits the ladder (elevated cards, deeper caption bar, darker code
   blocks) so all three surfaces read as one product.
5. **🔴 Raycast red is the new signature accent** (`#FF6363`, first in the preset
   list) — installs whose accent was only ever an old default migrate silently;
   a colour you actually picked is respected. The launcher also widens to a 744 px
   Raycast proportion by default (still user-tunable 560–900).

## 🆕 What's new in v2.3.0-alpha.1 — Connected (DEV_PLAN Phase 3)

The launcher reaches out: answers from an AI model, your browser's bookmarks,
and snippets that know what day it is.

1. **🤖 AI answers — `?` prefix (Task 3.1, the flagship).** Type `?` and a
   question: the request fires **off the UI thread the moment you settle on a
   prompt** (in-flight deduped, stale replies discarded), and the answer appears
   right on the result row — Enter copies the full multi-line text. Works with
   **local Ollama** (default `http://localhost:11434`, no key, nothing leaves
   your machine) and the **Anthropic Messages API** (key in Settings; it is
   stored only in `settings.json` on this PC and is **never written to the
   log** — every log line passes a redaction helper). Configure it all in the
   new **Settings → AI** page: enable, provider, endpoint, model, key. Answers
   are cached (8-entry scratchpad) so re-typing a prompt re-shows the answer
   instantly.
2. **🔖 Browser bookmarks — `B/` prefix (Task 3.2).** Lumo reads Chrome and
   Edge `Bookmarks` files (Default, Profile 1, Profile 2 … capped at 8 profiles)
   **in the background** and serves them from memory: fuzzy over name, folder
   path and URL. The empty query shows your **newest bookmarks first**; rows are
   ordinary Web rows — Enter opens, quick actions copy the URL, the hover ★
   pins. Read-only: Lumo never edits browser data, and a cheap mtime probe
   picks up new bookmarks without touching the search pipeline.
3. **📝 Snippet variables — Task 3.3.** Snippets (and macro step targets!) now
   expand `{{date}}` (ISO), `{{time}}`, `{{datetime}}`, `{{clipboard}}` (current
   clipboard text), and any `{{key:default}}` pair — `Dear {{name:Jane}}` —
   plus a `{{cursor}}` marker for future caret-positioning. Unknown tokens stay
   **verbatim** (a visible typo beats silent loss) and expansion is never
   recursive, so a clipboard containing `{{date}}` stays literal.
4. **🧪 Harness grown to 68 tests.** The AI request/response layer, the
   bookmark parser (garbage-in → zero-out, hard 3000-entry cap) and the
   expander are pure code with 25 new tests — including an assertion that the
   API key can never appear in a body or a log line.

## 🆕 What's new in v2.2.0-alpha.2 — Quick actions & favourites rework

A quality pass over Task 2.1/2.2 after real use: the menu gains its primary
action, pinning becomes a one-click gesture, and two real bugs go away.

1. **✂️ Open leads every menu.** The quick-action menu now starts with **Open** —
   exactly what pressing Enter does (both funnel through one shared execute
   path, so they can never drift). The pin/unpin pair sits visually apart
   behind a separator, items show their keyboard gesture (`Open   Enter`), and
   copying a web row now reports **Copied URL** instead of "path".
2. **★ Pin on hover.** Every pinnable row shows a **☆ on hover** (filled accent
   **★** while pinned, always visible so it can be unpinned from any view).
   One click pins — no menu needed. Pin state is stamped on every row by the
   search pipeline itself, so star and menu can never disagree.
3. **🛠 Tools and files join the fun.** Utility rows (`cmd:mute`, night light,
   Settings …) are now **openable and pinnable** — a utility you reach for
   constantly is the best possible favourite (transient record/shortcut-editor
   controls are excluded). Elevation works for **.bat/.cmd/.exe/.msc files**
   found by the file index, not just Start-Menu apps.
4. **🔍 Favourites, refined.** The section now shows **up to 12** pinned rows
   (a silent cap of 6 used to hide the rest) ordered **newest pin first** — the
   thing you just pinned lands at the top, where you just looked. Storage order
   on disk is unchanged, so upgrading is lossless.
5. **🐛 Fixes.** Right-clicking a header/hint row no longer flashes an empty
   menu shell (WPF opens the menu captured at event-raise; the handler now
   suppresses it with `e.Handled`). `Ctrl+→` **toggles** the menu closed
   instead of piling opens. The row model (`ResultItem`) moved to a WPF-free
   file so the whole pin/menu policy is unit-testable on any OS — **43 tests**
   now cover scoring, stores, settings and the action policy.

## 🆕 What's new in v2.2.0-alpha.1 — Actions (DEV_PLAN Phase 2)

The second slice of the [development plan](DEV_PLAN.md): the launcher stops being
just a list of rows and starts acting on them.

1. **✂️ Row quick actions (Task 2.1).** Right-click any result — or press
   `Ctrl+→` — for a native Win11-styled context menu: **open the containing
   folder** (`explorer /select` pre-selects the file), **copy path**, **copy
   name**, **open in terminal** (Windows Terminal, cmd fallback, starting in the
   right folder), **run as administrator** (UAC, with a graceful "elevation
   cancelled" status), and **pin / unpin favourites**. Each row type gets only
   the actions that make sense for it; the menu is rebuilt on every open.
2. **★ Pinned favourites (Task 2.2).** Pin anything — an app, a file, a URL, a
   shortcut — and it appears under a FAVOURITES header at the top of the empty
   view, Raycast-style. Keys are the result's `RunArgument` (OrdinalIgnoreCase);
   display data is persisted in `%APPDATA%\Lumo\favourites.json` (tolerant JSON,
   single-flight background save, temp-file swap — the same contract as
   `usage.json`), and shell icons are refreshed live at render time.
3. **👁️ Preview pane (Task 2.3).** Press `Tab` to flip open a preview of the
   selected row: the head of text files (first 200 lines / 512 KB, binary-safe
   with a `FileShare.ReadWrite` open so locked files still preview), image
   thumbnails (decoded at 240 px), clipboard entries, snippet bodies, and web
   URLs. Selection changes are debounced 120 ms, file reads run on a worker
   thread, and a generation counter drops anything stale — arrow-key walks never
   block or mis-render. `Esc` closes the preview first, then the window.
4. **🛠️ More system utilities (Task 2.4).** `U/` grows: **hibernate**
   (`SetSuspendState`), **mute / unmute volume** (`VK_VOLUME_MUTE` media key),
   **night light** and **battery** settings pages (`ms-settings:` URIs), and
   **restart in 10 seconds** — which arms a cancellable countdown and floats a
   "✕ Cancel pending restart" row (`shutdown /a`) to the top of `U/` until the
   timer fires or you abort.

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

1. Grab the latest zip from **[Releases](https://github.com/Anik1377/Lumo-Launcher/releases)**
   (every release is a prerelease — pick the newest `v*` at the top).
2. Extract anywhere (e.g. `D:\Tools\Lumo`).
3. Run `Lumo.exe`.
4. Requires the [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0/runtime).
5. Optional: make an empty `data` folder next to `Lumo.exe` for [portable mode](#platform).

## ⚙️ Settings — `%APPDATA%\Lumo\settings.json`

The Settings window writes this file for you; every key can still be edited by hand
(a selection — the full set is written on save and read tolerantly):

```json
{
  "Hotkey": "Alt+Space",
  "Theme": "dark",
  "WebEngine": "google",
  "HideOnFocusLoss": false,
  "AccentColor": "#FF6363",
  "BorderStyle": "Aurora",
  "BorderSpeedSec": 9.0,
  "AnimationsEnabled": true,
  "Acrylic": true,
  "StartWithWindows": false,
  "MaxIndexedFiles": 150000,
  "DisabledPlugins": [],
  "CustomWebProviders": { "lumo": "https://github.com/Anik1377/Lumo-Launcher?q={0}" },
  "AiEnabled": false,
  "AiStyle": "ollama",
  "AiEndpoint": "http://localhost:11434",
  "AiModel": "llama3.2",
  "VoiceEnabled": true,
  "VoiceLanguage": "",
  "VoiceEngine": "whisper",
  "VoiceModel": "base.en",
  "UpdatesEnabled": true,
  "FirstRunDone": true
}
```

`Theme` accepts `dark`, `light` or `auto` (follows the Windows colour mode).
`Hotkey` accepts combos of `Ctrl` `Alt` `Shift` `Win` + a letter, digit, `F1`–`F24`, `Space` or `` ` ``.
`WebEngine` accepts `google`, `bing` or `duckduckgo`.
`VoiceLanguage` (AI chat voice typing) is empty by default — it follows the Windows
display language; set a culture like `"en-GB"` to pin one.
`VoiceEngine` picks the transcription brain: `"whisper"` (default — whisper.cpp,
offline, model downloaded once on demand from the chat's mic) or `"windows"` (the
built-in SAPI recognizer). `VoiceModel` is one of `tiny.en`, `base.en` (default),
`base` (multilingual) or `small` — bigger models are more accurate and slower.

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
  "Keywords": "gmail work",
  "Hotkey": "Ctrl+Alt+M"
}
```

`Type` is `url`, `file`, `folder` or `macro` — for a macro, put one target per line in
`Steps` (URLs and paths, up to 12); they all open when the shortcut runs.
`Keywords` are optional extra terms that help `/sc` find it.
`Hotkey` (optional) registers a **global combo for this one shortcut** — it runs
from anywhere, even with Lumo hidden (up to 16, bare/Shift-only combos refused;
captured with the same recorder in the shortcut editor).

## 🧩 Plugins — `%APPDATA%\Lumo\plugins\`

One folder per plugin, each holding a declarative `plugin.json` — see the
**[plugin development guide](docs/PLUGIN_DEVELOPMENT.md)** for the complete
schema, routing rules, limits, the official first-party catalog and the
copyable AI authoring prompt. Manage everything from **Settings → Plugins**
or `P/` in the launcher.

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
- [x] Pin results / usage-frequency ranking (v2.1/v2.2 — MRU + favourites)
- [x] Plugin system for custom commands (v2.5 — declarative JSON, no code)
- [x] First-party plugin catalog + in-app install (v2.6.0-alpha.2)
- [ ] Everything SDK backend for instant full-disk search
- [ ] Result icons extracted from real shortcuts

## 📄 License

[MIT](LICENSE) © Najmul Islam Anik
