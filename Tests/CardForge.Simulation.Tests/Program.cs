using System;
using System.Collections.Generic;
using System.Linq;
using CardForge.Simulation;

var passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
}
SimRulePack CreateRules()
{
    var rules = new SimRulePack
    {
        id = "astral-test", name = "Astral test", version = "1.0.0",
        minimumPlayers = 2, maximumPlayers = 5, startingHand = 2,
        startingDeck = new List<string> { "spark", "spark", "spark", "spark" }
    };
    foreach (var id in new[] { "deck", "hand", "battlefield", "discard" })
        rules.zones.Add(new SimZone
        {
            id = id, name = id, visibility = id == "deck" ? "hidden" :
                id == "hand" ? "owner" : "public"
        });
    rules.resources.Add(new SimResource
    {
        id = "life", initialValue = 12, minimumValue = 0, maximumValue = 12
    });
    rules.resources.Add(new SimResource
    {
        id = "energy", initialValue = 4, minimumValue = 0, maximumValue = 8
    });
    rules.resources.Add(new SimResource
    {
        id = "renown", initialValue = 0, minimumValue = 0, maximumValue = 10
    });
    rules.cards.Add(new SimCard
    {
        id = "spark", name = "Spark", type = "creature", cost = 1,
        power = 2, toughness = 1,
        abilities = new List<SimAbility>
        {
            new SimAbility
            {
                id = "onplay", name = "Spark trigger", trigger = "play",
                effects = new List<SimEffect> { new SimEffect
                {
                    kind = "modify_resource", resourceId = "life", target = "target_player", amount = -3
                } }
            }
        }
    });
    rules.actions.Add(new SimAction
    {
        id = "play", name = "Play", sourceZone = "hand", destinationZone = "battlefield",
        costs = new List<SimCost> { new SimCost { resourceId = "energy", amount = 2 } },
        effects = new List<SimEffect> { new SimEffect
        {
            kind = "modify_resource", resourceId = "renown", amount = 2
        } }
    });
    rules.actions.Add(new SimAction
    {
        id = "grant", name = "Grant energy", effects = new List<SimEffect>
        {
            new SimEffect { kind = "modify_resource", resourceId = "energy", amount = 1 }
        }
    });
    rules.actions.Add(new SimAction
    {
        id = "pass", name = "Pass", effects = new List<SimEffect> {
            new SimEffect { kind = "end_turn" }
        }
    });
    rules.triggers.Add(new SimTrigger
    {
        id = "after-play", name = "Refund", causeActionIds = new List<string> { "play" },
        responseActionIds = new List<string> { "grant" }
    });
    rules.phases.Add(new SimPhase
    {
        id = "main", name = "Main", allowedActions = new List<string> { "play", "pass" }
    });
    rules.victoryRules.Add(new SimVictoryRule
    {
        id = "death", name = "Last standing", kind = "last-player-standing",
        outcome = "win", scope = "player", resourceId = "life",
        comparison = "less-than-or-equal", threshold = 0
    });
    rules.victoryRules.Add(new SimVictoryRule
    {
        id = "ascend", name = "Renown", kind = "resource-threshold",
        outcome = "win", scope = "player", resourceId = "renown",
        comparison = "greater-than-or-equal", threshold = 8
    });
    return rules;
}
long Resource(SimMatch match, string player, string id) =>
    match.resources.Single(r => r.ownerId == player && r.resourceId == id).value;

