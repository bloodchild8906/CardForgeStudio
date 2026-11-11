using System;
using System.Collections.Generic;
using System.Linq;

namespace CardForge.Simulation
{
    /// <summary>
    /// Authoritative and Unity-compatible CardForge match simulation.
    /// Rule content, legal actions, trigger edges, resource pools, zones and victory
    /// are entirely data-driven. No UnityEngine/ASP.NET dependencies.
    /// Thread safety: call mutations under a host-level per-match lock.
    /// </summary>
    public sealed partial class SimulationEngine
    {
        public const string SharedOwner = "__game__";
        private readonly Dictionary<string, SimRulePack> rules =
            new Dictionary<string, SimRulePack>(StringComparer.OrdinalIgnoreCase);

        private static bool Same(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        public void Register(SimRulePack pack)
        {
            if (pack == null || string.IsNullOrWhiteSpace(pack.id))
                throw new InvalidOperationException("Ruleset ID is required.");
            if (pack.minimumPlayers < 2 || pack.maximumPlayers < pack.minimumPlayers)
                throw new InvalidOperationException("Ruleset player bounds are invalid.");
            if (pack.phases == null || pack.phases.Count == 0 || pack.actions == null ||
                pack.actions.Count == 0)
                throw new InvalidOperationException("Rules require phases and actions.");
            if (pack.zones == null || pack.zones.Count == 0)
                throw new InvalidOperationException("Rules require at least one card zone.");
            EnsureUnique(pack.zones.Select(z => z.id), "zone");
            EnsureUnique(pack.resources.Select(r => r.id), "resource");
            EnsureUnique(pack.cards.Select(c => c.id), "card");
            EnsureUnique(pack.actions.Select(a => a.id), "action");
            EnsureUnique(pack.phases.Select(p => p.id), "phase");
            EnsureUnique(pack.triggers.Select(t => t.id), "trigger");
            EnsureUnique(pack.starterDecks.Select(t => t.id), "starter deck");
            foreach (var zone in pack.zones)
            {
                if (zone.capacity < 0) throw new InvalidOperationException("Negative zone capacity.");
                if (zone.initialCardIds == null) zone.initialCardIds = new List<string>();
                if (zone.allowedCardTypes == null) zone.allowedCardTypes = new List<string>();
                if (zone.capacity > 0 && zone.initialCardIds.Count > zone.capacity)
                    throw new InvalidOperationException("Zone starts over capacity: " + zone.id);
                if (!string.IsNullOrWhiteSpace(zone.deckStartCardType))
                {
                    if (!Same(zone.ownerScope, "player") || zone.capacity != 1 ||
                        zone.initialCardIds.Count > 0 || !pack.cards.Any(card =>
                            DeckLegality.TypeMatches(card.type, zone.deckStartCardType)))
                        throw new InvalidOperationException(
                            "Special deck-start zone must be player-owned, capacity 1, empty, and reference a real card type: " + zone.id);
                    if (zone.immovable && zone.returnOnLeave)
                        throw new InvalidOperationException("A zone cannot be immovable and return-on-leave.");
                }
                foreach (var cardId in zone.initialCardIds)
                    ValidateZoneCard(pack, zone, cardId);
            }
            foreach (var starter in pack.starterDecks)
            {
                if (starter.cardIds.Count == 0 || starter.cardIds.Any(id =>
                    !pack.cards.Any(c => Same(c.id, id))))
                    throw new InvalidOperationException("Starter contains unknown cards: " + starter.id);
                var starterErrors = DeckLegality.ValidateDeck(pack, starter.cardIds);
                if (starterErrors.Count > 0)
                    throw new InvalidOperationException("Invalid starter " + starter.id + ": " +
                        string.Join(" ", starterErrors));
            }
            foreach (var phase in pack.phases)
                foreach (var action in phase.allowedActions)
                    if (!pack.actions.Any(a => Same(a.id, action)))
                        throw new InvalidOperationException("Phase references unknown action " + action);
            foreach (var action in pack.actions)
            {
                if (!string.IsNullOrEmpty(action.sourceZone) &&
                    !pack.zones.Any(z => Same(z.id, action.sourceZone)))
                    throw new InvalidOperationException("Unknown source zone " + action.sourceZone);
                if (!string.IsNullOrEmpty(action.destinationZone) &&
                    !pack.zones.Any(z => Same(z.id, action.destinationZone)))
                    throw new InvalidOperationException("Unknown destination zone " + action.destinationZone);
                foreach (var cost in action.costs)
                    if (!pack.resources.Any(r => Same(r.id, cost.resourceId)) || cost.amount < 0)
                        throw new InvalidOperationException("Invalid action resource cost.");
            }
            foreach (var trigger in pack.triggers)
            {
                foreach (var action in trigger.causeActionIds.Concat(trigger.responseActionIds))
                    if (!pack.actions.Any(a => Same(a.id, action)))
                        throw new InvalidOperationException("Trigger references unknown action " + action);
            }
            rules[pack.id] = pack;
        }

        private static bool ZoneAllows(SimZone zone, SimCard card)
        {
            if (zone.allowedCardTypes == null || zone.allowedCardTypes.Count == 0)
                return true;
            var cardType = card.type ?? "";
            return zone.allowedCardTypes.Any(allowed =>
                Same(cardType, allowed) ||
                (cardType.Length > allowed.Length &&
                 cardType.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) &&
                 !char.IsLetterOrDigit(cardType[allowed.Length])));
        }

