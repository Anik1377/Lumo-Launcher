======================================================
  LUMO v3.0.0-alpha.7  —  universal launcher for Windows
  (ALPHA BUILD — UNSTABLE. Expect bugs; report them!)
======================================================

WHAT'S NEW IN v3.0.0-alpha.7 (clicking works again)
---------------------------------------------------------------
1. CLICK-TO-ASSIGN UNBRICKED. After one drag-to-swap, clicks
   on empty deck slots silently did nothing (a stale drag
   flag ate them). The click/drag decision is now a tested
   state machine that clears itself on every press - drag
   and click can no longer poison each other, in any order.
2. ONE CLICK ASSIGNS. In the app picker, a single click on
   an app now commits it to the slot. Enter and double-click
   still work; the scrollbar and empty space are safe.
3. NO PHANTOM LAUNCHES. A shaky press that crosses the drag
   threshold no longer launches the app on release.

WHAT'S NEW IN v3.0.0-alpha.6 (the App Deck gets useful)
---------------------------------------------------------------
1. THE APP PICKER. Clicking an empty deck slot no longer
   opens Windows Explorer - it opens a "Choose an app"
   window listing EVERY Start Menu + Desktop app on the PC
   with live fuzzy search, real icons, most-used first, and
   a full keyboard flow (type, arrow keys, Enter). The old
   file dialog is still one click away ("Browse for a
   file...") and the slot editor gained "Pick app..." too.
2. PAGES - A DECK FOR EVERY MOOD. The chip row above the
   grid holds full 3x3 layouts: + Add page offers Games,
   Studio, Office and Entertainment in one click, or add
   blanks and rename (double-click a chip). Numpad 1-9
   always fires the page you see; Ctrl+Tab cycles pages and
   Ctrl+1..9 jumps straight to one. Existing slots migrate
   untouched into the Main page.
3. IMPORT & EXPORT. The header buttons write every page to
   a shareable .lumodeck file and merge imported pages in
   with collision-safe names - nothing you had is ever
   overwritten. Backups and sharing made boring.
4. PLUS TEN MORE. Drag a card onto another to swap them;
   duplicate slot; sort page A->Z; clear page (with
   confirm); persisted per-slot launch counters; run-as-
   administrator per slot; launch window state (Normal/
   Min/Max) per slot; a right-click card menu (open file
   location, copy path, ...); multi-file drop fills empty
   slots in order; "you open these a lot" suggestions in
   the editor; and the deck window remembers its size and
   position across restarts. The tutorial page covers it
   all (five steps now).
5. UNDER THE HOOD. 27 new tests (438 total) pin the page
   model, the v2 appdeck.json format (v1 files migrate
   automatically), the .lumodeck round-trip, the new slot
   options and the picker ranking.

WHAT'S NEW IN v3.0.0-alpha.5 (the quality-of-life round)
---------------------------------------------------------------
1. THE APP DECK IS ITS OWN WINDOW. No longer a tab inside
   the AI page: open it from the deck button in the AI rail,
   the "Open App Deck" button in Settings -> General, or
   right from the tray menu. Numpad 1-9, the editor and the
   tutorial all work exactly as before - the deck just has
   a proper window now (resize it, minimize it, whatever).
2. OLLAMA INSTALL LOCATION. Settings -> AI now shows where
   ollama.exe lives. Point Lumo at any folder and the
   one-click installer will put Ollama there (/DIR), and
   the probe looks there first. Reset to the standard
   locations any time.
3. MODEL STORAGE LOCATION. The new "Model storage" panel
   shows the live models folder (the OLLAMA_MODELS location)
   and its size on disk. "Change folder" moves new downloads
   anywhere you like and restarts Ollama so it takes effect
   immediately; "Open folder" and "Restart Ollama" are there
   too. Uninstalling models was already one click ("Delete"
   on any installed model).
4. CACHE & TEMP CLEANUP. Settings -> About -> "Storage &
   maintenance" lists Lumo's own logs, staged update zips,
   %TEMP%\Lumo scratch files and the Whisper voice-model
   cache, each with its real size and a one-click Clear.
   Nothing user-created is ever touched.
5. UNDER THE HOOD. 17 new tests (411 total) pin the new
   location resolution, cleanup behavior and the standalone
   deck window; the CI smoke tests open both the AI hub and
   the deck window on real Windows runners.

WHAT'S NEW IN v3.0.0-alpha.4 (AI page fixed + deck tutorial)
---------------------------------------------------------------
1. THE AI PAGE OPENS AGAIN. Since the hub landed, the AI
   window could not construct at all: a nav-rail button
   based its style on a RadioButton style, which WPF rejects
   at load time - the error vanished into the log and the
   page just "did nothing". Fixed, and new CI smoke tests
   now open the real window on every build.
2. THE APP DECK OPENS NOW. A second silent failure: the deck
   asked for theme colors before it was inside the window,
   which threw and quietly killed the tab. Fixed with safe
   fallbacks; a failed build can never wedge the nav rail.
3. THE DECK TUTORIAL. "How it works" now opens a real
   guided page: the numpad map, four steps (assign, launch,
   go global, fine-tune) and the global-hotkeys switch right
   in the tour. It shows itself the first time you open the
   deck; reopen it any time from "How it works".
4. THE GRID MIRRORS THE NUMPAD. Cards now fill 7-8-9 /
   4-5-6 / 1-2-3 exactly like the physical keys. The little
   key-number badges on the cards were always correct.
5. SAVE RACE FIXED. appdeck.json can no longer regress to an
   older state when edits land mid-save. And if the AI page
   ever fails, Lumo now shows the reason (and where the log
   lives) instead of doing nothing.

WHAT'S NEW IN v3.0.0-alpha.3 (persona faces + mascot)
---------------------------------------------------------------
1. PERSONAS HAVE FACES. Eight little vector characters
   (Spark, Blob, Bot, Ghost, Star, Cat, Moon, Sprout), each
   in the persona's own color. The persona chip, the picker
   flyout and every assistant reply now wear the persona's
   face - you always know who is answering.
2. THE MASCOT. The chat's welcome screen is an animated
   character that wears the active persona's face: it bobs
   and breathes, blinks by itself, leans in while your voice
   is recorded, and looks away thoughtfully while a reply
   streams. It stops when animations are off or the window
   is hidden.
3. PERSONA EDITOR. Settings -> AI personas now has a face
   picker, a color picker and a live preview. Old persona
   files load unchanged.
4. Everything still runs 100% local - faces are vector
   drawings, not downloads.

WHAT'S NEW IN v3.0.0-alpha.2 (the Lumo hub + App Deck)
---------------------------------------------------------------
1. THE WINDOW IS A HUB. A quiet nav rail on the left edge:
   AI (the chat) and APP DECK, with a settings gear at the
   bottom. Switching tabs never disturbs the chat.
2. APP DECK. Nine quick-launch slots in a 3x3 grid that
   mirrors your numpad. Click a card to launch, drop a file
   onto a card to assign it, right-click to edit (name,
   target, arguments, start-in). Icons are pulled from the
   exe/shortcut automatically. Numpad 1-9 launches while
   Lumo is focused - the top-row digits work too.
3. OPTIONAL GLOBAL NUMPAD. Settings -> General can register
   numpad 1-9 system-wide so the deck fires from anywhere.
   Off by default - inside full-screen games the numpad
   belongs to the game first.
4. Slots live in appdeck.json beside your other settings.

WHAT'S NEW IN v3.0.0-alpha.1 (the v3 foundation)
---------------------------------------------------------------
1. REAL INSTALLER. Releases now ship LumoSetup-<version>.exe
   next to the portable zip: per-user install (no admin
   prompt) to %LOCALAPPDATA%\Programs\Lumo, start-menu +
   optional desktop shortcut, optional start-with-Windows,
   and a real uninstaller. Prefer portable? The zip still
   works exactly as before - keep every file and folder
   together (Lumo.exe + runtimes\).
2. A REAL DESIGN SYSTEM. Lumo now builds on the WPF-UI
   Fluent library, and every window paints from one shared
   theme engine (ThemeService) instead of four drifting
   hand-rolled copies.
3. THEMES YOU CAN SHARE. Settings -> Appearance has a theme
   gallery: Lumo Dark/Light, Claude Dusk, Parchment, Nord,
   Matcha, Graphite. Import/export as .json - send a look
   to a friend, drop theirs in. Applies live.
4. THE GLOW, FIXED TO MINIMALISM. The orbiting rim comet is
   gone (no more idle CPU). What remains is the edge shine:
   a 1 px light catch along the top edge settling into the
   hairline. Nothing moves; the shine is the glow.
5. SMOOTH SCROLLING. Settings panels, the AI chat log and
   long lists glide instead of jumping in hard wheel steps.

WHAT'S NEW IN v2.6.0-alpha.8 (the ChatGPT / Claude look)
---------------------------------------------------------------
1. THE AI CHAT NOW LOOKS LIKE CHATGPT. User messages sit in
   the quiet gray pill with a uniform round shape (the old
   inverted-white shout is gone), assistant answers are full-
   width plain text with bigger type and generous spacing
   between turns - the layout grammar of chatgpt.com and
   claude.ai, still natively in WPF (no WebView).
2. THE SIGNATURE SEND BUTTON. A solid circle with an inverted
   arrow - black on white in dark mode, white on black in
   light - like ChatGPT's. It dims when there is nothing to
   send and becomes the stop button while a reply streams.
3. BORDERLESS TOP PICKERS. The model and persona names in the
   title bar are plain text buttons now (hover breathes a soft
   fill) instead of boxed chips - the chatgpt.com top bar.
4. POLISH. Bigger rounded prompt pill, wider conversation
   column in a larger window, "What can I help with?" greeting,
   rounder code blocks, and ChatGPT's disclaimer line ("Lumo AI
   can make mistakes - double-check important answers.") under
   the composer.

WHAT'S NEW IN v2.6.0-alpha.7 (whisper actually starts)
---------------------------------------------------------------
1. THE FIX FOR "NATIVE LIBRARY NOT FOUND". alpha.5/6 hid the
   whisper.cpp engine (whisper.dll + 3 ggml dlls) inside
   Lumo.exe and let .NET unpack it to a temp folder on first
   run - but Whisper.net 1.9.1 only ever looks in a
   runtimes\win-x64 folder NEXT TO Lumo.exe. On real installs
   the two never met: recording worked, then every
   transcription died with "Native Library not found in
   default paths". The engine now ships as real files in
   runtimes\win-x64\ beside the exe - the exact layout the
   loader reads. No temp unpacking, nothing for an antivirus
   to lock mid-extract, and Lumo.exe is a lean ~4.9 MB
   single file again.
2. THE ZIP HAS A FOLDER NOW - KEEP IT TOGETHER. The release
   zip contains Lumo.exe, README.txt and a runtimes\ folder.
   Extract the WHOLE zip into one folder and run Lumo.exe
   from there; if you move or copy Lumo.exe, move the
   runtimes folder with it. Lumo checks the folder before
   every transcription - if something is missing you get a
   plain-English message ("the Whisper runtime file ... is
   missing next to Lumo.exe - re-extract the full Lumo zip")
   instead of the old cryptic loader error, in the chat and
   in lumo-log.txt.
3. EVERYTHING FROM alpha.6 CARRIES OVER: model downloads
   that resume + retry, the engine release before a model
   re-install (the "file is in use" fix), the auto-gain
   waveform, and the setup card's mic + close buttons.

WHAT'S NEW IN v2.6.0-alpha.6 (voice fixes)
---------------------------------------------------------------
1. WHISPER DOWNLOADS SURVIVE BAD NETWORKS. Broken or
   interrupted model downloads now RESUME where they stopped
   (a stable .part file + HTTP Range) and transient failures
   retry automatically - one network hiccup no longer kills a
   148-488 MB stream.
2. THE "FILE IS IN USE" ERROR IS GONE. Re-downloading a model
   used to fail because the loaded Whisper engine still held
   the file open - the engine is released before the fresh
   file moves into place, and the move retries past antivirus
   scans.
3. THE WAVEFORM REACTS TO EVERY MIC. The bars auto-gain
   against a decaying peak: a quiet microphone is amplified
   (~3.6x max) instead of drawing a near-flat line; silence
   stays flat; sensitivity recovers gradually after loud
   speech. Slightly taller bars too.
4. THE SETUP CARD HAS A MIC BUTTON AND A WAY BACK. Record
   right away with the built-in Windows speech (mic button on
   the card, no download), and a close (X) button (Esc works
   too) so a failed download can never trap you. The Windows
   fallback is session-only - it no longer permanently demotes
   Whisper in settings.json.
5. +2 TESTS -> 351.

WHAT'S NEW IN v2.6.0-alpha.5 (Whisper voice + the prompt-kit
AI chat)
---------------------------------------------------------------
1. WHISPER IS NOW THE VOICE ENGINE. The chat's mic no longer
   leans on the Windows recognizer - clips are transcribed by
   OpenAI Whisper via whisper.cpp, fully offline (no cloud, no
   key), dramatically more accurate on real speech. Install on
   demand: the first mic click offers a one-time setup card -
   pick a model (Tiny 78 MB / Base 148 MB / Base multi / Small
   488 MB), watch the progress bar, and recording starts the
   moment it lands. "Use Windows speech" on that card keeps the
   old recognizer one click away ("VoiceEngine": "windows" in
   settings.json; "VoiceModel" picks tiny.en / base.en / base /
   small).
2. PROOF-OF-LIFE RECORDING UI. While you speak, the prompt row
   becomes a recording overlay: a live scrolling waveform fed
   by the real capture buffers, a recording red dot, and an
   elapsed clock that excludes paused time. Transcribing
   freezes the waveform - you always know which stage runs.
3. THE STOP BUTTON IS IMPOSSIBLE TO MISS. Finishing a clip is
   a 46 px pulsing red cap with a soft glow, flanked by
   pause/resume (the paused stretch is really discarded from
   the clip) and cancel. Mic / Ctrl+M / Enter still finish too.
4. THE AI CHAT GETS THE FULL PROMPT-KIT TREATMENT (rebuilt
   natively in WPF, still no WebView): right-aligned accent
   chat bubbles with the sharp-tail corner, gradient message
   avatars, sine-wave typing dots, message timestamps, one-
   click code copy, collapsible Reasoning blocks that stream
   live for thinking models (deepseek-r1 and friends), image
   attachments for vision models (paste a screenshot or use
   the attach button - Ollama and Anthropic shapes both
   wired), and the polished prompt input with focus ring.
5. +33 TESTS -> 349. Model catalog, language mapping, meter
   math, the reasoning splitter, image payload guard rails and
   both providers' image JSON shapes are under test.

WHAT'S NEW IN v2.6.0-alpha.4 (voice typing rebuilt: record,
transcribe, show)
---------------------------------------------------------------
1. THE MIC NOW WORKS IN THREE CLEAN STEPS. Click the mic (or
   press Ctrl+M) and speak the whole thought - Lumo records the
   complete clip; click again (or press Enter) and the recording
   is recognized as ONE batch on the Windows speech stack; only
   then does the text appear in the prompt, ready to edit or
   send. No more half-thoughts committed at every pause - the
   recognizer now sees the full utterance with full context.
