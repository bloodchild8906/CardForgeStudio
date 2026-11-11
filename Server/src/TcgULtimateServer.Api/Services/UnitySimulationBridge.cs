using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using CardForge.GamePackages;
using CardForge.Simulation;
using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

/// <summary>
/// Authenticated API gateway -> Unity Dedicated Server simulation transport.
/// Authentication, deck ownership, wallets and packages stay with ASP.NET;
/// Unity is the sole owner of live match zones, resources, turns and actions.
/// </summary>
public sealed class UnitySimulationBridge : IDisposable
{
    private readonly HttpClient client;
    private readonly ConcurrentDictionary<string, SimMatch> games = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true
    };

    public bool Enabled { get; }
    public UnitySimulationBridge()
    {
        var url = Environment.GetEnvironmentVariable("CARDFORGE_SIMULATION_URL");
        Enabled = !string.IsNullOrWhiteSpace(url);
        client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        if (Enabled)
        {
            var uri = new Uri(url!);
            if (!uri.IsLoopback || uri.Scheme != Uri.UriSchemeHttp)
                throw new InvalidOperationException("Simulation gateway must use loopback HTTP.");
            client.BaseAddress = uri;
        }
    }

    public async Task<bool> Ready()
    {
        if (!Enabled) return false;
        try { return (await client.GetAsync("/health")).IsSuccessStatusCode; }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    public async Task<SimMatch> Create(RulePack pack, IReadOnlyList<string> humans, int opponents,
        bool backfill, int difficulty, IDictionary<string, List<string>> decks)
    {
        RequireEnabled();
        await Send<HostError>("/rules", Map(pack));
        var request = new HostCreateRequest
        {
            rulePackId = pack.Id, humans = humans.ToArray(), opponents = opponents,
            fillWithBots = backfill, difficulty = difficulty,
            decks = decks.Select(d => new HostDeck
            {
                playerId = d.Key, cardIds = d.Value.ToArray()
            }).ToArray()
        };
        var game = await Send<SimMatch>("/matches", request);
        games[game.id] = game;
        return game;
    }

    public async Task<SimMatch> Action(string id, string actor, ExecuteGameActionRequest request)
    {
        RequireEnabled();
        var action = new HostActionRequest
        {
            actorId = actor,
            action = new SimActionRequest
            {
                actionId = request.ActionId,
                sourceInstanceId = request.SourceInstanceId,
                targetInstanceId = request.TargetInstanceId,
                targetPlayerId = request.TargetPlayerId,
                parameters = (request.Parameters ?? new()).Select(pair =>
                    new SimParameter { key = pair.Key, value = pair.Value.ToString() }).ToList()
            }
        };
        var game = await Send<SimMatch>("/matches/" + Uri.EscapeDataString(id) + "/actions", action);
        games[id] = game;
        return game;
    }

    public async Task<SimMatchView> View(string id, string viewerId)
    {
        RequireEnabled();
        return await Send<SimMatchView>("/matches/" + Uri.EscapeDataString(id) + "/view",
            new SimParameter { key = "viewerId", value = viewerId });
    }

    public async Task<bool> Delete(string id)
    {
        RequireEnabled();
        var response = await client.DeleteAsync("/matches/" + Uri.EscapeDataString(id));
        games.TryRemove(id, out _);
        return response.IsSuccessStatusCode;
    }

    public bool TryCached(string id, out SimMatch game) => games.TryGetValue(id, out game!);
    public IReadOnlyCollection<SimMatch> Cached() => games.Values.ToList();

    public static object PublicSummary(SimMatch game) => new
    {
        id = game.id, rulePackId = game.rulePackId, players = game.players,
        activePlayerId = game.activePlayerId, currentPhaseId = game.currentPhaseId,
        turn = game.turn, finished = game.finished, winnerPlayerId = game.winnerPlayerId,
        opponentCount = game.opponentCount
    };

    public static ClientGameView Convert(SimMatchView view, RulePack pack)
    {
        var allowed = view.allowedActions
            .Select(id => pack.Actions.FirstOrDefault(action =>
                action.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Where(a => a is not null)
            .Select(action => new ClientGameAction(action!.Id, action.Name, action.SourceZone,
                action.DestinationZone, action.RequiresActivePlayer,
                action.Effects?.Select(e => e.Target).FirstOrDefault(t => !string.IsNullOrEmpty(t))))
            .ToList();
        return new ClientGameView(view.id, view.rulePackId, view.players, view.activePlayerId,
            view.currentPhaseId, view.turn, view.finished, view.winnerPlayerId, null,
            view.zones.Select(zone => new ClientGameZone(zone.ownerId, zone.zoneId,
                zone.cards.Select(card => new CardInstance(card.instanceId, card.cardId, card.ownerId)).ToList())).ToList(),
            view.resources.Select(resource => new ClientGameResource(resource.ownerId, resource.resourceId,
                resource.value)).ToList(),
            allowed, view.events.Select(evt =>
                new GameEvent(evt.type, evt.message, evt.actorId)).ToList())
        {
            TeamSize = view.opponentCount,
            Teams = view.teams.Select(member =>
                new ClientGameTeam(member.playerId, member.teamId)).ToList()
        };
    }

    public static SimRulePack Map(RulePack pack)
    {
        var sim = new SimRulePack
        {
            id = pack.Id, name = pack.Name, version = pack.Version,
            minimumPlayers = pack.MinimumPlayers, maximumPlayers = pack.MaximumPlayers,
            startingHand = pack.StartingRules.GetValueOrDefault("startingHand", 7),
            startingDeck = pack.StartingDeck.ToList(),
            cards = pack.Cards.Select(card => new SimCard
            {
                id = card.Id, name = card.Name, type = card.Type,
                cost = card.Cost, power = card.Power, toughness = card.Toughness,
                text = card.Text, assetId = card.AssetId,
                abilities = card.Abilities.Select(a => new SimAbility
                {
                    id = a.Id, name = a.Name, trigger = a.Trigger,
                    conditions = (a.Conditions ?? []).Select(Map).ToList(),
                    effects = (a.Effects ?? []).Select(Map).ToList()
                }).ToList()
            }).ToList(),
            zones = pack.Zones.Select(zone => new SimZone
            {
                id = zone.Id, name = zone.Name, ownerScope = zone.OwnerScope,
                visibility = zone.Visibility, ordered = zone.Ordered,
                capacity = zone.Capacity ?? 0
            }).ToList(),
            resources = pack.Resources.Select(resource => new SimResource
            {
                id = resource.Id, name = resource.Name, scope = resource.Scope,
                initialValue = resource.InitialValue, minimumValue = resource.MinimumValue,
                maximumValue = resource.MaximumValue ?? 0
            }).ToList(),
            phases = pack.Phases.Select(phase => new SimPhase
            {
                id = phase.Id, name = phase.Name,
                automaticAdvance = phase.AutomaticAdvance,
                allowedActions = phase.AllowedActions.ToList()
            }).ToList(),
            actions = pack.Actions.Select(action => new SimAction
            {
                id = action.Id, name = action.Name, sourceZone = action.SourceZone,
                destinationZone = action.DestinationZone,
                requiresActivePlayer = action.RequiresActivePlayer,
                costs = (action.Costs ?? new()).Select(cost => new SimCost
                {
                    resourceId = cost.Key, amount = cost.Value
                }).ToList(),
                conditions = (action.Conditions ?? []).Select(Map).ToList(),
                effects = (action.Effects ?? []).Select(Map).ToList()
            }).ToList(),
            triggers = pack.Triggers.Select(trigger => new SimTrigger
            {
                id = trigger.Id, name = trigger.Name, priority = trigger.Priority,
                causeActionIds = trigger.CauseActionIds.ToList(),
                responseActionIds = trigger.ResponseActionIds.ToList()
            }).ToList()
        };
        if (pack.Metadata.TryGetValue("victoryRules", out var victory) &&
            victory.ValueKind == JsonValueKind.Array)
        {
            var definitions = victory.Deserialize<List<VictoryDefinition>>(Json) ?? new();
            sim.victoryRules = definitions.Select(rule => new SimVictoryRule
            {
                id = rule.Id, name = rule.Name, kind = rule.Kind, outcome = rule.Outcome,
                scope = rule.Scope, resourceId = rule.ResourceId,
                zoneId = rule.ZoneId, comparison = rule.Operator,
                threshold = rule.Threshold
            }).ToList();
        }
        return sim;
    }

    private static SimCondition Map(TcgConditionDefinition condition) => new()
    {
        kind = condition.Kind, subject = condition.Subject, key = condition.Key,
        op = condition.Operator, value = condition.Value?.ToString()
    };

    private static SimEffect Map(TcgEffectDefinition effect) => new()
    {
        kind = effect.Kind, target = effect.Target, resourceId = effect.ResourceId,
        fromZone = effect.FromZone, toZone = effect.ToZone, amount = effect.Amount,
        eventType = effect.EventType, message = effect.Message,
        parameters = (effect.Parameters ?? new()).Select(pair =>
            new SimParameter { key = pair.Key, value = pair.Value.ToString() }).ToList()
    };

    private async Task<T> Send<T>(string path, object payload)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        using var response = await client.PostAsync(path,
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Unity simulation rejected request: " + body);
        return JsonSerializer.Deserialize<T>(body, Json) ??
            throw new InvalidOperationException("Unity simulation returned an empty response.");
    }
    private void RequireEnabled()
    {
        if (!Enabled) throw new InvalidOperationException("Unity simulation URL is not configured.");
    }
    public void Dispose() => client.Dispose();
}
