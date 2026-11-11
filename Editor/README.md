# CardForge Studio (TcgStudio)

CardForge Studio is a Unity-native, **editor-only** authoring suite for building **digital** trading card games. It provides a visual card/layout designer, a JSON-backed content library, a turn-flow and rules authoring system, and creator tools for extracting frames, slicing icon atlases, importing fonts, and analyzing card art with an optional vision model.

**JSON is the source of truth.** Every card, layout, block, icon, trigger, action, ability, keyword, and turn flow is a plain JSON document under `Assets/CardForgeStudio/CardForgeData/`. Unity assets (textures, fonts) provide renderable art. There is no hidden ScriptableObject database.

> **Status:** proof of concept. CardForge authors content and exports it; it is **not** a complete game runtime or a rules engine for production play. See [Limitations](#limitations).

---

## Requirements

| Requirement | Version / notes |
| --- | --- |
| Unity Editor | **7000.0.0a16** (Unity 7 alpha) — pinned in `ProjectSettings/ProjectVersion.txt` |
| Render pipeline | Universal Render Pipeline `17.7.0` |
| Input | Input System package `7.0.0` (new input handler only) |

---

## Quick start

1. Clone the repository and open its root folder (`CardForgeStudio`) as a Unity project with Unity `7000.0.0a16`.
2. Let Unity finish the first import (this recreates the ignored `Library/` folder).
3. Open **CardForge > Dashboard**, then choose **CardForge Studio**.
4. On a fresh checkout, `CardForgeStore.Reload()` seeds:
   - a seven-field schema (`title`, `cost`, `type`, `rules`, `power`, `art`, `faction`),
   - a starter **Core Set** block (`CORE`) with three layouts (two fronts, one back) and one card,
   - a starter six-phase **Standard turn** flow.
5. Pick a library in the sidebar and start authoring. Press **Ctrl/Cmd+K** to jump anywhere.

Full setup details: [docs/getting-started.md](docs/getting-started.md).

---

## Feature highlights

| Area | What it does |
| --- | --- |
| Layout designer | Canvas editor with zones, layers, variants, templates, SVG export. |
| Card database | Field values per schema field, zone overrides, art/icon binding, preview. |
| Blocks & editions | Clone blocks, reprint cards, validate, build AssetBundle exports. |
| Rules authoring | Turn flows, triggers, actions, abilities, keywords with validation. |
| Creator tools | Frame Composer, Icon Set Studio, Google Fonts, Ollama AI settings. |
| Import/export | Spreadsheet/JSON table import, StudioPackage exchange, AssetBundle export. |
| Runtime reader | `CardForgeRuntimeReader` loads exported bundles in player builds. |

---

## Repo layout

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

Unity generates `Library/`, `Logs/`, `Temp/`, `UserSettings/`, `*.csproj`, `*.slnx` — none are tracked.

---

## Commands

| Where | What |
| --- | --- |
| `CardForge > Dashboard` | Central entry point for the Studio, creator tools, publishing, client setup, server, lessons, and templates. |
| `CardForgeBatchBuild.BuildAllActive` | CI: export every block for the active build target. |

---

## Limitations

- **No runtime game engine.** CardForge authors and exports content; your game owns turns, combat, zones, decks, and card instances.
- **Reference response engine only.** `CardForgeRuleEngine` handles LIFO response windows; it does not advance states or evaluate conditions.
- **No text rendering in PNG export.** SVG export is text-accurate; the legacy PNG exporter does not render text.
- **Blend modes stored, not applied.** They appear in JSON but the renderer does not use them.
- **Keywords have no library screen.** Edit them via the Abilities designer keyword toggle or quick open.
- **AI output is untrusted.** Frame Composer vision results must be reviewed.
- **Unity 7 alpha.** Expect UI Toolkit API surface to shift between versions.

---

## Documentation

Start with the [project README](#) then drill into [docs/README.md](docs/README.md) for the full index: setup, architecture, studio UI, designer guide, rules, data model, import/export, creator tools, runtime integration, testing, contributing, troubleshooting, and glossary.

## See also

- [docs/getting-started.md](docs/getting-started.md) — setup and first run.
- [docs/architecture.md](docs/architecture.md) — assembly boundaries and data flow.
- [docs/file-map.md](docs/file-map.md) — which file does what.
- [docs/contributing.md](docs/contributing.md) — repo conventions and pull request checklist.