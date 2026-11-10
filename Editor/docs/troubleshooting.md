# Troubleshooting

Common problems and fixes, grouped by area. For setup see [getting-started.md](getting-started.md); for import errors see [import-export.md](import-export.md).

## Project and Unity

### Unity will not open the project

- The project pins Unity `7000.0.0a16` in `ProjectSettings/ProjectVersion.txt`. Install that exact alpha or update the file deliberately.
- Delete `Library/` and reopen. Unity rebuilds it; this is slow but safe because no authored data lives there.
- Check the Editor log at `Logs/Editor.log` and `%LOCALAPPDATA%\Unity\Editor\Editor.log`.

### `git clean -xfd` cannot delete files

Unity holds locks on `Library/ArtifactDB`, `Library/SourceAssetDB`, `Library/DataStore/uds.db`, `Logs/Editor.log`, `Temp/UnityLockfile` and `Temp/FSTimeGet-*`.

1. Save your work and close Unity.
2. Confirm no `Unity`/`unity` process remains.
3. Re-run `git clean -xfd`.

Never delete locked files while Unity is running; you can corrupt the artifact database.

### The working tree has unexpected changes after opening Unity

Unity rewrites some settings and creates ignored folders. Only tracked changes matter. Use `git status` and `git diff` to see them. If a tracked file changed unintentionally, `git checkout -- <file>` restores it.

### "Invalid document ID"

Ids must be 1-128 characters of letters, digits, `-` or `_`. Ids are used as file names. Fix the id in the JSON or re-import with generated ids.

## Studio window

### Nothing appears / the window is blank

- Open **CardForge > Open Studio** again.
- Check the Console for UI Toolkit UXML/USS load errors.
- Confirm `Assets/CardForge/Editor/UI/UXML/CardForgeWindow.uxml` and the USS files exist.
- Try **Studio settings > Show artwork** off if decorative art fails to load.

### A library is empty

- Layouts and cards are scoped to the **Active block**. Choose a block in the top-left dropdown.
- If there are no blocks, create one from **Blocks & Editions**.
- The seeded checkout has one block, `CORE`, with three layouts and one card.

### Keywords are missing from the sidebar

Keywords intentionally have no grid screen or sidebar entry. Open them from the Abilities designer (keyword editor toggle) or via quick open.

### Save does nothing

- Save is disabled in library mode. Open a document first.
- Check the status line; it shows the exception message.
- Validation errors are shown in a dialog when saving a block or exporting.

### Undo/redo does nothing

- Undo is per document and is cleared on document switch.
- Undo is disabled while **Preview variants** is on.
- The keyword editor's undo history uses the ability history key; this is a known quirk. Save and reopen if history is confusing.

### A zone will not move

- It may be **locked**. Unlock it in the zone inspector or **Edit > Lock / unlock**.
- **Preview variants** disables editing.
- The canvas may not have focus; click the canvas first.
- Snapping may make small moves appear to do nothing; hold `Alt` or increase grid spacing.

## Data and validation

### "Layout is assigned to cards. Reassign those cards first."

Deleting a layout is blocked while a card uses it as front or back. Reassign those cards, then delete.

### "Default back must refer to a back layout in this block."

Set a layout with `isBack = true` as the block's default back, or clear the field.

### "Missing field for zone ..."

A zone, layer, variant, layout or card requirement references a schema field id that no longer exists. Rebind it or remove the requirement.

### "Missing template for zone ..."

The zone references a `templateId` that is not in the layout's `templates` list. Recreate the template or clear the reference.

### "Missing image ..." / "Missing font asset ..."

The asset path is wrong or the asset was deleted. Reassign it in the inspector. Paths must start with `Assets/` and load as a main asset.

### "Card references a layout outside this block."

The card's `layoutId` or `backLayoutId` belongs to another block. Move or copy the layout into this block, or reassign the card.

### Export fails with a list of errors

Export runs `Validate(block)` first. Fix every listed error; the dialog shows the joined messages. Run **Project health** for the full audit.

## Import

### "A maximum of 256 columns is supported."

Split the sheet or delete unused columns.

### "Import at most 20,000 records at a time."

Split the file into batches. Imports are transactional per file.

### "A row contains more columns than the header."

Usually a wrong delimiter or unquoted commas. Quote cells containing the delimiter, or export as CSV/TSV.

### "A quoted cell is missing its closing quote."

Fix the CSV quoting. Doubled quotes (`""`) represent a literal quote.

