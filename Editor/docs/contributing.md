# Contributing

CardForge Studio is a Unity 7 alpha project. This page covers repository conventions, assembly boundaries, how to extend the data model, and how to keep the docs and tests in sync.

Related: [architecture.md](architecture.md), [file-map.md](file-map.md), [testing.md](testing.md).

## Repository

| Item | Value |
| --- | --- |
| Remote | `https://github.com/bloodchild8906/TcgStudio.git` |
| Branch | `main` |
| Unity | `7000.0.0a16` |
| Render pipeline | URP `17.7.0` |
| Input | Input System `7.0.0` |

`git` may not be on `PATH` in this environment. Use `C:\Program Files\Git\cmd\git.exe` directly if needed.

## Getting a clean tree

Unity generates ignored folders (`Library`, `Logs`, `Temp`, `UserSettings`, `*.csproj`, `*.slnx`). To discard local experiments and match the remote:

```
"C:\Program Files\Git\cmd\git.exe" fetch origin
"C:\Program Files\Git\cmd\git.exe" reset --hard origin/main
"C:\Program Files\Git\cmd\git.exe" clean -xfd
```

Close Unity first. `Library` and `Temp` contain locked files while the editor is running, so `clean -xfd` cannot remove them.

Snapshot before destructive resets if you might need the work:

```
"C:\Program Files\Git\cmd\git.exe" stash push -u -m "wip: describe the change"
```

## Commit conventions

- Keep commits focused: one feature or fix per commit.
- Commit Unity `.meta` files with every asset, folder and script. Missing `.meta` files break references.
- Do not commit `Library/`, `Temp/`, `Logs/`, `UserSettings/`, generated IDE projects or `CardForgeExports/`.
- Do not commit API keys. Ollama keys belong in `OLLAMA_API_KEY` or `SessionState`, never in JSON, `EditorPrefs` or docs.
- Write commit messages in the imperative mood (`Add zone variant preview`, not `Added ...`).
- Include a short body when the change affects data format, validation or export.

## Project layout

```
Assets/CardForgeStudio/CardForge/
  Runtime/            shipped data + rules + reader
  Runtime/DesignLogic shipped pure geometry
  Editor/             authoring, store, exporters
  Editor/Importing/   tabular import
  Editor/UI/          studio shell (UXML/USS/scripts)
  Editor/Extensions/  creator tools (one assembly each)
  Tests/              editor test assemblies
Assets/CardForgeStudio/CardForgeData/ authored JSON and generated assets
ProjectSettings/      Unity settings
Packages/             package manifest and lock
docs/                 this documentation
```

## Assembly boundaries

| Assembly | May reference | Must not reference |
| --- | --- | --- |
| `CardForge.Runtime` | UnityEngine | `UnityEditor`, editor assemblies |
| `CardForge.DesignLogic` | Runtime, DesignLogic, UnityEngine | `UnityEditor` |
| `CardForge.Editor` | Runtime, DesignLogic, `UnityEditor` | UI shell |
| `CardForge.EditorUI` | Editor, Runtime, DesignLogic | creator tool assemblies unless needed |
| extensions | Runtime, Editor, `UnityEditor` | each other unless declared |

Rules:

- If code must run in a player build, put it in `CardForge.Runtime` or `CardForge.DesignLogic` and keep it free of `UnityEditor`.
- Keep editor-only logic out of the runtime models. Models are plain `[Serializable]` classes.
- Each extension folder has its own `.asmdef`. Add references explicitly; do not rely on the default assembly.
- UI Toolkit shell code belongs in `CardForge.EditorUI`, not `CardForge.Editor`.

## Adding a data field

The models use `JsonUtility`, so changes are additive by default.

1. Add the public camelCase field to the model in `CardForgeModels.cs` with a safe default.
2. Normalize it in `CardForgeStore.Reload` (null list -> empty list, drop null entries).
3. Include it in `Validate` if it references another document or asset.
4. Include it in `ExportBlock` if it points at an asset that must be bundled.
5. Include it in `DataTransfer.Fields` automatically (primitive fields are picked up by reflection). If it needs custom parsing, extend `DataTransfer.Plan`.
6. Add it to the relevant editor UI (`CardForgeZoneEditor`, `CardForgeWorkflowEditors`, `CardForgeDesignTools`).
7. Update [data-model.md](data-model.md).
8. Add a test if the behavior is non-trivial.

