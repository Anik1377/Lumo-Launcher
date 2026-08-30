======================================================
  LUMO v2.0.0-alpha.2  —  universal launcher for Windows
  (ALPHA BUILD — UNSTABLE. Expect bugs; report them!)
======================================================

WHAT'S NEW IN v2.0.0-alpha.2 (the rim comet, rebuilt)
-----------------------------------------------------
1. TRUE PERIMETER COMET - the glow is now a bright head
   plus a soft tail that travel the REAL window outline
   (rounded-rect path animation) at constant speed,
   rounding every corner - no more diagonal sweeping.
2. INSIDE ONLY - the orbit layer is clipped to the
   window and an opaque patch covers everything but the
   outer 3 px band, so the light can never bleed outside
   or wash over content (z.ai chat-box style).
3. CALMER - 9 s per lap by default (Fast 6 / Normal 9 /
   Slow 14). The old static top wash is gone; the comet
   is the only glow.

WHAT'S NEW IN v2.0.0-alpha.1 (the Windows 11 overhaul)
------------------------------------------------------
1. GLASS IS GONE - Windows 11 DESIGN IN. Lumo now follows
   the Microsoft Fluent design language: solid #202020
   (dark) / #F3F3F3 (light) surfaces, hairline strokes,
   4 px control geometry, native DWM rounded corners and
   drop shadows, Segoe UI. No acrylic, no fallbacks -
   the same crisp native look everywhere.
2. NEW GLOW: THE RIM COMET - a single minimal accent
   light orbits INSIDE the window border (bright head +
   soft tail chasing the rim, like modern AI chat boxes).
   Nothing bleeds outside the window any more.
3. WIN11 SEARCH FIELD - rounded 4 px Fluent text box with
   the 2 px accent focus bar along the bottom while typing.
4. FULL-WINDOW SETTINGS APP - Settings now opens filling
   the whole work area like a real Windows 11 system app:
   nav sidebar with monochrome glyphs + Win11 selection
   pill, large page titles, cards with hairline borders,
   40x20 Win11 toggles, minimize button, and an
   "ALPHA - UNSTABLE" badge in the title bar.
5. ALL RELEASES MARKED ALPHA - past and future GitHub
   releases are labelled [ALPHA - unstable].

WHAT'S NEW IN v1.8.0 (the Fully Glass update)
---------------------------------------------
1. MUCH DEEPER GLASS - panel opacity dropped so the acrylic
   blur really showed your desktop through.
2. FROST SHEEN + REFRACTION EDGE - diagonal light band and
   a bright top hairline.
3. GLASS SETTINGS WINDOW - same acrylic backdrop with
   translucent sidebar, cards and fields.
   (All replaced by the v2.0 solid Windows 11 look.)

WHAT'S NEW IN v1.7.2 (critical launch fix)
------------------------------------------
1. FIXED - Lumo crashed on launch in v1.7.0/v1.7.1
   with "Unable to cast String to Geometry" (the
   settings gear icon was fed to WPF as text).
   Lumo now starts cleanly. Sorry about that!

WHAT'S NEW IN v1.7.1 (Enter-key fixes)
--------------------------------------
1. FIXED - pressing Enter on a clipboard-history
   entry (H/) did nothing. It now copies the entry
   and hides the launcher, ready to paste.
2. FIXED - after typing, the launcher pre-selected
   the section header row ("APPS"), so Enter did
   nothing until you pressed Down once. The first
   actionable result is selected automatically now.
3. FIXED - Up/Down arrows skip the section header
   rows instead of highlighting them.
4. FIXED - "Clear clipboard history" and other "x"
   rows showed an empty icon tile (missing vector
   icon mapping).

WHAT'S NEW IN v1.7 (the Glass update)
-------------------------------------
1. GLASS BACKDROP - the launcher sits on a live
   acrylic blur (real system acrylic on Win11 22H2+,
   composition acrylic on Win10). Falls back to the
   solid panel automatically on unsupported systems.
   Toggle: Settings -> Appearance -> Glass backdrop.
2. MODERN VECTOR ICONS - every row, hint and button
   uses a coherent Fluent-style outline icon set,
   razor sharp at any DPI and tinted by your accent.
3. NATIVE ROUNDED CORNERS + real drop shadow on Win11.
4. AMBIENT ACCENT WASH inside the top of the card
   (the glow halo, reimagined for glass).
5. FIXED - the search clear button now appears when
   you type (it was hidden by a style bug since v1.3).

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
