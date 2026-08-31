# Plugin development guide

Lumo plugins are **declarative JSON files** — no code, no DLLs, no builds. A
plugin is a folder containing one `plugin.json` that teaches the launcher new
keyword commands: open a web search, open a URL or file path, or put text on
the clipboard.

That design is deliberate. Because a plugin can only *describe* commands (it
can never execute code), the single-portable-exe promise and the "no
untrusted code execution" stance both survive intact — a plugin from a
stranger can do nothing your own keyboard couldn't.

- Install location: `%APPDATA%\Lumo\plugins\<id>\plugin.json`
  (or `<Lumo.exe folder>\data\plugins\<id>\plugin.json` in portable-data mode)
- Manage: **Settings → Plugins** (enable/disable per plugin, open the folder, rescan)
- Browse from the launcher: type **`P/`**
- Official catalog: **Settings → Plugins → First-party plugins → Browse catalog**

---

## Quick start — 60 seconds

1. Get a working starter: **Settings → Plugins → Copy starter plugin.json**
   (or in the launcher: `P/` → *Copy a starter*). It is a complete, valid
   manifest — two commands, one `web`, one `open`.
2. Make a folder anywhere: `%APPDATA%\Lumo\plugins\my-first-plugin\`
   (the **folder name is the plugin id** — lowercase letters, digits, `-`).
3. Paste and save the file as `plugin.json` inside it.
4. **Settings → Plugins → ⟳ Rescan** (or just open `P/` — the folder-level
   scan happens automatically when the plugins directory changes).
5. Type `so ` + a query, or `time`, and press Enter.

```json
{
  "name": "My first plugin",
  "author": "you",
  "version": "1.0",
  "commands": [
    {
      "keyword": "so",
      "name": "Stack Overflow search",
      "subtitle": "Search stackoverflow.com",
      "type": "web",
      "template": "https://stackoverflow.com/search?q={query}"
    },
    {
      "keyword": "time",
      "name": "What time is it",
      "subtitle": "Opens time.is — no query needed",
      "type": "open",
      "template": "https://time.is",
      "argOptional": true
    }
  ]
}
```

---

## Manifest schema

### Top level

| Field | Type | Required | Rules |
|---|---|---|---|
| `name` | string | no | Display name, max 60 chars. Empty → the plugin id is shown. |
| `author` | string | no | Max 60 chars. Shown in the installed list. |
| `version` | string | no | Max 20 chars. Shown as `v1.0.0`; used by the catalog to offer reinstall/updates. |
| `commands` | array | **yes** | 1–24 command objects. A manifest with zero commands is rejected. |

### Command object

| Field | Type | Required | Rules |
|---|---|---|---|
| `keyword` | string | **yes** | 1–24 chars, only `a-z 0-9 -`. Spaces become `-`; leading/trailing/double `-` are trimmed. Lowercased. Must be unique within the file. |
| `type` | string | no (default `web`) | `web` · `open` · `copy` — see below. |
| `template` | string | yes for `web`/`open` | The target URL or path. `{query}` is the placeholder for the typed text. Max 2000 chars. |
| `text` | string | yes for `copy` | The clipboard payload. `{query}` supported. Max 4000 chars. |
| `name` | string | no | Row title, max 60 chars. Empty → the keyword is shown. |
| `subtitle` | string | no | Row description, max 120 chars. Empty → a generated "searches/opens/copies" line. |
| `glyph` | string | no | 1–4 chars shown in the row's icon tile. Empty → `P`. |
| `argOptional` | bool | no (default `false`) | `true` → the bare keyword (no query) runs the command instead of asking for one. |

### Validation is strict on purpose

The parser rejects a manifest rather than installing a half-working plugin: a
command without its payload (a `web` command with no `template`, a `copy`
command with no `text`) or with an unusable keyword produces a logged error
and is skipped — you'll see it in the log and it simply won't appear. Fix the
manifest and rescan.

---

## The three command types

### `web` — a search URL (the default)

The `{query}` part is **URL-escaped**, the rest of the template is untouched.

```json
{ "keyword": "wiki", "type": "web", "template": "https://en.wikipedia.org/w/index.php?search={query}" }
```

Typing `wiki alan turing` opens
`https://en.wikipedia.org/w/index.php?search=alan%20turing`.

### `open` — a URL or file path

The `{query}` is substituted **raw** (no escaping) — this type is for local
paths, folder paths, and plain URLs. Environment variables are expanded
(`%USERPROFILE%` works).

```json
{ "keyword": "downloads", "type": "open", "template": "%USERPROFILE%\\Downloads", "argOptional": true }
```

```json
{ "keyword": "repo", "type": "open", "template": "https://github.com/Anik1377/Lumo-Launcher" }
```

### `copy` — text to the clipboard

```json
{ "keyword": "greet", "type": "copy", "text": "Dear {query},\n\n\n\nBest regards," }
```

Typing `greet Jane` copies `Dear Jane,\n\n\n\nBest regards,` — Ctrl+V pastes
it anywhere. Static payloads (no `{query}`) pair with `"argOptional": true` so
the bare keyword copies immediately.

---

## How routing works

1. **Static routes always win.** `A/ F/ C/ W/ I/ U/ H/ S/ B/ AI/ ? ! P/ /sc`
   are checked before plugins — a plugin keyword can never shadow a built-in
   view.
2. **Token-exact keyword routing.** A query routes to a plugin command when
   the whole query *is* the keyword (`time`) or the keyword followed by a
   space (`emo sunset`). A query that merely *starts with* the keyword (e.g.
   `timeout 5`) does NOT route — the keyword boundary is a space, nothing
   less.
