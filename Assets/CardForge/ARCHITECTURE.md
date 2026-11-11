# CardForge Studio — Digital TCG Authoring Architecture

CardForge is a Unity-native editor for creating **digital** collectible/trading card games. JSON is the authoritative project data; Unity assets provide renderable frames, icons, images, and fonts. The authoring tools are intentionally separate from the game runtime.

This POC does **not** add physical print layouts, mass print exports, or spreadsheet batch-editing features.

## Documentation

The long-form documentation lives in [`docs/`](../../docs/README.md): setup, architecture, studio UI, designer guide, rules, data model, import/export, creator tools, runtime integration, testing, contributing, troubleshooting and a glossary. This file remains the short architecture brief; when it and the implementation disagree, treat the source code as authoritative and update both.

## Assemblies and dependency direction

| Assembly definition | Purpose | Depends on |
| --- | --- | --- |
| `Runtime/CardForge.Runtime.asmdef` | Serializable cards, schemas, layouts, abilities, turn flows, rules and state machine contracts | Unity Runtime |
| `Runtime/DesignLogic/CardForge.DesignLogic.asmdef` | Pure normalized-frame geometry, atlas slicing, safe naming, and graph maths (pan/zoom, edge curves, layered layout, connection rules) | Runtime |
| `Editor/CardForge.Editor.asmdef` | Core designer, JSON store, card rendering, validation, bundle builds and the shared node graph canvas | Runtime, DesignLogic |
| `Editor/Extensions/Theme/CardForge.StudioTheme.Editor.asmdef` | Shared persisted theme controller | Unity Editor UI Toolkit |
| `Editor/Extensions/ImageProcessing/CardForge.ImageProcessing.Editor.asmdef` | Texture readback, slicing, PNG exports, frame overlays | Runtime, DesignLogic, Editor |
| `Editor/Extensions/Ollama/CardForge.Ollama.Editor.asmdef` | Ollama provider, vision result parser, credentials and settings | ImageProcessing |
| `Editor/Extensions/FrameComposer/CardForge.FrameComposer.Editor.asmdef` | Reference image analysis, editable zone proposals and layout publishing | Runtime, DesignLogic, Editor, Theme, ImageProcessing, Ollama |
| `Editor/Extensions/IconSets/CardForge.IconSets.Editor.asmdef` | PNG atlas/folder import, tile review and reusable icon JSON | Runtime, DesignLogic, Editor, Theme, ImageProcessing |
| `Editor/Extensions/GoogleFonts/CardForge.GoogleFonts.Editor.asmdef` | Google Fonts and local font import with licensing files | Theme, ImageProcessing |
| `Editor/UI/CardForge.EditorUI.asmdef` | Studio shell, library views, inspectors, creator-tool navigation, integrity dashboard | Editor and all creator-tool modules |

The editing assemblies are **Editor-only**. Game builds must reference `CardForge.Runtime` and optionally `CardForge.DesignLogic`, not EditorUI. Keep references one-directional to avoid assembly cycles. Public entry points include `CardForgeRuntimeReader.Load`, `CardForgeRuntimeReader.CreateRuleEngine`, `CardForgeRules.BuildStateMachine`, `CardForgeOllama.AnalyzeAsync`, `FrameGeometry.TryNormalize`, `AtlasGeometry.Cell`, `CardForgeImageAssets.SaveFrame`, and the three editor-window `Open` methods.

## New digital-first authoring tools

**Frame Composer** — `CardForge > Frame Composer` or Studio > Creator tools. Import a PNG/JPEG frame, run the optional Ollama Cloud analysis, or generate conservative manual region suggestions. The optional local edge-alignment pass refines Ollama's region boundaries against visible frame borders without sending another request. Drag or edit each zone, assign schema fields, then save a reusable layout. Assets are written under `Assets/CardForgeData/frames/<layout>-<id>/` as `reference.png`, `frame-overlay.png`, individual `regions/*.png`, and layout/zone-template JSON. AI estimates **must be reviewed** before saving.

**Ollama** — Defaults to direct Ollama Cloud at `https://ollama.com/api/chat` using `gemma4:31b`. Requires a vision-capable model and a valid API key. Set `OLLAMA_API_KEY` in the OS environment, or paste a key in `CardForge > AI Provider Settings` for the current editor session only. The session key is stored in Unity `SessionState`, never in project assets or `EditorPrefs`. Local API mode uses `http://localhost:11434/api/chat` and does not require an API key. Images are transmitted only when **Analyze** is clicked. Model responses are untrusted suggestions, not executable game rules.

**Icon Set Studio** — `CardForge > Icon Set Studio`. Import an existing atlas PNG/JPEG and specify rows/columns, select or omit cells, rename icons, trim transparent padding and export PNGs; or import all images in a folder. A `manifest.json` preserves the icon set. Individual icons are saved as Unity PNG assets and registered in the central CardForge icon library. Icons can then be bound to card fields, layout zones, and icon previews.

**Google Fonts** — `CardForge > Google Fonts Library`. Search for a Google Fonts family by name; the importer checks the official `google/fonts` source repository under `ofl`, `apache`, or `ufl`, saves available TTF/OTF files and source license under `Assets/CardForgeData/fonts/`, and imports them as Unity Font assets. Alternatively import a local TTF/OTF file. Use the **Type family** picker in Layout Designer to assign a font to a zone; fonts are included in digital AssetBundle exports and embedded in single-card SVG exports. Review each font license for redistribution obligations.

