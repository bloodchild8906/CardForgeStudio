# TcgULtimateServer

A native WinUI-managed C#/.NET 8 multiplayer trading-card-game server. It ships with a system-neutral rules engine, a visual content studio, JSON import/export, token auth, inventories, rankings, SignalR game/chat events, and a deterministic bot framework.

## Solution layout

Open `TcgULtimateServer.slnx` to work with the complete solution:

- `src/TcgULtimateServer.Api`: authoritative API, rules engine, real-time hubs, security, and asset delivery.
- `src/TcgULtimateServer.App`: native WinUI 3 management dashboard and local API process host.
- `scripts`: development launch tooling.
- `artifacts`: generated publish output (created on demand and safe to remove).

## Documentation

The complete operator and developer documentation is available in [`docs/README.md`](docs/README.md):

- [Getting started](docs/getting-started.md)
- [Connection tutorials](docs/connection-tutorials.md)
- [Unity client tutorial](docs/unity-client-tutorial.md)
- [Ready-to-open Unity project template](templates/TcgUnityClient/README.md)
- [Communications protocol](docs/communications-protocol.md)
- [REST API reference](docs/api-reference.md)
- [Security and deployment](docs/security-and-deployment.md)
- [Management feature documentation](docs/features/README.md)
- [Rules and visual content studio](docs/features/rules-and-content-studio.md)

## Run

Launch the native WinUI management dashboard (recommended):

