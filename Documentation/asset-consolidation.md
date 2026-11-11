# CardForgeStudio asset consolidation — 2026-11-11

## Asset root

All six formerly top-level asset groups now reside under `Assets/CardForgeStudio/`:
`CardForge/`, `CardForgeClient/`, `CardForgeData/`, `CardForgeDedicatedServer/`, `CardForgeLearn/`, `Plugins/`.

The original directory-level `.meta` files were moved with their folders. Serialized GUID references should therefore remain stable. The migration updated 341 path-bearing text files; `artifacts/asset-consolidation.log` records the migrated files. `Demo/Consolidate-UnityAssets.ps1` is the migration script. Rebuilding Unity's generated C# project files is safe because the assembly definitions retain their names.

## Runtime UX refresh

- UI Toolkit horizontal navigation and more compact match battlefield.
- Opponent player fields, command zones, mana sources and frontlines shown in the same view.
- All hidden card identities render the same reusable CardForge back.
- Five front templates: Creature, Spell, Quickspell, ManaSource, Commander.
- Card art resolves from the packaged local Resources images, falling back to the hash-verified remote manifest.
- Inspector displays the selected card and full card text.
- Deck collection now has search and type filters and the same face templates.
- Deleted unused legacy scrolling battlefield renderer and obsolete vertical sidebar implementation.

## Verified

- 240 unique card IDs, 252 card-art PNGs in both source and runtime resource directories.
- Three 42-card starter decks, each 40 distinct main cards plus one commander and one mana source.
- `dotnet build CardForge.Client.Runtime.csproj` passes with zero warnings and errors.
- Portable simulation: 62 assertions passed.
- Native server test suite: 47 assertions passed (existing nullable warnings remain in test code).
- Path scan found no old asset-root references in the supported source formats.
- Recent Unity Editor log scan found no compilation/import errors; it did log JobTempAlloc memory-leak diagnostics.

## Preserved deliberately

The art files under CardForgeData, CardForgeClient/Resources, and StreamingAssets are used by different authoring, local rendering, and server-delivery pathways. They have not been removed merely because the images are duplicates. Existing uncommitted project changes were preserved. Full in-Editor gameplay and visual layout validation still require interactive playtesting.
