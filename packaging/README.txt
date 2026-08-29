======================================================
  LUMO v1.3.1  —  universal launcher for Windows
======================================================

WHAT'S NEW IN v1.3 (Apple-clean UI + motion)
--------------------------------------------
1. REDESIGNED LAUNCHER
   * iOS-style dark/light palettes (system greys, softer
     separators, Apple text colours)
   * Accent-tinted hover & selection highlights - your
     accent colour now drives the whole highlight system
   * Larger result rows with kind chips (App / File /
     Web / Tool / =) on the right
   * Search-row magnifier icon + placeholder text +
     clear button (Ctrl+Backspace also clears)
   * Keyboard-style hint chips in the status bar

2. MOTION, EVERYWHERE
   * Spring-in: the window scales + fades + slides open
   * Cascading results: rows fade up one by one
   * Smooth hover/selection colour transitions
   * Quick fade-out when hiding
   * The glow border now PAUSES when the window is
     hidden or inactive - zero idle CPU
   * "UI animations" master switch in Settings ->
     Appearance turns ALL motion off (reduced motion)

3. REDESIGNED SETTINGS (macOS System Settings style)
   * Sidebar with coloured icon tiles
   * Segmented controls, iOS-style animated switches
   * Theme: Light / Dark / AUTO (follows Windows)
   * Animated page transitions between sections

4. NEW CUSTOMIZATION
   * "UI animations" on/off
   * Theme "Auto" mode
   * 9 accent presets now including iOS blue, green,
     orange and purple

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
