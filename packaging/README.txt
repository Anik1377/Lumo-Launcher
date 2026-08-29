======================================================
  LUMO v1.1  —  universal launcher for Windows
======================================================

WHAT'S NEW IN v1.1 (bug fixes)
------------------------------
1. HOTKEY FIXED
   The old default Win+Space is reserved by Windows (input language switch),
   which is why the hotkey never fired. v1.1 defaults to:
                     Alt + Space
   If Alt+Space is taken on your machine, Lumo automatically tries:
       Ctrl+Alt+Space -> Ctrl+Shift+Space -> Ctrl+Alt+M -> Win+Q
   The status bar at the bottom of the window always shows which combo
   is actually active.

2. SHORTCUT LAUNCH FIXED
   Double-clicking the Lumo shortcut now ALWAYS opens the search window:
   - first launch: window appears immediately
   - while already running: the second launch signals the running
     instance through a named pipe and its window pops up.

3. TRAY SINGLE-CLICK
   A single left-click on the tray icon now opens the window
   (double-click also works). Right-click gives you:
   Open Lumo / Toggle theme / Open settings folder / Exit.

4. TYPING NO LONGER FREEZES OR CRASHES
   The search pipeline was rebuilt: bounded in-memory search with an
   80 ms debounce, no blocking calls on the UI thread, and every
   handler is exception-guarded. If anything unexpected ever happens
   it is written to the log instead of crashing the app.

USAGE
-----
   Alt+Space           open / hide Lumo          (or click the tray icon)
   type anything       search installed apps + files + web
   A/<name>            applications only         e.g.  A/chrome
   F/<name>            file search               e.g.  F/report
   C/<expr>            calculator                e.g.  C/(1920*1080)/3
   W/<text or url>     web search / open URL     e.g.  W/weather
   I/<text>            image search              e.g.  I/aurora
   U/<tool>            utilities: lock, sleep, empty bin, restart,
                       shutdown, open settings, open log
   Up/Down             select result
   Enter               run selected result (calculator copies to clipboard)
   Esc                 hide window

SETTINGS (%APPDATA%\Lumo\settings.json)
---------------------------------------
   "Hotkey":  "Alt+Space"     (also accepts Ctrl+Alt+P style combos:
                               Ctrl / Alt / Shift / Win + letter, digit,
                               F1-F24, Space)
   "Theme":   "dark" or "light"
   "WebEngine": "google" | "bing" | "duckduckgo"
   "HideOnFocusLoss": false   (true = auto-hide when clicking elsewhere)

TROUBLESHOOTING
---------------
   * Diagnostics log (every startup, hotkey attempt and error is recorded):
       %LOCALAPPDATA%\Lumo\log.txt
   * If the hotkey doesn't respond: check the status bar text in the
     launcher window — it names the combo that actually registered.
     Change "Hotkey" in settings.json to any free combo and restart Lumo.
   * If results feel incomplete right after start, wait a few seconds:
     the background file index builds once (up to 150,000 files) and the
     status bar shows live progress.

REQUIREMENTS
------------
   Windows 10/11 x64 with the .NET 8 Desktop Runtime:
   https://dotnet.microsoft.com/download/dotnet/8.0/runtime
   (Choose ".NET Desktop Runtime 8.0.x  Windows x64")

Project page: https://github.com/Anik1377/Lumo-Launcher
