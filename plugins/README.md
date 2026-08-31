# First-party plugins

This folder is the **official Lumo plugin catalog**. The launcher app fetches
`registry.json` from this folder (via `raw.githubusercontent.com`) when you press
**Browse** on *Settings → Plugins → First-party plugins*, and downloads each
plugin's `plugin.json` straight into `%APPDATA%\Lumo\plugins\<id>\`.

Every plugin here is a single declarative `plugin.json` — no code, no DLLs, no
network calls of its own. See
[docs/PLUGIN_DEVELOPMENT.md](../docs/PLUGIN_DEVELOPMENT.md) for the full schema
and authoring guide.

## Layout

```
plugins/
  registry.json              ← the index the app fetches (id, name, description, version, url)
  <plugin-id>/
    plugin.json              ← the manifest the app downloads
```

## Adding or updating a first-party plugin

1. Create (or edit) `plugins/<id>/plugin.json`. The **folder name is the
   plugin id** — lowercase `a-z 0-9 -`, max 40 chars.
2. Validate it locally: parse it with any JSON tool, and check the rules in
   `docs/PLUGIN_DEVELOPMENT.md` (keyword charset, command types, size caps).
   The app rejects invalid manifests at install time and at scan time.
3. If it is new, add an entry to `registry.json`:
   ```json
   {
     "id": "my-plugin",
     "name": "My Plugin",
     "description": "One line, shown in the in-app browser.",
     "author": "you",
     "version": "1.0.0",
     "url": "https://raw.githubusercontent.com/Anik1377/Lumo-Launcher/main/plugins/my-plugin/plugin.json"
   }
   ```
4. Bump `version` in **both** the manifest and the registry entry when an
   installed plugin changes — the app shows "Reinstall" when the catalog
   version differs from the installed one.
5. Open a PR. Merging to `main` publishes the catalog instantly (raw URLs are
   per-branch), no release needed.

## Rules for first-party plugins

- **Keyword hygiene** — short, memorable, no collisions with built-in prefixes
  (`A/ F/ C/ W/ I/ U/ H/ S/ B/ AI/ ? ! P/ /sc`) or with each other (the app
  resolves duplicates by first-wins, which would silently shadow one command).
- **Templates must be stable** — prefer official search endpoints; test the
  URL with and without a query, and with spaces in the query.
- **No personal data** — templates are shared by every user.
- **Keep payloads small** — manifests are capped at 64 KB by the app anyway.
