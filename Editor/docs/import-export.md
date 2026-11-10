# Import and export

CardForge has three transfer paths:

1. **Table import** - spreadsheets and flat JSON into a library screen.
2. **Package import/export** - a self-describing `StudioPackage` JSON with stable ids and linked definitions.
3. **AssetBundle export** - runtime-ready JSON, images and fonts.

Related: [data-model.md](data-model.md), [runtime-guide.md](runtime-guide.md), [studio-ui.md](studio-ui.md).

## Table import

Open a library screen and choose **Import**. The wizard accepts:

| Format | Extensions |
| --- | --- |
| Excel | `.xls`, `.xlsx` |
| Delimited text | `.csv`, `.tsv` (also `.txt` with a detectable delimiter) |
| Flat JSON | `.json` (object or array of objects) |
| Google Docs | `.docx`, `.html`, `.htm`, `.zip` |
| Google Sheets/Docs URL | HTTPS `docs.google.com` link |

### Limits

| Limit | Value |
| --- | --- |
| File size | 32 MB |
| Columns | 256 |
| Rows | 20,000 |
| JSON nesting | 64 levels |
| Google download | 32 MB, 30 s timeout, 5 redirects |

Rows that are entirely blank are skipped. The first non-blank row supplies column names. Short rows are padded; a row with more cells than the header is rejected as a likely delimiter error.

### Delimiter detection

`TabularReader` counts `,`, `;` and `\t` in the first logical record outside quotes and picks the most frequent. Quoted cells support embedded delimiters, newlines and doubled quotes. Text after a closed quote (other than whitespace) is an error.

### JSON

Flat JSON supports objects and arrays of objects with scalar values. Nested objects/arrays are rejected and must be imported as a CardForge package instead. Keys become columns; missing keys become empty cells.

### Google Sheets and Docs

- Use a standard sharing link. The file must be readable without signing in.
- Sheets: the `gid` in the URL selects the worksheet; without it, the first sheet is used.
- Docs: the first table is read; the first row must contain headers.
- A sign-in page response is reported as an error; download the file locally instead.
- Only `https://docs.google.com` links are accepted.

### Worksheet selection

Workbooks with multiple sheets show a **Worksheet** dropdown. Each non-empty sheet becomes a table.

### Column mapping

The wizard auto-maps columns by normalized name:

- Exact match to a model field (`name`, `cardNumber`, `layoutId`, ...).
- For cards, a match to a schema field label becomes `field:<key>`.
- `cardname` and `displayname` map to `name`.

You can remap any column or choose **- Ignore -**. Each destination can be mapped only once.

### Existing-record policy

| Policy | Behavior |
| --- | --- |
| Create new IDs | Always creates new ids; incoming `id` values are ignored. |
| Update matching IDs | If the incoming `id` exists, updates that document in place. |
| Skip matching IDs | If the incoming `id` exists, skips the row. |

Cards and layouts must belong to the active block. If an id belongs to another block, the import rejects the row; activate that block or create new ids.

### Validation during planning

`DataTransfer.Plan` builds the documents in memory and reports row-level errors before anything is written:

- At least one column mapped, no duplicate destinations.
- Valid id (letters, digits, `-`, `_`; max 128), no duplicate ids in the source.
- Integer, finite float and boolean fields parse with invariant culture.
- Numeric card fields are valid numbers; boolean card fields are `true`/`false`.
- Name is not blank.
- Card front layout exists, belongs to the block, and is not a back.
- Back layout is a back in the block.
- Layout dimensions are at least 50 x 50.
- Ability `actionId` and `phaseId` references exist.

The wizard shows `N rows - X new - Y updates - Z skipped - E errors` plus the first 30 errors. Import is disabled while errors exist.

### Transactional apply

`DataTransfer.Apply` snapshots every JSON file under `Assets/CardForgeData` before writing. If any save throws, it deletes newly created files and restores the snapshot, then reloads the store. A failed import leaves the project unchanged.

## Package format

A `StudioPackage` is a single JSON document:

```json
{
  "format": "CardForge",
  "version": 1,
  "schema": { "version": 1, "fields": [] },
  "blocks": [],
  "layouts": [],
  "cards": [],
  "icons": [],
  "flows": [],
  "triggers": [],
  "actions": [],
  "abilities": [],
  "keywords": []
}
```

- `format` must be `CardForge`.
- `version` must be `1`.
- Every document needs a valid unique id.
- Packages preserve stable ids; existing records are skipped, not overwritten.
- Referenced textures are still Unity asset paths. Import those assets separately or move a built bundle.

### Schema matching