3. **First plugin owns a keyword.** Two installed plugins defining the same
   keyword: the alphabetically-first plugin folder keeps it, the duplicate
   command is skipped with a log line. Reordering = rename the folder.
4. **Discoverability.** `P/` lists every enabled command (Enter runs
   `argOptional` commands, fills `keyword ` for the rest). While typing on
   the empty view, keywords that *start with* what you typed appear as
   quick-hit rows too.

## Limits (hard caps, enforced by the app)

| Limit | Value |
|---|---|
| Installed plugins | 64 (alphabetical folders first) |
| Commands per plugin | 24 |
| Manifest size | 64 KB |
| Template length | 2000 chars |
| Copy text length | 4000 chars |
| Keyword length | 1–24 chars, `a-z 0-9 -` |

---

## Testing & debugging

- **Rescan**: Settings → Plugins → ⟳ Rescan reloads every manifest. Edits
  *inside* a folder don't move the plugins directory's mtime, so the
  keystroke path won't notice them until a rescan — new/deleted plugin
  *folders* are picked up automatically.
- **Log**: everything (parse errors, skipped keywords, scan counts) is in the
  diagnostics log — `%LOCALAPPDATA%\Lumo\log.txt` (or `data\log.txt` in
  portable mode). Open it via `U/log` or Settings → About.
- **Enable/disable** per plugin in Settings → Plugins — disabled plugins keep
  their files but stop routing.
- **A failed run is a status line, not a crash** — if a command's URL is
  malformed the launcher shows the error in the status bar and logs it.

---

## Publish to the official first-party catalog

The catalog lives in the Lumo repo under
[`plugins/`](https://github.com/Anik1377/Lumo-Launcher/tree/main/plugins).
Its `registry.json` is what the in-app **Browse catalog** button fetches, so
anything merged there becomes one-click installable for every user — no app
update needed.

1. Fork the repo, create `plugins/<your-id>/plugin.json`.
2. Add an entry to `plugins/registry.json`:

   ```json
   {
     "id": "your-id",
     "name": "Your Plugin",
     "description": "One line shown in the in-app browser.",
     "author": "you",
     "version": "1.0.0",
     "url": "https://raw.githubusercontent.com/Anik1377/Lumo-Launcher/main/plugins/your-id/plugin.json"
   }
   ```

3. The CI-style checks a reviewer will run (and the repo's own consistency
   test enforces): the manifest parses with the production rules, the id
   matches the folder, the registry version matches the manifest, and the
   keywords don't collide with anything already in the catalog.
4. Open a PR. Merging to `main` publishes instantly.

Guidelines for first-party plugins: short memorable keywords, official
search endpoints only, test URLs with a query *and* without, no personal
data in templates, keep manifests small.

---

## Let an AI write the plugin for you

The prompt below carries the entire manifest contract. Copy it into any AI
chat (ChatGPT, Claude, Gemini, a local Ollama model…), replace the last line
with what you want, and paste the answer into a `plugin.json`. It's also
copyable from inside the app: **Settings → Plugins → Copy AI prompt** (or
`P/` → *Copy AI plugin prompt*).

```text
Create a Lumo launcher plugin for me. Output ONLY a single valid plugin.json, no commentary.

Lumo plugins are declarative JSON files — no code. The file is saved as
%APPDATA%\Lumo\plugins\<folder-name>\plugin.json where <folder-name> is the plugin id.

Top-level fields:
  "name"    (string, display name, max 60 chars)
  "author"  (string, max 60 chars)
  "version" (string, e.g. "1.0.0")
  "commands" (array of 1–24 command objects)

Each command object:
  "keyword"   REQUIRED — 1–24 chars, only a-z 0-9 and '-'; no leading/trailing/double '-'.
              Users type this keyword in the launcher, optionally followed by a space and a query.
  "type"      "web" (open a search URL — default), "open" (open a URL or file path), or "copy" (copy text).
  "template"  for "web"/"open" — the target URL/path; "{query}" is replaced by the typed text
              (URL-escaped for "web", raw for "open").
  "text"      for "copy" — the text to put on the clipboard; may contain "{query}".
  "name"      optional row title (max 60 chars)
  "subtitle"  optional row description (max 120 chars)
  "glyph"     optional 1–4 char icon shown on the row
  "argOptional" true — when the command works with NO query ("{query}" omitted/empty), bare "keyword" runs it.

Rules: every command needs the payload its type requires; keywords must be unique within the file;
keep keywords short and memorable; web templates must be real, working search URLs.

My plugin: <DESCRIBE YOUR PLUGIN HERE — what sites/paths/texts, which keywords you want>
```

---

## FAQ

**Is a plugin able to run code?**
No. A manifest is data — the launcher expands `{query}` into a template and
performs one of three fixed actions (open URL / open path / copy text).
There is no script engine, no DLL loading, no fetch-on-behalf-of-plugin.

**Are web `web` templates escaped?**
The `{query}` part of a `web` command is URL-escaped (`Uri.EscapeDataString`);
`open` substitutes raw. Put credentials or odd characters in `open` payloads
at your own risk.

**Why didn't my edit show up?**
You edited inside an existing folder — the automatic freshness probe watches
the plugins *directory*, not each folder. Settings → Plugins → ⟳ Rescan.

**Why does my keyword do nothing?**
Either a built-in route owns the prefix (`A/…` style), another installed
plugin owns the exact token (first-wins — check the log), or the keyword
isn't token-exact in your query (`timeout 5` never routes to `time`).

**Can I reorder which plugin wins a keyword?**
The alphabetically-first plugin folder wins. Rename the folder to change the
order (ids are folder names).
