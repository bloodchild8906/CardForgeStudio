$ErrorActionPreference = 'Stop'
$root='D:\CardForgeStudio'
function Edit([string]$relative,[string]$old,[string]$new) {
 $p=Join-Path $root $relative
 $s=[IO.File]::ReadAllText($p)
 if(!$s.Contains($old)){throw "Generator/source edit missing: $relative / $($old.Substring(0,[Math]::Min(80,$old.Length)))"}
 [IO.File]::WriteAllText($p,$s.Replace($old,$new),[Text.UTF8Encoding]::new($false))
 Write-Output "Updated $relative"
}
$gen='Assets\CardForgeStudio\CardForge\Editor\CardForgeDemoGenerator.cs'
$expanded='Assets\CardForgeStudio\CardForge\Editor\CardForgeDemoGenerator.Expanded.cs'
Edit $expanded @'
                var type = index == 0 ? "Command" : index <= 34 ? "Unit" :
                    index <= 46 ? "Relic" : "Event";
'@ @'
                // Four dedicated Mana Sources replace four Relics in the 240-card set.
                var type = index == 0 ? "Command" : index == 46 ? "ManaSource" :
                    index <= 34 ? "Unit" : index <= 45 ? "Relic" : "Event";
'@
Edit $expanded @'
                var cost = type == "Command" ? 0 : type == "Unit" ?
                    1 + (index % 6) : type == "Relic" ? 2 + (index % 4) : 1 + (index % 4);
'@ @'
                var cost = type == "Command" ? 3 : type == "ManaSource" ? 0 :
                    type == "Unit" ? 1 + (index % 6) :
                    type == "Relic" ? 2 + (index % 4) : 1 + (index % 4);
                if (type == "ManaSource")
                    name = affinity + " Eternal Mana Source";
'@
Edit $expanded @'
                var actionId = card.Type == "Command" ? "ar-activate-command" :
                    card.Type == "Relic" ? "ar-install-relic" :
'@ @'
                var actionId = card.Type == "Command" ? "ar-activate-command" :
                    card.Type == "ManaSource" ? "ar-channel" :
                    card.Type == "Relic" ? "ar-install-relic" :
'@
Edit $expanded @'
                    minimumCards = 40, maximumCopiesPerCard = 4,
                    cardIds = selected.SelectMany(card => Enumerable.Repeat(card.Id, 2)).ToList()
'@ @'
                    minimumCards = 40, maximumCopiesPerCard = 3,
                    cardIds = selected.SelectMany(card => Enumerable.Repeat(card.Id, 2))
                        .Concat(new[] { ExpansionCardId(affinity, 0), ExpansionCardId(affinity, 46) })
                        .ToList()
'@
Edit $expanded @'
            if (list.Count != 4 || list.Any(deck => deck.cardIds.Count != 40) ||
'@ @'
            if (list.Count != 4 || list.Any(deck => deck.cardIds.Count != 42) ||
'@
Edit $expanded @'
                throw new InvalidOperationException("Exactly four 40-card starter decks must use a subset of the set.");
'@ @'
                throw new InvalidOperationException("Exactly four 40+2-card starter decks must use a subset of the set.");
'@
Edit $gen @'
                    new GameZoneDocument{id="battlefield",name="Starfield Frontline",ownerScope="player",visibility="public",ordered=false,capacity=6,
                        allowedCardTypes=new List<string>{"Unit"}},
'@ @'
                    new GameZoneDocument{id="battlefield",name="Starfield Frontline",ownerScope="player",visibility="public",ordered=false,capacity=6,
                        allowedCardTypes=new List<string>{"Unit","Command"}},
'@
Edit $gen @'
                    new GameZoneDocument{id="command",name="Command Deck",ownerScope="player",visibility="public",ordered=false,capacity=1,
                        allowedCardTypes=new List<string>{"Command"}},
'@ @'
                    new GameZoneDocument{id="command",name="Command Zone",ownerScope="player",visibility="public",ordered=false,capacity=1,
                        allowedCardTypes=new List<string>{"Command"},deckStartCardType="Command",returnOnLeave=true},
                    new GameZoneDocument{id="mana-source",name="Mana Source",ownerScope="player",visibility="public",ordered=false,capacity=1,
                        allowedCardTypes=new List<string>{"ManaSource"},deckStartCardType="ManaSource",immovable=true},
'@
Edit $gen @'
                sampleDecks = CreateFourStarterDecks()
'@ @'
                sampleDecks = CreateFourStarterDecks(),
                deckRules = new DeckConstructionRulesDocument
                {
                    minCards = 40, maxCards = 120, maxCopiesPerCard = 3,
                    typeLimits = new List<DeckTypeLimitDocument>
                    {
                        new DeckTypeLimitDocument{cardType="Command",minCount=1,maxCount=1},
                        new DeckTypeLimitDocument{cardType="ManaSource",minCount=1,maxCount=1}
                    }
                }
'@
Edit $gen @'
                Action("ar-activate-command", "Activate commander", "Resolve the seeded Command's ability.", "emit_event", "", source:"command", costResource:"energy", cost:1, condition:"source_in_zone"),
'@ @'
                Action("ar-activate-command", "Cast commander", "Pay three energy and cast your Commander from Command onto the Frontline.", "move_card", "", source:"command", destination:"battlefield", costResource:"energy", cost:3, condition:"source_in_zone"),
'@
Edit $gen @'
                    new GameBoardZonePlacement{id="ar-board-self-command",zoneId="command",side="self",label="Commander (1 Command max)",x=.8f,y=.67f,width=.17f,height=.14f,order=4},
'@ @'
                    new GameBoardZonePlacement{id="ar-board-self-command",zoneId="command",side="self",label="Commander (cast to Frontline)",x=.8f,y=.67f,width=.17f,height=.14f,order=4},
                    new GameBoardZonePlacement{id="ar-board-self-mana",zoneId="mana-source",side="self",label="Mana Source (permanent)",x=.81f,y=.48f,width=.16f,height=.12f,order=7},
                    new GameBoardZonePlacement{id="ar-board-opp-mana",zoneId="mana-source",side="opponent",label="Enemy Mana Source",x=.02f,y=.28f,width=.16f,height=.12f,order=8},
'@
Edit $gen @'
        static List<string> ShowcaseDeckCards()
        {
            return CardSet.SelectMany(card => Enumerable.Repeat(card.Id, 2)).ToList();
        }
'@ @'
        static List<string> ShowcaseDeckCards()
        {
            // Showcase the first LEGAL starter; the remaining cards stay in the
            // catalog, never bypassing the game's shared 40+2 construction rules.
            return CreateFourStarterDecks()[0].cardIds.ToList();
        }
'@
Write-Output 'Four distinct 42-card starters, mana source type, and shared board rules authored.'
