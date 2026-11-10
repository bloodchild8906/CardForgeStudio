# REST API reference

Development base URL: `https://localhost:5001`

## Access legend

| Label | Requirement |
|---|---|
| Public | No bearer token required. |
| Player | Any valid bearer session. |
| Moderator | Bearer session with `moderator` or `admin` role. |
| Admin | Bearer session with `admin` role. |
| Local | Loopback request with the local management key. |

Unless noted otherwise, request and response bodies are JSON.

## Health and authentication

| Method | Route | Access | Purpose |
|---|---|---|---|
| `GET` | `/api/health` | Public | Service name, status, version, and server time. |
| `POST` | `/api/auth/register` | Public | Register a handle and password; returns token and user. |
| `POST` | `/api/auth/login` | Public | Sign in; returns token and user. |
| `DELETE` | `/api/auth/session` | Player | Revoke the bearer session on the server. |
| `POST` | `/api/auth/local-management` | Local | Create the WinUI administrator session using `X-Tcg-Management-Key`. |

Registration and login body:

```json
{ "handle": "atlas", "password": "Use-A-Real-Secret" }
```

## Player identity, social, and progression

| Method | Route | Access | Purpose |
|---|---|---|---|
| `GET` | `/api/inventory/{userId}` | Public | Return the specified in-memory inventory. |
| `GET` | `/api/decks` | Player | List decks owned by the current user. |
| `POST` | `/api/decks` | Player | Create or update a deck owned by the current user. |
| `DELETE` | `/api/decks/{id}` | Player owner | Delete an owned deck. |
| `GET` | `/api/messages` | Player | List direct messages involving the current user. |
| `POST` | `/api/messages` | Player | Send a direct message. |
| `GET` | `/api/friends` | Player | List friend connections involving the current user. |
| `POST` | `/api/friends` | Player | Add a friend connection. |
| `DELETE` | `/api/friends/{id}` | Connection participant | Remove a friend connection. |
| `GET` | `/api/rankings` | Public | Return users ordered by rating. |
| `GET` | `/api/presence/count` | Public | Return the number of active player sessions. |

Deck body:

```json
{
  "id": null,
  "name": "Starter Control",
  "rulePackId": "mtg-like",
  "cardIds": ["spark-mage", "stone-golem", "bolt"]
}
```

Direct-message body:

```json
{ "recipientId": "player-id", "body": "Ready for a match?" }
```

Friend body:

```json
{ "friendUserId": "player-id" }
```

## Matchmaking and games

| Method | Route | Access | Purpose |
|---|---|---|---|
| `GET` | `/api/matchmaking/queues` | Public | List enabled queue status records. |
| `POST` | `/api/matchmaking/join` | Player | Join a queue. |
| `DELETE` | `/api/matchmaking/leave` | Player | Leave the current queue. |
| `GET` | `/api/matchmaking/me` | Player | Return the current user's queue entries. |
| `POST` | `/api/games` | Player | Create a game and optionally add an AI player. |
| `GET` | `/api/games` | Player | List games the caller participates in; administrators can list all games. |
| `GET` | `/api/games/{id}` | Player participant | Return a game state for a participant; administrators can inspect any game. |
| `POST` | `/api/games/{id}/actions` | Player | Execute a ruleset-defined action after server legality, phase, condition, cost, and effect enforcement. |
| `DELETE` | `/api/games/{id}` | Admin | Terminate a game. |

Join body:

```json
{ "queueId": "ranked", "region": "global", "partySize": 1 }
```

Create-game body:

```json
{
  "rulePackId": "mtg-like",
  "playerIds": ["player-id"],
  "addAi": true
}
```

## Rules and assets

| Method | Route | Access | Purpose |
|---|---|---|---|
| `GET` | `/api/rules` | Public | List rule packs and card definitions. |
| `GET` | `/api/rules/capabilities` | Public | List the current neutral schema version, conditions, effects, scopes, and compatibility features. |
| `GET` | `/api/rules/{id}` | Public | Return one normalized rule pack. |
| `POST` | `/api/rules/validate` | Admin | Validate and normalize a rule pack without saving it. |
| `PUT` | `/api/rules/{id}` | Admin | Validate and publish a rule pack from the visual editor. |
| `POST` | `/api/rules/upload` | Admin | Upload a JSON rule pack as multipart field `file`. |
| `DELETE` | `/api/rules/{id}` | Admin | Delete an unused rule pack. |
| `GET` | `/api/assets/manifest` | Public | List addressable asset metadata and URLs. |
| `POST` | `/api/assets/upload` | Admin | Upload `id`, `name`, and `file` multipart fields. |
| `GET` | `/api/assets/{id}` | Player | Download an asset with compression and ETag support. |
| `DELETE` | `/api/assets/{id}` | Admin | Delete an asset not referenced by a card. |

## State machines