### "Excel import requires Windows-1252 encoding support."

The editor runtime cannot resolve `System.Text.Encoding.CodePages`. Add it as an editor-only dependency (or use a CSV export instead).

### Google import fails

- The file must be shared so it is readable without signing in.
- Use an `https://docs.google.com` link. Lookalike hosts are rejected.
- A response starting with `<` is usually a sign-in page.
- Private files: download XLSX/CSV/JSON and import locally.
- Timeout is 30 seconds; large files may fail.

### "Choose a valid front layout in the active block."

The imported card row has no valid `layoutId`, or the layout is a back, or it is in another block. Activate the correct block or map a valid layout.

### "The matching ID belongs to another block."

You chose **Update matching IDs** for a card/layout that lives in a different block. Activate that block, or choose **Create new IDs**.

### "Schema key collision"

The package schema has a key that already exists with a different id. Enable **Match schema fields by key and type** (if the kinds match) or rename the key.

### Import says it succeeded but nothing changed

The package importer **skips existing ids**. If you re-import the same package, nothing is created. Use table import with **Update matching IDs** for in-place changes, or delete the old records first.

## Export and bundles

### "One or more JSON source files were not imported."

`AssetDatabase.ImportAsset`/`Refresh` did not register every JSON file. Refresh the project, check for JSON parse errors in the Console, and retry.

### "Unity AssetBundle build returned no bundle."

`BuildPipeline.BuildAssetBundles` failed. Check the Console for the underlying error (platform module missing, invalid asset, disk space).

### The bundle loads but data is missing

- The runtime reader requires `schema.json` and `block.json` in the bundle.
- It finds the manifest by JSON content, not filename. Do not strip or rewrite the manifest.
- JSON paths are matched case-insensitively first, then exactly.

### `GetImage` returns null

- The asset path must match the path inside the bundle.
- The exporter only adds images referenced by layouts, zones, layers, variants, overrides and Image/Icon fields.
- `GetImage` lowercases the path; assets with mixed-case names may still fail on case-sensitive platforms. Keep asset paths lowercase.

## Creator tools

### Frame Composer says "Image exceeds the 48MB import limit."

Downscale the frame before importing. The limit applies to the source file.

### AI analysis fails or returns nothing

- Set a vision-capable model in **AI Provider Settings**.
- Provide an API key via `OLLAMA_API_KEY` or paste one for the session.
- For local mode, ensure Ollama is running at `http://localhost:11434`.
- The response must be valid JSON with 1-16 usable zones. Try a clearer image or another model.
- AI output is untrusted; use **Detect regions locally** as a fallback and edit manually.

### "Icon Set Studio: Import up to 1024 icons at a time."

Split the folder into batches. Each file must be at most 24 MB.

### Font import fails

- Family names must match the `google/fonts` repository spelling.
- Not every family has TTF/OTF sources; download a desktop font and use **Import local TTF / OTF**.
- Downloads are limited to 20 MB and trusted GitHub hosts.
- Local fonts must be at least 100 bytes.
- Review the license before distribution.

### Fonts do not appear in a zone

Set `fontAssetPath` in the zone inspector. Fonts are Unity assets; refresh the project after import. Validation reports missing font assets.

## Tests

### Tests do not appear in the Test Runner

- Confirm the assembly defines `UNITY_INCLUDE_TESTS` and references `TestAssemblies`.
- Reimport the test assembly or restart Unity.
- Ensure **EditMode** is selected.

### Excel tests fail

The `.xls`/`.xlsx` fixtures must exist under `Assets/CardForge/Tests/Fixtures/`. If the code page provider is missing, Excel reading fails with the Windows-1252 message above.

## Performance

- Turn off **Show artwork** for a faster studio on large projects.
- Prefer gallery/list filters over very large unfiltered grids.
- Avoid keeping many designer windows open; each holds a store snapshot.
- Frame Composer and Icon Set Studio copy textures; close them after use.

## Recovery

- Authored JSON is under version control in `Assets/CardForgeData`. `git checkout -- Assets/CardForgeData` restores tracked files.
- Import is transactional and restores JSON on failure.
- If the store reports invalid JSON, fix the file named in the Console. `Reload` throws rather than silently discarding data.
- A local `git stash` can hold discarded work; list it with `git stash list` and restore with `git stash pop`.

## See also

- [import-export.md](import-export.md) for import/export rules.
- [creator-tools.md](creator-tools.md) for tool limits.
- [testing.md](testing.md) for test setup.