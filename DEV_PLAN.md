# Lumo — AI-Agent Development Plan

Purpose: Turn the 15 feature suggestions in feature-suggestions.md into an ordered, dependency-aware build plan that an AI coding agent (or a human dev) can execute task-by-task. Every task is written as a self-contained, verifiable unit with exact file paths, step-by-step steps, code sketches, and a "Definition of Done" checklist.

Repo: src/Lumo · Stack: C# / WPF / .NET 8 · Branch target: main

> **Version mapping (v2.5 note):** this plan was drafted against the v1.x numbering.
> On the current v2 baseline the phases ship as: Phase 0+1 → **v2.1.0-alpha.1**,
> Phase 2 → v2.2.0-alpha.x, Phase 3 → v2.3.0-alpha.x.
> The v2.4.0-alpha.x line was consumed by the Raycast UI overhaul + AI chat window
> (user-driven, outside this plan), so the remaining phases shifted up one minor:
> Phase 4 → **v2.5.0-alpha.x**, Phase 5 → v2.6.0-alpha.x.

## 0. Agent operating rules (read first)

These are the project's hard-won conventions. Violating them will cause regressions or crashes the codebase was explicitly built to avoid.

1. **Never touch the UI thread with blocking work.** Search, indexing, and macro execution must stay off the UI thread. Use the existing pattern: `Task.Run(...)` for background work, and marshal back to the UI thread only via the dispatcher. Never do Directory scans or File.ReadAllText synchronously inside a Search call that runs on keystroke.
2. **Every handler is exception-guarded.** Follow the SearchEngine/FileIndex/AppIndex pattern everywhere — a thrown exception from a background crawler must be caught + logged via `DiagnosticLogger.LogException(...)`, never propagated.
3. **The search pipeline is bounded and debounced.** SearchEngine.MaxResults = 24, file index cap is 150k default, AppIndex.MaxEntries = 2000. Keep result counts bounded. Do not turn synchronous, in-memory searches into per-keystroke disk/network calls.
4. **Log, don't crash.** Use `Lumo.Services.DiagnosticLogger.Log(category, message)` and `LogException(category, ex)`. Every new feature should log its start/errors.
5. **Match the existing style.** File-scoped namespaces, `new()` target-typed expressions, records for data rows, sealed classes, init-only properties, Services./Core. namespace prefixes, internal visibility for helpers used across the app.
6. **Settings are JSON-tolerated.** Settings.Load() is defensive about bad JSON (see GetStr/GetBool/GetNum). Any new persisted settings must follow the same tolerant-read pattern and be added to RestoreFrom() (and Clone()).
7. **Keep the single portable exe promise.** No NuGet packages that add heavy native deps. Prefer System.Net.Http, P/Invoke, and shell URIs.
8. **Verify the build** with `dotnet build src/Lumo/Lumo.csproj` and `dotnet test src/Lumo.Tests/Lumo.Tests.csproj` after each task. Keep GitHub Actions green.

Key file map:

| Concern | File |
|---|---|
| Search pipeline / prefix routing | Core/SearchEngine.cs |
| App index (Start Menu + Desktop .lnk/.url) | Core/AppIndex.cs |
| File index (background crawl + quick scan) | Core/FileIndex.cs |
| Fuzzy scoring | Core/Fuzzy.cs |
| Safe calculator | Core/Calculator.cs |
| Window snap manager | Core/WindowManager.cs |
| Settings (settings.json) | Services/Settings.cs, Core/AppPaths.cs |
| Shortcuts & macros (shortcuts.json) | Services/Shortcuts.cs |
| Clipboard history | Services/ClipboardHistory.cs |
| App/file icon resolution | Services/AppIcons.cs |
| Hotkey registration | Services/HotkeyService.cs |
| Single-instance named pipe | Services/SingleInstanceService.cs |
| Launcher window (command dispatch) | UI/LauncherWindow.xaml.cs |
| Settings window | UI/SettingsWindow.xaml.cs |

## Phase 0 — Foundation: test harness ✅ (shipped in v2.1.0-alpha.1)