var sim = new SimulationEngine();
var ruleset = CreateRules();
sim.Register(ruleset);
for (var opponents = 1; opponents <= 4; opponents++)
{
    var game = sim.Create("astral-test", new[] { "solo" }, opponents, true, 42);
    Check(game.players.Count == opponents + 1, "Seat count incorrect");
    Check(game.teams.Count(t => t.teamId == 0) == 1, "Solo team count incorrect");
    Check(game.teams.Count(t => t.teamId == 1) == opponents, "Opponent team count incorrect");
    Check(game.players.All(p => game.zones.Any(z => z.ownerId == p && z.zoneId == "hand")),
        "Every player needs a hand");
    var hidden = sim.View(game, "solo").zones.Single(z =>
        z.ownerId == game.players[1] && z.zoneId == "hand");
    Check(hidden.cards.Count == 2 && hidden.cards.All(c => c.cardId == "" && c.instanceId == ""),
        "Opponent hand contents leaked");
    var own = sim.View(game, "solo").zones.Single(z => z.ownerId == "solo" && z.zoneId == "hand");
    Check(own.cards.All(c => c.cardId == "spark" && c.instanceId.Length > 0),
        "Own hand must be readable");

    var card = own.cards[0];
    sim.Execute(game, "solo", new SimActionRequest
    {
        actionId = "play", sourceInstanceId = card.instanceId,
        targetPlayerId = game.players[1]
    });
    Check(Resource(game, "solo", "energy") == 3, "Resource cost and refund incorrect");
    Check(Resource(game, "solo", "renown") == 2, "Play action did not update renown");
    Check(Resource(game, game.players[1], "life") == 9, "Card ability damage missing");
    Check(game.events.Any(e => e.type == "trigger_resolved"), "Trigger edge did not fire");
    Check(game.zones.Single(z => z.ownerId == "solo" && z.zoneId == "battlefield").cards.Count == 1,
        "Card was not moved to battlefield");
    var rejected = false;
    try { sim.Execute(game, game.players[1], new SimActionRequest { actionId = "pass" }); }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "Out-of-turn action accepted");

    sim.Execute(game, "solo", new SimActionRequest { actionId = "pass" });
    Check(game.activePlayerId == game.players[1], "Pass did not advance active seat");
    foreach (var enemy in game.players.Skip(1).Take(opponents - 1))
        game.resources.Single(r => r.ownerId == enemy && r.resourceId == "life").value = 0;
    sim.Evaluate(game);
    Check(!game.finished, "Eliminating one teammate finished team match");
    game.resources.Single(r => r.ownerId == game.players.Last() && r.resourceId == "life").value = 0;
    sim.Evaluate(game);
    Check(game.finished && game.winnerPlayerId == "solo", "Surviving solo side did not win");
}

var noBots = false;
try { sim.Create("astral-test", new[] { "solo" }, 4, false); }
catch (InvalidOperationException) { noBots = true; }
Check(noBots, "Missing seats without backfill accepted");
var tooLarge = false;
try { sim.Create("astral-test", new[] { "solo" }, 5, true); }
catch (InvalidOperationException) { tooLarge = true; }
Check(tooLarge, "Unsupported 1v5 accepted");

Console.WriteLine($"Portable Unity simulation: {passed} assertions passed.");


/*
 * Astral Rift 240: executable content tests run against exactly the rules that
 * the Unity Dedicated Server loads after the editor stages the package.
 */
