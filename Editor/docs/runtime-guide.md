# Runtime guide

The `CardForge.Runtime` assembly ships in player builds. It loads exported AssetBundles, exposes the authored data, and provides the reference rule engine. It has no `UnityEditor` dependency and no UI.

Related: [architecture.md](architecture.md), [data-model.md](data-model.md), [rules-and-flows.md](rules-and-flows.md), [import-export.md](import-export.md).

## Assemblies

| Assembly | Ships in build | Purpose |
| --- | --- | --- |
| `CardForge.Runtime` | Yes | Models, rules, rule engine, bundle reader. |
| `CardForge.DesignLogic` | Yes | Pure geometry helpers (`FrameGeometry`, `AtlasGeometry`). |
| `CardForge.Editor` | No | Authoring, store, exporters. |
| `CardForge.EditorUI` | No | Studio shell. |
| extension assemblies | No | Creator tools. |

`CardForge.Runtime` has no assembly references and `autoReferenced: true`, so scripts in the default assembly can use it directly. Add an explicit reference if your project uses assembly definitions.

## Loading a bundle

```csharp
using CardForge;
using UnityEngine;

public sealed class CardForgeBootstrap : MonoBehaviour
{
    CardForgeRuntimeReader reader;

    void Start()
    {
        reader = CardForgeRuntimeReader.Load(
            System.IO.Path.Combine(Application.streamingAssetsPath, "cards-core-9d501abe"));

        SchemaDocument schema = reader.Schema;
        BlockDocument block = reader.Block;
        foreach (LayoutDocument layout in reader.Layouts) { /* ... */ }
        foreach (CardDocument card in reader.Cards) { /* ... */ }
    }

    void OnDestroy() => reader?.Dispose();
}
```

`Load`:

1. Calls `AssetBundle.LoadFromFile`.
2. Loads every `TextAsset` and finds the export manifest by its JSON content (`bundleName` + `jsonAssetPaths`).
3. Loads each manifest JSON path, dispatching by path:
   - `/schema.json` -> `Schema`
   - `/block.json` -> `Block`
   - `/layouts/` -> `Layouts`
   - `/cards/` -> `Cards`
   - `/turn-flows/` -> `TurnFlows`
   - `/triggers/` -> `Triggers`
   - `/actions/` -> `Actions`
   - `/abilities/` -> `Abilities`
   - `/keywords/` -> `Keywords`
   - `/icons/` -> `Icons`
4. Requires schema and block, otherwise throws and disposes the bundle.

`Dispose` calls `Bundle.Unload(false)`. Unload the reader when you leave the card game scene; textures loaded through it are not destroyed by `false`.

### Images

```csharp
Texture2D art = reader.GetImage(card.values
    .Find(v => v.fieldId == artField.id)?.value);
```

`GetImage` lowercases the path before `LoadAsset<Texture2D>`. Asset paths in the data are the Unity project paths recorded at author time, so they must match the asset names inside the bundle. The exporter adds those assets to the bundle under their project paths.

### Fonts

Fonts are included in the bundle but the runtime reader does not expose them. Load them by path:

```csharp
Font font = reader.Bundle.LoadAsset<Font>(zone.fontAssetPath.ToLowerInvariant());
```

## Rendering cards

The runtime assembly does **not** render cards. The editor canvas (`CardForgeCanvas`) is editor-only. A game needs its own renderer that reads the same data.

The reference behavior to reproduce:

1. Pick `card.layoutId` (and `backLayoutId` for the back).
2. Draw the layout background (`backgroundAssetPath` or `backgroundColor`).
3. Sort zones by `order`.
4. For each zone:
   - Skip when `visible` is false.
   - Resolve variants with `CardForgeRules.Resolve(zone, card, schema, out opacity)`.
   - Apply the card's `ZoneOverride` (position deltas, font size, color, text/asset override, hidden).
   - Draw layers in order, honoring visibility and opacity.
   - Draw text from the bound field (`CardForgeData.Value`), static text, image or icon.
5. Scale the 360 x 504 design space to your target resolution.