        private static void ValidateZoneCard(SimRulePack pack, SimZone zone, string cardId)
        {
            var definition = pack.cards.FirstOrDefault(card => Same(card.id, cardId));
            if (definition == null)
                throw new InvalidOperationException("Zone " + zone.id + " references missing card " + cardId);
            if (!ZoneAllows(zone, definition))
                throw new InvalidOperationException("Zone " + zone.id + " rejects card type " +
                    definition.type + " (" + definition.id + ")");
        }

        private static void SeedZone(SimRulePack pack, SimMatch game,
            string owner, SimZone zone, IEnumerable<string> cardIds)
        {
            var destination = FindZone(game, owner, zone.id);
            foreach (var cardId in cardIds)
            {
                ValidateZoneCard(pack, zone, cardId);
                if (zone.capacity > 0 && destination.cards.Count >= zone.capacity)
                    throw new InvalidOperationException("Zone " + zone.id + " starts over capacity.");
                destination.cards.Add(new SimCardInstance(Guid.NewGuid().ToString("N"), cardId, owner));
            }
        }

        private static void EnsureZoneCanReceive(SimRulePack pack, SimMatch game, string owner,
            string zoneId, string cardId)
        {
            var zone = pack.zones.FirstOrDefault(z => Same(z.id, zoneId));
            if (zone == null) throw new InvalidOperationException("Unknown zone " + zoneId);
            ValidateZoneCard(pack, zone, cardId);
            var dest = FindZone(game, owner, zoneId);
            if (zone.capacity > 0 && dest.cards.Count >= zone.capacity)
                throw new InvalidOperationException("Zone is full: " + zoneId);
        }

        private static void EnsureUnique(IEnumerable<string> ids, string kind)
        {
            if (ids.Any(string.IsNullOrWhiteSpace) ||
                ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Count())
                throw new InvalidOperationException("Invalid or duplicate " + kind + " IDs.");
        }

        public SimRulePack GetRules(string id)
        {
            SimRulePack result;
            if (!rules.TryGetValue(id, out result))
                throw new KeyNotFoundException("Unknown CardForge ruleset " + id);
            return result;
        }