2. SILENCE IS TRIMMED BEFORE RECOGNITION. Room tone at the
   edges made the recognizer invent filler words ("the", "uh");
   the clip is cut to the spoken part (with breathing room)
   before it ever reaches SAPI.
3. PREDICTABLE KEYS. While recording - mic / Ctrl+M / Enter
   finishes and transcribes, Esc cancels the clip. While
   transcribing - Esc discards the pending text. The
   transcription appends to whatever you already typed; what
   appears is exactly what a second Enter sends. Still offline:
   no cloud, no API key, no extra install. The toggle stays in
   Settings -> AI -> Voice input.

WHAT'S NEW IN v2.6.0-alpha.3 (voice typing in the AI chat)
---------------------------------------------------------------
1. DICTATE INTO THE AI CHAT. The prompt box grew a mic button
   - click it (or press Ctrl+M) and speak: offline Windows
   speech turns your voice into text live, right in the input,
   committing as you pause. Enter stops listening and sends
   exactly what you saw; Esc stops without sending; dictating
   appends to whatever you already typed. It runs on the
   desktop speech stack that ships with Windows itself - no
   cloud, no API key, no extra install. English ships with
   English Windows; more languages come from Windows Settings
   -> Time & Language -> Speech, or pin one with
   "VoiceLanguage" in settings.json.
