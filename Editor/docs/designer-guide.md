# Designer guide

This guide covers layout design, the card database and block/edition operations. For rules authoring see [rules-and-flows.md](rules-and-flows.md); for the shell see [studio-ui.md](studio-ui.md).

Open a layout or card from its library grid, or use **Quick open**. The designer replaces the grid until you press **Close editor**.

## Card geometry

- Default canvas: **360 x 504 px**, stored on each `LayoutDocument` as `width`/`height`.
- Minimum accepted size: 50 x 50 px.
- All zone coordinates are in layout pixels, relative to the card's top-left.
- The canvas is scaled to fit the available pane; zoom is clamped to 0.2x - 2.5x (compact previews can go lower).

## Layout designer

A layout is a reusable card face. Cards reference a front layout and an optional back layout.

### Toolbar

| Control | Options |
| --- | --- |
| **Add zone** | Text, Number, Icon, Image, StaticText (shown as "Label"). |
| **Undo / Redo** | Restores the previous/next document snapshot. |
| **Edit** | Copy, Paste, Duplicate, Lock/unlock, Show/hide, Delete. |
| **Arrange** | Align and distribute the selected zones. |
| **View** | Fit card, Actual size (100%), Show grid, Snap to grid, Grid spacing (4/8/16/24/32 px), Preview variants. |
| **Export** | SVG image. |
| Card navigation | Previous/Next card when previewing a card. |

Undo history is per document and is cleared when you switch documents. Undo is disabled while **Preview variants** is on.

### Canvas interaction

| Gesture | Result |
| --- | --- |
| Click a zone | Select it; the zone inspector shows its properties. |
| Shift+click | Add/remove from the multi-selection. |
| Drag a zone | Move all selected unlocked zones. |
| Drag a corner handle | Resize. Minimum 10 px. |
| Arrow keys | Nudge 1 px (10 px with Shift). |
| Alt while dragging/resizing | Ignore snap for this gesture. |
| Click empty canvas | Clear the selection. |

Locked zones cannot be moved, resized or deleted. Hidden zones remain visible on the design canvas as outlines but are not rendered in previews.

### Zones

Every zone is a `ZoneDefinition`:

| Property | Meaning |
| --- | --- |
| `name` | Display name in the inspector and lists. |
| `kind` | `Text`, `Number`, `Icon`, `Image` or `StaticText`. |
| `fieldId` | Binds the zone to a data field (not used by `StaticText`). |
| `staticText` / `staticAssetPath` | Literal content for static zones. |
| `x`, `y`, `width`, `height` | Pixel rectangle. |
| `fontSize`, `fontAssetPath`, `color`, `backgroundColor`, `bold`, `alignment` | Text styling. |
| `templateId` | Optional nine-slice/background template. |
| `visible`, `locked` | Authoring state. |
| `layers` | Ordered container surfaces (see below). |
| `variants` | Conditional alternatives (see below). |

For `Text` and `Number` zones the value comes from the card's field value, falling back to the field default. `Icon` zones resolve an `IconDocument` by asset path or render vector layers. `Image` zones draw a texture.

### Layers

A zone can contain any number of layers, drawn in `order`. A layer has its own name, visibility, blend mode, opacity, background color, asset path, template and requirements. Layers are useful for frames, glows and card-type overlays that share one content area.

- **+ Layer** adds a layer to the selected zone.
- Each layer is a foldout in the zone inspector.
- Layers can be bound to a field (`bindingPath`), restricted to a card type (`requiredCardType`) or gated by requirements.
- `blendMode` is stored but **not applied** by the renderer; only visibility and opacity are used.
- The first matching variant's `layerId` can force a layer's visibility and opacity.

### Variants

A variant is an alternative geometry/text/asset set for a zone. Variants let one layout represent multiple visual states without duplicating the layout.

Resolution order in `CardForgeRules.Resolve`:

1. If the card has `variantId`, use the variant with that id.
2. Otherwise use the first variant whose `requirements` all match.
3. Otherwise use the zone unchanged.

A variant overrides `staticText`, `staticAssetPath`, position, size, font size, color and visibility. If `layerId` points at a layer, the resolved zone is only visible when that layer is visible, and the layer opacity is applied.

**Preview variants** in the View menu resolves variants against the currently previewed card so you can see the result. Editing is disabled in this mode.

### Templates

Zone templates are reusable background slices stored on the layout (`templates`). A template has a background asset, background color, scale mode and slice insets.

- `ZoneImageScaleMode`: `Stretch`, `Cover`, `Contain`, `Tile`, `Slice`.
- `Slice` uses `sliceLeft`, `sliceRight`, `sliceTop`, `sliceBottom` insets (default 12).
- A zone references a template by `templateId`; a missing template is a validation error.

### Requirements

Requirements gate zones, layers, variants, layouts and cards. A requirement is `fieldId` + `comparison` + `expectedValue` (+ optional message).

Supported comparisons in the editor: `Equals`, `Not equals`, `Contains`, `Greater than`, `Less than`. The runtime matcher also understands `Greater than or equal` and `Less than or equal` and compares numerically when both values parse as numbers; otherwise it compares case-insensitively.

Add requirements in the **Required when** foldout of the zone inspector, or on the layout/card record.

## Card database

The **Database** tab edits one `CardDocument`.

### Fields