        public SimMatch Create(string rulePackId, IList<string> humans, int opponents,
            bool fillWithBots, int? randomSeed = null, IDictionary<string, IList<string>> decks = null)
        {
            var pack = GetRules(rulePackId);
            if (opponents < 1 || opponents > 4)
                throw new InvalidOperationException("Only 1v1, 1v2, 1v3 and 1v4 are supported.");
            if (humans == null || humans.Count == 0 ||
                humans.Distinct(StringComparer.Ordinal).Count() != humans.Count)
                throw new InvalidOperationException("Unique human player IDs are required.");
            var seats = opponents + 1;
            if (seats < pack.minimumPlayers || seats > pack.maximumPlayers)
                throw new InvalidOperationException("Ruleset does not support " + seats + " players.");
            if (humans.Count > seats || (!fillWithBots && humans.Count != seats))
                throw new InvalidOperationException("Match has unfilled or extra seats.");

            var game = new SimMatch
            {
                id = Guid.NewGuid().ToString("N"), rulePackId = pack.id, opponentCount = opponents
            };
            foreach (var id in humans) game.players.Add(id);
            while (game.players.Count < seats)
                game.players.Add("bot-" + Guid.NewGuid().ToString("N"));
            for (var i = 0; i < game.players.Count; i++)
                game.teams.Add(new SimTeamMember { playerId = game.players[i], teamId = i == 0 ? 0 : 1 });

            var random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
            foreach (var player in game.players.Concat(new[] { SharedOwner }))
            {
                var shared = player == SharedOwner;
                foreach (var zone in pack.zones.Where(z => Same(z.ownerScope, shared ? "game" : "player")))
                    game.zones.Add(new SimZoneState { ownerId = player, zoneId = zone.id });
                foreach (var zone in pack.zones.Where(z => Same(z.ownerScope, shared ? "game" : "player")))
                    SeedZone(pack, game, player, zone, zone.initialCardIds ?? new List<string>());
                foreach (var resource in pack.resources.Where(r => Same(r.scope, shared ? "game" : "player")))
                    game.resources.Add(new SimResourceState
                    {
                        ownerId = player, resourceId = resource.id, value = resource.initialValue
                    });
                if (shared) continue;

                var seatIndex = game.players.FindIndex(id => Same(id, player));
                IList<string> requested = decks != null && decks.ContainsKey(player) ?
                    decks[player] : (pack.starterDecks.Count > 0 ?
                        pack.starterDecks[seatIndex % pack.starterDecks.Count].cardIds : pack.startingDeck);
                if (requested == null || requested.Count == 0)
                    requested = pack.cards.Select(c => c.id).ToList();
                var deckErrors = DeckLegality.ValidateDeck(pack, requested);
                if (deckErrors.Count > 0)
                    throw new InvalidOperationException("Illegal deck for " + player + ": " +
                        string.Join(" ", deckErrors));
                var template = pack.starterDecks.FirstOrDefault(starter =>
                    starter.cardIds.Count == requested.Count &&
                    starter.cardIds.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                        .All(group => requested.Count(id => Same(id, group.Key)) == group.Count()));
                game.playerDecks.Add(new SimPlayerDeck
                {
                    playerId = player, cardIds = requested.ToList(),
                    starterDeckId = template?.id
                });
                // The full deck list has 42 cards, but the Commander and Mana Source
                // never enter the shuffled main deck. Both are seeded by shared zones.
                var mainCards = requested.ToList();
                foreach (var special in pack.zones.Where(zone =>
                    Same(zone.ownerScope, "player") && !string.IsNullOrWhiteSpace(zone.deckStartCardType)))
                {
                    var startingCard = mainCards.First(id => pack.cards.Any(card =>
                        Same(card.id, id) &&
                        DeckLegality.TypeMatches(card.type, special.deckStartCardType)));
                    mainCards.Remove(startingCard);
                    SeedZone(pack, game, player, special, new[] { startingCard });
                }
                var deck = FindZone(game, player, "deck", false);
                if (deck == null)
                {
                    if (requested.Count > 0) throw new InvalidOperationException("Deck zone is missing.");
                    continue;
                }
                foreach (var card in mainCards)
                    deck.cards.Add(new SimCardInstance(Guid.NewGuid().ToString("N"), card, player));
                for (var i = deck.cards.Count - 1; i > 0; i--)
                {
                    var index = random.Next(i + 1);
                    var swap = deck.cards[i];
                    deck.cards[i] = deck.cards[index];
                    deck.cards[index] = swap;
                }
                Draw(pack, game, player, "deck", "hand", Math.Min(pack.startingHand, deck.cards.Count));
            }
            game.currentPhaseId = pack.phases[0].id;
            game.activePlayerId = game.players[0];
            Emit(game, "game_started", "Match started: 1v" + opponents, game.activePlayerId);
            ApplyManaSourceTurnStart(pack, game);
            Evaluate(game, game.activePlayerId);
            return game;
        }

        private static SimZoneState FindZone(SimMatch game, string owner, string id, bool require = true)
        {
            var zone = game.zones.FirstOrDefault(z => Same(z.ownerId, owner) && Same(z.zoneId, id));
            if (require && zone == null) throw new InvalidOperationException("Zone not found: " + owner + "/" + id);
            return zone;
        }
        private static SimResourceState FindResource(SimMatch game, string owner, string id)
        {
            var resource = game.resources.FirstOrDefault(r => Same(r.ownerId, owner) && Same(r.resourceId, id));
            if (resource == null) throw new InvalidOperationException("Resource not found: " + owner + "/" + id);
            return resource;
        }
        private static void Emit(SimMatch game, string type, string message, string actor)
        {
            game.events.Add(new SimEvent
            {
                type = type, message = message, actorId = actor, sequence = game.actionSequence
            });
        }

