# Architecture

CardForge is a Unity-native editor for creating **digital** collectible/trading card games. JSON is the authoritative project data; Unity assets provide renderable frames, icons, images, and fonts. The authoring tools are intentionally separate from the game runtime.

This POC does **not** add physical print layouts, mass print exports, or spreadsheet batch-editing features.

## Documentation

The long-form documentation lives in [`docs/`](README.md): setup, architecture, studio UI, designer guide, rules, data model, import/export, creator tools, runtime integration, testing, contributing, troubleshooting and a glossary. This file remains the short architecture brief; when it and the implementation disagree, treat the source code as authoritative and update both.

## Assemblies and dependency direction

| Assembly definition | Purpose | Depends on |
| --- | --- | --- |
| `Runtime/CardForge.Runtime.asmdef` | Serializable cards, schemas, layouts, abilities, turn flows, rules and state machine contracts | Unity Runtime |
| `Runtime/DesignLogic/CardForge.DesignLogic.asmdef` | Pure normalized-frame geometry, atlas slicing and safe naming | Runtime |
| `Editor/CardForge.Editor.asmdef` | Core designer, JSON store, card rendering, validation and bundle builds | Runtime |
| `Editor/Extensions/Theme/CardForge.StudioTheme.Editor.asmdef` | Shared persisted theme controller | Unity Editor UI Toolkit |
| `Editor/Extensions/ImageProcessing/CardForge.ImageProcessing.Editor.asmdef` | Readable texture copies, cropping, PNG save, frame overlay | Runtime, Editor |
| `Editor/Extensions/Ollama/CardForge.Ollama.Editor.asmdef` | Vision chat request/response for Frame Composer | Runtime, Editor |
| `Editor/Extensions/GoogleFonts/CardForge.GoogleFonts.Editor.asmdef` | Font family download and manifest | Runtime, Editor |
| `Editor/Extensions/IconSets/CardForge.IconSets.Editor.asmdef` | Atlas slicing, folder import, icon manifests | Runtime, Editor |
| `EditorUI/CardForge.EditorUI.asmdef` | Studio shell, grids, inspector, quick open | Editor, Runtime, DesignLogic |

Dependencies flow one way: `EditorUI` -> `Editor` -> `Runtime`/`DesignLogic`. Creator tool extensions depend on `Runtime` and `Editor` but not on each other unless declared.

## Data flow

1. `CardForgeStore` owns all dictionaries (`Blocks`, `Layouts`, `Cards`, `Icons`, `TurnFlows`, `Triggers`, `Actions`, `Abilities`, `Keywords`) plus `Schema`.
2. `Reload()` reads JSON from `Assets/CardForgeData/`, normalizes null lists to empty, and seeds `schema.json` if missing.
3. Every screen in `CardForgeWindow` is a projection of the store; edits write JSON back to disk atomically (`<file>.tmp` then replace).
4. `ExportBlock()` validates, collects JSON/images/fonts, writes `export-manifest.json`, and builds an AssetBundle named `cards-<slug(code)>-<first 8 of block id>`.
5. `CardForgeRuntimeReader.Load(path)` opens the bundle, finds the manifest by JSON content, and dispatches each JSON path to `Schema`, `Block`, `Layouts`, `Cards`, `TurnFlows`, `Triggers`, `Actions`, `Abilities`, `Keywords`, `Icons`.

## Authoring flow

```
JSON on disk (Assets/CardForgeData/)
        |
        v
CardForgeStore.Reload()  <-- in-memory dictionaries
        |
        v
CardForgeWindow (UI Toolkit)  <-- grids, inspectors, designer
        |
        v
SaveDocument() / ExportBlock()  <-- JSON written back + AssetBundle
        |
        v
CardForgeRuntimeReader.Load()  <-- player-side bundle consumption
```

## IDs

New ids are `Guid.NewGuid().ToString("N")`: 32 lowercase hex characters, no dashes. `SafeId` accepts only letters, digits, `-` and `_`, up to 128 characters. Ids are stable across renames and double as file names.

## JSON

Public camelCase fields are serialized with Unity `JsonUtility`. Enums write as integers. `null` lists normalize to empty on reload. Unknown JSON members are ignored; missing members get C# defaults.

## EditorPrefs keys

| Key | Meaning |
| --- | --- |
| `CardForge.LastLibrary` | Last library screen. |
| `CardForge.Theme.Dark` | Dark theme on/off. |
| `CardForge.AI.Ollama.Cloud` | Use Ollama Cloud vs local. |
| `CardForge.AI.Ollama.Model` | Vision model name. |
| `CardForge.Recent` (prefix) | Recent document list entries. |

Ollama API keys are **never** stored in EditorPrefs or project files; they live in `OLLAMA_API_KEY` or `SessionState` only.

## Known gaps

- No automated tests for the UI shell, store save paths, bundle building, or rule engine response windows.
- `keywords` has no library screen or sidebar entry (edit via Abilities designer).
- PNG exporter does not render text; SVG is the production path.
- Blend modes are stored but never applied by the renderer.
- The rule engine compiles states/transitions but does not advance them; the host game owns full rules.

## See also

- [README.md](README.md) - documentation index.
- [data-model.md](data-model.md) - exact JSON fields and enums.
- [runtime-guide.md](runtime-guide.md) - loading and consuming bundles.
- [contributing.md](contributing.md) - repo conventions.