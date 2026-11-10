# `.cardforge` package format

A game package is a ZIP archive with `manifest.json` at its root. Package schema `1.0` declares immutable `packageId` + semantic `version` identity, its compiled rule pack, victory definitions, a content summary, and every other file with content kind, byte length, and SHA-256.

Required content kinds are `cards`, `zones`, `frames`, `assets`, `actions`, `abilities`, `keywords`, `turn-flow`, `rules`, and `victory`. Triggers, block metadata, and the editor field schema are also carried when available.

Canonical executable definitions are:

- `rules/rule-pack.json` — schema-v2 zones, resources, phases, actions, cards, and abilities consumed by the authoritative engine.
- `rules/victory.json` — declarative resource-threshold, empty-zone, last-player-standing, or turn-limit outcomes.

All other entries preserve editor intent and visual assets so a release is portable and auditable. Paths are forward-slash relative paths. Absolute paths, traversal segments, undeclared entries, duplicate paths, oversized archives, and hash mismatches are rejected.

Schemas live under `Shared/CardForge.GamePackages/Schemas`.