        public void Execute(SimMatch game, string actorId, SimActionRequest request)
        {
            if (game == null || request == null) throw new ArgumentNullException("game/request");
            var pack = GetRules(game.rulePackId);
            if (game.finished) throw new InvalidOperationException("Match is finished.");
            if (!game.players.Any(id => Same(id, actorId)) || game.eliminated.Contains(actorId))
                throw new InvalidOperationException("Actor is not an active participant.");
            // Nonactive-player response actions may be enabled by the turn designer.
            // Only actions explicitly marked as active-only require turn ownership.
            var phase = pack.phases.FirstOrDefault(p => Same(p.id, game.currentPhaseId));
            var action = pack.actions.FirstOrDefault(a => Same(a.id, request.actionId));
            if (phase == null || action == null || !phase.allowedActions.Any(id => Same(id, action.id)))
                throw new InvalidOperationException("Action is not allowed in this phase.");
            if (action.requiresActivePlayer && !Same(game.activePlayerId, actorId))
                throw new InvalidOperationException("It is not this player's turn.");

            SimCardInstance source = null;
            if (!string.IsNullOrWhiteSpace(action.sourceZone))
            {
                source = FindZone(game, actorId, action.sourceZone).cards
                    .FirstOrDefault(c => Same(c.instanceId, request.sourceInstanceId));
                if (source == null) throw new InvalidOperationException("Source card not owned in the required zone.");
            }
            if (!string.IsNullOrEmpty(request.targetPlayerId) &&
                (!game.players.Contains(request.targetPlayerId) ||
                 game.eliminated.Contains(request.targetPlayerId)))
                throw new InvalidOperationException("Invalid target player.");
            if (!string.IsNullOrEmpty(request.targetInstanceId) &&
                !game.zones.Any(z => z.cards.Any(c => Same(c.instanceId, request.targetInstanceId))))
                throw new InvalidOperationException("Target card does not exist.");

            if (!ConditionsSatisfied(game, actorId, request, action.conditions))
                throw new InvalidOperationException("Action conditions failed.");
            if (source != null)
            {
                var originZone = pack.zones.FirstOrDefault(z => Same(z.id, action.sourceZone));
                if (originZone?.immovable == true && !Same(action.id, "ar-level-mana"))
                    throw new InvalidOperationException("Cards in the Mana Source zone cannot move.");
                var cardDefinition = pack.cards.First(c => Same(c.id, source.cardId));
                var specialHome = pack.zones.FirstOrDefault(z =>
                    z.returnOnLeave && DeckLegality.TypeMatches(cardDefinition.type, z.deckStartCardType));
                if (specialHome != null && Same(action.sourceZone, specialHome.id) &&
                    !Same(action.destinationZone, "battlefield"))
                    throw new InvalidOperationException("Commander must be cast onto the battlefield.");
            }
            if (source != null && !string.IsNullOrWhiteSpace(action.destinationZone))
                EnsureZoneCanReceive(pack, game, actorId, action.destinationZone, source.cardId);
            if (!CanPay(pack, game, actorId, action.costs))
                throw new InvalidOperationException("Insufficient resource pool.");
            var extraEnergyCost = 0L;
            var baseEnergy = action.costs.Where(cost => Same(cost.resourceId, "energy"))
                .Sum(cost => cost.amount);
            if (source != null && Same(pack.id, "astral-rift"))
                extraEnergyCost = AstralExtraCost(pack, game, actorId, action, source, request);
            else if (source != null && Same(action.sourceZone, "hand") &&
                Same(action.destinationZone, "battlefield"))
            {
                var card = pack.cards.FirstOrDefault(entry => Same(entry.id, source.cardId));
                extraEnergyCost = Math.Max(0, (card?.cost ?? 0) - baseEnergy);
            }
            if (extraEnergyCost > 0 &&
                FindResource(game, actorId, "energy").value < baseEnergy + extraEnergyCost)
                throw new InvalidOperationException("Insufficient mana to perform " + action.name + ".");
            Pay(pack, game, actorId, action.costs);
            if (extraEnergyCost > 0)
                FindResource(game, actorId, "energy").value -= extraEnergyCost;

            if (source != null && !string.IsNullOrWhiteSpace(action.destinationZone))
                Move(pack, game, actorId, source.instanceId, action.sourceZone, action.destinationZone);
            foreach (var effect in action.effects)
                Apply(pack, game, actorId, request, effect);

            if (source != null)
            {
                var definition = pack.cards.FirstOrDefault(c => Same(c.id, source.cardId));
                if (definition != null)
                foreach (var ability in definition.abilities)
                {
                    if (!(Same(ability.trigger, action.id) || Same(ability.trigger, "action_executed") ||
                          Same(ability.trigger, "action:" + action.id))) continue;
                    if (!ConditionsSatisfied(game, actorId, request, ability.conditions)) continue;
                    foreach (var effect in ability.effects) Apply(pack, game, actorId, request, effect);
                    Emit(game, "ability_resolved", ability.name + " resolved.", actorId);
                }
            }

            // Trigger graph edges cannot recurse: only effects of response actions execute.
            foreach (var trigger in pack.triggers
                .Where(t => t.causeActionIds.Any(id => Same(id, action.id)))
                .OrderByDescending(t => t.priority))
                foreach (var responseId in trigger.responseActionIds)
                {
                    var response = pack.actions.First(a => Same(a.id, responseId));
                    if (!ConditionsSatisfied(game, actorId, request, response.conditions) ||
                        !CanPay(pack, game, actorId, response.costs)) continue;
                    Pay(pack, game, actorId, response.costs);
                    foreach (var effect in response.effects) Apply(pack, game, actorId, request, effect);
                    Emit(game, "trigger_resolved", trigger.name + " → " + response.name, actorId);
                }

            game.actionSequence++;
            Emit(game, "action_executed", actorId + " performed " + action.name, actorId);
            Evaluate(game, actorId);
        }

