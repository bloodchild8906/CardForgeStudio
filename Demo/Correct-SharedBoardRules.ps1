$ErrorActionPreference = 'Stop'
$root = 'D:\CardForgeStudio'
function Update([string]$relative, [scriptblock]$change) {
    $path = Join-Path $root $relative
    $s = [IO.File]::ReadAllText($path)
    $n = & $change $s
    if ($n -ceq $s) { throw "No changes to $relative" }
    [IO.File]::WriteAllText($path,$n,[Text.UTF8Encoding]::new($false))
    Write-Output "Updated $relative"
}
Update 'Assets\CardForgeStudio\CardForge\Runtime\CardForgeModels.cs' {
    param($s)
    $pattern = '(\r?\n\s*public string strategy = "";)\r?\n\s*public List<GameDeckZoneSeed> startingZones = new List<GameDeckZoneSeed>\(\);\r?\n\s*}\r?\n\s*\[Serializable\]\r?\n\s*public class GameDeckZoneSeed\r?\n\s*\{[^}]*\}'
    $replace = '$1' + [Environment]::NewLine + '        // Board rules and starting cards belong to GameZoneDocument.' + [Environment]::NewLine + '    }'
    $r = [regex]::Replace($s,$pattern,$replace,'Singleline')
    if($r -eq $s) {throw 'Deck authoring schema not found'}
    return $r
}
Update 'Assets\CardForgeStudio\CardForgeDedicatedServer\Runtime\Simulation\Simulation.Engine.cs' {
    param($s)
    $pattern = '(\s*foreach \(var starter in pack.starterDecks\)\s*\{\s*if \(starter\.cardIds\.Count == 0 \|\| starter\.cardIds\.Any\(id =>\s*!pack\.cards\.Any\(c => Same\(c\.id, id\)\)\)\)\s*throw new InvalidOperationException\("Starter contains unknown cards: " \+ starter\.id\);)\s*foreach \(var entry in starter\.startingZones\)\s*\{.*?\r?\n\s*\}\s*\}'
    $r = [regex]::Replace($s,$pattern,('$1'+[Environment]::NewLine+'            }'),'Singleline')
    if($r -eq $s) {throw 'Starter zone validation block not found'}
    $s=$r
    $pattern='(\s*var template = pack\.starterDecks\.FirstOrDefault\(starter =>\s*starter\.cardIds\.Count == requested\.Count &&\s*starter\.cardIds\.GroupBy\(id => id, StringComparer\.OrdinalIgnoreCase\)\s*\.All\(group => requested\.Count\(id => Same\(id, group\.Key\)\) == group\.Count\(\)\)\);)\s*if \(template != null\)\s*foreach \(var seed in template\.startingZones\)\s*\{.*?\r?\n\s*\}'
    $r=[regex]::Replace($s,$pattern,'$1','Singleline')
    if($r -eq $s) {throw 'Starter zone seeding block not found'}
    return $r
}
Update 'Assets\CardForgeStudio\CardForge\Editor\CardForgeGamePackageExporter.cs' {
    param($s)
    $pattern='(?s)            var starterDecks = .*?(?=\r?\n            return "\{" \+)'
    $replace=@'
            var starterDecks = "[" + string.Join(",", (game.sampleDecks ?? new List<GameDeckDocument>())
                .Select(deck => "{\"id\":" + Quote(deck.id) + ",\"name\":" + Quote(deck.name) +
                    ",\"strategy\":" + Quote(deck.strategy) +
                    ",\"cardIds\":" + StringArray(deck.cardIds) + "}")) + "]";
'@
    $r=[regex]::Replace($s,$pattern,$replace)
    if($r -eq $s) {throw 'Starter pack exporter block not found'}
    $s=$r
    $pattern='(?s)        static string BuildDeckJson\(GameDeckDocument deck\) =>.*?(?=\r?\n        static string BuildVictoryJson)'
    $replace=@'
        static string BuildDeckJson(GameDeckDocument deck) => "{\"version\":1,\"id\":" + Quote(deck.id) +
            ",\"name\":" + Quote(deck.name) + ",\"description\":" + Quote(deck.description) +
            ",\"strategy\":" + Quote(deck.strategy) +
            ",\"minimumCards\":" + deck.minimumCards +
            ",\"maximumCopiesPerCard\":" + deck.maximumCopiesPerCard +
            ",\"cardIds\":" + StringArray(deck.cardIds) + "}";
'@
    $r=[regex]::Replace($s,$pattern,$replace)
    if($r -eq $s) {throw 'Deck exporter block not found'}
    return $r
}
Update 'Assets\CardForgeStudio\CardForge\Editor\CardForgeDemoGenerator.cs' {
    param($s)
    return $s.Replace('label="Commander (starts in play)"','label="Commander (1 Command max)"')
}
Write-Output 'Shared board rules are now separate from starter decks.'
