# File map

Every tracked source file and its responsibility, plus content/UI asset locations and tests.

Related: [architecture.md](architecture.md), [README.md](../README.md).

## Runtime

| File | Responsibility |
| --- | --- |
| `Assets/CardForge/Runtime/CardForgeModels.cs` | All serializable models/enums and `CardForgeData` helper. Authoritative source for the JSON schema. |
| `Assets/CardForge/Runtime/CardForgeRuntimeReader.cs` | Loads exported AssetBundles, dispatches manifest JSON paths, exposes `Schema`, `Block`, layouts, cards, rules, icons, images, fonts. |
| `Assets/CardForge/Runtime/CardForgeRules.cs` | Compiles actions into resolution graphs, requirement matching, zone/layer variant resolution, compiled actions. |
| `Assets/CardForge/Runtime/CardForgeRuleEngine.cs` | Reference response-window engine: `RaiseTrigger`, `Respond`, `Pass`, `Resolve`, `GetAllowedResponseActions`, `GetKeywordAbilities`. LIFO, all-players-pass semantics. |
| `Assets/CardForge/Runtime/DesignLogic/` | Pure geometry: `FrameGeometry`, `AtlasGeometry`, safe naming. No UnityEditor dependency. |

## Editor (authoring)

| File | Responsibility |
| --- | --- |
| `Assets/CardForge/Editor/CardForgeStore.cs` | Authoritative store: dictionaries, `Reload`, `Save*` methods, `Validate`, `ExportBlock`, import snapshot/restore. |
| `Assets/CardForge/Editor/CardForgeStoreExtensions.cs` | Validation entry points: `Validate`, `ValidateFlow`, `ValidateTrigger`, `ValidateAction`, `ValidateAbility`, `ValidateKeyword`. |
| `Assets/CardForge/Editor/CardForgeDesignTools.cs` | Workspace tabs (Layouts, Database, Blocks, TurnFlow, Triggers, Actions, Abilities), save dispatch, undo history, canvas hosting. |
| `Assets/CardForge/Editor/CardForgeZoneEditor.cs` | Zone/layer/variant inspector: visibility, lock, fields, templates, requirements. |
| `Assets/CardForge/Editor/CardForgeWorkflowEditors.cs` | Turn flow, trigger, action, ability, keyword editors. |
| `Assets/CardForge/Editor/CardForgeBatchBuild.cs` | CI entry point `BuildAllActive`; exports every block for the active build target. |
| `Assets/CardForge/Editor/CardForgeArtCatalog.cs` | Texture asset picker for image/icon fields. |
| `Assets/CardForge/Editor/CardForgeRequirements.cs` | Requirement editor UI (comparison, expected value). |
| `Assets/CardForge/Editor/Importing/TabularReader.cs` | CSV/TSV/Excel/JSON/Google import reading; delimiter detection; row/column limits. |
| `Assets/CardForge/Editor/Importing/JsonReader.cs` | Flat JSON parsing for import. |
| `Assets/CardForge/Editor/UI/Scripts/CardForgeWindow.cs` | Studio shell: top bar, File menu, quick open, screen host, sidebar, status line. |
| `Assets/CardForge/Editor/UI/Scripts/Screens/LiveCatalog.cs` | Eight library screen definitions: columns, row source, create/duplicate/delete actions. |
| `Assets/CardForge/Editor/UI/Scripts/Screens/GridScreenController.cs` | Grid/gallery rendering, search, filters, sorting, selection, bulk actions. |
| `Assets/CardForge/Editor/UI/Scripts/Screens/DataTransfer.cs` | `StudioPackage`, import planning (`Plan`), transactional apply (`Apply`), export. |
| `Assets/CardForge/Editor/UI/Scripts/Screens/ImportWizard.cs` | Import wizard UI: file choose, Google URL, worksheet selection, column mapping, conflict policy. |
| `Assets/CardForge/Editor/UI/Scripts/Navigation/SidebarNavigation.cs` | Sidebar groups: Content, Game rules, Project, Creator tools. |
| `Assets/CardForge/Editor/UI/Scripts/StudioScreens.cs` | Screen titles, new-document nouns, creatable screen list. |

## Editor extensions (creator tools)

| Folder | Responsibility |
| --- | --- |
| `Editor/Extensions/FrameComposer/` | Frame Composer: local region detection, Ollama vision analysis, layout + PNG save. |
| `Editor/Extensions/IconSets/` | Icon Set Studio: atlas slicing, folder import, icon manifests. |
| `Editor/Extensions/GoogleFonts/` | Google Fonts importer: online family download, local TTF/OTF import, manifest. |
| `Editor/Extensions/Ollama/` | Ollama client: chat requests, cloud/local endpoints, API key handling. |
| `Editor/Extensions/ImageProcessing/` | `CardForgeImageAssets`, `CardForgeFrameEdgeRefiner`, `CardForgeIconPainter`. |
| `Editor/Extensions/Theme/` | `ThemeController`: dark/light theme switching. |

## Tests

| Assembly | Location | Focus |
| --- | --- | --- |
| `CardForge.Tests` | `Assets/CardForge/Tests/Editor` | Import (CSV/TSV/JSON/Google/Excel), rule matching. |
| `CardForge.DesignLogic.Tests` | `Assets/CardForge/Tests/DesignLogic/Editor` | Geometry normalization, atlas cells, filename sanitization. |
| `CardForge.CreatorTools.Tests` | `Assets/CardForge/Tests/CreatorTools/Editor` | Ollama JSON parsing, font host allow-list, edge refiner, cropping. |

## Content assets

| Location | Contents |
| --- | --- |
| `Assets/CardForgeData/schema.json` | Seeded seven-field schema. |
| `Assets/CardForgeData/blocks/<id>/` | Block JSON, layouts, cards, export manifest (generated). |
| `Assets/CardForgeData/icons/<id>.json` (+ `.png`) | Icon documents and generated rasters. |
| `Assets/CardForgeData/turn-flows/`, `triggers/`, `actions/`, `abilities/`, `keywords/` | Global rules documents. |
| `Assets/CardForgeData/fonts/<family>/` | Imported font files + manifest. |
| `Assets/CardForgeData/frames/` | Frame Composer output PNGs. |
| `Assets/CardForgeData/icon-sets/` | Icon Set Studio output PNGs. |

## Project

| File | Purpose |
| --- | --- |
| `Packages/manifest.json` | Package manifest (URP, Input System, Rider, VS, pipeline). |
| `ProjectSettings/ProjectVersion.txt` | Pins Unity `7000.0.0a16`. |
| `Assets/CardForge/ARCHITECTURE.md` | Original short architecture brief (with Documentation section). |
| `README.md` | Project overview and quick start. |
| `docs/` | This documentation tree. |

## Ignored (not tracked)

`Library/`, `Logs/`, `Temp/`, `UserSettings/`, `*.csproj`, `*.sln`, `*.slnx`, `.CardForgeBackups/`, `CardForgeExports/` (build output, commit or delete deliberately).

## See also

- [architecture.md](architecture.md) — assembly boundaries and dependency direction.
- [data-model.md](data-model.md) — exact JSON field definitions.
- [studio-ui.md](studio-ui.md) — studio shell and screen behavior.