        private static bool ConditionsSatisfied(SimMatch game, string actor, SimActionRequest request,
            List<SimCondition> conditions)
        {
            foreach (var condition in conditions ?? new List<SimCondition>())
            {
                if (Same(condition.kind, "active_player"))
                {
                    if (!Same(game.activePlayerId, actor)) return false;
                }
                else if (Same(condition.kind, "source_in_zone"))
                {
                    if (!game.zones.Any(z => Same(z.ownerId, actor) &&
                        z.cards.Any(c => Same(c.instanceId, request.sourceInstanceId)))) return false;
                }
                else if (Same(condition.kind, "resource_at_least"))
                {
                    long required;
                    if (!long.TryParse(condition.value ?? "0", out required)) required = 0;
                    if (FindResource(game, actor, condition.key).value < required) return false;
                }
                else if (Same(condition.kind, "parameter_equals"))
                {
                    if (!(request.parameters ?? new List<SimParameter>()).Any(p =>
                        Same(p.key, condition.key) && Same(p.value, condition.value))) return false;
                }
                else if (Same(condition.kind, "turn_at_least"))
                {
                    if (!int.TryParse(condition.value, out var minimumTurn) ||
                        game.turn < minimumTurn) return false;
                }
                else if (Same(condition.kind, "friendly_board_at_least") ||
                    Same(condition.kind, "own_discard_at_least"))
                {
                    var zone = Same(condition.kind, "friendly_board_at_least") ?
                        "battlefield" : "discard";
                    if (!int.TryParse(condition.value, out var minimumCount) ||
                        (FindZone(game, actor, zone, false)?.cards.Count ?? 0) < minimumCount)
                        return false;
                }
                else if (Same(condition.kind, "enemy_life_at_most"))
                {
                    if (!long.TryParse(condition.value, out var cap)) return false;
                    var team = game.teams.FirstOrDefault(t => Same(t.playerId, actor))?.teamId ?? 0;
                    if (!game.teams.Any(member => member.teamId != team &&
                        FindResource(game, member.playerId, "life").value <= cap)) return false;
                }
                else return false;
            }
            return true;
        }
        private static bool CanPay(SimRulePack pack, SimMatch game, string actor, List<SimCost> costs)
        {
            return (costs ?? new List<SimCost>()).All(cost =>
            {
                var definition = pack.resources.FirstOrDefault(r => Same(r.id, cost.resourceId));
                if (definition == null || cost.amount < 0) return false;
                var owner = Same(definition.scope, "game") ? SharedOwner : actor;
                return FindResource(game, owner, definition.id).value >= cost.amount;
            });
        }
        private static void Pay(SimRulePack pack, SimMatch game, string actor, List<SimCost> costs)
        {
            foreach (var cost in costs ?? new List<SimCost>())
            {
                var def = pack.resources.First(r => Same(r.id, cost.resourceId));
                FindResource(game, Same(def.scope, "game") ? SharedOwner : actor, def.id).value -= cost.amount;
            }
        }

