======================================================
  LUMO v2.2.0-alpha.1  —  universal launcher for Windows
  (ALPHA BUILD — UNSTABLE. Expect bugs; report them!)
======================================================

WHAT'S NEW IN v2.2.0-alpha.1 (DEV_PLAN Phase 2 — "Actions")
-----------------------------------------------------------
1. ROW QUICK ACTIONS - right-click (or press Ctrl+Right)
   on any result for the new context menu: open the
   containing folder (Explorer pre-selects the file),
   copy path, copy name, open a terminal in that folder
   (Windows Terminal, cmd fallback), run as administrator
   (UAC prompt), and pin / unpin favourites.
2. PINNED FAVOURITES - pin anything and it appears in a
   FAVOURITES section at the top of the empty view
   (Raycast style). Stored in %APPDATA%\Lumo\favourites.json.
3. PREVIEW PANE - press Tab to toggle a preview of the
   selected row: the head of text files (first 200 lines
   / 512 KB, binary-safe), image thumbnails, clipboard
   entries, snippet text, URLs. Esc closes the preview
   first, then the window. Reads happen on a background
   thread - the UI never blocks, and stale reads are
   dropped by a generation counter.
4. MORE SYSTEM UTILITIES (U/) - hibernate, mute/unmute
   volume, night light settings, battery settings, and
   "Restart in 10 seconds" with a cancellable countdown
   (search "cancel" in U/ to abort via shutdown /a).

WHAT'S NEW IN v2.1.0-alpha.1 (DEV_PLAN Phase 0+1 — "Smarter")
-------------------------------------------------------------
1. MRU RANKING - launches are counted (usage.json) and
   blended into the fuzzy score: things you actually use
   float to the top of equal-match lists (frequency x2
   max, recency nudge within 7 days).
2. INLINE UNITS + CURRENCY - "10 ft in cm", "5 kg in lbs",
   "50 usd to eur" evaluate right in the launcher; rates
   refresh from open.er-api.com in the background with a
   built-in offline fallback table.
3. PER-QUERY WEB SWITCH - "W/github lumo" searches GitHub,
   "W/youtube cats" searches YouTube, "W/wiki x" Wikipedia,
   and 13+ more built-in providers; add your own via
   "CustomWebProviders" in settings.json.
4. TEST HARNESS - 33 xUnit tests now gate every release
   in CI before the publish step.

WHAT'S NEW IN v2.0.0-alpha.5 (Apple-craft UI pass)
--------------------------------------------------
1. PRESS-DOWN FEEDBACK - rows and buttons dip to 98.5%
   the instant you press (60 ms ease-out), Spotlight-style.
2. MIRRORED EXIT - the window settles back down 10 px and
   fades on the same path it entered from.
3. SLIM OVERLAY SCROLLBAR, edge-fade hairlines, a 1 px
   top light-catch in dark mode, and full respect for the
   OS "show animations" setting.

HOTKEY NOTE (please read)
-------------------------
   Win + Space is RESERVED BY WINDOWS for switching
   keyboard layouts - no application can register it.
   That was the cause of the v1.0 "hotkey never works"
   report. The default is Alt+Space and it works
   everywhere. Change it any time in Settings -> Hotkey
   (click the box, press the combo, Apply).

USAGE
-----
   Alt+Space           open / hide Lumo          (or click the tray icon)
   type anything       search installed apps + files + web
   A/<name>            applications only         e.g.  A/chrome
   F/<name>            file search               e.g.  F/report
   C/<expr>            calculator + units        e.g.  C/(1920*1080)/3
                                                 10 ft in cm
                                                 50 usd to eur
   W/<text or url>     web search / open URL     e.g.  W/weather
                       per-query engine switch:  W/github · W/youtube
                                                 W/ddg · W/wiki · W/maps
   I/<text>            image search              e.g.  I/aurora
   U/<tool>            utilities: lock, sleep, hibernate, mute,
                       night light, battery, empty bin, restart,
                       restart in 10 s (cancellable), shutdown,
                       settings window, settings file, log
   H/                  clipboard history         Enter copies again
   S/                  snap the last window      left · right · max
   !<name>             snippets                  Enter copies text
   /sc [name]          shortcuts & macros        e.g.  /sc mail
   Up/Down             select result
   Enter               run selected result (calculator copies to clipboard)
   Tab                 toggle the preview pane for the selected row
   right-click         quick actions on a row (folder, copy path,
                       terminal, run as admin, pin to favourites)
   Ctrl+Right          open the same quick-action menu by keyboard
   Esc                 close preview first, then hide window
   Ctrl+Backspace      clear the search box

SETTINGS (%APPDATA%\Lumo\settings.json)
---------------------------------------
   The Settings window writes this file for you, but every key
   can also be edited by hand:
   "Hotkey":            "Alt+Space"   (Ctrl/Alt/Shift/Win + letter,
                                      digit, F1-F24, Space, `)
   "Theme":             "dark" | "light" | "auto"
   "WebEngine":         "google" | "bing" | "duckduckgo"
   "CustomWebProviders": { "gh": "https://github.com/search?q={0}" }
   "HideOnFocusLoss":   false
   "AccentColor":       "#0078D4"     (any #RRGGBB; Win11 blue default)
   "BorderEffect":      true          (rim glow - orbits inside the border)
   "BorderStyle":       "Aurora"      (Aurora|Sunset|Ocean|Ember|Mint|Solid)
   "BorderSpeedSec":    3.5           (seconds per rotation; lower = faster)
   "AnimationsEnabled": true          (master motion switch, v1.3)
   "StartWithWindows":  false         (applied on Save)
   "MaxIndexedFiles":   150000        (applied on next index rebuild)

TROUBLESHOOTING
---------------
   * Diagnostics log (every startup, hotkey attempt and error):
       %LOCALAPPDATA%\Lumo\log.txt
   * If the hotkey doesn't respond: the launcher status bar names
     the combo that actually registered. Use Settings -> Hotkey
     to pick another one - Lumo auto-falls back and tells you.
   * If results feel incomplete right after start, wait a few
     seconds: the background file index builds once (cap set in
     Settings -> Search) and the status bar shows live progress.

REQUIREMENTS
------------
   Windows 10/11 x64 with the .NET 8 Desktop Runtime:
   https://dotnet.microsoft.com/download/dotnet/8.0/runtime
   (Choose ".NET Desktop Runtime 8.0.x  Windows x64")

Project page: https://github.com/Anik1377/Lumo-Launcher
