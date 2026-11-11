# Native Unity Dedicated Server

## Build and launch

Unity version: check `ProjectSettings/ProjectVersion.txt`. Install Windows Dedicated Server build support. Then use **CardForge > Server > Build Native Windows Dedicated Server**; the builder creates the server scene and builds `Builds/DedicatedServer/CardForgeServer.exe`.

```powershell
powershell -ExecutionPolicy Bypass -File Demo/Start-CardForgeDedicatedServer.ps1 -NoClient
```

The launcher refuses to continue without that executable and never starts an alternate backend. Demo mode uses local JSON persistence and never attempts MongoDB installation, even if an old MongoDB URI exists in the launcher environment. Pass -Production to require MongoDB; a production launch checks or installs a local MongoDB service, or uses a separately managed instance selected through CARDFORGE_MONGODB_URI. Windows package installation may require administrator approval. Set `CARDFORGE_MONGODB_URI` to use a separately managed MongoDB instance; remote URIs skip local installation. The native process handles all API routes and game rules, typically on `http://127.0.0.1:5168`. Check `GET /api/health`. The server accepts `--port=5168`, `--bind=localhost` (default), and optional `--demo-mode`. Binding to `+` is allowed only without demo mode and requires OS URL reservation, firewall and reverse proxy configuration.

## Identity, security and storage

- Local demo mode seeds a loopback-only `demo` account and sample content. Do not expose it publicly.
- Production can bootstrap its first administrator with `CARDFORGE_ADMIN_HANDLE` and `CARDFORGE_ADMIN_PASSWORD`. Store credentials securely and unset the password variable after bootstrap.
- Accounts are salted with PBKDF2-SHA256 and sessions use cryptographically random bearer tokens. Tokens remain server memory only and expire; a restart requires clients to sign in again.
- Production stores users, decks, inventories, wallets, friends, messages, events and matches in MongoDB collections; local demo mode uses user-scoped JSON persistence. Player records are indexed by handle; decks by owner; messages by recipient and time; matches by ruleset and completion status. Content rules and artwork remain under `Application.persistentDataPath/CardForge/Content`. Back up MongoDB and this content directory.
- Rule and art publication endpoints require an admin token. Art uploads check size, SHA-256 and a restricted content type. Live rules cannot be replaced while unfinished games use that pack.
- Use an HTTPS reverse proxy for Internet play. Test rate limiting, backup/restore, monitoring, proxy headers and disaster recovery before deployment; these are not supplied as production guarantees.

## API

The client retains the `/api` interface: `health`, `auth/register`, `auth/login`, `auth/session`, `rules`, `games`, `games/{id}/client-view`, `games/{id}/actions`, `matchmaking/queues`, `matchmaking/join`, `matchmaking/result`, `decks`, `inventory/{id}`, `store/products`, `store/wallet`, `store/purchase`, `friends`, `messages`, `events`, `assets/manifest`, and `assets/{id}`.

Admin content publishing uses `POST /api/admin/rules/import` with a normalized `rulePack`, or `POST /api/admin/assets/{id}` with base64, contentType and SHA-256.

## Game format and scaling

Supported seat counts are exactly 2–5: **1v1, 1v2, 1v3 and 1v4**. Team 0 has one player; team 1 has the selected number of opponents. AI fills seats after the matchmaking wait threshold and scales difficulty with player skill and deck strength.

There is no configured maximum global client connection count, but the server has bounded request payloads, a finite request-processing budget per Unity frame, and queue backpressure. These protect service health. Real-world concurrent load is limited by machine capacity and needs measurement. Gameplay is main-thread-authoritative; large-scale operation will require storage and match-sharding work.

## Dynamic content

Server artwork has a manifest with SHA-256 and version-verified client caching. To change card images, upload new server assets without rebuilding the client.

The remote scene loader can use a dynamic Addressables catalog but a usable remote scene **requires** the Addressables content build, scene address/index and corresponding bundles hosted by the server. The native API can host catalog/bundle files; the content pipeline still needs to produce and deploy them. Do not claim scene hot updates from the index alone.

## Validation

```powershell
dotnet run --project Tests/CardForge.Simulation.Tests/CardForge.Simulation.Tests.csproj
dotnet run --project Tests/CardForge.NativeServer.Tests/CardForge.NativeServer.Tests.csproj
dotnet build Shared/CardForge.DedicatedServer.Validation/CardForge.DedicatedServer.Validation.csproj
```

The last project is a developer-only compile check bound to the locally installed Unity assemblies; final verification requires a **Unity Dedicated Server player build**, network smoke test and editor Play test.

## Windows graphical client

Build from Unity with CardForge > Demo > Build Windows Client, then run
Demo/Start-CardForgeDedicatedServer.ps1 without -NoClient to reuse the native
server and launch the Windows client. The client is built as
StandaloneBuildSubtarget.Player, not Dedicated Server. The client build generates
a UI Toolkit PanelSettings asset and runtime theme under
Assets/CardForgeStudio/CardForgeClient/Resources/CardForgeDemo. The launcher passes --server=
to the client and saves its log in Demo/.state/cardforge-client.log.
The header has a Retry connection control.