`CardForgeRules.Resolve` implements the variant precedence described in [designer-guide.md](designer-guide.md). `CardForgeRules.Matches` evaluates requirements. `CardForgeRules.LayerMatches` evaluates layer visibility, required card type and requirements.

Blend modes are stored but not applied. Text alignment is stored as a `TextAnchor` name; `CardForgeCanvas` has a parser, but the runtime does not.

## Requirements and variants

```csharp
bool visible = CardForgeRules.Matches(zone.requirements, card, schema);
ZoneDefinition resolved = CardForgeRules.Resolve(zone, card, schema, out float opacity);
bool layerVisible = CardForgeRules.LayerMatches(layer, card, schema);
```

Comparisons are case-insensitive text matches, except numeric comparisons (`Greater than`, `Greater than or equal`, `Less than`, `Less than or equal`) when both values parse as numbers. `Contains` is a case-insensitive substring test. A missing field reference returns `false`.

## Rule engine

```csharp
CardForgeRuleEngine engine = reader.CreateRuleEngine(turnFlowId);

// A game event opens a response window for every player.
RuleEngineResult opened = engine.RaiseTrigger(triggerId, sourcePlayerId, playerIds);
if (opened.kind == RuleEngineResultKind.WaitingForResponses)
{
    // Legal responses for the acting player:
    var allowed = engine.GetAllowedResponseActions(triggerId, playerId);

    // Add a response, or pass.
    engine.Respond(triggerId, playerId, actionId);
    engine.Pass(triggerId, playerId);
}
```

### Semantics

| Rule | Behavior |
| --- | --- |
| Response order | Last in, first out (LIFO). |
| Passing | Every player in the window must pass before resolution. |
| Nesting | A nested caused-trigger window must resolve before its parent. |
| Response legality | Only actions in the trigger's `responseActionIds` and in the action set. |
| Trigger emission | Only compiled actions with an `emitsTriggers` graph step emit caused triggers. |
| States | Compiled into `RuleStateMachineDefinition.states`; the engine does not advance them. |
| Conditions | Stored as text; the engine does not evaluate them. |
| Effects/costs/targets | Stored as text; the game applies them. |

### Result kinds

| Kind | Meaning |
| --- | --- |
| `Rejected` | The call was illegal; `reason` explains why. |
| `WaitingForResponses` | A window is open. |
| `ResponseAccepted` | A response was pushed. |
| `ResponseResolved` | The top response resolved. |
| `TriggerResolved` | The window closed with no responses left. |

Results include `triggerId`, `actionId`, `playerId`, `causedTriggerIds`, `graphStepIds` and `stateChangeStepIds`. Use `graphStepIds` to drive animation or effect sequencing.

### Keyword expansion

```csharp
IReadOnlyList<AbilityDocument> abilities = engine.GetKeywordAbilities(keywordId);
```

Use this to show reminder text or to resolve a card's keyword bundle in your own effect system.

## What the runtime does not do

- It does not render, layout or measure text.
- It does not evaluate condition strings or state transitions.
- It does not apply blend modes.
- It does not manage players, zones, decks or card instances.
- It does not validate data; export does that in the editor.
- It does not load addressables or remote bundles; you pass a file path.

CardForge is an authoring pipeline plus a reference response engine. Your game owns the full rules implementation.

## CI export

`CardForge.Editor.CardForgeBatchBuild.BuildAllActive` exports every block for the active build target. It is designed for batch mode and creates no menu items:

```
Unity -batchmode -quit -projectPath <path> -buildTarget StandaloneWindows64 \
  -executeMethod CardForge.Editor.CardForgeBatchBuild.BuildAllActive
```

On failure it logs the block and exits with code 1 in batch mode. Output goes to `CardForgeExports/<bundleName>/<BuildTarget>/`.

A typical pipeline then copies that folder into `Assets/StreamingAssets` or uploads it to your CDN before building the player.

## Versioning

Every document carries `version`. Current values: most documents `1`, icons `2`, `StudioPackage` `1`, `ExportManifest` `1`. The reader and store accept these versions; unknown package versions are rejected on import.

## See also

- [data-model.md](data-model.md) for field meanings.
- [rules-and-flows.md](rules-and-flows.md) for authoring semantics.
- [import-export.md](import-export.md) for bundle layout.