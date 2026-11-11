using System.Text.Json;
using System.Text.RegularExpressions;
using CardForge.GamePackages;
using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

public sealed class RulesEngine(AppState state)
{
    private const string SharedOwner = "__game__";
    private static readonly Regex Identifier = new("^[a-zA-Z0-9][a-zA-Z0-9_-]{0,79}$", RegexOptions.Compiled);
    private static readonly HashSet<string> EffectKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "move_card", "draw", "modify_resource", "set_data", "emit_event", "advance_phase", "end_turn", "finish_game"
    };
    private static readonly HashSet<string> ConditionKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "active_player", "source_in_zone", "resource_at_least", "parameter_equals"
    };

    public object Capabilities() => new
    {
        schemaVersion = "2",
        model = "system-neutral-tcg",
        effectKinds = EffectKinds.OrderBy(value => value),
        conditionKinds = ConditionKinds.OrderBy(value => value),
        scopes = new[] { "player", "game" },
        visibility = new[] { "public", "owner", "hidden" },
        victoryKinds = new[] { "resource-threshold", "empty-zone", "last-player-standing", "turn-limit" },
        compatibility = new[] { "legacy-v1-auto-upgrade" }
    };

    public RulePack Upload(Stream stream)
    {
        RulePack pack;
        try
        {
            pack = JsonSerializer.Deserialize<RulePack>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Invalid rule pack JSON.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Invalid rule pack JSON.", exception);
        }

        return Save(pack);
    }

    public RulePack Save(RulePack source)
    {
        var validation = Validate(source);
        if (!validation.Valid || validation.Normalized is null)
            throw new InvalidOperationException(string.Join(" ", validation.Errors));

        state.RulePacks[validation.Normalized.Id] = validation.Normalized;
        return validation.Normalized;
    }

    public RulePackValidationResult Validate(RulePack source)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var pack = Normalize(source, warnings);

        ValidateIdentifier(pack.Id, "Rule pack", errors);
        if (string.IsNullOrWhiteSpace(pack.Name)) errors.Add("Rule pack name is required.");
        if (string.IsNullOrWhiteSpace(pack.Version)) errors.Add("Rule pack version is required.");
        if (pack.MinimumPlayers < 1 || pack.MaximumPlayers < pack.MinimumPlayers)
            errors.Add("Player limits must define a valid minimum and maximum.");
        if (pack.Cards.Count == 0) errors.Add("A TCG rule pack must contain at least one card definition.");
        if (pack.Zones.Count == 0) errors.Add("At least one zone is required.");
        if (pack.Phases.Count == 0) errors.Add("At least one phase is required.");
        if (pack.Actions.Count == 0) errors.Add("At least one action is required.");

        ValidateUnique(pack.Cards.Select(card => card.Id), "card", errors);
        ValidateUnique(pack.Zones.Select(zone => zone.Id), "zone", errors);
        ValidateUnique(pack.Resources.Select(resource => resource.Id), "resource", errors);
        ValidateUnique(pack.Phases.Select(phase => phase.Id), "phase", errors);
        ValidateUnique(pack.Actions.Select(action => action.Id), "action", errors);

        foreach (var card in pack.Cards)
        {
            ValidateIdentifier(card.Id, "Card", errors);
            if (string.IsNullOrWhiteSpace(card.Name)) errors.Add($"Card '{card.Id}' needs a name.");
            ValidateUnique(card.Abilities.Select(ability => ability.Id), $"ability on card '{card.Id}'", errors);
            foreach (var ability in card.Abilities)
                ValidateEffectsAndConditions(ability.Effects, ability.Conditions, $"ability '{ability.Id}'", pack, errors);
        }

        var cardIds = pack.Cards.Select(card => card.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var cardId in pack.StartingDeck.Where(cardId => !cardIds.Contains(cardId)))
            errors.Add($"Starting deck references unknown card '{cardId}'.");
        var startingHand = Math.Max(0, pack.StartingRules.GetValueOrDefault("startingHand", 7));
        if (pack.StartingDeck.Count > 0 && pack.StartingDeck.Count < startingHand)
            errors.Add("Starting deck cannot contain fewer cards than the starting hand.");

        foreach (var zone in pack.Zones)
        {
            ValidateIdentifier(zone.Id, "Zone", errors);
            if (!zone.OwnerScope.Equals("player", StringComparison.OrdinalIgnoreCase) && !zone.OwnerScope.Equals("game", StringComparison.OrdinalIgnoreCase))
                errors.Add($"Zone '{zone.Id}' has unsupported owner scope '{zone.OwnerScope}'.");
            if (zone.Capacity is < 1) errors.Add($"Zone '{zone.Id}' capacity must be positive when supplied.");
        }

        foreach (var resource in pack.Resources)
        {
            ValidateIdentifier(resource.Id, "Resource", errors);
            if (resource.InitialValue < resource.MinimumValue) errors.Add($"Resource '{resource.Id}' starts below its minimum.");
            if (resource.MaximumValue is not null && resource.InitialValue > resource.MaximumValue) errors.Add($"Resource '{resource.Id}' starts above its maximum.");
        }

        var actionIds = pack.Actions.Select(action => action.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var phase in pack.Phases)
        {
            ValidateIdentifier(phase.Id, "Phase", errors);
            foreach (var actionId in phase.AllowedActions.Where(actionId => !actionIds.Contains(actionId)))
                errors.Add($"Phase '{phase.Id}' references unknown action '{actionId}'.");
        }

        foreach (var action in pack.Actions)
        {
            ValidateIdentifier(action.Id, "Action", errors);
            ValidateZoneReference(action.SourceZone, $"Action '{action.Id}' source", pack, errors);
            ValidateZoneReference(action.DestinationZone, $"Action '{action.Id}' destination", pack, errors);
            foreach (var resourceId in action.Costs?.Keys ?? Enumerable.Empty<string>())
                if (!pack.Resources.Any(resource => resource.Id.Equals(resourceId, StringComparison.OrdinalIgnoreCase)))
                    errors.Add($"Action '{action.Id}' costs unknown resource '{resourceId}'.");
            ValidateEffectsAndConditions(action.Effects, action.Conditions, $"action '{action.Id}'", pack, errors);
        }

        return new RulePackValidationResult(errors.Count == 0, errors, warnings, errors.Count == 0 ? pack : null);
    }

    public RulePack Get(string id) => state.RulePacks.TryGetValue(id, out var pack)
        ? pack
        : throw new KeyNotFoundException("Rule pack not found.");

    public void Start(GameState game)
    {
        var pack = Get(game.RulePackId);
        if (game.Players.Count < pack.MinimumPlayers || game.Players.Count > pack.MaximumPlayers)
            throw new InvalidOperationException($"This ruleset requires {pack.MinimumPlayers} to {pack.MaximumPlayers} players.");

        foreach (var player in game.Players)
        {
            game.Zones[player] = [];
            game.Resources[player] = [];
            foreach (var zone in pack.Zones.Where(zone => zone.OwnerScope.Equals("player", StringComparison.OrdinalIgnoreCase)))
                game.Zones[player][zone.Id] = [];
            foreach (var resource in pack.Resources.Where(resource => resource.Scope.Equals("player", StringComparison.OrdinalIgnoreCase)))
                game.Resources[player][resource.Id] = resource.InitialValue;
        }

        game.Zones[SharedOwner] = [];
        game.Resources[SharedOwner] = [];
        foreach (var zone in pack.Zones.Where(zone => zone.OwnerScope.Equals("game", StringComparison.OrdinalIgnoreCase)))
            game.Zones[SharedOwner][zone.Id] = [];
        foreach (var resource in pack.Resources.Where(resource => resource.Scope.Equals("game", StringComparison.OrdinalIgnoreCase)))
            game.Resources[SharedOwner][resource.Id] = resource.InitialValue;

        var deckZone = pack.Zones.FirstOrDefault(zone => zone.Id.Equals("deck", StringComparison.OrdinalIgnoreCase))?.Id
            ?? pack.Zones.First(zone => zone.OwnerScope.Equals("player", StringComparison.OrdinalIgnoreCase)).Id;
        var handZone = pack.Zones.FirstOrDefault(zone => zone.Id.Equals("hand", StringComparison.OrdinalIgnoreCase))?.Id;
        var startingHand = Math.Max(0, pack.StartingRules.GetValueOrDefault("startingHand", 7));
        foreach (var player in game.Players)
        {
            var deckDefinitions = pack.StartingDeck.Count > 0 ? pack.StartingDeck : pack.Cards.Select(card => card.Id).ToList();
            var deck = deckDefinitions.Select(cardId => new CardInstance(Guid.NewGuid().ToString("N"), cardId, player)).ToList();
            game.Zones[player][deckZone].AddRange(deck);
            if (handZone is not null)
                Draw(game, player, deckZone, handZone, startingHand);

            if (game.Resources[player].TryGetValue("life", out var life)) game.Life[player] = checked((int)life);
            else game.Life[player] = pack.StartingRules.GetValueOrDefault("startingLife", 0);
            game.Hands[player] = handZone is null ? [] : game.Zones[player][handZone];
            var battlefieldZone = pack.Zones.FirstOrDefault(zone => zone.Id.Equals("battlefield", StringComparison.OrdinalIgnoreCase))?.Id;
            game.Battlefield[player] = battlefieldZone is null ? [] : game.Zones[player][battlefieldZone];
        }

        game.ActivePlayerId = game.Players[0];
        game.CurrentPhaseId = pack.Phases[0].Id;
        game.Events.Add(new("game_started", $"Game started with {game.Players.Count} players using {pack.GameSystem} rules.", null, new { pack.SchemaVersion, pack.GameSystem }, DateTimeOffset.UtcNow));
    }

    public GameState Play(GameState game, string actorId, string instanceId) => Execute(game, actorId, new ExecuteGameActionRequest("play_card", instanceId));

    public GameState Execute(GameState game, string actorId, ExecuteGameActionRequest request)
    {
        if (game.Finished) throw new InvalidOperationException("The game is finished.");
        if (!game.Players.Contains(actorId)) throw new InvalidOperationException("Player is not in this game.");
        var pack = Get(game.RulePackId);
        var action = pack.Actions.FirstOrDefault(candidate => candidate.Id.Equals(request.ActionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Action '{request.ActionId}' is not defined by this ruleset.");
        var phase = pack.Phases.First(candidate => candidate.Id.Equals(game.CurrentPhaseId, StringComparison.OrdinalIgnoreCase));
        if (!phase.AllowedActions.Contains(action.Id, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Action '{action.Id}' is not allowed during phase '{phase.Id}'.");
        if (action.RequiresActivePlayer && !game.ActivePlayerId.Equals(actorId, StringComparison.Ordinal))
            throw new InvalidOperationException("It is not this player's priority.");

        var sourceCard = FindCard(game, actorId, request.SourceInstanceId);
        // Validate the card move up front so a rejected action never leaves its costs deducted.
        if (sourceCard is not null && !string.IsNullOrWhiteSpace(action.SourceZone) && !string.IsNullOrWhiteSpace(action.DestinationZone) &&
            game.Zones.GetValueOrDefault(actorId)?.GetValueOrDefault(action.SourceZone)?.Any(card => card.InstanceId == request.SourceInstanceId) != true)
            throw new InvalidOperationException($"Card is not in zone '{action.SourceZone}'.");
        foreach (var condition in action.Conditions ?? [])
            if (!ConditionMatches(game, actorId, request, condition))
                throw new InvalidOperationException($"Condition '{condition.Kind}' was not satisfied for action '{action.Id}'.");

        foreach (var cost in action.Costs ?? [])
        {
            var resources = PlayerResources(game, actorId);
            if (resources.GetValueOrDefault(cost.Key) < cost.Value)
                throw new InvalidOperationException($"Not enough {cost.Key} to perform '{action.Name}'.");
        }
        foreach (var cost in action.Costs ?? []) PlayerResources(game, actorId)[cost.Key] -= cost.Value;

        if (sourceCard is not null && !string.IsNullOrWhiteSpace(action.SourceZone) && !string.IsNullOrWhiteSpace(action.DestinationZone))
            MoveCard(game, actorId, request.SourceInstanceId, action.SourceZone, action.DestinationZone);

        foreach (var effect in action.Effects ?? []) ApplyEffect(pack, game, actorId, request, effect);
        if (sourceCard is not null)
        {
            var definition = pack.Cards.FirstOrDefault(card => card.Id.Equals(sourceCard.CardId, StringComparison.OrdinalIgnoreCase));
            foreach (var ability in definition?.Abilities.Where(ability => TriggerMatches(ability.Trigger, action.Id)) ?? [])
            {
                if ((ability.Conditions ?? []).All(condition => ConditionMatches(game, actorId, request, condition)))
                {
                    foreach (var effect in ability.Effects ?? []) ApplyEffect(pack, game, actorId, request, effect);
                    game.Events.Add(new("ability_resolved", $"{ability.Name} resolved after {action.Name}.", actorId, new { abilityId = ability.Id, actionId = action.Id }, DateTimeOffset.UtcNow));
                }
            }
        }
        game.ActionSequence++;
        game.Events.Add(new("action_executed", $"{actorId} performed {action.Name}.", actorId, new { action.Id, game.ActionSequence, request.Parameters }, DateTimeOffset.UtcNow));
        EvaluateVictoryRules(pack, game, actorId);
        return game;
    }

    public GameState EvaluateVictory(GameState game, string? actorId = null)
    {
        EvaluateVictoryRules(Get(game.RulePackId), game, actorId ?? game.ActivePlayerId);
        return game;
    }

    private static RulePack Normalize(RulePack source, List<string> warnings)
    {
        var cards = (source.Cards ?? []).Select(card => card with
        {
            Tags = card.Tags ?? [],
            Properties = card.Properties ?? [],
            Abilities = (card.Abilities ?? []).Select(ability => ability with
            {
                Conditions = ability.Conditions ?? [],
                Effects = ability.Effects ?? [],
                Metadata = ability.Metadata ?? []
            }).ToList()
        }).ToList();
        var startingRules = source.StartingRules ?? [];
        var zones = (source.Zones ?? []).Select(zone => zone with
        {
            OwnerScope = string.IsNullOrWhiteSpace(zone.OwnerScope) ? "player" : zone.OwnerScope,
            Visibility = string.IsNullOrWhiteSpace(zone.Visibility) ? "owner" : zone.Visibility
        }).ToList();
        var resources = (source.Resources ?? []).Select(resource => resource with
        {
            Scope = string.IsNullOrWhiteSpace(resource.Scope) ? "player" : resource.Scope
        }).ToList();
        var phases = (source.Phases ?? []).Select(phase => phase with
        {
            AllowedActions = phase.AllowedActions ?? [],
            Metadata = phase.Metadata ?? []
        }).ToList();
        var actions = (source.Actions ?? []).Select(action => action with
        {
            Costs = action.Costs ?? [],
            Conditions = action.Conditions ?? [],
            Effects = action.Effects ?? [],
            Metadata = action.Metadata ?? []
        }).ToList();
        if (zones.Count == 0 && phases.Count == 0 && actions.Count == 0)
        {
            warnings.Add("Legacy v1 rule pack upgraded to the system-neutral v2 schema.");
            zones =
            [
                new("deck", "Deck", "player", "hidden", true),
                new("hand", "Hand", "player", "owner", true),
                new("battlefield", "Play Area", "player", "public"),
                new("discard", "Discard", "player", "public", true)
            ];
            resources = [new("life", "Life", "player", startingRules.GetValueOrDefault("startingLife", 20), 0)];
            actions =
            [
                new("play_card", "Play card", "hand", "battlefield", true, null, [new("source_in_zone")], [new("end_turn")]),
                new("end_turn", "End turn", null, null, true, null, null, [new("end_turn")])
            ];
            phases = [new("main", "Main", ["play_card", "end_turn"])];
        }

        return source with
        {
            SchemaVersion = "2",
            GameSystem = string.IsNullOrWhiteSpace(source.GameSystem) ? "custom" : source.GameSystem.Trim(),
            Cards = cards,
            StartingRules = startingRules,
            Zones = zones,
            Resources = resources,
            Phases = phases,
            Actions = actions,
            StartingDeck = source.StartingDeck ?? [],
            Metadata = source.Metadata ?? []
        };
    }

    private static void ValidateEffectsAndConditions(List<TcgEffectDefinition>? effects, List<TcgConditionDefinition>? conditions, string owner, RulePack pack, List<string> errors)
    {
        foreach (var condition in conditions ?? [])
            if (!ConditionKinds.Contains(condition.Kind)) errors.Add($"{owner} uses unsupported condition '{condition.Kind}'.");
        foreach (var effect in effects ?? [])
        {
            if (!EffectKinds.Contains(effect.Kind)) errors.Add($"{owner} uses unsupported effect '{effect.Kind}'.");
            ValidateZoneReference(effect.FromZone, $"{owner} effect source", pack, errors);
            ValidateZoneReference(effect.ToZone, $"{owner} effect destination", pack, errors);
            if (!string.IsNullOrWhiteSpace(effect.ResourceId) && !pack.Resources.Any(resource => resource.Id.Equals(effect.ResourceId, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"{owner} references unknown resource '{effect.ResourceId}'.");
        }
    }

    private static void ValidateZoneReference(string? zoneId, string owner, RulePack pack, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(zoneId) && !pack.Zones.Any(zone => zone.Id.Equals(zoneId, StringComparison.OrdinalIgnoreCase)))
            errors.Add($"{owner} references unknown zone '{zoneId}'.");
    }

    private static void ValidateIdentifier(string value, string kind, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || !Identifier.IsMatch(value)) errors.Add($"{kind} ID '{value}' must use 1-80 letters, numbers, hyphens, or underscores.");
    }

    private static void ValidateUnique(IEnumerable<string> ids, string kind, List<string> errors)
    {
        var duplicates = ids.Where(id => !string.IsNullOrWhiteSpace(id)).GroupBy(id => id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key);
        foreach (var duplicate in duplicates) errors.Add($"Duplicate {kind} ID '{duplicate}'.");
    }

    private static bool ConditionMatches(GameState game, string actorId, ExecuteGameActionRequest request, TcgConditionDefinition condition)
    {
        if (condition.Kind.Equals("active_player", StringComparison.OrdinalIgnoreCase)) return game.ActivePlayerId == actorId;
        if (condition.Kind.Equals("source_in_zone", StringComparison.OrdinalIgnoreCase))
            return game.Zones.GetValueOrDefault(actorId)?.Values.SelectMany(cards => cards).Any(card => card.InstanceId == request.SourceInstanceId) == true;
        if (condition.Kind.Equals("resource_at_least", StringComparison.OrdinalIgnoreCase))
        {
            var minimum = condition.Value is { } value && value.TryGetInt64(out var parsed) ? parsed : 0;
            return PlayerResources(game, actorId).GetValueOrDefault(condition.Key ?? "") >= minimum;
        }
        if (condition.Kind.Equals("parameter_equals", StringComparison.OrdinalIgnoreCase))
            return request.Parameters?.TryGetValue(condition.Key ?? "", out var actual) == true && condition.Value is { } expected && actual.ToString() == expected.ToString();
        return false;
    }

    private static void ApplyEffect(RulePack pack, GameState game, string actorId, ExecuteGameActionRequest request, TcgEffectDefinition effect)
    {
        var targetPlayer = effect.Target?.Equals("target_player", StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(request.TargetPlayerId)
            ? request.TargetPlayerId
            : actorId;
        if (effect.Kind.Equals("draw", StringComparison.OrdinalIgnoreCase))
        {
            Draw(game, targetPlayer, effect.FromZone ?? "deck", effect.ToZone ?? "hand", checked((int)Math.Max(0, effect.Amount)));
            return;
        }
        if (effect.Kind.Equals("move_card", StringComparison.OrdinalIgnoreCase))
        {
            MoveCard(game, targetPlayer, request.TargetInstanceId ?? request.SourceInstanceId, effect.FromZone, effect.ToZone);
            return;
        }
        if (effect.Kind.Equals("modify_resource", StringComparison.OrdinalIgnoreCase))
        {
            var definition = pack.Resources.First(resource => resource.Id.Equals(effect.ResourceId, StringComparison.OrdinalIgnoreCase));
            var owner = definition.Scope.Equals("game", StringComparison.OrdinalIgnoreCase) ? SharedOwner : targetPlayer;
            var resources = game.Resources[owner];
            var next = resources.GetValueOrDefault(definition.Id) + effect.Amount;
            resources[definition.Id] = Math.Clamp(next, definition.MinimumValue, definition.MaximumValue ?? long.MaxValue);
            if (definition.Id.Equals("life", StringComparison.OrdinalIgnoreCase) && owner != SharedOwner) game.Life[owner] = checked((int)resources[definition.Id]);
            return;
        }
        if (effect.Kind.Equals("set_data", StringComparison.OrdinalIgnoreCase) && effect.Parameters is not null)
        {
            foreach (var item in effect.Parameters) game.Data[item.Key] = item.Value;
            return;
        }
        if (effect.Kind.Equals("emit_event", StringComparison.OrdinalIgnoreCase))
        {
            game.Events.Add(new(effect.EventType ?? "rules_event", effect.Message ?? "Rules event", actorId, effect.Parameters, DateTimeOffset.UtcNow));
            return;
        }
        if (effect.Kind.Equals("advance_phase", StringComparison.OrdinalIgnoreCase))
        {
            AdvancePhase(pack, game);
            return;
        }
        if (effect.Kind.Equals("end_turn", StringComparison.OrdinalIgnoreCase))
        {
            EndTurn(pack, game, actorId);
            return;
        }
        if (effect.Kind.Equals("finish_game", StringComparison.OrdinalIgnoreCase))
        {
            game.Finished = true;
            game.WinnerPlayerId = actorId;
            game.Events.Add(new("game_finished", $"{actorId} won the game.", actorId, new { reason = "finish_game" }, DateTimeOffset.UtcNow));
        }
    }

    private static CardInstance? FindCard(GameState game, string playerId, string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || !game.Zones.TryGetValue(playerId, out var zones)) return null;
        return zones.Values.SelectMany(cards => cards).FirstOrDefault(card => card.InstanceId == instanceId);
    }

    private static bool TriggerMatches(string trigger, string actionId) =>
        trigger.Equals("action_executed", StringComparison.OrdinalIgnoreCase) ||
        trigger.Equals(actionId, StringComparison.OrdinalIgnoreCase) ||
        trigger.Equals("action:" + actionId, StringComparison.OrdinalIgnoreCase);

    private static void EvaluateVictoryRules(RulePack pack, GameState game, string actorId)
    {
        if (game.Finished || !pack.Metadata.TryGetValue("victoryRules", out var element) || element.ValueKind != JsonValueKind.Array) return;
        var definitions = element.Deserialize<List<VictoryDefinition>>(new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true }) ?? [];
        foreach (var rule in definitions)
        {
            if (game.Finished) break;
            if (rule.Kind.Equals("turn-limit", StringComparison.OrdinalIgnoreCase) && Compare(game.Turn, rule.Operator, rule.Threshold))
            {
                FinishGame(game, actorId, rule, $"Turn limit {rule.Threshold} reached.");
                continue;
            }

            if (rule.Kind.Equals("last-player-standing", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var player in game.Players.Where(player => !game.EliminatedPlayerIds.Contains(player)).ToList())
                {
                    var value = game.Resources.GetValueOrDefault(player)?.GetValueOrDefault(rule.ResourceId) ?? 0;
                    if (Compare(value, rule.Operator, rule.Threshold)) Eliminate(game, player, rule);
                }
                var remaining = game.Players.Where(player => !game.EliminatedPlayerIds.Contains(player)).ToList();
                if (remaining.Count == 1) FinishGame(game, remaining[0], rule, $"{remaining[0]} is the last player standing.");
                continue;
            }

            foreach (var player in game.Players.Where(player => !game.EliminatedPlayerIds.Contains(player)).ToList())
            {
                var matched = false;
                if (rule.Kind.Equals("resource-threshold", StringComparison.OrdinalIgnoreCase))
                {
                    var owner = rule.Scope.Equals("game", StringComparison.OrdinalIgnoreCase) ? SharedOwner : player;
                    matched = Compare(game.Resources.GetValueOrDefault(owner)?.GetValueOrDefault(rule.ResourceId) ?? 0, rule.Operator, rule.Threshold);
                }
                else if (rule.Kind.Equals("empty-zone", StringComparison.OrdinalIgnoreCase))
                {
                    var owner = rule.Scope.Equals("game", StringComparison.OrdinalIgnoreCase) ? SharedOwner : player;
                    matched = game.Zones.GetValueOrDefault(owner)?.GetValueOrDefault(rule.ZoneId)?.Count == 0;
                }
                if (!matched) continue;
                if (rule.Outcome.Equals("eliminate", StringComparison.OrdinalIgnoreCase)) Eliminate(game, player, rule);
                else { FinishGame(game, player, rule, $"{player} satisfied {rule.Name}."); break; }
            }
            var survivors = game.Players.Where(player => !game.EliminatedPlayerIds.Contains(player)).ToList();
            if (!game.Finished && survivors.Count == 1) FinishGame(game, survivors[0], rule, $"{survivors[0]} is the final remaining player.");
        }
    }

    private static void Eliminate(GameState game, string playerId, VictoryDefinition rule)
    {
        if (game.EliminatedPlayerIds.Contains(playerId)) return;
        game.EliminatedPlayerIds.Add(playerId);
        game.Events.Add(new("player_eliminated", $"{playerId} was eliminated by {rule.Name}.", playerId, new { rule.Id }, DateTimeOffset.UtcNow));
    }

    private static void FinishGame(GameState game, string winner, VictoryDefinition rule, string message)
    {
        game.Finished = true;
        game.WinnerPlayerId = winner;
        game.VictoryRuleId = rule.Id;
        game.Events.Add(new("game_finished", message, winner, new { rule.Id, winner }, DateTimeOffset.UtcNow));
    }

    private static bool Compare(long actual, string comparison, long expected) => comparison switch
    {
        "less-than" => actual < expected,
        "less-than-or-equal" => actual <= expected,
        "equal" => actual == expected,
        "greater-than" => actual > expected,
        _ => actual >= expected
    };

    private static void EndTurn(RulePack pack, GameState game, string actorId)
    {
        var next = (game.Players.IndexOf(actorId) + 1) % game.Players.Count;
        game.ActivePlayerId = game.Players[next];
        game.CurrentPhaseId = pack.Phases[0].Id;
        game.Turn++;
    }

    private static void AdvancePhase(RulePack pack, GameState game)
    {
        var current = pack.Phases.FindIndex(phase => phase.Id.Equals(game.CurrentPhaseId, StringComparison.OrdinalIgnoreCase));
        var next = (current + 1) % pack.Phases.Count;
        if (next == 0) EndTurn(pack, game, game.ActivePlayerId);
        else game.CurrentPhaseId = pack.Phases[next].Id;
    }

    private static void Draw(GameState game, string playerId, string sourceZone, string destinationZone, int amount)
    {
        if (!game.Zones.TryGetValue(playerId, out var zones) || !zones.TryGetValue(sourceZone, out var source) || !zones.TryGetValue(destinationZone, out var destination)) return;
        for (var index = 0; index < amount && source.Count > 0; index++)
        {
            var card = source[^1];
            source.RemoveAt(source.Count - 1);
            destination.Add(card);
        }
    }

    private static void MoveCard(GameState game, string playerId, string? instanceId, string? sourceZone, string? destinationZone)
    {
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(sourceZone) || string.IsNullOrWhiteSpace(destinationZone))
            throw new InvalidOperationException("A card instance, source zone, and destination zone are required.");
        if (!game.Zones.TryGetValue(playerId, out var zones) || !zones.TryGetValue(sourceZone, out var source) || !zones.TryGetValue(destinationZone, out var destination))
            throw new InvalidOperationException("The requested zone is not available for this player.");
        var card = source.FirstOrDefault(candidate => candidate.InstanceId == instanceId) ?? throw new InvalidOperationException($"Card is not in zone '{sourceZone}'.");
        source.Remove(card);
        destination.Add(card);
    }

    private static Dictionary<string, long> PlayerResources(GameState game, string playerId) => game.Resources.TryGetValue(playerId, out var resources)
        ? resources
        : throw new InvalidOperationException("Player resources are not initialized.");
}
