# Unity client product roadmap

This roadmap turns `templates/TcgUnityClient` into a production game client for TcgULtimateServer. It separates implemented foundations from planned work and gives every major feature an acceptance gate.

## Product principles

- The server is authoritative for accounts, inventory, currency, matchmaking, rewards, and game state.
- The player signs in through the server; the server's local WinUI management console does not require a player login.
- Every online workflow must have loading, empty, error, reconnect, and maintenance states.
- Desktop, mobile, and future console layouts share services and presenters while using platform-appropriate views.
- Remote content and LiveOps configuration must not require a client rebuild when the server contract is unchanged.
- Accessibility, localization, telemetry, moderation, and performance are release requirements rather than late additions.

## Current foundation

| Area | Status | Included now |
|---|---|---|
| Project | Ready | Unity project, generated scene, environment settings, editor setup command |
| Account | Foundation | Register, sign in, in-memory session, sign out, server health |
| Lobby | Foundation | Live player count, queue status, rankings, AI game creation |
| Matchmaking | Foundation | Ranked/casual join, leave, personal queue status |
| Collection | Foundation | Inventory and achievement retrieval with grouped card counts |
| Decks | Foundation | List, save, and delete service APIs; deck summary UI |
| Social | Foundation | Friends and direct-message retrieval, add friend, send message |
| Games | Foundation | Game browser, game-state polling, recent event display |
| Real time | Optional foundation | SignalR game state, chat, announcements, reconnect entry points |
| Security | Foundation | Server-managed sign-in, bearer session, timestamped HMAC action packets |

`Foundation` means the server contract and reusable client path exist. It does not mean production art, complete UX, persistence, or all validation are finished.

## Release 0.1 - Connected vertical slice

Goal: a tester can install the client, sign in, find a match, play the starter ruleset, and reconnect.

- Replace the runtime-built demonstration UI with reusable prefab or UI Toolkit screens and a shared design-token system.
- Add environment profiles for local, LAN, test, staging, and production servers.
- Complete SignalR dependency installation, automatic reconnect, resubscription, and REST fallback.
- Build a board presenter for players, zones, turn state, priority, stack, targets, and the event log.
- Add hand interaction, card inspection, valid-action highlighting, confirmation, cancel, and pass-priority controls.
- Display card art from the asset manifest with caching, hash validation, placeholders, and retry.
- Add match found, loading, mulligan, victory, defeat, draw, disconnect, and abandoned-match flows.
- Add structured error codes and user-friendly messages for authentication, queueing, and gameplay failures.

Acceptance gate: two packaged clients can complete a match through the server, recover from one forced disconnect, and produce a replayable event history.

## Release 0.2 - Player account and collection

Goal: players can manage their identity and owned content without administrative assistance.

- Profile summary, avatar, title, progression, rating history, season rank, and privacy controls.
- Collection browser with search, sort, filters, rarity, faction, type, cost, ownership, favorites, and new-item markers.
- Card detail with art, rules text, legal formats, variants, acquisition sources, related cards, and usage statistics.
- Deck builder with drag-and-drop editing, curve and type charts, validation, sideboard, formats, tags, notes, import, export, duplicate, and delete.
- Server-side deck legality checks and clear validation results before queue entry.
- Achievements, quests, reward track, claim-all, reward reveal, and reward history.
- Wallet and currency history with separate earned, purchased, promotional, and premium balances.
- Secure optional session persistence using platform credential storage and explicit sign-out-everywhere support.

Acceptance gate: a player can inspect every owned item, build a legal deck, select it for a queue, claim a reward, and see the resulting inventory and wallet changes.

## Release 0.3 - Social and communication

Goal: players can safely form relationships, communicate, and play together.

- Friend requests with pending, accepted, declined, blocked, muted, favorite, and recently played states.
- Presence details, activity privacy, rich status, party invitations, spectate permissions, and joinable games.
- Direct-message conversations with unread counts, paging, timestamps, moderation state, and offline delivery.
- Game and party chat with channels, emotes, rate limits, profanity policy, reporting, mute, block, and parental controls.
- Parties with leader transfer, ready checks, deck validation, queue restrictions, and reconnect behavior.
- Player profile cards with report, block, invite, message, compare rank, and match-history actions.
- Server announcements, inbox messages, maintenance notices, terms updates, and targeted campaigns.