        private static void Draw(SimRulePack pack, SimMatch game, string owner, string from, string to, int count)
        {
            var source = FindZone(game, owner, from, false);
            var dest = FindZone(game, owner, to, false);
            if (source == null || dest == null) return;
            var originDefinition = pack.zones.FirstOrDefault(z => Same(z.id, from));
            if (originDefinition != null && originDefinition.immovable)
                throw new InvalidOperationException("Cannot draw or move cards from an immovable zone.");
            var definition = pack.zones.FirstOrDefault(z => Same(z.id, to));
            for (var i = 0; i < count && source.cards.Count > 0; i++)
            {
                if (definition != null && definition.capacity > 0 && dest.cards.Count >= definition.capacity) break;
                var card = source.cards[source.cards.Count - 1];
                if (definition != null && !ZoneAllows(definition,
                    pack.cards.First(c => Same(c.id, card.cardId)))) break;
                source.cards.RemoveAt(source.cards.Count - 1);
                dest.cards.Add(card);
            }
        }
        private static void Move(SimRulePack pack, SimMatch game, string owner, string cardId, string from, string to)
        {
            if (string.IsNullOrWhiteSpace(cardId))
                throw new InvalidOperationException("Card instance must be selected.");
            var source = FindZone(game, owner, from);
            var card = source.cards.FirstOrDefault(c => Same(c.instanceId, cardId));
            if (card == null) throw new InvalidOperationException("Card not in the source zone.");
            var definition = pack.cards.First(c => Same(c.id, card.cardId));
            var home = pack.zones.FirstOrDefault(zone => !string.IsNullOrWhiteSpace(zone.deckStartCardType) &&
                DeckLegality.TypeMatches(definition.type, zone.deckStartCardType));
            if (home != null && home.immovable)
                throw new InvalidOperationException("Mana Sources cannot be moved or removed.");
            // Commander can only leave Command through casting onto the battlefield.
            if (home != null && Same(from, home.id) && !Same(to, "battlefield"))
                throw new InvalidOperationException("Commander must be cast onto the battlefield.");
            // Removal, exile and return-to-hand effects instead return a cast Commander home.
            var commanderReturned = home != null && home.returnOnLeave &&
                Same(from, "battlefield") && !Same(to, "battlefield");
            if (commanderReturned) to = home.id;
            if (Same(from, to)) return;
            EnsureZoneCanReceive(pack, game, owner, to, card.cardId);
            var dest = FindZone(game, owner, to);
            source.cards.Remove(card);
            dest.cards.Add(card);
            // Damage is reset when a card changes zones.
            PutState(game, "damage:" + card.instanceId, 0);
            if (Same(to, "battlefield"))
                PutState(game, "entered:" + card.instanceId, game.turn);
            if (commanderReturned)
            {
                var next = StateNumber(game, "tax:" + card.instanceId) + 1;
                PutState(game, "tax:" + card.instanceId, next);
                Emit(game, "commander_returned", definition.name +
                    " returned to Command; future cast cost increased by +" + (next * 2) + ".", owner);
            }
        }
        private static void Apply(SimRulePack pack, SimMatch game, string actor, SimActionRequest request, SimEffect effect)
        {
            var ownTeam = game.teams.FirstOrDefault(team => Same(team.playerId, actor))?.teamId ?? 0;
            var enemies = game.players.Where(player =>
                !game.eliminated.Contains(player) &&
                game.teams.Any(team => Same(team.playerId, player) && team.teamId != ownTeam)).ToList();
            var target = (Same(effect.target, "opponent") || Same(effect.target, "enemy_team") ||
                Same(effect.target, "target_player")) ?
                (!string.IsNullOrWhiteSpace(request.targetPlayerId) &&
                 enemies.Contains(request.targetPlayerId) ? request.targetPlayerId :
                 enemies.FirstOrDefault() ?? actor) : actor;
            switch ((effect.kind ?? "").ToLowerInvariant())
            {
                case "level_mana_source":
                    LevelManaSource(pack, game, actor, request);
                    break;
                case "attack_player":
                    ResolveAttack(pack, game, actor, request, false);
                    break;
                case "attack_creature":
                    ResolveAttack(pack, game, actor, request, true);
                    break;
                case "draw":
                    Draw(pack, game, target, string.IsNullOrEmpty(effect.fromZone) ? "deck" : effect.fromZone,
                        string.IsNullOrEmpty(effect.toZone) ? "hand" : effect.toZone,
                        (int)Math.Min(Math.Max(effect.amount, 0), 120));
                    break;
                case "move_card":
                    Move(pack, game, target, request.targetInstanceId ?? request.sourceInstanceId,
                        effect.fromZone, effect.toZone);
                    break;
                case "modify_resource":
                    if (Same(effect.target, "enemy_team"))
                    {
                        foreach (var enemy in enemies)
                            ModifyPool(pack, game, enemy, effect.resourceId, effect.amount);
                    }
                    else ModifyPool(pack, game, target, effect.resourceId, effect.amount);
                    break;
                case "scale_resource":
                    // Each friendly card on the stated board multiplies the bonus.
                    var zoneId = string.IsNullOrWhiteSpace(effect.fromZone) ? "battlefield" : effect.fromZone;
                    var boardSize = FindZone(game, actor, zoneId, false)?.cards.Count ?? 0;
                    var scaled = Math.Max(-200, Math.Min(200, effect.amount * Math.Min(boardSize, 20)));
                    if (Same(effect.target, "enemy_team"))
                    {
                        foreach (var enemy in enemies)
                            ModifyPool(pack, game, enemy, effect.resourceId, scaled);
                    }
                    else ModifyPool(pack, game, target, effect.resourceId, scaled);
                    break;
                case "drain_resource":
                    if (target == actor) break;
                    var amountToDrain = Math.Max(0, Math.Min(effect.amount, 20));
                    var poolToDrain = FindResource(game, target, effect.resourceId);
                    var drained = Math.Min(amountToDrain, Math.Max(0, poolToDrain.value));
                    ModifyPool(pack, game, target, effect.resourceId, -drained);
                    ModifyPool(pack, game, actor, effect.resourceId, drained);
                    break;
                case "mill":
                    // Move the top cards of an opponent's deck into their discard.
                    var millCount = (int)Math.Min(12, Math.Max(0, effect.amount));
                    var otherDeck = FindZone(game, target, "deck", false);
                    var otherDiscard = FindZone(game, target, "discard", false);
                    if (otherDeck != null && otherDiscard != null)
                        for (var i = 0; i < millCount && otherDeck.cards.Count > 0; i++)
                        {
                            var last = otherDeck.cards.Count - 1;
                            var discarded = otherDeck.cards[last];
                            otherDeck.cards.RemoveAt(last);
                            otherDiscard.cards.Add(discarded);
                        }
                    break;
                case "reclaim":
                    // Salvage the most recently discarded card, respecting hand capacity.
                    Draw(pack, game, actor, "discard", "hand",
                        (int)Math.Min(8, Math.Max(0, effect.amount)));
                    break;
                case "destroy_target":
                    var enemyCard = game.zones.Where(z => enemies.Contains(z.ownerId) &&
                        Same(z.zoneId, "battlefield")).SelectMany(z => z.cards)
                        .FirstOrDefault(c => Same(c.instanceId, request.targetInstanceId));
                    if (enemyCard == null)
                        enemyCard = game.zones.Where(z => enemies.Contains(z.ownerId) &&
                            Same(z.zoneId, "battlefield")).SelectMany(z => z.cards).FirstOrDefault();
                    if (enemyCard != null)
                        Move(pack, game, enemyCard.ownerId, enemyCard.instanceId, "battlefield", "discard");
                    break;
                case "emit_event":
                    Emit(game, effect.eventType ?? "rules_event", effect.message ?? "Rules event", actor);
                    break;
                case "advance_phase":
                    AdvancePhase(pack, game);
                    break;
                case "end_turn":
                    EndTurn(pack, game);
                    break;
                case "finish_game":
                    Finish(game, actor, "finish_game");
                    break;
                case "set_data":
                    foreach (var item in effect.parameters ?? new List<SimParameter>())
                    {
                        var existing = game.data.FirstOrDefault(entry => Same(entry.key, item.key));
                        if (existing != null) existing.value = item.value;
                        else game.data.Add(new SimParameter { key = item.key, value = item.value });
                    }
                    Emit(game, "set_data", "Game metadata updated.", actor);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported rule effect " + effect.kind);
            }
        }
        private static void ModifyPool(SimRulePack pack, SimMatch game, string owner,
            string resourceId, long delta)
        {
            var definition = pack.resources.FirstOrDefault(r => Same(r.id, resourceId));
            if (definition == null)
                throw new InvalidOperationException("Unknown non-physical resource " + resourceId);
            var resource = FindResource(game, Same(definition.scope, "game") ? SharedOwner : owner,
                definition.id);
            // Shields intercept incoming life damage and are spent before life.
            if (Same(resourceId, "life") && delta < 0)
            {
                var shield = game.resources.FirstOrDefault(r =>
                    Same(r.ownerId, owner) && Same(r.resourceId, "shield"));
                if (shield != null && shield.value > 0)
                {
                    var absorbed = Math.Min(shield.value, -delta);
                    shield.value -= absorbed;
                    delta += absorbed;
                }
            }
            var next = delta > 0 && resource.value > long.MaxValue - delta ?
                long.MaxValue : delta < 0 && resource.value < long.MinValue - delta ?
                long.MinValue : resource.value + delta;
            resource.value = Math.Max(definition.minimumValue,
                definition.maximumValue > 0 ? Math.Min(definition.maximumValue, next) : next);
        }

