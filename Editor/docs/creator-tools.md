# Creator tools

Four auxiliary windows help you get art into CardForge: Frame Composer, Icon Set Studio, Google Fonts, and Ollama AI settings. Each has its own menu item under **CardForge** and a sidebar entry in the studio.

Related: [designer-guide.md](designer-guide.md), [import-export.md](import-export.md), [file-map.md](file-map.md).

## Frame Composer

**CardForge > Frame Composer**, or **File > Extract card frame...**, or the sidebar tool.

Frame Composer turns a reference card frame image into an editable `LayoutDocument` plus PNG layer assets.

### Workflow

1. Choose or drag a frame image (PNG/JPEG). The import limit is **48 MB**.
2. The image is copied into the project and loaded as a texture.
3. Detect content regions:
   - **Detect regions locally** uses conservative geometric proposals tuned to common card frames (title, cost, artwork, type, rules, power).
   - **Analyze with Ollama Cloud** sends the image to a vision model and parses proposed rectangles.
4. Review and edit regions on the preview canvas. Regions can be moved, resized, added and removed.
5. Optionally enable **Snap AI regions to visible edges** to refine proposed boxes against nearby high-contrast frame edges.
6. Choose the destination edition (block), layout name and whether it is a card back.
7. **Save extracted layout + PNG assets**.

### Local proposals

The local detector adds six regions sized as fractions of the frame:

| Region | Kind | Suggested field |
| --- | --- | --- |
| Card name | Text | `title` |
| Cost | Number | `cost` |
| Artwork | Image | `art` |
| Card type | Text | `type` |
| Rules | Text | `rules` |
| Power | Number | `power` |

These are starting points, not image analysis. Always review them.

### AI proposals

`CardForgeOllama.AnalyzeAsync` sends the frame and a prompt that asks for 1-16 zones as JSON:

```json
{
  "zones": [
    { "name": "Title", "kind": "Text", "field": "title",
      "x": 0.07, "y": 0.04, "w": 0.74, "h": 0.09, "confidence": 0.9 }
  ]
}
```

Coordinates are normalized to a 1000 x 1000 space. `FrameGeometry.TryNormalize` converts and clamps them to image pixels, rejecting non-finite or degenerate boxes. Kinds are parsed case-insensitively and fall back to `Text`. A proposed `field` is matched to a schema key and becomes a real `fieldId`.

AI output is untrusted: it can hallucinate regions. The prompt explicitly forbids covering the entire background and requires visually localizable content, but you must review every box.

### Saving

On save, Frame Composer:

- Writes the layout into the selected block.
- Saves each region as an independent PNG under `Assets/CardForgeStudio/CardForgeData/frames/`.
- Builds a composited frame overlay with detected content slots made transparent while preserving a small border.
- Writes zone entries that reference the PNG layers.

If no block exists, create one first. The save button is disabled without a source image and a destination block.

### Related code

| File | Role |
| --- | --- |
| `Extensions/FrameComposer/CardForgeFrameComposerWindow.cs` | Window and workflow. |
| `Extensions/ImageProcessing/CardForgeImageAssets.cs` | Readable copies, cropping, PNG save/import, overlay and region export. |
| `Extensions/ImageProcessing/CardForgeFrameEdgeRefiner.cs` | Local edge refinement of rectangles. |
| `Extensions/Ollama/CardForgeOllama.cs` | Vision request/response. |
| `Runtime/DesignLogic/FrameGeometry.cs` | Normalization and clamping. |

## Icon Set Studio

**CardForge > Icon Set Studio**, **File > Import icon set...**, or the sidebar tool.

Icon Set Studio slices an atlas or imports a folder of images and registers each icon as an `IconDocument`.

### Atlas mode

1. Choose a **Sprite atlas** texture (or **Import image...** to copy a PNG/JPEG into the project; limit **48 MB**).
2. Set **Columns** and **Rows** (1-32 each, up to 1024 cells).
3. Set a **Default prefix** for generated names.
4. Toggle **Trim transparent pixels**.
5. Uncheck blank cells and rename individual icons.
6. **Import selected PNG icons**.

Each cell is exported as an independent PNG under `Assets/CardForgeStudio/CardForgeData/icon-sets/`, and an `IconDocument` is saved with its asset path. Names default to `<prefix>-001`, `<prefix>-002`, and so on.

### Folder mode

**Import PNG folder...** reads `.png`/`.jpg`/`.jpeg` files from a single folder:

- Maximum **1024 files**.
- Maximum **24 MB per file**.
- Writes an `IconSetManifest` with the set name and entries, plus one PNG and one icon document per file.

### Manifest

```json
{
  "name": "Symbols",
  "entries": [
    { "name": "symbol-001", "assetPath": "Assets/CardForgeStudio/CardForgeData/icon-sets/..." }
  ]
}
```

### Using icons

Icons appear in the **Icons** library. Bind an icon to a card through an `Icon`-kind schema field, or to a zone as a static asset. Icons with vector `layers` are rendered procedurally by `CardForgeIconPainter`; icons with only an `assetPath` load the PNG.

### Vector shapes

`CardForgeIconPainter` supports: `Circle`, `Ring`, `Square`, `Diamond`, `Triangle`, `Star`, `Bolt`, `Shield`, `Cross`. Layers have visibility, opacity, color, normalized position, size and rotation.

## Google Fonts

**CardForge > Google Fonts Library**, **File > Google Fonts importer**, or the sidebar tool.

The importer downloads open-licensed TTF/OTF families from the official `google/fonts` repository or imports a local font.

### Online import

1. Enter a family name (for example `Inter`, `Cinzel`, `Redaction`).
2. **Import Google font**.
3. The tool queries the GitHub contents API, finds TTF/OTF files and a license file, downloads them, and writes:
   - `Assets/CardForgeStudio/CardForgeData/fonts/<family>/<font files>`
   - `Assets/CardForgeStudio/CardForgeData/fonts/<family>/LICENSE.txt` when available
   - `Assets/CardForgeStudio/CardForgeData/fonts/<family>/manifest.json`

The manifest records the family, source and license path.

### Local import

**Import local TTF / OTF...** copies a font into `Assets/CardForgeStudio/CardForgeData/fonts/` and refreshes the asset database. Fonts must be **100 bytes to 20 MB**. You are responsible for redistribution rights.

### Security

- Only HTTPS URLs on `raw.githubusercontent.com`, `github.com` or the GitHub API host are allowed.
- Downloads are capped at 20 MB.
- A network connection is required for online imports.

### Using fonts

Set a zone's `fontAssetPath` in the zone inspector. Validation checks that the font asset exists. Fonts are collected into the export manifest and bundled with the block.

### License

Google Fonts are open-licensed, but terms vary. The importer saves an available license; review it before distributing a game.

## Ollama AI settings

**CardForge > AI Provider Settings**, **File > Ollama AI settings**, or quick open.

| Setting | Default | Storage |
| --- | --- | --- |
| Use Ollama Cloud | on | `EditorPrefs` `CardForge.AI.Ollama.Cloud` |
| Vision model | `gemma4:31b` | `EditorPrefs` `CardForge.AI.Ollama.Model` |
| API key | none | `OLLAMA_API_KEY` environment variable or `SessionState` only |

Endpoints:

- Cloud: `https://ollama.com/api/chat`
- Local: `http://localhost:11434/api/chat`

The key is **not** written to project files or `EditorPrefs`. A pasted key lives in `SessionState` and survives domain reloads only for the editor session. Prefer the environment variable for shared machines.

Requests use `stream: false` and `format: "json"`. Responses are parsed defensively; invalid or unusable output raises a `FormatException` with guidance to try a clearer image or a different vision model.

### Choosing a model

Use a vision-capable model for frame analysis. The model name is free text; the default may not exist in your Ollama account. Local models must be pulled and running first.

## Image processing details

`CardForgeImageAssets` centralizes texture work:

| Capability | Notes |
| --- | --- |
| Readable GPU copies | Copies a texture through a render target so pixels can be read. |
| PNG save/import | Writes PNGs under `Assets/` and imports them. |
| Safe cropping | Clamps rectangles to texture bounds. |
| Icon import with trim | Crops transparent borders when requested. |
| Frame overlay and region export | Builds the composited frame and per-region PNGs. |
| JPEG encoding | Used when a vision endpoint needs a smaller payload. |
| Safe names | Slugifies file and asset names. |

`CardForgeFrameEdgeRefiner` is pure local logic: for each proposed rectangle it searches nearby high-contrast edges and nudges the box toward them. It never calls the network.

## See also

- [designer-guide.md](designer-guide.md) for using extracted layouts and icons.
- [troubleshooting.md](troubleshooting.md) for AI and download failures.
- [testing.md](testing.md) for the creator-tool tests.