# Studio UI

The studio is a single Unity Editor window built with UI Toolkit. Everything you author is stored as JSON under `Assets/CardForgeData`; the window is only a view over that store.

Open it from **CardForge > Open Studio** or **Window > CardForge Studio**.

Related: [getting-started.md](getting-started.md), [designer-guide.md](designer-guide.md), [file-map.md](file-map.md).

## Window anatomy

```
+-----------------------------------------------------------------------+
| brand | File | Studio settings | Quick open | Project health | Exit  |
+---------------+---------------------------------------+---------------+
| Active block  |                                       |               |
| [CORE ...]    |          Library screen               |   Inspector   |
|               |   (grid / gallery / empty state)      |   (selection) |
| Content       |                                       |               |
|  Layouts      |                                       |               |
|  Cards        |                                       |               |
|  Icons        |                                       |               |
| Game rules    |                                       |               |
|  Turn flows   |                                       |               |
|  Triggers     |                                       |               |
|  Actions      |                                       |               |
|  Abilities    |                                       |               |
| Project       |                                       |               |
|  Blocks       |                                       |               |
|  Data fields  |                                       |               |
| Creator tools |                                       |               |
|  Frame Composer                                       |               |
|  Icon Set Studio                                      |               |
|  Google Fonts                                         |               |
+---------------+---------------------------------------+---------------+
| status line                                                           |
+-----------------------------------------------------------------------+
```

The window has a minimum size of 1040 x 640. It remembers the last library screen in the `CardForge.LastLibrary` EditorPref.

When a document is open the middle area switches from a library screen to the designer, the header shows the document title and breadcrumb, and a **Close editor** button appears.

## Top bar

| Control | Purpose |
| --- | --- |
| **Active block** dropdown | Chooses which block layouts, cards and blocks screens operate on. Labels use `CODE . Name`; duplicate labels get `(1)`, `(2)` suffixes. |
| **File** menu | Save, reload from disk, validate, open creator tools, close editor, exit. |
| **Studio settings** | Switch dark/light theme; show/hide decorative artwork. |
| **Quick open (Ctrl/Cmd+K)** | Command palette for screens, documents and tools. |
| **Project health** | Opens the read-only audit window. |
| **Exit studio** | Closes the CardForge window and leaves Unity running. |

### File menu

| Item | Effect |
| --- | --- |
| Save (Ctrl/Cmd+S) | Saves the open document. Disabled in library mode. |
| Reload from disk | Re-reads every JSON document and refreshes the current screen. |
| Validate project references | Opens Project health. |
| Extract card frame... | Opens Frame Composer bound to the current store and block. |
| Import icon set... | Opens Icon Set Studio bound to the current store. |
| Google Fonts importer | Opens the Google Fonts window. |
| Ollama AI settings | Opens AI provider settings. |
| Close editor | Returns to the current library screen. |
| Exit studio | Closes the window. |

## Sidebar navigation

The sidebar is grouped and only shows screens that exist.

| Group | Screen | Description |
| --- | --- | --- |
| Content | Layouts | Card faces and backs for the active block. |
| Content | Cards | Cards in the active block. |
| Content | Icons | Icon documents and generated PNGs. |
| Game rules | Turn flows | Phase graphs. |
| Game rules | Triggers | Event and response definitions. |
| Game rules | Actions | Effects and resolution graphs. |
| Game rules | Abilities | Card abilities, keyword bundles, phase bindings. |
| Project | Blocks | Blocks and editions, validation and bundle builds. |
| Project | Data fields | Opens the schema editor (via the cards screen). |
| Creator tools | Frame Composer | Frame image to layout extraction. |
| Creator tools | Icon Set Studio | Atlas or folder icon import. |
| Creator tools | Google Fonts | Font family import. |

`Keywords` has no sidebar entry or grid screen. Open it from the Abilities designer with the keyword editor toggle, or from quick open.

## Library screens

There are eight grid screens. Each is a projection of the store, scoped to the active block where relevant.

| Screen | Title | Columns | Row source |
| --- | --- | --- | --- |
| `layouts` | Layouts | Side, Zones, Cards, Status | Active block `layoutIds` |
| `cards` | Card Database | Number, Type, Front, Back, Keywords, Abilities, Status | Active block `cardIds` |
| `blocks` | Blocks & Editions | Code, Cards, Layouts, Validation | All blocks |
| `turnflow` | Turn Flows | Phases, Connections, Validation | All turn flows |
| `triggers` | Triggers | Event, Rules, Responses | All triggers |
| `actions` | Actions | Viability trigger, Trigger outputs, Graph, Phase schedules, Abilities | All actions |
| `abilities` | Abilities | Card binding, Keyword bundles, Activation trigger, Trigger outputs, Action, Phase, References | All abilities |
| `icons` | Icon Workshop | Layers, PNG asset | All icons |

Status cells show `Ready`/`Valid` in green or an issue count in amber/red. Blocks and turn flows are validated live.

### Grid behavior