        private static void ApplyManaSourceTurnStart(SimRulePack pack, SimMatch game)
        {
            foreach (var zoneRule in pack.zones.Where(z => z.immovable &&
                DeckLegality.TypeMatches(z.deckStartCardType, "ManaSource")))
            {
                var card = FindZone(game, game.activePlayerId, zoneRule.id, false)?.cards
                    .FirstOrDefault();
                if (card == null) continue;
                if (pack.resources.Any(resource => Same(resource.id, "energy")))
                    ModifyPool(pack, game, game.activePlayerId, "energy", 1);
                var definition = pack.cards.FirstOrDefault(c => Same(c.id, card.cardId));
                if (definition == null) continue;
                var request = new SimActionRequest
                {
                    actionId = "turn_started", sourceInstanceId = card.instanceId
                };
                foreach (var ability in definition.abilities ?? new List<SimAbility>())
                {
                    if (!Same(ability.trigger, "turn_started") ||
                        !ConditionsSatisfied(game, game.activePlayerId, request,
                            ability.conditions)) continue;
                    foreach (var effect in ability.effects)
                    {
                        // Prevent custom passives from recursively opening turns or
                        // moving the permanently attached mana card.
                        if (Same(effect.kind, "end_turn") || Same(effect.kind, "advance_phase") ||
                            Same(effect.kind, "move_card") || Same(effect.kind, "finish_game"))
                            throw new InvalidOperationException(
                                "Mana Source turn-start effects may not change turn control or move cards.");
                        Apply(pack, game, game.activePlayerId, request, effect);
                    }
                    Emit(game, "ability_resolved", ability.name + " activated from Mana Source.",
                        game.activePlayerId);
                }
            }
        }

