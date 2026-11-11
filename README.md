# CardForgeStudio

CardForgeStudio is the unified Unity authoring and authoritative-runtime repository assembled from TcgStudio and TcgULtimateServer. The repository root is also the Unity project root.

## Repository layout

- `Assets` — the Unity 7 CardForge authoring studio, including cards, frame/layout composition, zones, actions, abilities, keywords, triggers, and turn-flow designers.
- `Packages` and `ProjectSettings` — Unity package manifest, local shared-package reference, and project settings.
- `Editor` — Studio documentation and generated IDE files; Unity sources now live under `Assets` at the repository root.
- `Server` — the .NET 8 authoritative game API and native WinUI management dashboard.
- `Shared` — versioned `.cardforge` contracts, JSON schemas, and validation shared by editor and server.
- `GamePackages` — a source-controlled home for intentional sample packages; generated packages remain ignored by default.
- `Tests` — package/import/runtime integration tests.
- `Documentation` — unified architecture, package format, and editor-to-server workflow.

Open the repository root `D:\CardForgeStudio` in Unity Hub with Unity `7000.0.0a16`, then use **CardForge > Dashboard** to reach the authoring, publishing, client, and learning tools. Open `CardForgeStudio.Backend.slnx` for the .NET components. Existing TcgStudio and TcgULtimateServer features remain in their respective assemblies and sibling folders inside the Unity project root.

See [Documentation/README.md](Documentation/README.md) for the integration workflow.
