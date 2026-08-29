======================================================
  LUMO v1.5.0  —  universal launcher for Windows
======================================================

WHAT'S NEW IN v1.4 (Shortcuts & macros + smoother motion)
---------------------------------------------------------
1. SHORTCUTS & MACROS - your own one-tap launches
   * Type /sc to browse your saved shortcuts
   * Run one with /sc name  (e.g.  /sc mail)
   * Four kinds: URL, file, folder, and MACRO
     (a macro opens several targets at once -
      one per line, up to 12)
   * Create: type /sc + any name, press Enter on
     "Create shortcut ..." - the editor opens with
     the name pre-filled (Browse button included,
     Ctrl+Enter saves)
   * Manage: Settings -> Shortcuts (edit / delete);
     the launcher picks up changes live
   * Stored in %APPDATA%\Lumo\shortcuts.json
   * Shortcut names also surface in the default view
     while you type, and the empty view lists a few

2. SMOOTHER MOTION
   * Result rows no longer re-animate on every
     keystroke - the cascade plays when the launcher
     opens or the view changes shape; typing updates
     instantly (feels snappier)
   * Input debounce tightened 80 ms -> 60 ms

3. FIXED IN v1.3.1 (kept)
   * Startup crash caused by the new animations in
     v1.3.0 - sorry about that!

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
   C/<expr>            calculator                e.g.  C/(1920*1080)/3
   W/<text or url>     web search / open URL     e.g.  W/weather
   I/<text>            image search              e.g.  I/aurora
   U/<tool>            utilities: lock, sleep, empty bin, restart,
                       shutdown, settings window, settings file, log
   /sc [name]          shortcuts & macros        e.g.  /sc mail
   Up/Down             select result
   Enter               run selected result (calculator copies to clipboard)
   Esc                 hide window
   Ctrl+Backspace      clear the search box

SETTINGS (%APPDATA%\Lumo\settings.json)
---------------------------------------
   The Settings window writes this file for you, but every key
   can also be edited by hand:
   "Hotkey":            "Alt+Space"   (Ctrl/Alt/Shift/Win + letter,
                                      digit, F1-F24, Space, `)
   "Theme":             "dark" | "light" | "auto"
   "WebEngine":         "google" | "bing" | "duckduckgo"
   "HideOnFocusLoss":   false
   "AccentColor":       "#7C6CFF"     (any #RRGGBB)
   "BorderEffect":      true          (animated glow border)
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
