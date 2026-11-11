# CardForgeStudio architecture — native-only

## Process and ownership

**One server executable:** `Builds/DedicatedServer/CardForgeServer.exe`, built with Unity's `StandaloneBuildSubtarget.Server`. It hosts `DedicatedServerHost` and `NativeServerApi` within the Unity process; it does not launch `dotnet` or proxy gameplay through another server.

`CardForge.Simulation.asmdef` references portable, testable `Simulation.Engine.cs`, `Simulation.Models.cs`, `Simulation.Bots.cs` and `Simulation.Protocol.cs` sources. The same source builds as .NET Standard 2.1 for tests. The Unity host owns the runtime match map, applies all actions and validates zones, resource costs, phase legality, triggers, abilities and outcomes. Clients never execute authoritative rules.

`NativeAccounts` persists accounts, salted PBKDF2 password hashes, inventories, decks, virtual-credit wallets, friend relationships, messages, products and events. `NativeServerApi` provides the authenticated HTTP contract. Match snapshots are stored separately. The native matchmaker supports 1v1, 1v2, 1v3 and 1v4; only the first seat is the solo side. AI fills unoccupied opponent seats after the queue wait interval.

## Editor and publishing

The Unity editor's CardForge tooling defines cards, data zones, nonphysical resource pools, board placements, rules actions, triggers, turn phases and victory settings. The `.cardforge` ZIP archive is an **authoring/distribution format**. `NativeContentPublisher` reads its rules and victory definitions, converts dictionary costs and parameters into the portable runtime contract, and stages rules and card images in `StreamingAssets`.

For a running local native demo instance, editor publishing uses authenticated `POST /api/admin/rules/import` and `POST /api/admin/assets/{id}`. Rules replacement is rejected while unfinished matches use that ruleset. Assets may change independently of client releases.

## Client and remote delivery

The UI Toolkit client connects to the native REST API for accounts, game views, actions, decks, social screens, store and events. Sensitive card identities are redacted in opponent hidden zones. Artwork uses metadata and SHA-256 checked per-asset downloads/caching. The Addressables scene loader supports server-hosted catalogs when a valid index, catalog and matching bundles have actually been published.

## Limits and precautions

- No explicit total player-connection cap; concurrent service capacity depends on host resources.
- Request size and pending queue backpressure are safety controls, not a player cap.
- Player data and match state now use the official MongoDB C# driver and indexed BSON collections. Production deployments still need MongoDB authentication, backups, load testing, TLS termination, monitoring and authenticated deployment tooling.
- Gameplay is on the Unity main thread; network request parsing is asynchronous and queued for simulation. Plan worker/shard architecture before scaling to very high concurrency.
- The existing test suites verify portable simulation and native request handling; they are not substitutes for a Unity player build and deployment smoke test.