Do not rename existing fields without a migration: old JSON would silently lose data. Add a new field and migrate on `Reload` instead.

## Adding a new document type

1. Add a `[Serializable]` class with `version`, `id`, `name`.
2. Add a dictionary to `CardForgeStore`, a path helper, a `Save*` method and a `LoadEntries` call in `Reload`.
3. Add validation in `CardForgeStoreExtensions`.
4. Add it to `StudioScreens`, `LiveCatalog` (screen definition + rows + create/duplicate/delete), `DataTransfer.DocumentType/Exists/Current/Save`, and `ExportBlock`.
5. Add an editor in `CardForgeDesignTools` / `CardForgeWorkflowEditors`.
6. Update [data-model.md](data-model.md), [studio-ui.md](studio-ui.md) and [file-map.md](file-map.md).

## Adding a menu item or tool

- Studio-level commands go in `CardForgeWindow` (File menu, Studio settings or quick open entries).
- Standalone tools use `[MenuItem("CardForge/<Tool>")]` and open an `EditorWindow`.
- Add a sidebar entry with `AddToolNavigation` if the tool belongs in the studio.
- Add a quick-open entry in `BuildToolEntries`.
- Keep tool windows on the shared style sheet (`Assets/CardForgeStudio/CardForge/Editor/UI/USS/CardForge.uss`) and `ThemeController`.

## UI conventions

- Build UI with UI Toolkit (`VisualElement`, `Button`, `TextField`, ...), not IMGUI.
- Reuse the USS classes (`cf-app`, `cf-tool-title`, `primary-button`, `small-button`, `small-danger`, ...).
- Use the shared `ThemeController` so dark and light themes work.
- Do not hard-code colors that only work in one theme.
- Keep keyboard handling local to the focused element; avoid global shortcuts that conflict with Unity.
- New windows should set a sensible `minSize`.

## Style

The codebase is compact C#:

- Four-space indentation, braces on the same line.
- `camelCase` fields, `PascalCase` types and methods.
- `var` is common; use explicit types when it aids clarity.
- Prefer expression-bodied members for tiny helpers.
- Keep comments for non-obvious behavior, not for restating code.
- Do not add `using` directives you do not need.
- Do not introduce new third-party dependencies without checking the license and adding it to `Packages/manifest.json` or `ThirdParty/`.

## Docs

- This `docs/` tree is the long-form documentation. Update it with behavior changes.
- `Assets/CardForgeStudio/CardForge/ARCHITECTURE.md` is the short original brief; keep it consistent.
- Use ASCII punctuation in docs to avoid encoding problems in mixed Windows tooling.
- Link to related pages; every `docs/*.md` should be reachable from `docs/README.md`.
- Document limits (file sizes, row counts, frame counts) next to the feature that enforces them.

## CI

`CardForgeBatchBuild.BuildAllActive` is the export entry point:

```
Unity -batchmode -quit -projectPath <path> -buildTarget StandaloneWindows64 \
  -executeMethod CardForge.Editor.CardForgeBatchBuild.BuildAllActive
```

A minimal pipeline:

1. Check out the repo.
2. Restore Unity's package cache (or let Unity resolve `Packages/manifest.json`).
3. Run EditMode tests.
4. Run `BuildAllActive`.
5. Copy `CardForgeExports/` into `Assets/StreamingAssets` or upload it.
6. Build the player.

Do not cache `Library/` between incompatible Unity versions.

## Pull request checklist

- [ ] Working tree contains no generated Unity folders.
- [ ] All new assets have `.meta` files.
- [ ] Code compiles with no new warnings.
- [ ] EditMode tests pass.
- [ ] Data format changes are backward compatible or migrated.
- [ ] `docs/` is updated.
- [ ] No secrets, absolute machine paths or `EditorPrefs` dumps.

## See also

- [testing.md](testing.md) for the test assemblies.
- [architecture.md](architecture.md) for dependency direction.
- [troubleshooting.md](troubleshooting.md) for common setup problems.