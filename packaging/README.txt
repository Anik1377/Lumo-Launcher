======================================================
  LUMO v2.4.0-alpha.7  —  universal launcher for Windows
  (ALPHA BUILD — UNSTABLE. Expect bugs; report them!)
======================================================

WHAT'S NEW IN v2.4.0-alpha.7 (the tray icon round)
---------------------------------------------------------------
1. THE TRAY FINALLY WEARS THE NEW ICON. The magic-wand art
   shipped in alpha.6 only reached the exe/window icons -
   the notification-area icon was still drawn at runtime as
   the old purple "L" tile. It now loads the real multi-size
   app.ico from the embedded resources (crisp at 16-64 px).
2. HONEST VERSION LABELS. The tray tooltip said "Lumo v1.4",
   the hotkey tooltip "v2.1.0", Settings truncated to "v2.4".
   All three now derive from one source (the build's
   informational version), e.g. "Lumo v2.4.0-alpha.7 - press
   alt+space" and the full label on the Settings about page.

WHAT'S NEW IN v2.4.0-alpha.2 (the frosted-glass material)
---------------------------------------------------------------
1. REAL FROSTED GLASS. The launcher card is now translucent
   with a genuine Windows acrylic blur-behind - the frosted
   desktop shows through the palette exactly like Raycast.
   Falls back to the solid card automatically on older
   Windows builds, remote sessions, or if the compositor
   refuses (set "Acrylic": false in settings.json to opt out).
2. RAYCAST ROW ANATOMY. The selected row's title now takes
   the accent colour - Raycast's most recognizable tell -
   while selection stays quiet. Rows round to 8 px, icons
   sit in 28 px tiles, subtitles breathe at 12.5 px.
3. UPPERCASE SECTION HEADERS. 11 px, semibold, muted - the
   root list now groups exactly like Raycast's home screen.
4. RAYCAST KEYCAPS. Footer hint chips tighten to the DESIGN
   spec: 4 px radius, min-width 20, brighter 11 px text.
5. A 54 PX SEARCH BAND. The input-as-header gets one more
   notch of breathing room at 16 px input type.

