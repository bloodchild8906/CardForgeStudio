# Getting started

CardForge Studio is a Unity-native authoring suite for digital trading card games. This page walks you through setup, the seeded data, a five-minute tour, running tests, and bundle export.

Related: [README.md](../README.md), [architecture.md](architecture.md), [studio-ui.md](studio-ui.md).

## 1. Open the project

Open the folder as a Unity project with Unity `7000.0.0a16` (pinned in `ProjectSettings/ProjectVersion.txt`). No package installation is required: `Packages/manifest.json` already lists URP, Input System, IDE integration, and built-in modules. The Excel importer is vendored as an editor-only DLL at `Assets/CardForgeStudio/CardForge/Editor/ThirdParty/ExcelDataReader/ExcelDataReader.dll`.

Let Unity finish the first import (this recreates the ignored `Library/` folder).

## 2. What is tracked vs generated

| Tracked in git | Generated / ignored |
| --- | --- |
| `Assets/CardForgeStudio/CardForge/**` source code and assets | `Library/`, `Logs/`, `Temp/`, `UserSettings/` |
| `Assets/CardForgeStudio/CardForgeData/**` JSON and imported art/fonts | `*.csproj`, `*.sln`, `*.slnx`, IDE state |
| `Packages/`, `ProjectSettings/` | `.CardForgeBackups/` |
| `docs/`, `README.md` | Build output under `CardForgeExports/` is **not** ignored (commit or delete deliberately) |

## 3. First run: seeded data

Open **CardForge > Open Studio** (or **Window > CardForge Studio**). `CardForgeStore.Reload()` reads `Assets/CardForgeStudio/CardForgeData/`. If `schema.json` is missing, it seeds a starter project and writes it to disk:

**Schema** (`Assets/CardForgeStudio/CardForgeData/schema.json`) — seven fields:

| key | label | kind | default |
| --- | --- | --- | --- |
| `title` | Card Name | Text | `New Discovery` |
| `cost` | Cost | Number | `3` |
| `type` | Type | Text | `Creature` |
| `rules` | Rules Text | Text | (empty in code; the checked-in file uses a placeholder sentence) |
| `power` | Power | Number | `2` |
| `art` | Art | Image | — |
| `faction` | Faction Icon | Icon | — |

**Starter block** `Core Set` (`CORE`), id `9d501abea1464fbd963fba15d4fe1b5b` in the checked-in data:

- `Standard Front` layout (360×504) with Name, Cost, Artwork, Type, Rules and Power zones,
- `New front` layout (a second front),
- `Classic Back` layout with a static `CARD FORGE` title, set as the default back,
- one card `New Discovery` (`001`).

**Starter turn flow** `Standard turn` with phases `Untap`, `Upkeep`, `Draw`, `Main`, `Combat`, `End`, linked sequentially with `continue`.

If `schema.json` exists but is invalid JSON, reload throws `InvalidDataException("CardForge schema.json is invalid. Fix the JSON before reloading.")`.

## 4. Five-minute tour

1. **Layouts** — select `Standard Front`. Drag zones on the canvas, resize with the corner grips, or edit exact values in the inspector. Use **View** to fit, toggle the grid, or preview variants.
2. **Cards** — select `New Discovery`, edit the bound fields on the right, assign art through the art catalog, and toggle **Edit overrides** to move a zone only for this card.
3. **Blocks & editions** — validate the block, then **Build this block bundle**.
4. **Data fields** — add or change schema fields; layouts and cards bind by field id.
5. **Turn Flows / Triggers / Actions / Abilities** — author rules data (see [rules-and-flows.md](rules-and-flows.md)).
6. **Project health** (sidebar or File menu) — run structural validation across all documents.

Press **Ctrl/Cmd+K** at any time for the command palette.

## 5. Run the tests

In Unity: **Window > General > Test Runner**, select **EditMode**, then **Run All**.

Or from the command line:

```
Unity -batchmode -projectPath <path> -runTests -testPlatform EditMode \
  -testResults results.xml -quit
```

See [testing.md](testing.md) for the three test assemblies and what they cover.

## 6. Export a bundle

From **Blocks & editions**, select the `CORE` block and click **Build bundle**. This writes `export-manifest.json` and builds an AssetBundle under:

```
CardForgeExports/cards-core-9d501abe/<BuildTarget>/
```

The runtime reader (`CardForgeRuntimeReader`) loads the bundle in a player build. See [runtime-guide.md](runtime-guide.md) and [import-export.md](import-export.md).

## See also

- [architecture.md](architecture.md) — how the store loads and writes JSON.
- [studio-ui.md](studio-ui.md) — the studio shell, grids, and shortcuts.
- [data-model.md](data-model.md) — every JSON field and enum.