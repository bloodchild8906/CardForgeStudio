# Rules and flows

CardForge separates rules authoring from rules execution. You author turn flows, triggers, actions, abilities and keywords in the studio; `CardForgeRules` compiles them into a state machine and `CardForgeRuleEngine` executes response windows at runtime.

Related: [designer-guide.md](designer-guide.md), [runtime-guide.md](runtime-guide.md), [data-model.md](data-model.md).

## The model at a glance

```
TurnFlow (nodes = phases, links = transitions)
   |
   +-- PhaseActionRule -> Action
   |        |
   |        +-- graphNodes / graphLinks (resolution order)
   |        +-- causeTriggerIds -> Trigger
   |
Trigger -- responseActionIds -> Action
   ^
   | causeActionIds / causeAbilityIds
   |
Ability -- actionId -> Action
   |     -- activationTriggerId -> Trigger
   |     -- phaseId -> TurnFlowNode
   |     -- graphNodes / graphLinks
   |
Keyword -- abilityIds -> Ability
```

A **turn flow** is the phase sequence. A **trigger** describes when something may respond. An **action** describes what happens. An **ability** binds an action (and optional trigger and phase) to a card or a reusable rule. A **keyword** is a named bundle of abilities that cards can reference by name.

## Turn flows

A `TurnFlowDocument` has `nodes` (phases) and `links` (transitions).

### Phases

Each `TurnFlowNode` has:

| Field | Meaning |
| --- | --- |
| `name` | Phase name, e.g. `Main`. |
| `phase` | Phase category, e.g. `Main`, `Combat`. |
| `check` | What players/engine should verify in this phase. |
| `resolution` | How scheduled actions resolve. |
| `optional` | Marks the phase as optional. |
| `x`, `y` | Canvas position. |
| `actionRules` | Scheduled actions for this phase. |

### Scheduling actions in a phase

A `PhaseActionRule` links an action to a phase:

| Field | Meaning |
| --- | --- |
| `actionId` | The action to run. |
| `availability` | When the action may be used. |
| `condition` | Free-form condition text. |
| `priority` | Sort priority. |
| `resolutionOrder` | Ordering rule, e.g. priority order. |
| `optional` | Whether the action may be skipped. |
| `beforeTriggerIds` | Triggers that must resolve before this action. |
| `afterTriggerIds` | Triggers that resolve after this action. |

Validation requires a unique action per phase, a non-empty availability and resolution order, unique before/after trigger ids, and existing trigger/action references.

### Transitions

`TurnFlowLink` has `from`, `to` and a `label` (default `continue`). Validation rejects missing endpoints, duplicate connections, and phases unreachable from the first phase.

The starter flow created by `CreateStarterWorkflow` has six phases: Untap, Upkeep, Draw, Main, Combat, End, each linked to the next with `continue`.

### Editing

The **Turn Flow** designer shows the phase graph. Use **Add phase**, drag phases, connect them, and open a phase to edit its schedule. **Diagram > Validate** runs `ValidateFlow`; **Auto arrange** lays the graph out in a grid.

## Triggers

A `TriggerDocument` describes an event and the responses allowed around it.

| Field | Meaning |
| --- | --- |
| `name` | Display name. |
| `eventType` | Event category, e.g. `State changed`. |
| `source` | Who/what can cause it, e.g. `Any object`. |
| `fromZone`, `toZone`, `orientation` | Optional event filters. |
| `rules` | Qualifying rules. |
| `condition` | Free-form additional condition. |
| `responseWindow` | Timing, e.g. `Immediate response window`. |
| `priority` | Ordering hint. |
| `causeActionIds` | Actions that can cause this trigger. |
| `causeAbilityIds` | Abilities that can cause this trigger. |
| `responseActionIds` | Actions legal inside the response window. |

Each `TriggerRule` has `subject`, `property`, `comparison`, `expectedValue` and `condition`. A trigger needs at least one rule.

### Link synchronization

Trigger/action causation is bidirectional in the store:

- Saving a trigger adds the trigger id to every action listed in `causeActionIds`, and removes stale links.
- Saving an action adds the action id to every trigger listed in `causeTriggerIds`, and removes stale links.

Edit either side and the other side is repaired automatically. Other reference types (response actions, ability bindings) are not auto-synced and must be validated.

### Validation

`ValidateTrigger` checks: name, event type, at least one rule, subject/property on every rule, unique and existing response actions, unique and existing source actions, unique and existing source abilities, and a non-empty response window.

## Actions

An `ActionDocument` is the effect.

| Field | Meaning |
| --- | --- |
| `name` | Display name. |
| `triggerId` | Optional viability trigger gate. |
| `resolution` | Resolution order text. |
| `effect` | Human-readable effect. |
| `condition` | Free-form condition. |
| `cost` | Cost text. |
| `targets` | Target text. |
| `orientationChange` | `None`, `Rotate90`, `Rotate180`, `Rotate270`, `FlipFrontBack`. |
| `orientationTarget` | Which card is affected. |
| `causeTriggerIds` | Triggers emitted by this action. |
| `graphNodes`, `graphLinks` | Resolution graph. |

