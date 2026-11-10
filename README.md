# CardForgeStudio

CardForgeStudio is the unified authoring and authoritative-runtime repository assembled from TcgStudio and TcgULtimateServer.

## Repository layout

- `Editor` — the Unity 7 CardForge authoring studio, including cards, frame/layout composition, zones, actions, abilities, keywords, triggers, and turn-flow designers.
- `Server` — the .NET 8 authoritative game API and native WinUI management dashboard.
- `Shared` — versioned `.cardforge` contracts, JSON schemas, and validation shared by editor and server.
- `GamePackages` — a source-controlled home for intentional sample packages; generated packages remain ignored by default.
- `Tests` — package/import/runtime integration tests.
- `Documentation` — unified architecture, package format, and editor-to-server workflow.

Open `CardForgeStudio.slnx` for the .NET components and open `Editor` with Unity 7000.0.0a16 for authoring. Existing TcgStudio and TcgULtimateServer features remain in their original assemblies beneath those two directories.

See [Documentation/README.md](Documentation/README.md) for the integration workflow.
