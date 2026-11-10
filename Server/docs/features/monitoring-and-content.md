# Monitoring and content features

## Overview

The landing dashboard provides a concise operational snapshot.

### Displays

- Service online state.
- Live players active during the last two minutes.
- Current access level.
- Dashboard session duration.
- Security posture and transport indicators.
- User, game, workflow, asset, audit, matchmaking, store, and transaction totals.
- Quick links to common management workspaces.
- Full management-feature directory with ready and roadmap status.

### Operator workflow

1. Confirm the service is online.
2. Review live players and operational totals.
3. Use quick actions for urgent work.
4. Open the management map to confirm feature ownership and status.

## Management map

The management map groups every implemented and planned capability into game/content, player operations, AI, LiveOps, commerce/economy, moderation/security, reporting/archives, and system administration.

Use it to:

- Confirm whether a requested capability is ready or roadmap.
- Identify the role required for a live control.
- Find the correct navigation workspace.
- Review platform coverage during release planning.

## Service health

Service health combines the public health response with administrator summary and operations totals.

Review:

- API health and version.
- Server time.
- Registered and disabled users.
- Active games and workflow instances.
- Asset, content, bot, campaign, store, queue, and transaction counts.
- Recent audit volume.

If health fails, confirm the API child process is running and `https://localhost:5001/api/health` responds.

## Rankings

Rankings presents registered users ordered by server rating.

Use it to:

- Confirm rating order.
- Find public player IDs for testing.
- Compare rating totals before and after match processing.

The underlying route is `GET /api/rankings`.

## API explorer

The read-only API explorer runs selected safe requests and displays formatted JSON. It is intended for diagnostics and schema inspection, not bulk administration.

Recommended uses:

- Verify health and current public resources.
- Inspect rule packs, games, rankings, or manifests.
- Confirm a response shape while developing a client.

Use the dedicated management pages for state-changing operations so validation, confirmation, and status feedback remain clear.

## Content catalog

The content catalog summarizes:

- Rule packs and cards.
- Addressable assets.
- State-machine configurations.
- Managed content packs.
- Store products and bundles.
- LiveOps campaigns, rewards, and achievements.

Use it as the read-only starting point before opening editing, upload, or removal tools.

## Games

The Games workspace lists in-memory game sessions and their current status.

Capabilities:

- Inspect game ID, rule pack, players, turn, active player, and finished state.
- Refresh the live list.
- Terminate a selected game as an administrator.

Termination removes the game from server memory and creates an audit entry. Confirm that no support investigation or replay export still requires the session.

## Content packs

Content packs are versioned uploaded bundles managed independently from rule packs and addressable assets.

Capabilities:

- Upload a pack with ID, name, version, description, and file.
- Review file name, content type, length, upload time, and SHA-256 hash.
- Activate or deactivate a pack.
- Download the original uploaded file.
- Delete an obsolete pack.

Operational guidance:

- Use stable IDs and increment versions deliberately.
- Verify hashes after moving packs between environments.
- Deactivate before deleting when clients may still reference a pack.
- Keep source bundles in durable version-controlled or artifact storage.

## Visual content studio

The visual content studio manages system-neutral JSON rule packs, generic workflow definitions, and links to every content authoring and upload workspace. See [Rules and visual content studio](rules-and-content-studio.md) for the schema and complete workflow.

### Rule packs

- Review pack ID, name, version, cards, and starting rules.
- Remove a pack only when it is not required by an active game.
- Upload new rule packs through **Upload center**.

### Workflow definitions

- Review configuration ID, version, initial state, states, transitions, guards, effects, and terminal states.
- Remove a definition only when no required instance depends on it.
- Upload definitions through **Upload center**.

### Assets

- Review stable ID, display name, content type, length, URL, and SHA-256 identity.
- Delete only assets not referenced by card definitions.
- Use the manifest and ETags for client caching.

## Workflow runtime

The generic workflow runtime supports stateful processes without requiring a new server implementation for each workflow.

Capabilities:

- List active instances.
- Create an instance from a configuration.
- Inspect current state, context, event history, and finished status.
- Apply a legal event with optional JSON data.
- Terminate an instance.

Transition rules:

- The current state must match the transition source.
- Guard conditions must match context or event data.
- Effects merge approved values into context.
- Reaching a terminal state marks the instance finished.

## Upload center

The upload center is the controlled entry point for external content.

Supported uploads:

- JSON rule packs.
- JSON state-machine configurations.
- Addressable asset files with operator-supplied ID and display name.

Validation occurs on the server. Uploaded JSON is treated as declarative data and is not executed as server-side code.

Before upload:

1. Validate IDs and versions.
2. Confirm references use existing asset or rule identifiers.
3. Keep a source copy outside the running process.
4. Upload to a non-production environment first.
5. Review the resulting catalog entry and audit log.