| Feature | Behavior |
| --- | --- |
| Search | Debounced 160 ms; matches name and row text. `Esc` clears. |
| Filters | Sidebar filter groups derived from `type` and `status` columns; button shows active count. |
| Sorting | Click column headers; custom multi-column sorter. |
| Selection | Multi-select rows; a bulk bar appears with Duplicate and Delete. |
| Gallery | Toggle between table and card gallery; keyboard arrows move, Enter opens, Space selects. |
| Inspector | Right pane shows the selected row and its actions. |
| Empty state | Offers create or clear search/filters. |
| Import | Opens the import wizard for the current screen. |
| Export | Opens the export dialog (CSV or JSON package). |

Row height is 48 px. Double-clicking a row (or pressing Enter in the gallery) runs the first inspector action, which is usually the editor.

### Inspector actions

| Screen | Actions |
| --- | --- |
| `blocks` | Edit block, Activate, Validate, Build bundle, Duplicate, Delete |
| all others | Edit/Open, Duplicate, Export, Delete |

`Activate` sets the active block. `Validate` runs `CardForgeStore.Validate` and shows a dialog. `Build bundle` exports to an AssetBundle and reveals the output folder. `Delete` confirms, then deletes the JSON from disk.

## Quick open

Press **Ctrl/Cmd+K** or click **Quick open**. The palette lists:

- **Go to** every library screen.
- **Create** a new document for creatable screens.
- **Tools** for project health, reload, Frame Composer, Icon Set Studio, Google Fonts, AI settings and theme.
- **Recent** documents (last eight, persisted by `RecentDocuments`).
- **Documents** matching the typed query.

Type to filter, use the arrow keys to move, Enter to run, Esc to close.

## Shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl/Cmd+S` | Save the open document. |
| `Ctrl/Cmd+K` | Open quick open. |
| `Ctrl/Cmd+F` | Focus the grid search field. |
| `Ctrl/Cmd+D` | Duplicate selected rows. |
| `Delete` or `Cmd+Backspace` | Delete selected rows (with confirmation). |
| `Esc` | Clear search; close quick open/modal. |
| `Ctrl/Cmd+Z` | Undo in the designer. |
| `Ctrl/Cmd+Shift+Z` or `Ctrl+Y` | Redo in the designer. |
| `Ctrl/Cmd+C` / `Ctrl/Cmd+V` | Copy / paste selected zones (layout designer). |
| `Ctrl/Cmd+D` | Duplicate selected zones (layout designer). |
| `Delete` / `Backspace` | Delete selected unlocked zones (layout designer). |
| Arrow keys | Nudge selected zones 1 px. |
| `Shift` + arrows | Nudge selected zones 10 px. |
| `Alt` + drag/resize | Disable snapping for that gesture. |
| `Enter` | Open selected gallery item. |
| `Space` | Toggle gallery selection. |

Designer shortcuts only apply while a document is open and the canvas has focus.

## Designer workspace

The designer replaces the library screen. Its tabs map to the document kind:

| Tab | Editor |
| --- | --- |
| Layouts | Card layout canvas plus zone inspector; icon studio toggle. |
| Database | Card fields, preview and record details; schema editor toggle. |
| Blocks | Block metadata and edition operations. |
| Turn Flow | Phase graph and phase scheduling. |
| Triggers | Trigger rules, response windows and links. |
| Actions | Action effect, trigger links and resolution graph. |
| Abilities | Ability binding, rules and graph; keyword editor toggle. |

See [designer-guide.md](designer-guide.md) and [rules-and-flows.md](rules-and-flows.md) for the workflows.

## Project health

**Project health** opens a read-only audit window. It shows counts for blocks, layouts, cards, flows, triggers, actions, abilities and keywords, plus validation findings from:

- `CardForgeStore.Validate(block)`
- `ValidateFlow`
- `ValidateTrigger`
- `ValidateAction`
- `ValidateAbility`
- `ValidateKeyword`

Click a finding to jump to the owning editor. The window is styled with the shared theme and cannot modify data.

## Themes and artwork

- **Switch theme** toggles `cf-theme-dark`/`cf-theme-light` on the root and stores `CardForge.Theme.Dark`.
- **Show artwork** toggles decorative banners and backgrounds (`CardForgeArtwork.Enabled`). Hiding artwork can speed up the window on slow machines.

## Status line

The bottom status label shows `Autosave` while a document is open, `Saved` after an explicit save, or an error message. This is a status label, not an autosave: edits are written only when you press Save (Ctrl/Cmd+S) or when a tool performs an explicit save. Close the editor with unsaved changes only after saving.

## Related preferences

| EditorPref | Meaning |
| --- | --- |
| `CardForge.LastLibrary` | Last library screen. |
| `CardForge.Theme.Dark` | Dark theme on/off. |
| `CardForge.AI.Ollama.Cloud` | Use Ollama Cloud vs local. |
| `CardForge.AI.Ollama.Model` | Vision model name. |
| `CardForge.Recent` (prefix) | Recent document list entries. |

## See also

- [designer-guide.md](designer-guide.md) for the canvas and inspectors.
- [import-export.md](import-export.md) for the grid import/export dialogs.
- [troubleshooting.md](troubleshooting.md) when a screen is empty or an action fails.