        private static void EndTurn(SimRulePack pack, SimMatch game)
        {
            var i = game.players.FindIndex(p => Same(p, game.activePlayerId));
            for (var attempt = 0; attempt < game.players.Count; attempt++)
            {
                i = (i + 1) % game.players.Count;
                if (!game.eliminated.Contains(game.players[i])) break;
            }
            game.activePlayerId = game.players[i];
            game.currentPhaseId = pack.phases[0].id;
            game.turn++;
            foreach (var zone in game.zones.Where(z => Same(z.zoneId, "battlefield")))
                foreach (var card in zone.cards)
                    PutState(game, "damage:" + card.instanceId, 0);
            ApplyManaSourceTurnStart(pack, game);
        }
        private static void AdvancePhase(SimRulePack pack, SimMatch game)
        {
            var i = pack.phases.FindIndex(p => Same(p.id, game.currentPhaseId));
            if (i < 0 || i == pack.phases.Count - 1) EndTurn(pack, game);
            else game.currentPhaseId = pack.phases[i + 1].id;
        }
        private static bool Compare(long actual, string op, long threshold)
        {
            switch ((op ?? "").ToLowerInvariant())
            {
                case "less-than": return actual < threshold;
                case "less-than-or-equal": return actual <= threshold;
                case "equal": return actual == threshold;
                case "greater-than": return actual > threshold;
                case "greater-than-or-equal": return actual >= threshold;
                default: return actual >= threshold;
            }
        }
        private static void Finish(SimMatch game, string winner, string reason)
        {
            if (game.finished) return;
            game.finished = true;
            game.winnerPlayerId = winner;
            Emit(game, "game_finished", winner + " wins (" + reason + ")", winner);
        }
        public void Evaluate(SimMatch game, string actor = null)
        {
            if (game.finished) return;
            var pack = GetRules(game.rulePackId);
            foreach (var rule in pack.victoryRules)
            {
                if (game.finished) break;
                if (Same(rule.kind, "turn-limit"))
                {
                    if (Compare(game.turn, rule.comparison, rule.threshold))
                        Finish(game, actor ?? game.activePlayerId, rule.name);
                    continue;
                }
                foreach (var player in game.players.Where(p => !game.eliminated.Contains(p)).ToList())
                {
                    bool applies = false;
                    if (Same(rule.kind, "resource-threshold") || Same(rule.kind, "last-player-standing"))
                    {
                        var pool = game.resources.FirstOrDefault(r => Same(r.ownerId, player) &&
                            Same(r.resourceId, rule.resourceId));
                        applies = pool != null && Compare(pool.value, rule.comparison, rule.threshold);
                    }
                    else if (Same(rule.kind, "empty-zone"))
                    {
                        var zone = FindZone(game, Same(rule.scope, "game") ? SharedOwner : player,
                            rule.zoneId, false);
                        applies = zone != null && zone.cards.Count == 0;
                    }
                    if (!applies) continue;
                    if (Same(rule.outcome, "eliminate") || Same(rule.kind, "last-player-standing"))
                    {
                        if (!game.eliminated.Contains(player))
                        {
                            game.eliminated.Add(player);
                            Emit(game, "player_eliminated", player + " eliminated", player);
                        }
                    }
                    else if (Same(rule.outcome, "win"))
                    {
                        // Player-scoped wins apply to their entire team.
                        Finish(game, player, rule.name);
                        break;
                    }
                }

                if (!game.finished)
                {
                    var alive = game.players.Where(p => !game.eliminated.Contains(p)).ToList();
                    var teams = game.teams.Where(t => alive.Contains(t.playerId))
                        .Select(t => t.teamId).Distinct().ToList();
                    if (teams.Count == 1 && alive.Count > 0)
                        Finish(game, alive[0], "team eliminated");
                }
            }
        }

        public SimMatchView View(SimMatch game, string viewerId)
        {
            var pack = GetRules(game.rulePackId);
            if (!game.players.Any(p => Same(p, viewerId)))
                throw new UnauthorizedAccessException("Match viewer is not a participant.");
            var view = new SimMatchView
            {
                id = game.id, rulePackId = game.rulePackId, players = game.players.ToList(),
                activePlayerId = game.activePlayerId, currentPhaseId = game.currentPhaseId,
                turn = game.turn, opponentCount = game.opponentCount,
                finished = game.finished, winnerPlayerId = game.winnerPlayerId,
                teams = game.teams.Select(t => new SimTeamMember
                { playerId = t.playerId, teamId = t.teamId }).ToList(),
                resources = game.resources.Select(r => new SimResourceState
                { ownerId = r.ownerId, resourceId = r.resourceId, value = r.value }).ToList(),
                events = game.events.Skip(Math.Max(0, game.events.Count - 30)).ToList(),
                data = game.data.Where(item =>
                    item.key.StartsWith("level:") || item.key.StartsWith("tax:") ||
                    item.key.StartsWith("damage:") || item.key.StartsWith("exh:") ||
                    item.key.StartsWith("entered:"))
                    .Select(item => new SimParameter { key = item.key, value = item.value }).ToList()
            };
            foreach (var zone in game.zones)
            {
                var definition = pack.zones.FirstOrDefault(z => Same(z.id, zone.zoneId));
                var visibility = definition == null ? "hidden" : definition.visibility;
                var show = Same(visibility, "public") ||
                    Same(visibility, "owner") && Same(zone.ownerId, viewerId);
                view.zones.Add(new SimZoneState
                {
                    ownerId = zone.ownerId, zoneId = zone.zoneId,
                    cards = zone.cards.Select(card => show ?
                        new SimCardInstance(card.instanceId, card.cardId, card.ownerId) :
                        new SimCardInstance("", "", "")).ToList()
                });
            }
            if (!game.finished)
            {
                var phase = pack.phases.FirstOrDefault(p => Same(p.id, game.currentPhaseId));
                if (phase != null)
                    view.allowedActions.AddRange(phase.allowedActions.Where(id =>
                        Same(game.activePlayerId, viewerId) ||
                        pack.actions.Any(action => Same(action.id, id) && !action.requiresActivePlayer)));
            }
            return view;
        }
    }
}
