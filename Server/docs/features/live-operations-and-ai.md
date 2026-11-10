# Live operations and AI features

## Matchmaking control

The matchmaking workspace manages queue behavior and waiting players.

### Queue configuration

- Stable queue ID and display name.
- Mode, such as ranked or casual.
- Minimum and maximum players.
- Initial rating range.
- Rating-range expansion per waiting second.
- Maximum wait before bot backfill eligibility.
- Bot-backfill enabled state.
- Queue enabled state.

### Live queue operations

- Review waiting player and party counts.
- Review the longest wait time.
- Inspect individual entries with user ID, rating, party size, region, and join time.
- Run a matching pass manually.
- Review matches created, players matched, bots added, remaining players, and game IDs.
- Flush all waiting entries from a queue.

### Recommended workflow

1. Keep disabled queues configured but unavailable to clients.
2. Validate rating range and expansion against expected population.
3. Review long waits before increasing backfill or range expansion.
4. Use manual runs for diagnostics and controlled tests.
5. Flush only during maintenance or when entries are known to be invalid.

The server starts with `ranked` and `casual` queues.

## AI bot manager

Bot profiles describe deterministic server-controlled opponents.

### Profile fields

- ID and display name.
- Personality description.
- Difficulty.
- Model version label.
- Matchmaking pool.
- Enabled state.
- Daily match limit.

### Operations

- Create or replace profiles.
- Enable or disable availability.
- Assign bots to matchmaking pools.
- Set daily load guardrails.
- Delete unused profiles.
- Run deterministic baseline simulations.

### Simulation output

- Simulation run ID.
- Bot profile ID.
- Match count.
- Estimated win rate.
- Average turns.
- Completion time.

Simulations are operational baselines, not a substitute for model evaluation, adversarial testing, or production telemetry.

## Events, seasons, quests, and tournaments

Campaigns share one management model with different `kind` values.

Fields:

- Stable ID.
- Name and description.
- Kind: event, season, quest, or tournament.
- Start and end timestamps.
- Active state.

Operators can create, replace, activate, deactivate, and delete campaigns.

Campaign checklist:

1. Use UTC timestamps.
2. Confirm the end is after the start.
3. Avoid conflicting IDs.
4. Link communication and reward plans before activation.
5. Verify player-facing clients understand the selected kind.
6. Deactivate before deleting when historical reporting may be required.

## Rewards

Reward campaigns define a trigger and grant.

Fields:

- ID and display name.
- Trigger, such as `match_win`.
- Reward type, such as `gold`.
- Amount.
- Optional distribution limit.
- Active state.

Operational guidance:

- Prefer explicit distribution limits for limited campaigns.
- Align reward currency with the economy configuration.
- Review early warnings for unlimited active rewards.
- Test trigger naming with the client and server event design before launch.

## Achievements

Achievement definitions contain:

- Stable ID.
- Name and description.
- Point value.
- Active state.

The player inventory records earned achievement IDs. Keep IDs stable after release so historical player records remain understandable.

## LiveOps release workflow

1. Create the campaign in an inactive state.
2. Create required rewards and achievements.
3. Confirm store and economy dependencies.
4. Configure or review matchmaking queues and bot availability.
5. Run early-warning checks.
6. Verify start/end times and client presentation in a test environment.
7. Activate the campaign and related definitions.
8. Broadcast a server announcement when appropriate.
9. Monitor queue waits, player count, chat, warnings, and audit logs.
10. Export reports and archive the release state after completion.

## AI and LiveOps roadmap

The management map also tracks planned model rollback, model promotion, inference-budget controls, richer economy simulation, and deeper automated balance testing. These roadmap items are not represented as completed live controls.