| Method | Route | Access | Purpose |
|---|---|---|---|
| `GET` | `/api/state-machines/configurations` | Public | List workflow definitions. |
| `GET` | `/api/state-machines/configurations/{id}` | Public | Return one workflow definition. |
| `POST` | `/api/state-machines/configurations/validate` | Admin | Validate and normalize a workflow without saving it. |
| `PUT` | `/api/state-machines/configurations/{id}` | Admin | Validate and publish a workflow from the visual editor. |
| `POST` | `/api/state-machines/configurations/upload` | Admin | Upload a JSON configuration as multipart field `file`. |
| `DELETE` | `/api/state-machines/configurations/{id}` | Admin | Delete an unused configuration. |
| `POST` | `/api/state-machines` | Player | Create an instance from a configuration. |
| `GET` | `/api/state-machines` | Player participant | List the caller's workflow instances; administrators can list all instances. |
| `GET` | `/api/state-machines/{id}` | Player participant | Return an instance to its owner, participant, or administrator. |
| `POST` | `/api/state-machines/{id}/transitions` | Player participant | Apply a legal transition when the caller is an instance participant. |
| `DELETE` | `/api/state-machines/{id}` | Admin | Terminate an instance. |

Create-instance body:

```json
{
  "configurationId": "workflow",
  "data": { "requestedBy": "player-id" },
  "participantIds": ["another-player-id"]
}
```

The creator is always added as the instance owner and participant. Add `participantIds` when the workflow is intended to coordinate more than one player.

Transition body:

```json
{ "event": "approve", "data": { "note": "Validated" } }
```

## Administration and player intelligence

| Method | Route | Access | Purpose |
|---|---|---|---|
| `GET` | `/api/admin/users` | Moderator | List registered accounts. |
| `GET` | `/api/admin/summary` | Admin | Return dashboard totals. |
| `GET` | `/api/admin/presence` | Admin | Return live player details and last activity. |
| `GET` | `/api/admin/chat` | Moderator | Return direct messages and game channels. |
| `POST` | `/api/admin/chat/announcements` | Admin | Broadcast a server announcement. |
| `PATCH` | `/api/admin/users/{id}` | Moderator | Update permitted role or disabled state. |
| `GET` | `/api/admin/players/{id}/profile` | Admin | Return full player intelligence record. |
| `POST` | `/api/admin/players/{id}/wallet-adjustments` | Admin | Adjust a player currency balance with reason. |
| `GET` | `/api/admin/economy/transactions?userId={id}` | Admin | List all or player-filtered currency transactions. |
| `PATCH` | `/api/admin/messages/{id}` | Admin | Flag/unflag and remove/restore a direct message. |
| `PATCH` | `/api/admin/decks/{id}` | Admin | Suspend or reinstate a deck. |
| `GET` | `/api/admin/logs?limit=500` | Admin | Return bounded audit entries. |

Announcement body:

```json
{ "body": "Ranked maintenance begins in ten minutes." }
```

Wallet-adjustment body:

```json
{ "currency": "gold", "amount": 500, "reason": "Support grant" }
```

## AI bot management

| Method | Route | Access | Purpose |
|---|---|---|---|
| `GET` | `/api/admin/bots` | Admin | List bot profiles. |
| `PUT` | `/api/admin/bots/{id}` | Admin | Create or replace a bot profile. |
| `DELETE` | `/api/admin/bots/{id}` | Admin | Delete a bot profile. |
| `POST` | `/api/admin/bots/{id}/simulations` | Admin | Run deterministic baseline simulations. |
| `POST` | `/api/admin/bots/{id}/rollback` | Admin | Restore the bot's prior model assignment when one is available. |

Bot body:

```json
{
  "id": "arcane-guide",
  "name": "Arcane Guide",
  "personality": "Patient control strategist",
  "difficulty": "advanced",
  "modelVersion": "rules-v1",
  "matchmakingPool": "ranked",
  "enabled": true,
  "dailyMatchLimit": 1000
}
```

## Matchmaking administration

| Method | Route | Access | Purpose |
|---|---|---|---|
| `GET` | `/api/admin/matchmaking/queues` | Admin | List all queue configurations and status. |
| `PUT` | `/api/admin/matchmaking/queues/{id}` | Admin | Create or replace a queue. |
| `DELETE` | `/api/admin/matchmaking/queues/{id}` | Admin | Delete a queue. |
| `GET` | `/api/admin/matchmaking/queues/{id}/entries` | Admin | List waiting entries. |
| `POST` | `/api/admin/matchmaking/queues/{id}/run` | Admin | Run one matching pass. |
| `DELETE` | `/api/admin/matchmaking/queues/{id}/entries` | Admin | Flush waiting entries. |

Queue body:

```json
{
  "id": "ranked",
  "name": "Ranked Duel",
  "mode": "ranked",
  "minPlayers": 2,
  "maxPlayers": 2,
  "initialRatingRange": 100,
  "ratingExpansionPerSecond": 10,
  "maxWaitSeconds": 90,
  "botBackfill": true,
  "enabled": true
}
```

## LiveOps