`src/Lumo.Tests` (xUnit, multi-target net8.0 for any-OS runs + net8.0-windows on CI)
covering Fuzzy, Calculator, Settings tolerant reads/round-trips, and the UsageStore.
CI runs tests before publish.

## Phase 1 — v2.1 "Smarter" ✅ (shipped in v2.1.0-alpha.1)

- **Task 1.1 — MRU / frequency-based ranking.** Services/UsageStore.cs +
  Core/AppPaths.UsageFile; ScoreWithUsage blend (freq ×1–2, recency +0.25 ≤7d);
  recorded centrally in SearchEngine.Execute; blended in AppIndex.Query /
  FileIndex.Query; single-flight background save.
- **Task 1.2 — Inline unit + currency conversion.** Core/UnitConverter.cs
  (length/mass/volume/data/temperature) + Services/ExchangeRateService.cs
  (static fallback table + 12 h background refresh from open.er-api.com);
  hooked before Calculator.TryEvaluate in CalcRows.
- **Task 1.3 — Web-engine quick-switch.** Core/WebProviders.cs provider map
  (github/youtube/ddg/maps/wiki/images/so/npm/nuget/news/amazon/scholar/…);
  Settings.CustomWebProviders (tolerant read + RestoreFrom) override built-ins.

## Phase 2 — v2.2 "Actions" ✅ (shipped in v2.2.0-alpha.1; 2.1/2.2 reworked in v2.2.0-alpha.2)

- **Task 2.1 — Quick actions on result rows:** right-click / Ctrl+→ context menu
  (OpenContainingFolder, CopyPath, CopyName, OpenTerminal, RunAsAdmin, Pin).
  *Rework (alpha.2):* menu leads with **Open** (shared execute path with Enter),
  separator + gesture hints, **Tool rows openable & pinnable** (record/shortcut-editor
  commands excluded), elevation for elevatable **File** rows, `e.Handled` fix for the
  empty-menu flash on non-actionable rows, Ctrl+→ toggles the menu.
- **Task 2.2 — Pinned favourites + FAVOURITES header** on the empty view
  (Services/Favourites.cs, key = RunArgument).
  *Rework (alpha.2):* newest-pin-first display (storage order unchanged), cap raised
  6 → 12, hover **★** pin affordance stamped via `SearchEngine.Annotate`
  (`ResultItem.CanPin/Pinned`), `Favourites.Toggle()` for both UI entry points.
- **Task 2.3 — Tab-key preview pane** (text head/image thumbnail/clipboard text,
  async read with generation counter).
- **Task 2.4 — More system utilities** (hibernate, night light, battery, mute,
  countdown-restart with cancel).

## Phase 3 — v2.3 "Connected" ✅ (shipped in v2.3.0-alpha.1)

- **Task 3.1 — AI / natural-language command (? prefix, flagship).**
  Core/AiProviders.cs (pure request/response layer, Ollama chat + Anthropic
  Messages shapes, mandatory key redaction) + Services/AiService.cs (45 s
  timeout, 8-entry answer cache, in-flight dedupe). The window fires ONE
  off-thread request per settled prompt (generation counter kills stale
  replies) and re-renders when it lands — the synchronous pipeline only ever
  reads the cache. Settings → AI page: enable, provider, endpoint, model,
  key (settings.json only, never logged).
- **Task 3.2 — Bookmarks & browser history B/.** Core/Bookmarks.cs (pure
  tolerant parser, 3000-entry cap) + Services/BookmarkIndex.cs (Chrome/Edge
  profile discovery ≤ 8 files, background load, mtime re-probe, fuzzy over
  name/folder/URL, newest-first on the empty query). Rows are ordinary Web
  rows: open / copy / pin all work through the existing paths.
- **Task 3.3 — Snippet variable expansion.** Core/SnippetExpander.cs
  ({{date}}/{{time}}/{{datetime}}/{{clipboard}}/{{key:default}}/{{cursor}};
  unknown tokens stay verbatim, no recursion) applied to snippet copies and
  macro step targets.