2. ONE TOGGLE. Settings -> AI -> Voice input switches the
   feature and reports whether a recognizer is installed.
3. +20 TESTS -> 310. Recognizer picking, dictation text
   composition, and the new settings keys are under test.

WHAT'S NEW IN v2.6.0-alpha.2 (the plugin ecosystem)
---------------------------------------------------------------
1. FIRST-PARTY PLUGIN CATALOG. Settings -> Plugins now has a
   Browse catalog button: it fetches the official plugin set
   straight from the Lumo repo and installs any plugin with one
   click - manifest downloaded, validated, activated instantly.
   Launch set: Developer Search (so, mdn, npm, pypi, crates,
   docker), Developer Tools (regex, devdocs, caniuse, jsonfmt,
   ghstatus, speedtest), Social Search (reddit, x, yt, twitch,
   pins), Movies & Music (imdb, sp, netflix, tmdb, lastfm),
   Shopping (amzn, ebay, ali, etsy), Quick Jumps (gmail, gcal,
   gdrive, keep, notion, trello, whatsweb, teleweb), Web
   Utilities (tr, weather, wayback, isdown, tempmail) and Copy
   Kit (lorem, greet, shrug, tableflip, divider). P/ also
   gained a "Download first-party plugins" row. The catalog is
   fetched on demand only - never at startup, never per
   keystroke.