var astralPath = @"D:\CardForgeStudio\Assets\StreamingAssets\CardForge\Rules\astral-rift.json";
if (System.IO.File.Exists(astralPath))
{
    var options = new System.Text.Json.JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true, IncludeFields = true
    };
    var astral = System.Text.Json.JsonSerializer.Deserialize<SimRulePack>(
        System.IO.File.ReadAllText(astralPath), options)!;

    Check(astral.version == "3.0.0", "Astral Rift 3 ruleset not staged");
    Check(astral.cards.Count == 240 && astral.cards.Select(c => c.id).Distinct().Count() == 240,
        "Expected exactly 240 distinct cards");
    Check(astral.cards.GroupBy(c => c.name).All(g => g.Count() == 1),
        "All cards must have distinct printed names");
    Check(astral.cards.All(c => c.abilities.Count > 0 && c.abilities.Any(a =>
        a.id == "ar-ability-unique-" + c.id && a.effects.Count > 0)),
        "Every card needs an executable, unique signature ability");
    foreach (var (type, expected) in new[] {
        ("Commander", 4), ("ManaSource", 12), ("Creature", 128),
        ("Spell", 48), ("QuickSpell", 48) })
        Check(astral.cards.Count(c => DeckLegality.TypeMatches(c.type, type)) == expected,
            "Incorrect 240-card type distribution: " + type);
    Check(astral.starterDecks.Count == 3, "Must have exactly three starter decks");
    foreach (var starter in astral.starterDecks)
    {
        Check(starter.cardIds.Count == 42 && starter.cardIds.Distinct().Count() == 42,
            "Starter requires 42 distinct cards");
        Check(starter.cardIds.Count(id =>
            astral.cards.Any(c => c.id == id && DeckLegality.TypeMatches(c.type, "ManaSource"))) == 1,
            "Starter requires one Mana Source");
        Check(starter.cardIds.Count(id =>
            astral.cards.Any(c => c.id == id && DeckLegality.TypeMatches(c.type, "Commander"))) == 1,
            "Starter requires one Commander");
        Check(DeckLegality.ValidateDeck(astral, starter.cardIds).Count == 0,
            "Starter is illegal: " + starter.name);
    }
    Check(astral.zones.Single(z => z.id == "command").visibility == "public" &&
        astral.zones.Single(z => z.id == "command").returnOnLeave &&
        astral.zones.Single(z => z.id == "mana-source").visibility == "public" &&
        astral.zones.Single(z => z.id == "mana-source").immovable,
        "Public commander and permanent Mana Source board zones required");
    Check(astral.zones.Single(z => z.id == "hand").visibility == "owner" &&
        astral.zones.Single(z => z.id == "deck").visibility == "hidden",
        "Private hand and deck visibility required");

    var engine = new SimulationEngine();
    engine.Register(astral);
    var game = engine.Create("astral-rift", new[] { "human" }, 1, true, 19);
    var botId = game.players.Single(id => id != "human");
    var command = game.zones.Single(z => z.ownerId == "human" && z.zoneId == "command");
    var mana = game.zones.Single(z => z.ownerId == "human" && z.zoneId == "mana-source");
    var field = game.zones.Single(z => z.ownerId == "human" && z.zoneId == "battlefield");
    var hand = game.zones.Single(z => z.ownerId == "human" && z.zoneId == "hand");
    var library = game.zones.Single(z => z.ownerId == "human" && z.zoneId == "deck");
    var botHand = game.zones.Single(z => z.ownerId == botId && z.zoneId == "hand");
    var energy = game.resources.Single(r => r.ownerId == "human" && r.resourceId == "energy");
    Check(command.cards.Count == 1 && mana.cards.Count == 1,
        "Commander and Mana Source must be on board from match creation");
    Check(library.cards.Count == 35 && hand.cards.Count == 5,
        "40-card main deck must shuffle and draw an initial five");
    Check(energy.value >= 1, "Mana Source must generate one mana on first turn");
    var ownView = engine.View(game, "human");
    var enemyView = engine.View(game, botId);
    Check(ownView.zones.Single(z => z.ownerId == botId && z.zoneId == "hand")
        .cards.All(c => string.IsNullOrWhiteSpace(c.cardId)),
        "Opponent hand must remain hidden");
    Check(ownView.zones.Single(z => z.ownerId == botId && z.zoneId == "command")
        .cards.Single().cardId != "",
        "All players' Commanders must be inspectable");
    Check(enemyView.zones.Single(z => z.ownerId == "human" && z.zoneId == "mana-source")
        .cards.Single().cardId != "",
        "All players' Mana Sources must be inspectable");

    var manaId = mana.cards.Single().instanceId;
    game.currentPhaseId = "ar-main";
    energy.value = 20;
    for (var level = 1; level <= 3; level++)
    {
        var before = energy.value;
        engine.Execute(game, "human", new SimActionRequest {
            actionId = "ar-level-mana", sourceInstanceId = manaId });
        Check(energy.value == before - (level + 1),
            "Mana Source levels must cost 2, 3 and 4 mana");
        Check(game.data.Any(d => d.key == "level:" + manaId && d.value == level.ToString()),
            "Mana Source level missing in game data");
    }
    var levelRejected = false;
    try
    {
        engine.Execute(game, "human", new SimActionRequest {
            actionId = "ar-level-mana", sourceInstanceId = manaId });
    }
    catch (InvalidOperationException) { levelRejected = true; }
    Check(levelRejected, "Mana Source must cap out at level 3");

    var commander = command.cards.Single();
    var commanderCard = astral.cards.Single(c => c.id == commander.cardId);
    energy.value = 20;
    var beforeCast = energy.value;
    engine.Execute(game, "human", new SimActionRequest {
        actionId = "ar-cast-commander", sourceInstanceId = commander.instanceId });
    Check(command.cards.Count == 0 && field.cards.Any(c => c.instanceId == commander.instanceId),
        "Commander must enter battlefield when cast");
    Check(energy.value <= beforeCast - commanderCard.cost,
        "Commander base mana cost was not charged");

    var removeCommander = new SimAction
    {
        id = "astral-test-remove-commander", name = "Defeat commander",
        sourceZone = "battlefield", destinationZone = "discard"
    };
    astral.actions.Add(removeCommander);
    astral.phases.Single(p => p.id == "ar-main").allowedActions.Add(removeCommander.id);
    engine.Execute(game, "human", new SimActionRequest {
        actionId = removeCommander.id, sourceInstanceId = commander.instanceId });
    Check(command.cards.Count == 1 &&
        command.cards.Single().instanceId == commander.instanceId,
        "Defeated commander must return to its public zone");
    Check(game.data.Any(item => item.key == "tax:" + commander.instanceId && item.value == "1"),
        "Defeated commander must gain +2 mana recast tax");
    energy.value = 20;
    var recastBefore = energy.value;
    engine.Execute(game, "human", new SimActionRequest {
        actionId = "ar-cast-commander", sourceInstanceId = commander.instanceId });
    Check(energy.value <= recastBefore - commanderCard.cost - 2,
        "Commander recast must cost exactly two extra mana before ability gains");
    astral.actions.Remove(removeCommander);
    astral.phases.Single(p => p.id == "ar-main").allowedActions.Remove(removeCommander.id);

    // Public battlefield combat uses attack power plus the leveled Mana Source aura.
    var creature = astral.cards.First(card => DeckLegality.TypeMatches(card.type, "Creature"));
    var instance = new SimCardInstance("test-creature", creature.id, "human");
    field.cards.Add(instance);
    game.currentPhaseId = "ar-clash";
    var enemyLife = game.resources.Single(r => r.ownerId == botId && r.resourceId == "life");
    var oldLife = enemyLife.value;
    engine.Execute(game, "human", new SimActionRequest {
        actionId = "ar-attack-player", sourceInstanceId = instance.instanceId, targetPlayerId = botId });
    Check(enemyLife.value == oldLife - creature.power - 4,
        "Creature attacks must include level-three Mana Source +4 power");
    var doubleAttack = false;
    try
    {
        engine.Execute(game, "human", new SimActionRequest {
            actionId = "ar-attack-player", sourceInstanceId = instance.instanceId, targetPlayerId = botId });
    }
    catch (InvalidOperationException) { doubleAttack = true; }
    Check(doubleAttack, "Creatures cannot attack twice in a turn");

    // Fast QuickSpells remain legally playable in the opponent's turn.
    game.activePlayerId = botId;
    game.currentPhaseId = "ar-main";
    var quick = astral.cards.First(c => DeckLegality.TypeMatches(c.type, "QuickSpell"));
    hand.cards.Add(new SimCardInstance("test-quickspell", quick.id, "human"));
    energy.value = 20;
    Check(engine.View(game, "human").allowedActions.Contains("ar-cast-quickspell"),
        "Nonactive player must see QuickSpell response action");
    engine.Execute(game, "human", new SimActionRequest {
        actionId = "ar-cast-quickspell", sourceInstanceId = "test-quickspell" });
    Check(game.zones.Single(z => z.ownerId == "human" && z.zoneId == "discard")
        .cards.Any(c => c.instanceId == "test-quickspell"),
        "Reactive QuickSpell must resolve to discard");

    Check(mana.cards.Single().instanceId == manaId,
        "Mana Source must never leave its permanent zone");
    Console.WriteLine("Astral Rift 3: 240 distinct cards, three 42-card unique starters, levels, tax, combat, hidden zones, fast spells verified.");
}
else Console.WriteLine("Astral Rift 3 rules not staged; base simulation tests only.");
