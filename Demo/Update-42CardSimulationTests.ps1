$ErrorActionPreference='Stop'
$p='D:\CardForgeStudio\Tests\CardForge.Simulation.Tests\Program.cs'
$s=[IO.File]::ReadAllText($p)
function Rep([string]$a,[string]$b) {
 if(!$script:s.Contains($a)){throw "Missing test pattern $($a.Substring(0,[Math]::Min(75,$a.Length)))"}
 $script:s=$script:s.Replace($a,$b)
}
Rep @'
        Check(astral.starterDecks.All(d => d.cardIds.Count == 40), "40 cards per starter deck");
'@ @'
        Check(astral.starterDecks.All(d => d.cardIds.Count == 42), "42 cards per starter deck");
        Check(astral.deckRules.minCards == 40 && astral.deckRules.maxCopiesPerCard == 3,
            "Main deck minimum 40, global maximum three copies");
        Check(astral.cards.Count(card => DeckLegality.TypeMatches(card.type,"ManaSource")) == 4,
            "Exactly four affinity Mana Sources in the 240-card set");
        Check(astral.starterDecks.All(d => DeckLegality.ValidateDeck(astral, d.cardIds).Count == 0),
            "All four starter decks obey global deck-construction rules");
        Check(astral.starterDecks.All(d =>
            d.cardIds.Count(id => astral.cards.Any(card => card.id == id &&
                DeckLegality.TypeMatches(card.type, "Command"))) == 1 &&
            d.cardIds.Count(id => astral.cards.Any(card => card.id == id &&
                DeckLegality.TypeMatches(card.type, "ManaSource"))) == 1),
            "Every 42-card starter contains exactly one Commander and one Mana Source");
'@
Rep @'
        Check(astral.starterDecks.All(d => d.cardIds.GroupBy(c => c).All(g => g.Count() == 2)),
            "Two copies per starter-card choice");
'@ @'
        Check(astral.starterDecks.All(d => d.cardIds.GroupBy(c => c).All(g => g.Count() <= 3)),
            "Every card respects three-copy construction limit");
'@
Rep @'
        Check(astral.starterDecks.SelectMany(d => d.cardIds).Distinct().Count() == 80,
            "80 total selected starter cards, leaving 160 unselected");
'@ @'
        Check(astral.starterDecks.SelectMany(d => d.cardIds).Distinct().Count() == 88,
            "88 distinct starter cards including eight special selections; other cards remain unselected");
'@
Rep @'
        Check(frontline.capacity == 6 && frontline.allowedCardTypes.SequenceEqual(new[] { "Unit" }),
            "Unit-only frontline has six positions");
'@ @'
        Check(frontline.capacity == 6 && frontline.allowedCardTypes.SequenceEqual(new[] { "Unit", "Command" }),
            "Unit and cast Commander share six frontline positions");
'@
Rep @'
        Check(commandZone.capacity == 1 && commandZone.allowedCardTypes.SequenceEqual(new[] { "Command" }),
            "Commander zone has one command position");
'@ @'
        var manaZone = astral.zones.Single(z => z.id == "mana-source");
        Check(commandZone.capacity == 1 && commandZone.allowedCardTypes.SequenceEqual(new[] { "Command" }) &&
            commandZone.returnOnLeave && commandZone.deckStartCardType == "Command",
            "Command zone has one Commander that returns after removal");
        Check(manaZone.capacity == 1 && manaZone.immovable && manaZone.deckStartCardType == "ManaSource",
            "Mana Source zone holds one permanently immovable Mana Source");
'@
Rep @'
        Check(game.players.All(p => game.zones.Single(z =>
            z.ownerId == p && z.zoneId == "command").cards.Count == 0),
            "All starter decks begin with the same empty Command zone");
        // A card may be seeded by a zone definition, never by a deck.
        commandZone.initialCardIds.Add("ar-card-solar-001");
        sim.Register(astral);
        var universal = sim.Create("astral-rift", new[] { "rule-test" }, 4, true, 42);
        Check(universal.players.All(p => universal.zones.Single(z =>
            z.ownerId == p && z.zoneId == "command").cards.Single().cardId ==
            "ar-card-solar-001"), "Global Command starting card is identical for every starter deck");
        commandZone.initialCardIds.Clear();
        sim.Register(astral);
'@ @'
        Check(game.players.All(p =>
            game.zones.Single(z => z.ownerId == p && z.zoneId == "command").cards.Count == 1 &&
            game.zones.Single(z => z.ownerId == p && z.zoneId == "mana-source").cards.Count == 1),
            "Every player has one of each deck-selected special, under the same board rules");
        Check(game.players.All(p =>
            game.zones.Single(z => z.ownerId == p && z.zoneId == "deck").cards.Count == 35 &&
            game.zones.Single(z => z.ownerId == p && z.zoneId == "hand").cards.Count == 5),
            "Only 40 ordinary cards are shuffled, five drawn, two specials kept on board");
        Check(game.players.Select(p => game.zones.Single(z => z.ownerId == p && z.zoneId == "command")
            .cards.Single().cardId).Distinct().Count() == 4,
            "Commanders differ by starter despite identical board rules");
        var specialGame = sim.Create("astral-rift", new[] { "commander-test" }, 1, true, 19);
        specialGame.currentPhaseId = "ar-main";
        var command = specialGame.zones.Single(z =>
            z.ownerId == "commander-test" && z.zoneId == "command");
        var source = specialGame.zones.Single(z =>
            z.ownerId == "commander-test" && z.zoneId == "mana-source");
        var commanderInstance = command.cards.Single();
        var manaInstance = source.cards.Single();
        var availableEnergy = specialGame.resources.Single(r =>
            r.ownerId == "commander-test" && r.resourceId == "energy");
        var preCastEnergy = availableEnergy.value;
        sim.Execute(specialGame, "commander-test", new SimActionRequest
        { actionId = "ar-activate-command", sourceInstanceId = commanderInstance.instanceId });
        Check(command.cards.Count == 0 &&
            specialGame.zones.Single(z => z.ownerId == "commander-test" &&
                z.zoneId == "battlefield").cards.Any(c => c.instanceId == commanderInstance.instanceId),
            "Commander must be cast from Command to the battlefield");
        Check(availableEnergy.value <= preCastEnergy,
            "Commander casting charges mana before resolving abilities");
        sim.Execute(specialGame, "commander-test", new SimActionRequest
        { actionId = "ar-retire", sourceInstanceId = commanderInstance.instanceId });
        Check(command.cards.Count == 1 &&
            command.cards.Single().instanceId == commanderInstance.instanceId &&
            !specialGame.zones.Single(z => z.ownerId == "commander-test" &&
                z.zoneId == "discard").cards.Any(c => c.instanceId == commanderInstance.instanceId),
            "Removed Commander returns to Command, not discard");
        Check(source.cards.Single().instanceId == manaInstance.instanceId &&
            source.cards.Count == 1, "Mana Source is not consumed by casting/removal");
'@
[IO.File]::WriteAllText($p,$s,[Text.UTF8Encoding]::new($false))
Write-Output 'Updated 42-card starter, Commander casting/return and Mana Source simulation tests.'