**Project health** — Studio navigation or File > Validate project references. Walks blocks, turn flows, triggers, actions and abilities; reports broken links and structural problems and opens the relevant editor.

## Navigation and shortcuts

- **Quick open** — `Ctrl/Cmd+K` (or the sidebar button) opens a searchable palette: go to any screen, create any document, run a studio tool, reopen one of the last eight documents, or jump to any layout, card, icon, flow, trigger, action, ability or block by name. Entries are built in `Editor/UI/Scripts/Navigation/QuickOpenEntries.cs`; recents are kept per user in `EditorPrefs` by `RecentDocuments`.
- **Library screens** — `Ctrl/Cmd+F` focuses search (`Esc` clears it; search is debounced), `Delete` removes the selection, `Ctrl/Cmd+D` duplicates it. Deleting always asks for confirmation.
- Screen titles, sidebar order and "New ..." nouns live in `StudioScreens`; add a new screen there first.
- Stylesheets: `QuickOpen.uss` styles the palette, `Motion.uss` holds transitions and keyboard focus states. Both use only theme tokens.

## Graph canvas

Turn flows, action graphs and ability graphs share one canvas, `Editor/Graph/GraphCanvasView.cs`; `CardForgeWorkflowCanvas` and `CardForgeResolutionGraphCanvas` only map their documents to `GraphNodeData` / `GraphLinkData` and forward events.

- **Mouse:** drag the background (or the middle button) to pan, wheel to zoom around the cursor, drag a node to move it (hold Alt to skip the 10 px snap), drag from a node's right dot onto another node to connect, double-click the background to add a node, click a line to select it.
- **Keyboard:** `F` fits everything, `0` resets zoom, arrow keys nudge the selected node (Shift for larger steps), `Delete` removes the selected line, `Esc` clears the selection. The toolbar also offers Fit, zoom and Tidy (layered left-to-right layout that wraps long chains into rows).
- **Persistence:** pan and zoom are remembered per document for the editor session (`GraphViewMemory`), so redrawing the designer keeps your place.
- **Logic:** maths lives in `Runtime/DesignLogic/Graph*.cs` and is covered by `Graph*Tests`; the canvas itself only wires input to it. Colours come from `--cf-graph-*` custom properties in `USS/Graph.uss`, and the graph stays a dark stage in both themes.

## Testing

Unity Test Runner > **EditMode** contains:

- `CardForge.DesignLogic.Tests` — no file/network/editor dependencies: valid/invalid AI rectangles, bounds clamping, lossless uneven sprite atlas slicing, safe asset naming, graph layout (chains, branches, cycles, wrapping, no overlap), pan/zoom/fit maths, edge hit testing and connection rules.
- `CardForge.CreatorTools.Tests` — no network or user-data writes: Ollama response fixtures, font download-host restrictions, image cropping.
- `CardForge.Tests` — existing validation/import tests.

To compile independently, open the generated `CardForge.DesignLogic.csproj`, `CardForge.Ollama.Editor.csproj`, etc., or run `dotnet build CardForge.EditorUI.csproj` after Unity has regenerated IDE project files from the asmdefs. The asmdefs (not generated `.csproj` files) define canonical dependencies. Run EditMode tests in Unity to execute NUnit tests; a successful C# build is not the same as a completed test run.

## Source data and release boundaries

- User-authored JSON: `Assets/CardForgeData/`.
- Keyword JSON bundles reusable ability definitions under a title; card records reference keyword IDs for `[keyword]` rules text and can also reference card-specific ability IDs.
- Source-card layouts, zone dimensions, per-card override data and stored IDs remain independent of EditorWindow state.
- UI theme: `Editor/UI/USS/StudioPolish.uss` provides the shared screen styles; `ThemeController` persists theme preference.
- Light theme: the base sheets keep their dark literal colours, and `Editor/UI/USS/Light/*.uss` re-colours the same rules when `ThemeController` puts `cf-theme-light` on the studio root. Canvases painted from C# (card canvas, turn-flow and resolution graphs), the card preview tiles and the art banners stay dark on purpose. When a dark rule changes, update its twin in `Light/`; new rules should use the `--cf-color-*` tokens so no twin is needed.
- Studio views and tool windows only create assets after explicit import/save actions.
- Future game runtimes must implement trusted execution of effect/condition strings; visual graph definitions and state-machine export are data contracts, not a secure gameplay interpreter. The tools validate links but do not claim to simulate an entire digital TCG match.
- An Ollama Cloud request requires network access and a configured account; importing a font from Google Fonts also requires network access. These are **not** automatically fetched on project open.

## Extending modules safely

Place new pure algorithms in `Runtime/DesignLogic`, image transformations in `Editor/Extensions/ImageProcessing`, provider requests in `Editor/Extensions/Ollama`, and editor interaction in the appropriate extension assembly. Add public XML comments for callable APIs and an EditMode fixture for edge cases. Only the top-level studio should reference all editor modules. Never add references from Runtime or lower-level extension assemblies back to `EditorUI`.
