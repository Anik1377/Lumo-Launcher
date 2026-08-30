# Lumo — AI-Agent Development Plan

Purpose: Turn the 15 feature suggestions in feature-suggestions.md into an ordered, dependency-aware build plan that an AI coding agent (or a human dev) can execute task-by-task. Every task is written as a self-contained, verifiable unit with exact file paths, step-by-step steps, code sketches, and a "Definition of Done" checklist.

Repo: src/Lumo · Stack: C# / WPF / .NET 8 · Branch target: main

> **Version mapping (v2.1 note):** this plan was drafted against the v1.x numbering.
> On the current v2 baseline the phases ship as: Phase 0+1 → **v2.1.0-alpha.1**,
> Phase 2 → v2.2.0-alpha.x, Phase 3 → v2.3.0-alpha.x, Phase 4 → v2.4.0-alpha.x,
> Phase 5 → v2.5.0-alpha.x.

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

## Phase 3 — v2.3 "Connected" (#6, #7, #9)

- **Task 3.1 — AI / natural-language command (? prefix, flagship).**
- **Task 3.2 — Bookmarks & browser history B/.**
- **Task 3.3 — Snippet variable expansion ({{date}}, {{name:…}}, {{cursor}}).**

## Phase 4 — v2.4 "Platform" (#11, #12)

- **Task 4.1 — PrefixRouter refactor** (IPrefixHandler + declarative routes).
- **Task 4.2 — Plugin / command system** (%APPDATA%\Lumo\plugins\<id>\plugin.json).
- **Task 4.3 — Custom per-shortcut / per-command hotkeys.**

## Phase 5 — v2.5 "Product" (#13, #14, #15)

- **Task 5.1 — Auto-update service** (GitHub Releases check + staged download).
- **Task 5.2 — Portable data mode** (data/ folder next to the exe).
- **Task 5.3 — Onboarding / first-run tour.**

## Cross-cutting: settings-window checklist

Any task that adds a persisted setting MUST also: add the property to
Services/Settings.cs with a safe default · read it tolerantly in Load() ·
copy it in RestoreFrom(o) · if surfaced in the UI, bind to the live Settings
instance.
