using System;
using System.Collections.Generic;
using System.Linq;

namespace CardForge.Simulation
{
    /// <summary>
    /// Astral Rift gameplay extensions. Kept in a separate pure-C# file so the
    /// dedicated server, portable test runner and Unity player use identical rules.
    /// </summary>
    public sealed partial class SimulationEngine
    {
        private static int StateNumber(SimMatch game, string key)
        {
            var item = game.data.FirstOrDefault(value => Same(value.key, key));
            return item != null && int.TryParse(item.value, out var count) ? count : 0;
        }

        private static void PutState(SimMatch game, string key, int number)
        {
            var item = game.data.FirstOrDefault(value => Same(value.key, key));
            if (item == null) game.data.Add(new SimParameter { key = key, value = number.ToString() });
            else item.value = number.ToString();
        }

        private static SimCard Card(SimRulePack rules, SimCardInstance instance)
        {
            var definition = rules.cards.FirstOrDefault(c => Same(c.id, instance.cardId));
            if (definition == null) throw new InvalidOperationException("Unknown card definition.");
            return definition;
        }

        private static string CardKind(SimCard card)
        {
            if (card == null) return "";
            var name = card.type ?? "";
            return name.Split(new[] { ' ', '-', '\u2014' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "";
        }

        private static bool HasKeyword(SimCard card, string keyword)
        {
            return (card.keywords ?? new List<string>()).Any(item => Same(item, keyword));
        }

        private static SimCardInstance SourceOnBattlefield(SimMatch game, string player, string id)
        {
            return FindZone(game, player, "battlefield", false)?.cards
                .FirstOrDefault(instance => Same(instance.instanceId, id));
        }

        private static string FindEnemy(SimMatch game, string actor, string requested)
        {
            var team = game.teams.FirstOrDefault(member => Same(member.playerId, actor))?.teamId ?? 0;
            var enemy = game.players.FirstOrDefault(player =>
                (string.IsNullOrEmpty(requested) || Same(requested, player)) &&
                !Same(player, actor) && !game.eliminated.Contains(player) &&
                game.teams.Any(member => Same(member.playerId, player) && member.teamId != team));
            if (enemy == null) throw new InvalidOperationException("Select a valid enemy target.");
            return enemy;
        }

        private static int Aura(SimMatch game, string player)
        {
            var source = FindZone(game, player, "mana-source", false)?.cards.FirstOrDefault();
            // A Mana Source is a permanent and passively strengthens friendly creatures.
            return source == null ? 0 : 1 + StateNumber(game, "level:" + source.instanceId);
        }

        private static int AttackPower(SimRulePack pack, SimMatch game, SimCardInstance instance)
        {
            var definition = Card(pack, instance);
            return Math.Max(0, definition.power + Aura(game, instance.ownerId));
        }

        private static int Health(SimRulePack pack, SimMatch game, SimCardInstance instance)
        {
            var definition = Card(pack, instance);
            return Math.Max(1, definition.toughness + Aura(game, instance.ownerId));
        }

        private static void ValidateAttack(SimRulePack pack, SimMatch game,
            string actor, SimCardInstance attacker, SimActionRequest request, bool creature)
        {
            if (attacker == null || SourceOnBattlefield(game, actor, attacker.instanceId) == null)
                throw new InvalidOperationException("Choose one of your battlefield creatures.");
            var definition = Card(pack, attacker);
            if (!DeckLegality.TypeMatches(definition.type, "Creature") &&
                !DeckLegality.TypeMatches(definition.type, "Commander"))
                throw new InvalidOperationException("Only creatures or cast commanders may attack.");
            if (StateNumber(game, "exh:" + attacker.instanceId) == game.turn)
                throw new InvalidOperationException("This creature already attacked this turn.");
            if (StateNumber(game, "entered:" + attacker.instanceId) == game.turn &&
                !HasKeyword(definition, "haste"))
                throw new InvalidOperationException("Summoning sickness: this creature cannot attack yet.");
            var enemy = FindEnemy(game, actor, request.targetPlayerId);
            var defenders = FindZone(game, enemy, "battlefield", false)?.cards
                ?? new List<SimCardInstance>();
            if (creature)
            {
                if (string.IsNullOrWhiteSpace(request.targetInstanceId) ||
                    !defenders.Any(card => Same(card.instanceId, request.targetInstanceId)))
                    throw new InvalidOperationException("Select an opposing battlefield creature.");
            }
            else if (defenders.Any(card => HasKeyword(Card(pack, card), "guard")))
                throw new InvalidOperationException("An enemy Guard creature protects its player.");
        }

        /// <summary>
        /// Returns mana not already represented by an action's authored costs.
        /// Validates all card and combat restrictions before paying anything.
        /// </summary>
        private static long AstralExtraCost(SimRulePack pack, SimMatch game, string actor,
            SimAction action, SimCardInstance source, SimActionRequest request)
        {
            if (!Same(pack.id, "astral-rift") || source == null) return 0;
            var card = Card(pack, source);
            var kind = CardKind(card);
            string required = null;
            switch (action.id)
            {
                case "ar-play-creature": required = "Creature"; break;
                case "ar-cast-commander": required = "Commander"; break;
                case "ar-cast-spell": required = "Spell"; break;
                case "ar-cast-quickspell": required = "QuickSpell"; break;
                case "ar-level-mana": required = "ManaSource"; break;
                case "ar-attack-player":
                    ValidateAttack(pack, game, actor, source, request, false);
                    return 0;
                case "ar-attack-creature":
                    ValidateAttack(pack, game, actor, source, request, true);
                    return 0;
            }
            if (required != null && !Same(kind, required))
                throw new InvalidOperationException("This action requires a " + required + " card.");
            if (Same(action.id, "ar-level-mana"))
            {
                var current = StateNumber(game, "level:" + source.instanceId);
                if (current >= 3) throw new InvalidOperationException("Mana Source is already at level 3.");
                return 2 + current;
            }
            if (required == null) return 0;
            var authoredCost = action.costs.Where(cost => Same(cost.resourceId, "energy"))
                .Sum(cost => cost.amount);
            var tax = Same(kind, "Commander") ? 2L * StateNumber(game, "tax:" + source.instanceId) : 0L;
            return Math.Max(0, (long)card.cost + tax - authoredCost);
        }

        private static void ResolveAttack(SimRulePack pack, SimMatch game, string actor,
            SimActionRequest request, bool vsCreature)
        {
            var attacker = SourceOnBattlefield(game, actor, request.sourceInstanceId);
            ValidateAttack(pack, game, actor, attacker, request, vsCreature);
            PutState(game, "exh:" + attacker.instanceId, game.turn);
            var enemy = FindEnemy(game, actor, request.targetPlayerId);
            var attack = AttackPower(pack, game, attacker);
            if (!vsCreature)
            {
                ModifyPool(pack, game, enemy, "life", -attack);
                Emit(game, "creature_attack", Card(pack, attacker).name + " attacks for " +
                    attack + " damage.", actor);
                return;
            }

            var defender = SourceOnBattlefield(game, enemy, request.targetInstanceId);
            var response = HasKeyword(Card(pack, attacker), "ranged") ?
                0 : AttackPower(pack, game, defender);
            var defenderDamage = StateNumber(game, "damage:" + defender.instanceId) + attack;
            var attackerDamage = StateNumber(game, "damage:" + attacker.instanceId) + response;
            PutState(game, "damage:" + defender.instanceId, defenderDamage);
            PutState(game, "damage:" + attacker.instanceId, attackerDamage);
            Emit(game, "creature_clash", Card(pack, attacker).name + " clashes with " +
                Card(pack, defender).name + ".", actor);

            if (defenderDamage >= Health(pack, game, defender))
                Move(pack, game, enemy, defender.instanceId, "battlefield", "discard");
            if (attackerDamage >= Health(pack, game, attacker))
                Move(pack, game, actor, attacker.instanceId, "battlefield", "discard");
        }

        private static void LevelManaSource(SimRulePack pack, SimMatch game, string actor,
            SimActionRequest request)
        {
            var source = FindZone(game, actor, "mana-source").cards
                .FirstOrDefault(card => Same(card.instanceId, request.sourceInstanceId));
            if (source == null) throw new InvalidOperationException("Select your Mana Source.");
            var level = StateNumber(game, "level:" + source.instanceId);
            if (level >= 3) throw new InvalidOperationException("Mana Source is already at level 3.");
            PutState(game, "level:" + source.instanceId, level + 1);
            Emit(game, "mana_level", Card(pack, source).name + " reached level " +
                (level + 1) + ". Friendly creatures gain stronger passive bonuses.", actor);
        }
    }
}