| Method | Route | Access | Purpose |
|---|---|---|---|
| `GET` | `/api/admin/liveops/campaigns` | Admin | List events, seasons, quests, and tournaments. |
| `PUT` | `/api/admin/liveops/campaigns/{id}` | Admin | Create or replace a campaign. |
| `DELETE` | `/api/admin/liveops/campaigns/{id}` | Admin | Delete a campaign. |
| `GET` | `/api/admin/liveops/rewards` | Admin | List reward campaigns. |
| `PUT` | `/api/admin/liveops/rewards/{id}` | Admin | Create or replace a reward. |
| `DELETE` | `/api/admin/liveops/rewards/{id}` | Admin | Delete a reward. |
| `GET` | `/api/admin/liveops/achievements` | Admin | List achievements. |
| `PUT` | `/api/admin/liveops/achievements/{id}` | Admin | Create or replace an achievement. |
| `DELETE` | `/api/admin/liveops/achievements/{id}` | Admin | Delete an achievement. |

Campaign body:

```json
{
  "id": "winter-cup",
  "name": "Winter Cup",
  "kind": "tournament",
  "description": "Limited-time competitive event",
  "startsAt": "2026-12-01T00:00:00Z",
  "endsAt": "2026-12-08T00:00:00Z",
  "active": true
}
```

## Store and economy

| Method | Route | Access | Purpose |
|---|---|---|---|
| `GET` | `/api/admin/store/products` | Admin | List store products and bundles. |
| `PUT` | `/api/admin/store/products/{id}` | Admin | Create or replace one product. |
| `DELETE` | `/api/admin/store/products/{id}` | Admin | Delete a product. |
| `GET` | `/api/admin/economy` | Admin | Return currency and tax configuration. |
| `PUT` | `/api/admin/economy` | Admin | Replace economy configuration. |

Product body:

```json
{
  "id": "starter-booster",
  "name": "Starter Booster",
  "description": "A new-player card bundle",
  "price": 100,
  "currency": "gold",
  "assetId": null,
  "contentPackId": null,
  "active": true
}
```

Economy body:

```json
{
  "softCurrency": "gold",
  "premiumCurrency": "gems",
  "dailySoftCurrencyCap": 5000,
  "storeTaxPercent": 0
}
```

## Operations, warnings, reports, and archives

| Method | Route | Access | Purpose |
|---|---|---|---|
| `GET` | `/api/admin/operations/summary` | Admin | Return AI, campaign, reward, store, and warning totals. |
| `GET` | `/api/admin/early-warnings` | Admin | Generate current configuration warnings. |
| `GET` | `/api/admin/reports/{reportType}?format=json|csv` | Admin | Download a generated report. |
| `POST` | `/api/admin/archives` | Admin | Create an in-session ZIP archive. |
| `GET` | `/api/admin/archives` | Admin | List generated archives. |
| `GET` | `/api/admin/archives/{id}` | Admin | Download one archive. |
| `POST` | `/api/admin/archives/{id}/verify` | Admin | Verify archive length and SHA-256 integrity. |

Available report domains are exposed by the Reports UI and include player, game, content, bot, LiveOps, economy, and audit data.

Archive body:

```json
{ "scope": "full" }
```

## Content packs

| Method | Route | Access | Purpose |
|---|---|---|---|
| `GET` | `/api/admin/content-packs` | Admin | List pack metadata and active state. |
| `POST` | `/api/admin/content-packs/upload` | Admin | Upload pack metadata and file content. |
| `PATCH` | `/api/admin/content-packs/{id}` | Admin | Activate or deactivate a pack. |
| `DELETE` | `/api/admin/content-packs/{id}` | Admin | Delete a pack. |
| `GET` | `/api/admin/content-packs/{id}/download` | Admin | Download the original pack file. |

The upload is `multipart/form-data` with metadata fields used by the WinUI **Content packs** workspace and a `file` part containing the original bundle.

## CardForgeStudio game packages

| Method | Path | Access | Purpose |
| --- | --- | --- | --- |
| `GET` | `/api/admin/game-packages` | Admin | List imported immutable package versions and the active version of each package. |
| `POST` | `/api/admin/game-packages/validate` | Admin | Validate a multipart `.cardforge` file without importing it. |
| `POST` | `/api/admin/game-packages/import` | Admin | Validate and import a multipart `.cardforge` file; optional form field `activate=true` activates it. |
| `POST` | `/api/admin/game-packages/{packageId}/versions/{version}/activate` | Admin | Activate a previously imported version and publish its compiled rule pack. |
| `GET` | `/api/admin/game-packages/{packageId}/versions/{version}/download` | Admin | Download the original immutable archive. |

The importer rejects unsafe or undeclared paths, duplicate entries, excessive expanded size/compression ratios, hash or length mismatches, unsupported package schemas, invalid rule references, and invalid victory definitions. A package version cannot be overwritten with different bytes. Activation returns `409 Conflict` while unfinished matches use that game ID. Validated archives and `active.json` are written atomically to `CARDFORGE_GAME_PACKAGE_STORE` (or the server's `data/game-packages` default) and restored on startup.

## SignalR routes

| Route | Purpose |
|---|---|
| `/hubs/game` | Authoritative game state, game chat, card actions, and announcements. |
| `/hubs/state-machine` | Workflow state subscriptions and secure transitions. |

See [Communications protocol](communications-protocol.md) for hub methods and secure packet signatures.