Acceptance gate: two players can become friends, create a party, exchange messages, queue together, report abuse, and have mute/block enforced by every communication surface.

## Release 0.4 - Matchmaking, ranked, and tournaments

Goal: robust competitive and casual play across regions and formats.

- Queue browser driven entirely by server configuration, including mode, format, party size, region, deck requirements, and availability window.
- Estimated wait, expanding rating range, connection quality, selected region, queue penalties, and cancellation rules.
- Ranked seasons, placement matches, divisions, tiers, promotion/demotion, decay, leaderboards, and season rewards.
- Casual, unranked, practice, direct challenge, private room, draft, sealed, singleton, and rotating modes.
- Tournament registration, check-in, brackets, pairings, round timers, match reporting, disputes, drops, standings, and prizes.
- Spectating with delay, hidden information rules, friend permissions, featured matches, and broadcast overlay data.
- Match history with filters, deck snapshot, opponent profile, result, rating change, timeline, replay, and report action.

Acceptance gate: all enabled server queues render without client code changes and ranked/tournament results reconcile with server standings and rewards.

## Release 0.5 - Store and economy

Goal: a trustworthy multi-product commerce experience controlled by server configuration.

- Store home, categories, tabs, search, featured carousels, bundles, packs, cosmetics, currencies, subscriptions, and event offers.
- Multiple products per offer, quantity choices, purchase limits, eligibility, regional availability, start/end schedules, and personalized offers.
- Localized pricing, platform store integration, receipt validation, pending purchases, restore purchases, refunds, and chargeback states.
- Product detail with complete contents, duplicate protection, odds disclosure, preview, gifting rules, and legal notices.
- Cart and confirmation where supported; otherwise a clear single-purchase flow with idempotency.
- Pack opening and reward reveal with skip, accessibility, duplicate conversion, and full transaction receipt.
- Wallet ledger, purchase history, support reference, parental spending controls, and server maintenance lockout.
- Economy telemetry for sinks, sources, conversion, inflation, inventory distribution, and suspicious behavior.

Acceptance gate: every configured active product displays correctly, receipts are validated server-side, retries cannot duplicate grants, and wallet/transaction history always reconciles.

## Release 0.6 - LiveOps and content delivery

Goal: operate events, rewards, balance, and content without shipping a new binary.

- Event hub for seasons, campaigns, tournaments, limited modes, featured queues, and time-limited store offers.
- Daily/weekly quests, event missions, milestones, streaks, login calendars, battle passes, and claim states.
- Remote rule packs, card data, localization, images, audio, VFX references, and content-pack manifests.
- Version negotiation, minimum supported client, maintenance windows, staged rollout, rollback, and content compatibility checks.
- Download manager with size preview, Wi-Fi preference, pause/resume, retry, disk-space checks, hash verification, and cleanup.
- News, patch notes, banners, deep links, inbox rewards, surveys, and targeted player segments.
- Experiment assignments and feature flags with server-side exposure logging and safe fallback defaults.

Acceptance gate: an operator can launch and roll back an event, reward campaign, product set, queue, rule pack, and announcement without rebuilding the client.

## Release 0.7 - AI and training

Goal: support onboarding, practice, and reliable bot-backed operations.

- Tutorial campaign, contextual tips, rules glossary, guided deck, practice puzzles, and replayable lessons.
- AI opponent selection by personality, difficulty, faction preference, deck, model version, and matchmaking policy.
- Local presentation of AI thinking state without exposing private strategy or delaying server authority.
- Bot backfill disclosure, player consent rules, post-match identification, and fair ranked restrictions.
- PvE encounters, scripted bosses, challenges, modifiers, and progression rewards.
- Automated match diagnostics uploaded with correlation IDs for failed AI actions and simulation mismatches.

Acceptance gate: a new player can finish onboarding, build a starter deck, defeat a beginner bot, and enter a protected beginner queue.