Double-click `TcgULtimateServer.cmd`, or run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Start-TcgULtimateServer.ps1
```

The launcher performs an incremental build so it never opens a stale dashboard. The WinUI app starts the local API, waits for its HTTPS health check, displays the service state, and stops the managed server when the dashboard closes.

The redesigned management console groups tools into Monitor, Content & Runtime, Player Operations, Live Operations, and Governance workspaces. It includes live service health, a content catalog, rankings, a read-only API explorer, game termination controls, managed content packs, rule-pack and workflow inventories, a workflow runtime, file uploads, detailed player intelligence, account and message moderation, audit-log review, AI bot management, LiveOps controls, store and economy configuration, automated early-warning signals, reports, and archive export. Administrator-only navigation appears after an administrator signs in; moderators receive the restricted account-management tools allowed by the server.

Starting the server directly in Development also opens the WinUI dashboard:

```powershell
dotnet run --project src/TcgULtimateServer.Api/TcgULtimateServer.Api.csproj
```

Publish the native WinUI 3 client as one executable:

```powershell
dotnet publish src/TcgULtimateServer.App/TcgULtimateServer.App.csproj -p:PublishProfile=win-x64
```

The output is `artifacts/publish/win-x64/TcgULtimateServer.exe`. There is no browser UI; the server root intentionally returns `404` and only exposes API/SignalR endpoints. The dashboard uses HTTPS for REST plus WSS for SignalR.

## CardForgeStudio game packages

The preferred release boundary is a `.cardforge` package published by the unified Unity editor. The native **Game packages** workspace imports the archive, displays every immutable version, activates a selected version, downloads the original release, and supports rollback by activating an earlier version.

Import validates normalized ZIP paths, expansion limits, manifest/schema version, required content kinds, declared byte lengths, every SHA-256, the compiled schema-v2 rule pack, and declarative victory definitions. Activation is explicit and is blocked while unfinished games use the same game definition.

Validated archives and active-version pointers are stored under `data/game-packages` beside the server by default and restored on startup. Set `CARDFORGE_GAME_PACKAGE_STORE` to an approved durable absolute directory in production.

```powershell
curl -H "Authorization: Bearer <token>" -F "file=@core-1.0.0.cardforge" -F "activate=true" https://localhost:5001/api/admin/game-packages/import
```

Raw JSON rule-pack upload remains available for compatibility and targeted development.

## Rule-pack upload

Rule packs can be uploaded directly from **Upload center** in the WinUI dashboard. The endpoint is administrator-only and requires a bearer token:

```powershell
curl -H "Authorization: Bearer <token>" -F "file=@rules.json" https://localhost:5001/api/rules/upload
```

Schema version 2 rule packs define `zones`, `resources`, `phases`, `actions`, and generic `cards` with optional custom properties and abilities. The authoritative server validates references, conditions, effects, costs, phases, and player limits. Existing version 1 packs are upgraded for compatibility. Operators can also create, validate, publish, import, and export packs in **Visual content studio**. See [`docs/examples/system-neutral-rule-pack.json`](docs/examples/system-neutral-rule-pack.json).

## Generic state machines

The server also accepts arbitrary JSON state-machine configurations. A configuration contains `id`, `name`, `version`, `initialState`, a `states` array, and `transitions`; each transition contains `event`, `from`, and `to`, with optional JSON `data` and configuration `metadata`.

Upload a configuration with `POST /api/state-machines/configurations/upload` using a multipart field named `file`. Authenticated clients can create an instance with `POST /api/state-machines` and `{ "configurationId": "workflow", "data": { ... } }`, inspect it with `GET /api/state-machines/{id}`, and apply an event with `POST /api/state-machines/{id}/transitions` and `{ "event": "start", "data": { ... } }`. The server validates that every transition is legal for the current state and records a generic event history; uploaded data is treated as data and never executed as server-side code. Real-time clients can use `/hubs/state-machine`, call `Join`, and subscribe to `stateMachineState` updates.

Configurations may optionally include `schemaVersion`, `metadata`, `stateMetadata`, and `terminalStates`. Transitions may include `guards`, `effects`, `data`, and `metadata`. A guard is a JSON object whose properties must equal values in the instance `context` for the transition to be available, for example `{ "role": "admin" }`. An effect is a JSON object merged into the context after the transition, for example `{ "approved": true }`. Multiple transitions may use the same event and source state when their guards distinguish them. Reaching a terminal state marks the instance as finished. This declarative model supports workflows, approvals, sessions, games, and other stateful processes without requiring a new server implementation for each configuration.

Guard and effect keys support dot-separated paths. Guard values can use `$exists`, `$equals`, `$in`, `$gt`, `$gte`, `$lt`, and `$lte`, for example `{ "account.role": { "$in": ["admin", "reviewer"] }, "cart.total": { "$gte": 100 } }`. Effects support replacement by default and `$increment`, `$append`, or `$remove`, for example `{ "cart.total": { "$increment": 10 }, "audit.tags": { "$append": "approved" }, "temporaryToken": { "$remove": true } }`.

Transition guards can inspect the triggering request payload through the transient `$event` path, such as `{ "$event.amount": { "$gt": 0 } }`. The payload is not persisted automatically. Use an effect such as `{ "lastAmount": { "$fromEvent": "amount" } }` to copy a value from the event payload into the instance context.

## Addressable assets

The server owns and delivers card assets. Clients first request `GET /api/assets/manifest`, then download an asset from its returned URL with the session bearer token. Every asset has a stable ID, content type, byte length, SHA-256 hash, and ETag for cache validation. Rule-pack card definitions expose their `assetId`; production deployments can extend `AssetService` to back content with object storage without changing the client protocol.

## Secure communications

Production traffic must terminate TLS at Kestrel or a reverse proxy; HTTPS redirection and HSTS are enabled, and SignalR uses WSS on an HTTPS origin. Hub actions require a bearer session and packet timestamp, unique packet IDs block replay, payloads are capped at 2 KB, and each user is limited to 60 actions per minute. Use a trusted certificate; never expose the development certificate or HTTP listener directly to the internet.

State-changing SignalR methods require an authenticated compressed packet. Compress the UTF-8 payload with GZIP, Base64-encode the compressed bytes, and sign this UTF-8 string with HMAC-SHA256 using the session token as the key:

`{scope}.{packetId}.{sentAtUnixMs}.{compressedPayload}`

Send the Base64 signature with the packet. Scopes are `chat:{gameId}`, `play:{gameId}`, and `transition:{instanceId}:{eventName}`. The server enforces a 30-second clock window, one-time packet IDs, a 60-message-per-minute user limit, and a 2 KB decompressed payload limit (256 bytes for card IDs). `GameHub.ChatMessage`, `GameHub.PlayCard`, and `StateMachineHub.Transition` require the compressed payload, packet ID, timestamp, and signature. REST and long-polling responses use Brotli or GZIP when supported by the client.

Authenticated addressable assets from `/api/assets/{id}` use the same response compression policy. The server prefers Brotli, falls back to GZIP, and sends the original bytes when compression is not smaller. Clients should honor `Content-Encoding` and should cache/validate using the original SHA-256 ETag; compression does not change the asset identity or hash.

## Administration and moderation

Set `TCG_ADMIN_HANDLES` before registering administrator accounts. It accepts a comma-separated list of handles, for example `$env:TCG_ADMIN_HANDLES="operator"`, and matching registrations receive the `admin` role. Do not expose this environment variable to clients or commit it to source control.

The native WinUI client exposes **Accounts** to moderators and administrators. Moderators can enable or disable standard user accounts, but cannot alter roles, manage privileged accounts, or disable themselves. Administrators can manage roles and account state. Rule engine, workflow runtime, uploads, game termination, asset deletion, and audit logs remain administrator-only. The server independently enforces these permissions:

- `GET /api/admin/summary` — user, disabled-account, game, state-machine, asset, and log counts.
- `GET /api/admin/users` — account list with role and disabled state.
- `PATCH /api/admin/users/{id}` — change `role` (`user`, `moderator`, or `admin`) and/or `disabled` status.
- `GET /api/admin/logs?limit=500` — bounded request, error, and moderation audit entries.

- `DELETE /api/games/{id}` — terminate a game.
- `DELETE /api/rules/{id}` — remove an unused rule pack.
- `GET /api/state-machines` and `DELETE /api/state-machines/{id}` — inspect or terminate workflow instances.
- `DELETE /api/state-machines/configurations/{id}` — remove an unused workflow definition.
- `DELETE /api/assets/{id}` — remove an asset that is not referenced by a card.

Audit entries are retained in a bounded in-memory view and appended as JSONL. Set `TCG_AUDIT_LOG_PATH` to an approved durable location for production, for example `$env:TCG_AUDIT_LOG_PATH="D:\\TcgULtimateServerData\\audit.ndjson"`. The service restores recent entries on startup and continues in-memory operation if the configured path is unavailable. Use file permissions appropriate for potentially sensitive operational data and forward the structured entries to a centralized logging platform for production monitoring.

## Live operations, economy, and reporting

Administrators can manage the following native WinUI modules:

- **AI bot manager** — configure bot profiles, matchmaking pools, availability, daily match limits, and deterministic difficulty-baseline simulations.
- **Content packs** — upload versioned bundles, control activation, download archived source files, and review SHA-256 integrity metadata.
- **Player intelligence** — inspect inventories, achievements, friend connections, direct messages, decks, and match history; flag or remove messages and suspend or reinstate decks.
- **Events, rewards & achievements** — create or update time-bounded events, seasons, quests, tournaments, reward campaigns, and achievement definitions.
- **Store & economy** — manage in-game product availability, price and currency, plus soft-currency limits and store tax guardrails.
- **Early warning** — review automated configuration signals for bot fleet, LiveOps coverage, unlimited rewards, store availability, and moderation volume.
- **Reports & archives** — download player, game, content, bot, LiveOps, economy, or audit reports as JSON or CSV; create ZIP archives containing a JSON snapshot plus integrity manifest; verify the SHA-256 of an in-session archive before saving it.

All operations endpoints are administrator-only and recorded in the audit log. The management API exposes the data through these route groups:

- `/api/admin/bots` and `/api/admin/bots/{id}/simulations`
- `/api/admin/liveops/campaigns`, `/rewards`, and `/achievements`
- `/api/admin/store/products` and `/api/admin/economy`
- `/api/admin/early-warnings` and `/api/admin/operations/summary`
- `/api/admin/reports/{reportType}?format=json|csv`
- `/api/admin/archives`, `/api/admin/archives/{id}`, and `/api/admin/archives/{id}/verify`

The server's gameplay and operations state remains in memory. Download generated archives to an approved durable location before stopping the service; durable databases, scheduled report delivery, and off-machine backup retention should be added for a production deployment.

## External asset uploads

Assets can be uploaded independently of rule packs from **Upload center**, or with an administrator-authenticated `multipart/form-data` request to `POST /api/assets/upload`. Include fields named `id` and `name`, plus a `file` part. The server validates the ID and metadata, limits uploads to 50 MB, stores the original bytes, computes a stable SHA-256 hash, and returns an `AssetDescriptor`. The returned URL can be used by authenticated clients through `/api/assets/{id}`; downloads use the negotiated Brotli/GZIP transfer compression described above. A future object-storage implementation can replace `AssetService` without changing the upload, manifest, or download contracts.