## Phase 4 — v2.5 "Platform" (#11, #12) ✅ (shipped in v2.5.0-alpha.1)

- **Task 4.1 — PrefixRouter refactor ✅** Core/PrefixRouter.cs: `IPrefixHandler`
  (Prefix + ExactAliases + Handle) and a longest-prefix-first, case-insensitive
  `PrefixRouter`. SearchEngine's 14-branch if-chain became a declarative route
  table registered in the ctor; every route is exception-guarded per handler
  (§0 rule 2). Behavior-preserving — the row builders are untouched.
- **Task 4.2 — Plugin / command system ✅** (shipped as declarative JSON, not
  executable code — keeps §0 rule 7's single-portable-exe + no-untrusted-code
  promise). Core/Plugins.cs (pure parser/validator/expander) +
  Services/PluginStore.cs (%APPDATA%\Lumo\plugins\<id>\plugin.json; 64 plugins,
  24 commands each, first plugin owns a keyword, tolerant corrupt-file skip,
  mtime-probed EnsureFresh). Commands: `web` / `open` / `copy` with a {query}
  placeholder (URL-escaped for web). Routing: `P/` browser, token-exact keyword
  ("kw …" / bare "kw"), quick-hits on the default view; static routes always
  win over plugin keywords. Settings → Plugins page: enable/disable per plugin,
  open folder, rescan, copy-a-starter.
- **Task 4.3 — Custom per-shortcut / per-command hotkeys ✅** ShortcutDef.Hotkey
  (persisted in shortcuts.json) + HotkeyService multi-registration
  (TryRegisterId under ShortcutHotkeyBase + n, ≤16, registration refuses bare /
  Shift-only combos). The shortcut editor gained a hotkey capture box (same key
  set as the main hotkey); the launcher re-registers all of them on save and
  runs the shortcut from anywhere — launcher hidden included.

## Phase 5 — v2.6 "Product" (#13, #14, #15) ✅ (shipped in v2.6.0-alpha.1) — ALL PHASES COMPLETE

- **Task 5.1 — Auto-update service ✅** GitHub Releases check + staged download.
  Core/UpdateCheck.cs (pure: ReleaseVersion — SemVer-ish parse where the final
  release outranks any prerelease and alpha.9 < alpha.10 numerically — plus the
  tolerant /releases payload picker: drafts skipped, no-zip releases skipped,
  the "Lumo…zip" CI asset found among alien assets). Services/UpdateService.cs:
  /releases list (never /latest — every Lumo release is a prerelease), once per
  24 h automatic check 15 s after startup, manual "Check now", staged download
  to DataDir\updates via a Guid temp file (80 MB sanity cap) — staged on purpose:
  the portable exe is never self-replaced while running; the user extracts the
  zip over Lumo.exe. Settings: UpdatesEnabled + LastUpdateCheckUtc. UI: tray
  balloon (click → Settings → About) + the Settings → About updates card
  (toggle, check now, download progress, open staged zip).
- **Task 5.2 — Portable data mode ✅** AppPaths.ResolveRoots: a "data" folder
  next to Lumo.exe redirects every store (settings, shortcuts, chats, personas,
  plugins, usage, favourites, log, staged updates) into it — Lumo.exe + data/
  travels together. Without the folder the classic %LOCALAPPDATA%/%APPDATA%
  roots are used, byte-for-byte unchanged. Static-init order note: the roots
  resolve as the FIRST field initializer, so LogFile/SettingsFile see real
  values. Settings → About shows the live location + portable badge.
- **Task 5.3 — Onboarding / first-run tour ✅** UI/OnboardingWindow — three
  quiet steps (hotkey → prefixes → data & updates), launcher-card styling,
  Esc/Skip/✕ all count as seen (never traps the user). Shown once via
  Settings.FirstRunDone on the first launch, replayable from Settings → About.

## Cross-cutting: settings-window checklist

Any task that adds a persisted setting MUST also: add the property to
Services/Settings.cs with a safe default · read it tolerantly in Load() ·
copy it in RestoreFrom(o) · if surfaced in the UI, bind to the live Settings
instance.
