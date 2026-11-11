param(
    [string]$PackagePath = 'D:\CardForgeStudio\GamePackages\astral-rift-demo-2.0.0.cardforge'
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if(!(Test-Path $PackagePath)){throw "Package does not exist: $PackagePath"}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($PackagePath)
try {
    function ReadEntry([string]$name) {
        $entry=$archive.GetEntry($name)
        if(!$entry){throw "Required package entry missing: $name"}
        $reader=[IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    $pack=ReadEntry 'rules/rule-pack.json'|ConvertFrom-Json
    $victory=ReadEntry 'rules/victory.json'|ConvertFrom-Json
    if($pack.cards.Count -ne 240 -or $pack.starterDecks.Count -ne 4){
        throw 'Expected 240 cards and exactly four starter decks.'
    }
    $pack | Add-Member -NotePropertyName 'startingHand' -NotePropertyValue ([int]$pack.startingRules.startingHand) -Force
    $pack | Add-Member -NotePropertyName 'victoryRules' -NotePropertyValue $victory.rules -Force
    foreach($rule in $pack.victoryRules){
        if($rule.PSObject.Properties.Match('comparison').Count -eq 0){
            $rule | Add-Member -NotePropertyName 'comparison' -NotePropertyValue $rule.operator
        }
    }
    foreach($action in $pack.actions){
        if($action.costs -isnot [Array]){
            $parts=@()
            if($action.costs){
                foreach($property in $action.costs.PSObject.Properties){
                    $parts+= [pscustomobject]@{ resourceId=$property.Name;amount=[long]$property.Value }
                }
            }
            $action.costs=@($parts)
        }
        foreach($condition in $action.conditions){
            if($null -ne $condition.value){$condition.value=[string]$condition.value}
        }
        foreach($effect in $action.effects){
            if($effect.parameters -and $effect.parameters -isnot [Array]){
                $items=@()
                foreach($property in $effect.parameters.PSObject.Properties){
                    $items+= [pscustomobject]@{key=$property.Name;value=[string]$property.Value}
                }
                $effect.parameters=@($items)
            }
        }
    }
    $rulesFolder=Join-Path $root 'Assets\StreamingAssets\CardForgeStudio\CardForge\Rules'
    $assetFolder=Join-Path $root 'Assets\StreamingAssets\CardForgeStudio\CardForge\Assets'
    New-Item -ItemType Directory -Path $rulesFolder,$assetFolder -Force | Out-Null
    $count=0
    foreach($card in $pack.cards){
        $id='astral-rift.card.'+$card.id
        $card | Add-Member -NotePropertyName 'assetId' -NotePropertyValue $id -Force
        $relative=[string]$card.properties.art
        $artPath=Join-Path $root ($relative.Replace('/',[IO.Path]::DirectorySeparatorChar))
        $absolute=[IO.Path]::GetFullPath($artPath)
        $artRoot=[IO.Path]::GetFullPath((Join-Path $root 'Assets\CardForgeStudio\CardForgeData\demo\astral-rift\art'))+[IO.Path]::DirectorySeparatorChar
        if(!$absolute.StartsWith($artRoot,[StringComparison]::OrdinalIgnoreCase) -or
           !($absolute.EndsWith('.png',[StringComparison]::OrdinalIgnoreCase))){
            throw "Card has an unsafe art path: $($card.id)"
        }
        if(!(Test-Path $absolute)){throw "Missing card artwork $absolute"}
        Copy-Item -LiteralPath $absolute -Destination (Join-Path $assetFolder ($id+'.png')) -Force
        $count++
    }
    $json=$pack|ConvertTo-Json -Depth 99 -Compress
    [IO.File]::WriteAllText((Join-Path $rulesFolder 'astral-rift.json'),$json,
        [Text.UTF8Encoding]::new($false))
    Write-Output "Staged $($pack.cards.Count) cards, $($pack.starterDecks.Count) starter decks and $count server images."
}
finally { $archive.Dispose() }
