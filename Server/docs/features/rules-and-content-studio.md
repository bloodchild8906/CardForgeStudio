# Rules and visual content studio

## System-neutral rules engine

Rule schema version 2 models trading card games through configurable building blocks instead of assuming one game's terminology or turn structure.

| Building block | Purpose |
|---|---|
| Zones | Player-owned or shared locations such as decks, hands, boards, queues, markets, discard piles, exile areas, or custom spaces. |
| Resources | Player or game values such as life, energy, action points, score, momentum, influence, or counters. |
| Phases | Ordered game stages with an explicit set of allowed actions. |
| Actions | Legal player operations with source/destination zones, costs, conditions, and effects. |
| Conditions | Reusable legality checks enforced by the authoritative server. |
| Effects | Server-executed state changes such as movement, drawing, resource changes, events, phase changes, turns, and game completion. |
| Cards | Generic content records with tags, custom properties, assets, and triggered abilities. |

The server validates identifiers, uniqueness, cross-references, player limits, resource bounds, action costs, phases, and supported primitives before a pack can be published. Existing version 1 packs are normalized to version 2 for compatibility.

Use `GET /api/rules/capabilities` to discover the primitives supported by the running server. A complete starter file is available at [`../examples/system-neutral-rule-pack.json`](../examples/system-neutral-rule-pack.json).

## Visual rule designer

Open **Content & Runtime > Visual content studio**.

1. Select an installed rule pack or choose **New template**.
2. Edit the ID, name, version, game system, and player limits in the guided fields.
3. Review zones, resources, phases, actions, and cards in the component browser.
4. Use the advanced JSON editor for custom properties, abilities, conditions, effects, and metadata.
5. Choose **Validate** to run authoritative server validation.
6. Choose **Publish** to save the normalized definition and write an audit entry.
7. Use **Import JSON** or **Export JSON** to move definitions between environments.

Publishing replaces the definition with the same ID atomically. Active games keep their current in-memory state, so schedule incompatible rule changes carefully.

## Visual workflow designer

The same workspace includes a state-machine designer for tournament stages, quests, moderation cases, publishing approvals, reward delivery, and other operational processes.

- Guided fields cover identity, version, and initial state.
- The visual browser lists states and transitions.
- Advanced JSON supports guards, effects, terminal states, and metadata.
- Validation and publishing use protected server endpoints and audit logging.
- **Workflow runtime** creates instances and tests transitions against live server behavior.

## Content browser and uploaders

The top of the studio links every first-class content workspace:

- Rules and cards.
- Workflows and runtime instances.
- Addressable assets and JSON uploads.
- Versioned content packs.
- Store products, bundles, currencies, and economy settings.
- Events, seasons, rewards, quests, and achievements.

The **Content catalog** provides a read-only inventory across these content families. Dedicated pages provide the visual forms, bulk controls, file pickers, validation, publishing, activation, download, and removal operations appropriate to each type.

## Safety model

- The native console receives a loopback-only management session; it does not require an operator login.
- Player authentication remains server-managed.
- Validate and publish endpoints require an administrator bearer token.
- Rule and workflow JSON is declarative data, not executable server code.
- File limits, references, and IDs are checked by the server.
- Sensitive changes are recorded in the audit log.