## Release 0.8 - Safety, support, and compliance

Goal: make the client operable and safe at production scale.

- Report flows for player, message, chat, deck name, exploit, cheating, and inappropriate content with evidence attachment.
- Early-warning messaging, temporary restrictions, appeal status, enforcement notices, and support links.
- Age gate, consent, privacy choices, data export request, account deletion request, parental controls, and regional legal notices.
- Terms, privacy policy, code of conduct, licenses, accessibility statement, and versioned acceptance records.
- Support center with diagnostics bundle, network test, service status, FAQ, ticket handoff, and correlation ID copy.
- Anti-tamper signals, rooted/jailbroken device policy where applicable, clock anomaly handling, and secure platform storage.
- Rate-limit UX and graceful responses for maintenance, partial outage, degraded chat, disabled commerce, and unavailable matchmaking.

Acceptance gate: moderation and support can trace a client incident from correlation ID through server logs without exposing secrets or private data.

## Release 1.0 - Quality and launch readiness

Goal: ship a supportable cross-platform production client.

- Keyboard, mouse, controller, touch, screen-reader semantics, scalable text, color-blind support, reduced motion, and remapping.
- Localization pipeline with pluralization, right-to-left layout, locale-aware numbers/dates, fallback fonts, and pseudo-localization tests.
- Audio mixer, music, SFX, voice, dynamic range, mute-on-background, haptics, and per-channel settings.
- Graphics presets, resolution, window mode, frame cap, render scale, safe area, battery mode, and device capability detection.
- First-run settings, legal acceptance, patching, account linking, server selection policy, and migration from older client versions.
- Telemetry consent, crash reporting, performance traces, gameplay analytics, funnel events, and privacy-safe identifiers.
- Automated edit-mode, play-mode, contract, UI, reconnect, soak, device, and build-pipeline tests.
- Signed builds, symbol handling, reproducible release configuration, deployment rings, rollback, and release notes.

Acceptance gate: platform certification checklist passes, critical user journeys meet performance budgets, and release/rollback is rehearsed in staging.

## Cross-cutting architecture backlog

| System | Required design |
|---|---|
| Navigation | Typed routes, guarded screens, deep links, modal stack, back behavior |
| State | Session, profile, catalog, inventory, wallet, party, queue, match, LiveOps stores |
| Networking | Typed contracts, cancellation, retries, timeouts, idempotency, correlation IDs |
| Real time | Reconnect state machine, subscriptions, sequencing, deduplication, snapshots |
| Content | Manifest versions, cache index, hashes, dependency graph, eviction policy |
| UI | Design tokens, reusable controls, responsive layouts, skeleton and empty states |
| Gameplay | Authoritative presenter, action intents, prediction policy, animation queue |
| Persistence | Non-secret preferences, secure credentials, migrations, corruption recovery |
| Observability | Structured logs, metrics, traces, crash context, privacy filters |
| Testing | Fake server, contract fixtures, deterministic games, UI automation, soak tests |

## Definition of done for every feature

1. Server authorization and validation are defined.
2. Client loading, empty, success, error, retry, offline, and maintenance states exist.
3. Keyboard/controller/touch navigation and accessibility labels are verified.
4. Localization and variable-length text are supported.
5. Analytics, logs, and correlation IDs avoid credentials and private message content.
6. Automated tests cover the normal flow and at least one failure/reconnect flow.
7. Documentation describes configuration, support diagnostics, and operational ownership.
8. The feature is exercised against a packaged server and packaged client, not only in the editor.

## Recommended next implementation order

1. Finish the real-time dependency and authoritative board interaction.
2. Replace the demonstration UI with the production navigation and design system.
3. Complete collection/deck editing and deck legality validation.
4. Add match found, reconnect, results, history, and replay viewing.
5. Complete friends, conversations, party, block, mute, and reporting.
6. Add the store catalog, wallet ledger, idempotent purchase flow, and receipts.
7. Add remote content, LiveOps event hub, quests, rewards, and feature flags.
8. Complete accessibility, localization, telemetry, automated testing, and release automation.