### Resolution graph

Each `ResolutionGraphNode` has `title`, `detail`, `tone`, position, and two boundary flags:

- `causesStateChange` - the step changes game state.
- `emitsTriggers` - the step is where the action's caused triggers fire.

Graph links have `from`, `to` and a label (default `next`).

`CardForgeRules.CompileAction` orders nodes depth-first from every root (nodes with no incoming link), then appends any remaining nodes. The compiled result records the ordered step ids, state-change step ids and trigger-boundary step ids.

Important: `causeTriggerIds` are only emitted when at least one graph step has `emitsTriggers = true`. Otherwise the compiler clears the caused triggers. Validation warns you to mark a boundary step when caused triggers are configured.

### Validation

`ValidateAction` checks: name, viability trigger exists, unique and existing caused triggers, trigger-emission boundary when caused triggers exist, orientation target when an orientation change is set, non-empty resolution, and a valid resolution graph (at least one titled step, valid endpoints, no duplicates, connected when there is more than one step).

## Abilities

An `AbilityDocument` binds rules to a card, keyword bundle or phase.

| Field | Meaning |
| --- | --- |
| `name` | Display name. |
| `cardId` | Optional bound card; empty means reusable. |
| `activationTriggerId` | Trigger that activates the ability. |
| `actionId` | Main action. |
| `phaseId` | Phase it is available in. |
| `condition` | Free-form condition. |
| `operation` | What it does to the current action. |
| `effect` | Human-readable effect. |
| `priority` | Ordering. |
| `scope` | Scope, e.g. `Game state`. |
| `responseTiming` | When responses are allowed. |
| `responseResolution` | How responses resolve. |
| `responseActionIds` | Additional response actions. |
| `causeTriggerIds` | Triggers emitted. |
| `rules` | `AbilityRule` list. |
| `graphNodes`, `graphLinks` | Resolution graph. |

An `AbilityRule` has `target`, `property`, `operation`, `value` and `condition`. Every rule needs a property.

Validation also checks that a caused-trigger ability has an `emitsTriggers` graph step, and that every reference (card, trigger, action, phase) exists.

## Keywords

A `KeywordDocument` is a named bundle of abilities plus rules text.

| Field | Meaning |
| --- | --- |
| `name` | Keyword name, e.g. `Flying`. |
| `rulesText` | Full rules text. |
| `reminderText` | Short reminder. |
| `abilityIds` | Ordered abilities. |
| `tags` | Free-form tags. |

Cards reference keywords through `keywordIds`. The runtime exposes `GetKeywordAbilities(keywordId)` to expand a keyword into its ability documents.

Validation: name, at least one ability, no duplicate abilities, all abilities exist.

## Validation summary

| Validator | Entry point |
| --- | --- |
| Block and references | `CardForgeStore.Validate(block)` |
| Turn flow | `CardForgeStore.ValidateFlow(flow)` |
| Trigger | `CardForgeStore.ValidateTrigger(trigger)` |
| Action | `CardForgeStore.ValidateAction(action)` |
| Ability | `CardForgeStore.ValidateAbility(ability)` |
| Keyword | `CardForgeStore.ValidateKeyword(keyword)` |

Run them all from **Project health**. Export runs `Validate(block)` first and refuses on errors.

## Runtime compilation

`CardForgeRules.BuildStateMachine(flow, triggers, actions, abilities, keywords)`:

1. Copies all triggers, actions, abilities and keywords into the machine.
2. Compiles every action's graph.
3. Creates one `RuleStateDefinition` per phase, copying valid scheduled actions and adding abilities whose `phaseId` matches.
4. Sets `initialStateId` to the first phase.
5. Converts each flow link into a `RuleStateTransition` with `resolution = link.label`.

## Runtime execution

`CardForgeRuleEngine` implements response windows:

- `RaiseTrigger(triggerId, sourcePlayerId, playerIds)` opens a window.
- `Respond(triggerId, playerId, actionId)` pushes a response if the action is allowed by the trigger and the player is in the window.
- `Pass(triggerId, playerId)` marks a pass; when every player has passed, the engine resolves the top of the stack.
- `Resolve(triggerId)` resolves once every player has passed.
- `GetAllowedResponseActions` lists legal responses for a player.
- `GetKeywordAbilities` expands a keyword.

Semantics that are currently hard-coded:

- Responses resolve **last in, first out** (LIFO).
- All players in the window must pass before resolution.
- Nested caused triggers must be resolved before their parent window.
- The engine tracks states/transitions in the compiled machine but does **not** advance them or evaluate `condition` text.
- `RuleStateDefinition` / `RuleStateTransition` and `compiledActions` are data for a host game; the engine does not interpret effect, cost, targets or state changes.

This means CardForge is an authoring tool plus a reference engine, not a complete game rules engine. See [runtime-guide.md](runtime-guide.md) for integration.

## See also

- [data-model.md](data-model.md) for exact fields.
- [runtime-guide.md](runtime-guide.md) for loading and driving the engine.
- [testing.md](testing.md) for what is covered by tests.