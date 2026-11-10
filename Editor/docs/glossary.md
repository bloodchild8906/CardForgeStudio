# Glossary

Terms used throughout CardForge and this documentation.

Related: [README.md](README.md), [data-model.md](data-model.md), [rules-and-flows.md](rules-and-flows.md).

## A

**Ability**
An `AbilityDocument` that binds an action (and optional trigger and phase) to a card or a reusable rule. Abilities can carry rules, response actions, caused triggers and a resolution graph.

**Action**
An `ActionDocument` describing an effect, its optional viability trigger, orientation change, caused triggers and resolution graph. Actions are what triggers respond with and what turn-flow phases schedule.

**Active block**
The block selected in the studio top bar. Layouts, cards and block operations are scoped to it.

**AssetBundle**
Unity's packaged asset format. `ExportBlock` builds one containing the block's JSON, images and fonts. Load it with `CardForgeRuntimeReader`.

**Atlas**
A single image containing a grid of icons. Icon Set Studio slices atlases into individual PNGs.

## B

**Back layout**
A layout with `isBack = true`. Cards may reference one as `backLayoutId`.

**Block**
A `BlockDocument` grouping layouts and cards, typically a set or edition. Has a `code` used in bundle names.

**Bundle name**
`cards-<slug(code)>-<first 8 chars of block id>`, for example `cards-core-9d501abe`.

## C

**Card**
A `CardDocument`: a front layout, optional back, field values, overrides, requirements and rule links.

**CardForgeData**
The static helper class with `NewId`, `Copy`, `Value`, `SetValue`, `GetOverride` and `EnsureOverride`. Also the name of the JSON data root `Assets/CardForgeData`.

**Compiled action**
A `RuleCompiledAction` produced by `CardForgeRules.CompileAction`: ordered graph steps, state-change steps, trigger boundaries and caused triggers.

**Creator tools**
Frame Composer, Icon Set Studio, Google Fonts and Ollama AI settings.

## D

**Data field**
A `FieldDefinition` in `schema.json`. Fields have a stable id, a unique key, a kind and a default. Cards store values by field id.

**Designer**
The embedded editor (`CardForgeDesignerView`) that replaces a library screen when a document is open.

## E

**Edition**
Informal synonym for a block, especially when cloning or reprinting.

**Export manifest**
`ExportManifest` JSON written beside the bundle. Lists the exact JSON, image and font asset paths. The runtime reader locates it by content.

## F

**Field kind**
`Text`, `Number`, `Icon`, `Image` or `Boolean`. Stored as an integer enum in JSON.

**Frame Composer**
The tool that turns a card frame image into a layout and PNG region assets, locally or with a vision model.

## G

**Gallery**
The card/tile view of a library screen, alternative to the data grid.

## I

**Icon**
An `IconDocument`: either a PNG asset path, a stack of vector `IconLayer`s, or both. Rendered by `CardForgeIconPainter` when layers exist.

**Import plan**
The in-memory result of `DataTransfer.Plan`: documents to create/update/skip plus row errors. Applied transactionally.

## K

**Keyword**
A `KeywordDocument`: a named bundle of abilities with rules and reminder text. Cards reference keywords by id.

## L

**Layer**
A `ZoneLayer` inside a zone: an ordered surface with its own visibility, opacity, asset, template, binding and requirements.

**Layout**
A `LayoutDocument`: the card face geometry, background, templates, zones and requirements.

**Library screen**
One of the eight grid screens (layouts, cards, blocks, turn flow, triggers, actions, abilities, icons).

**LIFO**
Last in, first out. The rule engine resolves the most recent response first.

## M

**Meta file**
Unity's `.meta` sidecar for every asset and folder. Must be committed; it stores the GUID used by references.

## O

**Ollama**
The vision/LLM provider used by Frame Composer. Supports cloud and local endpoints.

**Override**
See **Zone override**.

## P

**Package**
A `StudioPackage` JSON containing a schema and any mix of documents with stable ids. Used for cross-project transfer.

**Phase**
A `TurnFlowNode` in a turn flow, e.g. Main or Combat. Phases schedule actions and can host abilities.

**Provenance**
`sourceBlockId` / `sourceCardId` on a card, recording which card it was reprinted or cloned from.

## Q

**Quick open**
The `Ctrl/Cmd+K` command palette for screens, documents and tools.

## R

**Requirement**
A `RequirementDefinition` comparing a field to an expected value. Gates zones, layers, variants, layouts and cards.

**Resolution graph**
The `graphNodes` / `graphLinks` on an action or ability describing step order. Steps can mark state changes and trigger emission boundaries.

**Response window**
A `RuleResponseWindow`: the set of players who may respond to a trigger, their passes, and the response stack.

**Rule engine**
`CardForgeRuleEngine`, the reference runtime executor for response windows.

## S

**Schema**
`schema.json`, a `SchemaDocument` listing data fields.

**Store**
`CardForgeStore`, the authoritative JSON loader/saver. All screens are projections of it.

**StudioPackage**
See **Package**.

## T

**Template**
A `ZoneTemplateDefinition`: a reusable nine-slice or scaled background for zones and layers.

**Trigger**
A `TriggerDocument` describing an event, its qualifying rules, response window and legal responses.

**Turn flow**
A `TurnFlowDocument`: phases and transitions, plus per-phase action schedules.

## U

**UI Toolkit**
Unity's UXML/USS UI system used by the studio shell and tool windows.

## V

**Variant**
A `ZoneVariant`: conditional geometry/text/asset replacement for a zone, selected by `card.variantId` or requirements.

## Z

**Zone**
A `ZoneDefinition`: a rectangular content area on a layout, bound to a field or static content.

**Zone override**
A `ZoneOverride` on a card adjusting a layout zone's position, size, font, color, text, asset or visibility for that card only.

## See also

- [data-model.md](data-model.md) for exact fields.
- [rules-and-flows.md](rules-and-flows.md) for rule semantics.
- [studio-ui.md](studio-ui.md) for UI terms.