Packages can include a schema. On import:

- A field with the same id but a different key/kind is rejected.
- A field with a new id but an existing key is a collision.
- Enable **Match schema fields by key and type** to remap incoming fields to existing ids when the kinds match. Requirements and card values are remapped too.

### Import order

`ImportPackage` writes in dependency order: schema, blocks, layouts, cards, triggers, actions, flows, abilities, keywords, icons. It rejects missing references (for example a block referencing an absent layout, a card whose front layout is unavailable, a trigger referencing a missing action, or a keyword referencing a missing ability).

### Native flat JSON

For a single screen, the wizard also accepts a bare array or object of that screen's documents (without the `format` wrapper). This is wrapped into a `StudioPackage` internally and validated the same way. It expects flat records, not nested packages.

## Export dialog

Choose **Export** on a grid. The dialog offers:

| Option | Behavior |
| --- | --- |
| JSON | Writes a `StudioPackage`. |
| CSV | Writes the visible field columns as CSV. |
| Selected only | Exports only checked rows. |
| Include linked assets | Closes over related definitions (JSON only). |

`ExportFormat` also defines `PrintSheet`, but the current shell uses JSON and CSV.

### CSV export

- One header row plus one row per selected document.
- Card exports include `field:<key>` columns with the card's field values.
- UTF-8 with BOM, CRLF line endings, all cells quoted.
- Written to a path you choose, then revealed in the file manager.

### Linked-asset closure

When **Include linked assets** is on, a JSON export adds:

- The full schema.
- Cards referenced by exported blocks, plus their layouts.
- All icons when cards or layouts are present.
- Abilities referenced by cards, triggers and keywords.
- Keywords referenced by cards.
- Actions referenced by abilities, triggers and flow schedules.
- Triggers referenced by abilities, actions and flow schedules.
- Flows containing referenced phases.

This produces a package that can be imported into another project with minimal missing references.

## AssetBundle export

`CardForgeStore.ExportBlock(block, target)`:

1. Runs `Validate(block)`; throws with all errors if any exist.
2. Collects JSON: schema, block, block layouts, block cards, all turn flows, triggers, actions, abilities, keywords, and icons referenced by exported images.
3. Collects images: layout backgrounds, templates, zone/layer/variant assets, card override assets, image/icon field values.
4. Collects fonts from zone `fontAssetPath`.
5. Writes `export-manifest.json` and imports it.
6. Builds an AssetBundle named `cards-<slug(code)>-<first 8 chars of block id>`.
7. Copies the JSON tree beside the bundle.

### Output layout

```
<project>/CardForgeExports/<bundleName>/<BuildTarget>/
  <bundleName>                 (AssetBundle file)
  json/
    schema.json
    blocks/<blockId>/block.json
    blocks/<blockId>/layouts/<layoutId>.json
    blocks/<blockId>/cards/<cardId>.json
    blocks/<blockId>/export-manifest.json
    turn-flows/<id>.json
    ...
```

The manifest lists the exact JSON, image and font asset paths inside the bundle. The runtime reader uses it instead of guessing from file names.

### CI

`CardForge.Editor.CardForgeBatchBuild.BuildAllActive` exports every block for the active build target. See [runtime-guide.md](runtime-guide.md) and [contributing.md](contributing.md).

## Common import/export failures

| Symptom | Cause | Fix |
| --- | --- | --- |
| "A maximum of 256 columns is supported." | Sheet too wide. | Split the sheet. |
| "Import at most 20,000 records at a time." | Too many rows. | Split the file. |
| "A row contains more columns than the header." | Wrong delimiter or stray commas. | Clean the source or quote cells. |
| "Choose a valid front layout in the active block." | Card rows point outside the block. | Activate the block or fix `layoutId`. |
| "The matching ID belongs to another block." | Updating an id from a different block. | Activate that block or use new ids. |
| "Schema key collision" | Package schema differs from the project. | Enable schema matching or rename the key. |
| "Excel import requires Windows-1252 encoding support." | Editor runtime lacks the code page provider. | Add `System.Text.Encoding.CodePages` as an editor dependency. |
| Google returns a sign-in page. | File is not public. | Share publicly or download locally. |
| Export throws validation errors. | Broken references or missing assets. | Run Project health and fix the listed issues. |
| "Unity AssetBundle build returned no bundle." | Build pipeline failed. | Check the Console for the underlying error. |

## See also

- [data-model.md](data-model.md) for field names and types.
- [runtime-guide.md](runtime-guide.md) for consuming bundles.
- [troubleshooting.md](troubleshooting.md) for more fixes.