# CardForgeStudio

CardForgeStudio is a Unity-based digital TCG/CCG creator and game client with **one supported server runtime: the Unity Dedicated Server**. The former ASP.NET Core / WinUI server has been retired.

## Start

1. Open `D:\CardForgeStudio` in Unity Hub (project's pinned Unity version is in `ProjectSettings/ProjectVersion.txt`).
2. Use **CardForge > Generate Sample Game** to author Astral Rift and stage its rules and art.
3. Use **CardForge > Server > Build Native Windows Dedicated Server**. Windows Dedicated Server build support must be installed.
4. Run `powershell -ExecutionPolicy Bypass -File Demo/Start-CardForgeDedicatedServer.ps1 -NoClient`.
5. Open `Assets/CardForgeStudio/CardForgeClient/Scenes/TcgClient.unity` and press Play, or use **CardForge > Play Sample Game**.

Play is gated on the native host's `/api/health` response. No .NET gateway, old server fallback, or separate management dashboard is started.

## Structure

- `Assets/CardForge` — card design, frame composition, action/trigger/turn/ability editors, game package exporter, board and zone editor.
- `Assets/CardForgeDedicatedServer` — Unity server host, native social/account/store/content/matchmaking API, authoritative simulation integration, build editor tool.
- `Assets/CardForgeClient` — UI Toolkit client, network protocol, remote artwork and scene Addressables loader.
- `Assets/StreamingAssets/CardForge` — built-in rules and initial assets shipped **with the server**, not the client.
- `Shared/CardForge.Simulation` — portable .NET Standard 2.1 rule engine source, compiled by Unity through `CardForge.Simulation.asmdef`.
- `Shared/CardForge.GamePackages` — .cardforge package manifest and validation contracts.
- `GamePackages` — exported versioned content archives.
- `Demo` — native Unity server startup script.
- `Tests/CardForge.NativeServer.Tests`, `Tests/CardForge.Simulation.Tests` — native API and portable simulation regression suites.
- `Documentation` — current architecture, publishing, operations, format and demo guides.

## Test without launching Unity

```powershell
dotnet run --project Tests/CardForge.Simulation.Tests/CardForge.Simulation.Tests.csproj
dotnet run --project Tests/CardForge.NativeServer.Tests/CardForge.NativeServer.Tests.csproj
dotnet build Shared/CardForge.DedicatedServer.Validation/CardForge.DedicatedServer.Validation.csproj
```

The last command checks native host C# against installed Unity assemblies; it **does not replace** the Unity Dedicated Server player build or a real network smoke test.

## Server scope

Supported matches: **1v1, 1v2, 1v3 and 1v4**, with AI replacement for missing opponents. Matchmaking takes rating and computed deck strength into account. The server does not impose a global connection-count setting, but all deployments are bounded by CPU, RAM, bandwidth and operating-system limits; request queue backpressure is intentional.

Server-side game state, accounts, decks, inventories, virtual-credit purchases, social data and messages persist in MongoDB, maintained by the native Unity host. The startup script prepares a local MongoDB Community Server if one is not already available; production can use CARDFORGE_MONGODB_URI. Artwork is downloaded by clients on demand. Remote Addressables catalogs/scenes require an actual remotely built catalog and matching bundles; a placeholder scene index alone is not a completed remote-content deployment.

Read [Documentation](Documentation/README.md) for setup, security and limitations.
