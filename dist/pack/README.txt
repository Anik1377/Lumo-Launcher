======================================================
  LUMO v1.2  —  universal launcher for Windows
======================================================

WHAT'S NEW IN v1.2 (advanced settings + glow border)
----------------------------------------------------
1. FULL SETTINGS WINDOW
   Open it from any of these places:
     * the "Settings" row in the launcher's default view
     * the small "⚙ settings" text in the launcher status bar
     * the tray icon right-click menu -> "Settings…"
     * type  U/settings  and press Enter
   Sections:
     General    start with Windows, hide-on-focus-loss, web engine
     Appearance theme, accent colour (8 presets + custom hex),
                glow border on/off, 6 border styles, 3 speeds,
                with a live preview strip
     Hotkey     click-and-press hotkey recorder (no more manual
                JSON editing), Apply button tests it immediately
     Search     max indexed files slider (10k - 300k),
                rebuild index now, open log / settings folders
     About      version info + project links

2. GLOW BORDER EFFECT (the chat-bubble look)
   The launcher window now has an animated multi-colour gradient
   border that slowly rotates, plus a soft glow halo bleeding out
   behind the window. Styles: Aurora, Sunset, Ocean, Ember, Mint,
   Solid accent. Speed: Fast / Normal / Slow. Turn it off any time
   in Settings -> Appearance.

3. NEW IN THIS BUILD
   * Start Lumo when Windows starts (per-user Run key, no admin)
   * Accent colour theming across the whole UI
   * Tunable file-index cap + "Rebuild index now"
   * Small fade/slide-in animation when the launcher appears

HOTKEY NOTE (please read)
-------------------------
   Win + Space is RESERVED BY WINDOWS for switching keyboard
   layouts - no application can register it. That was the cause
   of the v1.0 "hotkey never works" report. The default is
   Alt+Space and it works everywhere. Change it any time in
   Settings -> Hotkey (click the box, press the combo, Apply).

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
   Up/Down             select result
   Enter               run selected result (calculator copies to clipboard)
   Esc                 hide window

SETTINGS (%APPDATA%\Lumo\settings.json)
---------------------------------------
   The Settings window writes this file for you, but every key
   can also be edited by hand:
   "Hotkey":            "Alt+Space"   (Ctrl/Alt/Shift/Win + letter,
                                      digit, F1-F24, Space, `)
   "Theme":             "dark" | "light"
   "WebEngine":         "google" | "bing" | "duckduckgo"
   "HideOnFocusLoss":   false
   "AccentColor":       "#7C6CFF"     (any #RRGGBB)
   "BorderEffect":      true          (animated glow border)
   "BorderStyle":       "Aurora"      (Aurora|Sunset|Ocean|Ember|Mint|Solid)
   "BorderSpeedSec":    3.5           (seconds per rotation; lower = faster)
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
