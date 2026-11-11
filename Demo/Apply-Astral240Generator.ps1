$ErrorActionPreference='Stop'
$p='D:\CardForgeStudio\Assets\CardForgeStudio\CardForge\Editor\CardForgeDemoGenerator.cs'
$s=[IO.File]::ReadAllText($p)
function Change([string]$old,[string]$new){
 if(!$script:s.Contains($old)){throw "Cannot find generator segment: $($old.Substring(0,[Math]::Min(92,$old.Length)))"}
 $script:s=$script:s.Replace($old,$new)
}
Change 'public static class CardForgeDemoGenerator' 'public static partial class CardForgeDemoGenerator'
Change 'public const string Version = "1.0.0";' 'public const string Version = "2.0.0";'
Change 'const string ShowcaseDeckId = "ar-deck-showcase";' 'const string ShowcaseDeckId = "ar-deck-solar-starter";'
Change 'static readonly DemoCard[] CardSet =' 'static readonly DemoCard[] CoreCards ='
Change @'
        };

        public static void GenerateBatch()
'@ @'
        };
        private static readonly DemoCard[] CardSet =
            CoreCards.Concat(GenerateExtraCards()).ToArray();

        public static void GenerateBatch()
'@
Change @'
            CreateRules(store);
            var block = CreateBlock(store, fields);
'@ @'
            CreateRules(store);
            CreateUniqueCardAbilities(store);
            var block = CreateBlock(store, fields);
'@
Change '/012' '/240'

Change 'minimumPlayers = 2, maximumPlayers = 4, startingHand = 5' 'minimumPlayers = 2, maximumPlayers = 5, startingHand = 5'
Change @'
                    new GameZoneDocument{id="battlefield",name="Starfield",ownerScope="player",visibility="public",ordered=false,capacity=8},
'@ @'
                    new GameZoneDocument{id="battlefield",name="Starfield Frontline",ownerScope="player",visibility="public",ordered=false,capacity=6,
                        allowedCardTypes=new List<string>{"Unit"}},
                    new GameZoneDocument{id="relic-bay",name="Relic Bay",ownerScope="player",visibility="public",ordered=false,capacity=2,
                        allowedCardTypes=new List<string>{"Relic"}},
                    new GameZoneDocument{id="command",name="Command Deck",ownerScope="player",visibility="public",ordered=false,capacity=1,
                        allowedCardTypes=new List<string>{"Command"}},
'@
Change @'
                    new GameZoneDocument{id="rift",name="Shared Rift",ownerScope="game",visibility="public",ordered=false,capacity=4}
'@ @'
                    new GameZoneDocument{id="rift",name="Shared Rift",ownerScope="game",visibility="public",ordered=false,capacity=3,
                        allowedCardTypes=new List<string>{"Event"},
                        initialCardIds=new List<string>{"ar-card-solar-048"}}
'@
Change @'
                    new GameResourceDocument{id="energy",name="Starlight Energy",scope="player",initialValue=3,minimumValue=0,maximumValue=12},
'@ @'
                    new GameResourceDocument{id="energy",name="Starlight Energy",scope="player",initialValue=3,minimumValue=0,maximumValue=12},
                    new GameResourceDocument{id="shield",name="Barrier Shield",scope="player",initialValue=0,minimumValue=0,maximumValue=20},
'@
$start=$s.IndexOf('                sampleDecks = new List<GameDeckDocument>')
$end=$s.IndexOf(([string][char]10 + '            };'),$start)
if($start -lt 0 -or $end -lt 0){throw 'Cannot find sample deck block.'}
$s=$s.Substring(0,$start)+@'
                boardLayout = new List<GameBoardZonePlacement>
                {
                    new GameBoardZonePlacement{id="ar-board-self-units",zoneId="battlefield",side="self",label="Your Frontline (Unit x6)",x=.22f,y=.61f,width=.56f,height=.14f,order=0},
                    new GameBoardZonePlacement{id="ar-board-opp-units",zoneId="battlefield",side="opponent",label="Enemy Frontline",x=.22f,y=.12f,width=.56f,height=.14f,order=1},
                    new GameBoardZonePlacement{id="ar-board-self-relic",zoneId="relic-bay",side="self",label="Your Relic Bay (x2)",x=.02f,y=.66f,width=.18f,height=.13f,order=2},
                    new GameBoardZonePlacement{id="ar-board-opp-relic",zoneId="relic-bay",side="opponent",label="Enemy Relics",x=.80f,y=.1f,width=.18f,height=.13f,order=3},
                    new GameBoardZonePlacement{id="ar-board-self-command",zoneId="command",side="self",label="Commander (starts in play)",x=.8f,y=.67f,width=.17f,height=.14f,order=4},
                    new GameBoardZonePlacement{id="ar-board-opp-command",zoneId="command",side="opponent",label="Enemy Commander",x=.02f,y=.1f,width=.17f,height=.14f,order=5},
                    new GameBoardZonePlacement{id="ar-board-rift",zoneId="rift",side="shared",label="Rift Event",x=.39f,y=.40f,width=.22f,height=.14f,order=6}
                },
                sampleDecks = CreateFourStarterDecks()
'@ + $s.Substring($end)
Change @'
                Action("ar-deploy", "Deploy card", "Move a card from hand to the battlefield.", "move_card", "ar-trigger-deploy", source:"hand", destination:"battlefield", costResource:"energy", cost:2, condition:"source_in_zone"),
'@ @'
                Action("ar-deploy", "Deploy unit", "Move a Unit from hand to the Frontline.", "move_card", "ar-trigger-deploy", source:"hand", destination:"battlefield", costResource:"energy", cost:2, condition:"source_in_zone"),
                Action("ar-install-relic", "Install relic", "Move a Relic to your Relic Bay (max two).", "move_card", "", source:"hand", destination:"relic-bay", costResource:"energy", cost:1, condition:"source_in_zone"),
                Action("ar-cast-event", "Cast event", "Resolve an Event from hand then discard it.", "move_card", "", source:"hand", destination:"discard", costResource:"energy", cost:1, condition:"source_in_zone"),
                Action("ar-activate-command", "Activate commander", "Resolve the seeded Command's ability.", "emit_event", "", source:"command", costResource:"energy", cost:1, condition:"source_in_zone"),
'@
Change '"ar-channel", "ar-deploy", "ar-stabilize", "ar-retire"' '"ar-channel", "ar-deploy", "ar-install-relic", "ar-cast-event", "ar-activate-command", "ar-stabilize", "ar-retire"'
Change 'var affinity = index / 3;' 'var affinity = index < CoreCards.Length ? index / 3 : (index - CoreCards.Length) / 57;'
[IO.File]::WriteAllText($p,$s,[Text.UTF8Encoding]::new($false))
Write-Output 'Generator now authors 240 cards, four 40-card starters, restricted/seeded zones, and new actions.'