2. PLUGIN DEVELOPMENT GUIDE. docs/PLUGIN_DEVELOPMENT.md on
   GitHub: the full manifest schema, the three command types,
   routing rules, limits, debugging, and how to publish your
   own plugin to the official catalog with a PR.
3. AI AUTHORING PROMPT. Copy a ready-made prompt (Settings ->
   Plugins -> Copy AI prompt, or P/) that makes any AI chat
   write a valid plugin.json for you - describe the plugin in
   one line, paste the answer, rescan, done.
4. README REFERENCE. The README now documents every feature,
   every prefix, every W/ quick-switch keyword, every utility
   and the snippet variables as lookup tables.

WHAT'S NEW IN v2.6.0-alpha.1 (DEV_PLAN Phase 5 — "Product")
---------------------------------------------------------------
1. AUTO-UPDATE. Lumo now checks GitHub Releases about once a
   day (quietly, 15 s after startup, nothing without your
   click). When a newer build is out you get a tray nudge;
   Settings -> About has the full card: check now, download
   with progress, open the staged zip. Installing stays a
   two-step by design: extract the zip over your Lumo.exe.
   Turn it off any time (Settings -> About -> toggle).
2. PORTABLE DATA MODE. Make a folder named "data" next to
   Lumo.exe and everything - settings, shortcuts, chats,
   plugins, favourites, even the log - lives inside it.
   Lumo.exe + data/ on a USB stick = your whole setup travels.
   Without the folder, nothing changes (data stays in
   %APPDATA%\Lumo as before). The live location is shown on
   Settings -> About.
3. FIRST-RUN TOUR. A three-step intro (hotkey, prefixes,
   data & updates) greets the first launch - skip or close it
   any time, replay it from Settings -> About.

WHAT'S NEW IN v2.5.0-alpha.1 (DEV_PLAN Phase 4 — "Platform")
---------------------------------------------------------------
1. PLUGINS - your own launcher commands as plain JSON. Make a
   folder in %APPDATA%\Lumo\plugins\<name>\ with a plugin.json
   defining keyword commands: web search (URL template with
   {query}), open a path/URL, or copy text. Type P/ in Lumo to
   browse, or just type the keyword ("emo sunset"). Settings ->
   Plugins manages them, and P/ can copy a starter file. No
   code, no installs, nothing leaves your PC.
2. PER-SHORTCUT GLOBAL HOTKEYS - edit any shortcut (/sc) and
   give it a combo like Ctrl+Alt+G: it runs from anywhere,
   even when Lumo is hidden. Up to 16; captured in the same
   editor, live the moment you save.
3. DECLARATIVE ROUTER - the search prefixes became a proper
   route table (same behavior, cleaner engine, per-route error
   guard, and the doorway plugins plug into).

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
