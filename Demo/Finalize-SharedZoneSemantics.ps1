$ErrorActionPreference='Stop'
$root='D:\CardForgeStudio'
function Change([string]$file,[string]$pattern,[string]$replacement) {
    $path=Join-Path $root $file
    $text=[IO.File]::ReadAllText($path)
    $new=[regex]::Replace($text,$pattern,$replacement,'Singleline')
    if($new -eq $text) { throw "Expected source pattern missing in $file" }
    [IO.File]::WriteAllText($path,$new,[Text.UTF8Encoding]::new($false))
    Write-Output "Updated $file"
}
Change 'Assets\CardForgeStudio\CardForge\Editor\CardForgeDemoGenerator.Expanded.cs' '(?m)^\s*var commander = ExpansionCardId\(affinity, 0\);\r?\n' ''
Change 'Assets\CardForgeStudio\CardForge\Editor\CardForgeDemoGenerator.Expanded.cs' ',\s*startingZones = new List<GameDeckZoneSeed>\s*\{\s*new GameDeckZoneSeed \{ zoneId = "command", cardId = commander, count = 1 \}\s*\}' ''
Change 'Tests\CardForge.Simulation.Tests\Program.cs' 'Check\(game\.players\.All\(p => game\.zones\.Single\(z =>\s*z\.ownerId == p && z\.zoneId == "command"\)\.cards\.Count == 1\),\s*"Every commander begins on the board"\);' @'
Check(game.players.All(p => game.zones.Single(z =>
            z.ownerId == p && z.zoneId == "command").cards.Count == 0),
            "All starter decks begin with the same empty Command zone");
        // A card may be seeded by a zone definition, never by a deck.
        commandZone.initialCardIds.Add("ar-card-solar-060");
        sim.Register(astral);
        var universal = sim.Create("astral-rift", new[] { "rule-test" }, 4, true, 42);
        Check(universal.players.All(p => universal.zones.Single(z =>
            z.ownerId == p && z.zoneId == "command").cards.Single().cardId ==
            "ar-card-solar-060"), "Global Command starting card is identical for every starter deck");
        commandZone.initialCardIds.Clear();
        sim.Register(astral);
'@
$paths = @(
  'Assets\CardForgeStudio\CardForgeData\game-definitions\astral-rift-core.json',
  'Assets\StreamingAssets\CardForgeStudio\CardForge\Rules\astral-rift.json'
)
foreach($f in $paths) {
    $path=Join-Path $root $f
    if(!(Test-Path $path)){continue}
    $text=[IO.File]::ReadAllText($path)
    $result=[regex]::Replace($text,',\s*"startingZones"\s*:\s*\[[^\[\]]*\]','')
    if($result -ne $text) {
        [IO.File]::WriteAllText($path,$result,[Text.UTF8Encoding]::new($false))
        Write-Output "Removed deck-level startingZones from $f"
    }
}
Write-Output 'All deck-specific zone seeds removed; global zone seeding retained.'