- Field values are stored as `values` (`fieldId` + string `value`).
- The available fields come from `schema.json`.
- Each field has a kind (`Text`, `Number`, `Icon`, `Image`, `Boolean`), default value, help text, decimal places and prefix/suffix.
- Number and Boolean fields are validated on import and in some editors; they are stored as strings.
- Image and Icon fields show the art catalog picker.

### Art catalog

`CardForgeArtCatalog` lists texture assets under `Assets` with a search box. Selecting a texture stores its asset path on the field or zone override. Only paths beginning with `Assets/` that load as a main asset are valid.

### Card record

| Property | Meaning |
| --- | --- |
| `name` | Card name. |
| `cardNumber` | Display number; auto-assigned `001`, `002`, ... when created. |
| `layoutId` | Front layout in the active block. |
| `backLayoutId` | Optional back layout in the active block. |
| `cardType` | Free-form type used by layer `requiredCardType` filters. |
| `variantId` | Forces a specific zone variant. |
| `keywordIds` | Linked keywords. |
| `abilityIds` | Linked abilities. |
| `sourceBlockId` / `sourceCardId` | Reprint provenance. |
| `zoneOverrides` | Per-card tweaks (see below). |
| `requirements` | Card-level gating. |

### Zone overrides

A `ZoneOverride` adjusts a layout zone for one card:

| Field | Effect |
| --- | --- |
| `deltaX`, `deltaY` | Offset from the layout position. |
| `deltaWidth`, `deltaHeight` | Size delta. |
| `fontSize` | `0` inherits the layout. |
| `color` | Empty inherits the layout. |
| `textOverride` | Replaces rendered text. |
| `assetPathOverride` | Replaces the image/icon. |
| `hidden` | Hides the zone for this card. |

Overrides are matched by `zoneId`. Reprinting a card into a block with different layouts clears overrides when the layout is remapped, because the zone ids no longer apply.

### Record details

The **Card record details** panel shows the id, block, front, back and source provenance. Use it to confirm that a card is an independent instance rather than an alias.

### Preview

The card preview draws the front layout with the card's values, variants and overrides. The back can be inspected by selecting the back layout or previewing it from the layout screen.

## Schema editor

Open **Data fields** in the sidebar (or the schema toggle in the card database). The schema editor edits `SchemaDocument.fields`.

| Property | Meaning |
| --- | --- |
| `key` | Stable machine key used in imports (`field:<key>`). Must be unique. |
| `label` | Human label. |
| `kind` | Text, Number, Icon, Image, Boolean. |
| `defaultValue` | Value used when a card has no explicit value. |
| `help` | Help text. |
| `decimalPlaces` | Display precision for numbers. |
| `prefix`, `suffix` | Display decoration. |

Deleting a field does not automatically remove values from cards; validate after schema changes and clean up zones that reference the removed field.

## Blocks and editions

A block groups layouts and cards (for example a set or edition).

### Block editor

| Property | Meaning |
| --- | --- |
| `name` | Display name. |
| `code` | Short set code; used in bundle names. |
| `description` | Notes. |
| `layoutIds` | Layouts owned by the block. |
| `cardIds` | Cards owned by the block. |
| `defaultBackLayoutId` | Default back assigned to new cards. |

### Edition operations

| Operation | Behavior |
| --- | --- |
| Create block | Creates a block with code `NEW`. |
| Activate | Makes the block active for the layout/card screens. |
| Clone block | Copies layouts (renewing zone ids) and optionally cards, remapping layout and override ids and recording reprint provenance. |
| Reprint card | Copies one card into another block. If the target lacks the source layout, the card is remapped to the target's first front layout and its overrides are cleared. |
| Import layout | Copies a layout into the destination block with a new id and renewed zone ids. |
| Delete card | Removes the card id from the block and deletes its JSON. |
| Delete layout | Refused while any card in the block uses the layout as front or back. Clears the block's default back if it matches. |

### Validation

**Validate** runs `CardForgeStore.Validate(block)`. It checks:

- Block code is present and at least one layout exists.
- Default back is a back layout in the block.
- Schema keys are unique and non-empty.
- Layout dimensions are at least 50 x 50.
- Background, template, layer, variant and font assets exist.
- Zone, layer and variant fields and templates exist.
- Cards reference layouts inside the block.
- Card overrides reference existing assets.
- Keyword and ability links exist.
- Image/Icon field values point at real assets.

Export refuses to run while validation errors exist.

### Bundle builds

**Build bundle** calls `CardForgeStore.ExportBlock`. It writes an `export-manifest.json`, builds an AssetBundle for the active build target, and copies the JSON beside the bundle under `CardForgeExports/<bundleName>/<BuildTarget>/`. See [import-export.md](import-export.md).

## SVG export

**Export > SVG image** writes a text-accurate SVG of the current layout (or of the previewed card). It embeds fonts and vector icons where possible. The PNG exporter still exists but does not render text and is not wired into the current shell; use SVG for production art.

## Designer tips

- Name zones and layers before building rules; validation messages use those names.
- Use templates for frames you reuse across many zones.
- Prefer variants over duplicate zones when only text or geometry changes.
- Use per-card overrides for one-off art, not for systematic differences.
- Save before validating or exporting. Validation reads the store, not unsaved editor state.
- Zone ids are stable. Copying a layout renews zone ids so overrides from the source do not leak.

## See also

- [data-model.md](data-model.md) for every field and enum.
- [rules-and-flows.md](rules-and-flows.md) for abilities and keyword links.
- [import-export.md](import-export.md) for bulk card/layout creation.