WHAT'S NEW IN v2.4.0-alpha.1 (the Raycast-grade UI overhaul)
---------------------------------------------------------------
1. A COMPLETE VISUAL OVERHAUL, INSPIRED BY RAYCAST. The
   flat Windows 11 grays are gone. Every surface now sits
   on a near-black, faintly blue-tinted ladder (canvas
   #0E0F12, elevated fields #17181C, hairline strokes
   #26282D) - the same tiering Raycast's command palette
   uses. Light mode mirrors the structure on a #FAFAFB
   canvas with white fields.
2. INTER, EMBEDDED. Lumo now ships the Inter type family
   (Regular / Medium / SemiBold / Bold, SIL OFL licence
   included) inside the exe - the same type system Raycast
   builds on. Headlines, rows, cards and captions all
   render in Inter at every weight.
3. THE SEARCH ROW IS THE HEADER. No more boxed field: the
   input sits flush with the top of the card like
   Raycast/Spotlight, with a full-bleed hairline beneath.
   Result rows are quieter too - medium-weight titles,
   8 px-class icon tiles, and a calm neutral selection
   with the accent demoted to a 3 px pill on the edge.
4. SETTINGS AND THE AI CHAT JOIN THE SAME SYSTEM. Cards
   at 8 px with hairline strokes, a deeper sidebar tier,
   6 px controls, WinUI-style caption buttons (the close
   button glows red on hover), and a richer window shadow.
5. RAYCAST RED IS THE NEW SIGNATURE ACCENT (#FF6363,
   first in the preset list). If you only ever had the old
   default colour, it migrates silently; a colour you
   actually picked is respected. The launcher also widens
   to a 744 px Raycast proportion by default (Settings >
   Appearance can still set 560-900).
6. Same single portable exe as before - no new
   dependencies, no WebView2, now ~2 MB with Inter on
   board.

WHAT'S NEW IN v2.3.0-alpha.4 (the polish pass)
---------------------------------------------------------------
1. THE AI CHAT GOT THE PROMPT-KIT MAKEOVER - the welcome
   screen now opens on a glowing gradient logo orb that
   gently breathes, the assistant avatar is a gradient
   disc with a sparkle mark, typing dots pulse on a
   smooth wave, and every message enters with a short
   fade-and-rise. The window edge, shadow and card now
   line up perfectly (no more floating shadow), and the
   scrollbar is the slim overlay style instead of the
   chunky stock one.
2. THE PROMPT INPUT FEELS ALIVE - an accent ring lights
   up around the input while it holds focus, the send
   button is a gradient cap with a soft shadow that
   lifts on hover and dips on press, and the hint row
   uses the same key chips as the launcher.
3. CLEARER MODEL + ERROR SIGNALS - the model chip in
   the caption now carries a status dot (green for
   private local models, accent for the Anthropic API),
   errors render as a compact warning card, and the
   "AI is off" banner gained an Open AI settings link
   that jumps straight to Settings > AI.
4. THE LAUNCHER GOT A GEOMETRY PASS - softer radii on
   the search field, result rows, preview pane and the
   right-click menu; bigger icon tiles; and the kind
   badges are pill-shaped now. Everything still follows
   your theme and accent colour.
5. Same single portable exe as before - no new
   dependencies, no WebView2, still ~500 KB.

WHAT'S NEW IN v2.3.0-alpha.3 (the AI chat tab)
---------------------------------------------------------------
1. A DEDICATED AI CHAT WINDOW - type AI/ and press Enter
   (or type AI/ then your question and press Enter). A
   full conversation window opens: chat bubbles, the
   whole history in context, markdown answers, code
   blocks with Copy, a stop button, and per-answer copy.
   "AI" alone also offers the chat on Enter.
2. STREAMING ANSWERS - local Ollama models stream token
   by token (true streaming); Anthropic plays back with
   a typewriter reveal, so both feel alive. A three-dot
   thinking indicator pulses while the model works.
3. PROMPT-KIT DESIGN LANGUAGE - the UI follows the
   open-source prompt-kit chat kit (prompt-kit.com),
   rebuilt natively in WPF: centred conversation column,
   accent user bubbles with a sharp tail corner, avatar
   dot replies, suggestion chips on the empty state, a
   big rounded prompt input with a circular send button.
   NO browser engine is embedded - the portable exe
   promise holds (still one ~500 KB Lumo.exe).
4. SMART CONTEXT - the chat remembers your conversation
   (last 16 turns are sent per request, so local models
   stay fast) and the AI-off state shows a guidance
   banner instead of failing silently.
5. MORE TESTS - the suite grew from 92 to 117: the
   multi-turn request builder, both streaming parsers,
   the key-never-in-body rule and the markdown renderer.

WHAT'S NEW IN v2.3.0-alpha.2 (one-click local AI setup)
---------------------------------------------------------------
1. INSTALL OLLAMA FROM THE APP - Settings > AI now has a
   "Local models (Ollama)" card. If Ollama is missing, one
   button downloads the official installer from ollama.com
   and runs it silently (Windows may show a UAC prompt).
   If it is installed but not responding, a Start button
   brings the local server up.
2. ONE-CLICK LIGHTWEIGHT MODELS - the same card lists a
   curated catalog with live progress:
     qwen2.5:0.5b  ~0.4 GB     deepseek-r1:1.5b ~1.1 GB
     qwen2.5:1.5b  ~1.0 GB     gemma2:2b        ~1.6 GB
     llama3.2:1b   ~1.3 GB     llama3.2:3b      ~2.0 GB
     phi3.5        ~2.2 GB     llama3.1:8b      ~4.9 GB
   Pulls stream with a global percent across layers; a
   finished model is set active automatically if the
   current one isn't on disk. Installed models can be
   switched (Use) or removed (Delete) to free space.
3. SMARTER ? ROWS - with AI enabled and no local runtime,
   the ? view offers "Get local AI free — install Ollama
   in one click" (Enter opens Settings > AI) instead of a
   dead-end asking row. Remote Ollama gateways are never
   offered a local installer.
4. MORE TESTS - the suite grew from 68 to 92, covering
   the pull-line/tags parsers, the local-endpoint guard
   and the model catalog invariants.

WHAT'S NEW IN v2.3.0-alpha.1 (DEV_PLAN Phase 3 — "Connected")
---------------------------------------------------------------
1. ASK AI FROM THE LAUNCHER (? PREFIX) - type ? then a
   question. The answer appears right in the result list;
   Enter copies the full text. Works with local Ollama
   (default http://localhost:11434, no API key, nothing
   leaves your PC) or the Anthropic API. Set it up in the
   new Settings > AI page: enable, provider, endpoint,
   model, key. The key is stored only in settings.json on
   this PC and is NEVER written to the log file.
2. BROWSER BOOKMARKS (B/ PREFIX) - Lumo reads your Chrome
   and Edge bookmarks (all profiles, read-only) and makes
   them searchable: name, folder and URL, newest first
   when the query is empty. Bookmark rows open, copy and
   pin just like web results.
3. SNIPPET VARIABLES - your !snippets (and macro steps)
   can now embed live values:
     {{date}}      2026-08-30
     {{time}}      14:05
     {{datetime}}  2026-08-30 14:05
     {{clipboard}} the text on your clipboard right now
     {{name:Jane}} any key:default pair -> the default
   Unknown {{tokens}} are left visible instead of silently
   vanishing, so typos are easy to spot.
4. MORE TESTS - the suite grew from 43 to 68, now covering
   the AI request layer (including the never-log-the-key
   rule), the bookmark parser and the variable expander.

WHAT'S NEW IN v2.2.0-alpha.3 (bugfix — solid quick-action menu)
---------------------------------------------------------------
1. FIX: THE RIGHT-CLICK MENU WAS SEE-THROUGH - the menu card
   referenced a colour token (FieldBrush) that was never
   defined in the launcher window, so the card rendered with
   NO fill at all and whatever was on screen showed through
   the menu. The card now uses the palette's opaque field
   colour: solid dark #2D2D2D in dark mode, solid #FBFBFB in
   light mode, matching the Settings window.
2. SAME FIX FOR THE SEARCH BOX - the search field used the
   same missing token and had been silently rendering as
   plain background; it now gets its proper inset field fill.

WHAT'S NEW IN v2.2.0-alpha.2 (quick actions + favourites rework)
---------------------------------------------------------------
1. OPEN LEADS THE MENU - the quick-action menu now starts
   with Open (exactly what Enter does), the pin/unpin pair
   sits behind a separator, items show their gesture key
   ("Open   Enter"), and copying a web row reports
   "Copied URL" instead of "Copied path".
2. PIN ON HOVER - pinnable rows show a star on hover:
   outline ☆ to pin, filled accent ★ while pinned (always
   visible, so you can unpin from any view). One click,
   no menu needed.
3. TOOLS + FILES JOIN - utility rows (U/ commands like
   mute, night light, Settings) are now openable and
   PINNABLE. Elevation now works for .bat/.cmd/.exe/.msc
   files found by the file index, not just Start-Menu
   apps.
4. FAVOURITES, REFINED - the FAVOURITES section now shows
   up to 12 pins (the old silent cap was 6) ordered
   NEWEST PIN FIRST; storage on disk is unchanged, so
   upgrading is lossless.
5. FIXES - right-clicking a header no longer flashes an
   empty menu; Ctrl+Right now TOGGLES the menu closed;
   the pin/menu policy is covered by 43 unit tests.

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
