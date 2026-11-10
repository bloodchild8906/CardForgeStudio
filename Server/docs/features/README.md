# Management feature documentation

The native WinUI console is the central operations workspace. It receives a local server-issued administrator session, displays server-managed player data, and calls the same protected API used by external administration integrations.

## Navigation catalog

| Group | Workspace | Access | Detailed guide |
|---|---|---|---|
| Monitor | Overview | Local console | [Monitoring and content](monitoring-and-content.md) |
| Monitor | Management map | Local console | [Monitoring and content](monitoring-and-content.md) |
| Monitor | Service health | Local console | [Monitoring and content](monitoring-and-content.md) |
| Monitor | Rankings | Local console | [Monitoring and content](monitoring-and-content.md) |
| Monitor | API explorer | Local console | [Monitoring and content](monitoring-and-content.md) |
| Content & Runtime | Content catalog | Local console | [Monitoring and content](monitoring-and-content.md) |
| Content & Runtime | Games | Authenticated console | [Monitoring and content](monitoring-and-content.md) |
| Content & Runtime | Content packs | Admin | [Monitoring and content](monitoring-and-content.md) |
| Content & Runtime | Visual content studio | Admin | [Rules and visual content studio](rules-and-content-studio.md) |
| Content & Runtime | Workflow runtime | Admin | [Monitoring and content](monitoring-and-content.md) |
| Content & Runtime | Upload center | Admin | [Monitoring and content](monitoring-and-content.md) |
| Player Operations | Accounts & access | Moderator or admin | [Player operations](player-operations.md) |
| Player Operations | Player intelligence | Admin | [Player operations](player-operations.md) |
| Player Operations | Live chat center | Moderator or admin | [Player operations](player-operations.md) |
| Live Operations | Matchmaking control | Admin | [Live operations and AI](live-operations-and-ai.md) |
| Live Operations | AI bot manager | Admin | [Live operations and AI](live-operations-and-ai.md) |
| Live Operations | Events, seasons & rewards | Admin | [Live operations and AI](live-operations-and-ai.md) |
| Live Operations | Store & economy | Admin | [Commerce and governance](commerce-and-governance.md) |
| Live Operations | Early warning | Admin | [Commerce and governance](commerce-and-governance.md) |
| Governance | Audit logs | Admin | [Commerce and governance](commerce-and-governance.md) |
| Governance | Reports & archives | Admin | [Commerce and governance](commerce-and-governance.md) |

## Shared UI behavior

- The top bar shows service state, API URL, live player count, and local operator role.
- Navigation is grouped by operational responsibility.
- Disabled items indicate that the current session lacks the required role.
- Sensitive actions display confirmation or require an explicit selected record.
- Status notifications appear in the top bar after operations.
- Lists use bounded heights so pages remain scannable and can be vertically scrolled.
- Management actions are independently authorized by the API and recorded in the audit log where applicable.

## Implemented feature groups

- Service lifecycle and health monitoring.
- Live player presence and chat monitoring.
- Complete registered-player inspection.
- Account, message, and deck moderation.
- Matchmaking queue configuration and manual operation.
- AI bot profiles and simulation baselines.
- Rule packs, cards, assets, content packs, and uploads.
- Generic state-machine configuration and runtime control.
- Events, seasons, quests, tournaments, rewards, and achievements.
- Multi-product store and economy configuration.
- Player wallet adjustments and currency transaction ledger.
- Automated operational early warnings.
- Audit review, reports, archive creation, download, and integrity verification.

## Roadmap visibility

The **Management map** keeps planned capabilities visible without presenting them as completed controls. Current roadmap entries include:

- Additional no-code action and ability form controls.
- Investigation and appeal workflows.
- AI model rollback and inference-budget controls.
- Economy analytics and simulation.
- Scheduled reports and retention policy automation.
- Full backup restore.
- Plugin management and expanded system settings.

Roadmap labels are product visibility, not API guarantees.
