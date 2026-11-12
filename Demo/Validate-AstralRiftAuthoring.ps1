$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$data=Join-Path $root 'Assets\CardForgeData'
$native=Get-Content (Join-Path $root 'Assets\StreamingAssets\CardForge\Rules\astral-rift.json') -Raw -Encoding UTF8|ConvertFrom-Json
$block=Get-Content (Join-Path $data 'blocks\astral-rift-core\block.json') -Raw -Encoding UTF8|ConvertFrom-Json
$game=Get-Content (Join-Path $data 'game-definitions\astral-rift-core.json') -Raw -Encoding UTF8|ConvertFrom-Json
$flow=Get-Content (Join-Path $data 'turn-flows\ar-standard-turn.json') -Raw -Encoding UTF8|ConvertFrom-Json
$schema=Get-Content (Join-Path $data 'schema.json') -Raw -Encoding UTF8|ConvertFrom-Json
$errs=New-Object System.Collections.Generic.List[string]
function Check($condition,[string]$message){if(!$condition){$script:errs.Add($message)}}
$nativeIds=@($native.cards|ForEach-Object {$_.id})
Check (@($native.cards).Count -eq 240) 'Native card count is not 240'
Check (@($native.starterDecks).Count -eq 3) 'Native starter deck count is not 3'
Check (@($block.cardIds).Count -eq 240) 'Block card count is not 240'
Check (@($block.cardIds|Select-Object -Unique).Count -eq 240) 'Block has duplicate card IDs'
Check (@($block.cardIds|Where-Object {$_ -notin $nativeIds}).Count -eq 0) 'Block still references legacy cards'
Check ($game.packageVersion -eq '3.0.0') 'Editor game version not 3.0.0'
Check ($game.packageId -eq 'astral-rift') 'Editor package ID differs from native'
Check (@($game.sampleDecks).Count -eq 3) 'Editor deck count not 3'
Check (@($game.zones).Count -eq @($native.zones).Count) 'Editor game zones differ'
Check (@($game.resources).Count -eq @($native.resources).Count) 'Editor resources differ'
Check (@($flow.nodes).Count -eq @($native.phases).Count) 'Editor phases differ'
$fronts=@{ 'Commander'='commander'; 'Creature'='creature'; 'Spell'='spell'; 'QuickSpell'='quickspell'; 'ManaSource'='manasource' }
foreach($kind in $fronts.Keys){
    $slug=$fronts[$kind]
    $layoutFile=Join-Path $data ('blocks\astral-rift-core\layouts\ar-layout-'+$slug+'.json')
    $frameFile=Join-Path $root ('Assets\CardForgeStudio\CardForgeData\demo\astral-rift\frames\front-'+$slug+'.png')
    Check (Test-Path $layoutFile) ("Missing "+$kind+" editable front")
    Check (Test-Path $frameFile) ("Missing "+$kind+" front artwork")
    if(Test-Path $layoutFile){
        $layout=Get-Content $layoutFile -Raw -Encoding UTF8|ConvertFrom-Json
        Check ($layout.zones.Count -ge 11) ("Incomplete zones in "+$kind+" front")
        Check ($layout.isBack -eq $false) ("Wrong face side for "+$kind)
    }
}
$fields=@{}
foreach($field in $schema.fields){$fields[$field.key]=$field.id}
function Value($card,[string]$key) {
    if(!$fields.ContainsKey($key)){return ''}
    $id=$fields[$key]
    $found=@($card.values|Where-Object {$_.fieldId -eq $id}|Select-Object -First 1)
    if($found.Count -gt 0){return [string]$found[0].value}
    return ''
}
foreach($nativeCard in $native.cards){
    $id=[string]$nativeCard.id
    $file=Join-Path $data ('blocks\astral-rift-core\cards\'+$id+'.json')
    if(!(Test-Path $file)){$errs.Add("Missing editor card: $id");continue}
    $card=Get-Content $file -Raw -Encoding UTF8 |ConvertFrom-Json
    Check ($card.name -eq $nativeCard.name) ("Card name mismatch: "+$id)
    $expectedFront=if($fronts.ContainsKey([string]$card.cardType)){'ar-layout-'+$fronts[[string]$card.cardType]}else{''}
    Check (![string]::IsNullOrWhiteSpace($expectedFront)) ("Unknown editor card type: "+$id)
    Check ($card.layoutId -eq $expectedFront) ("Wrong type-specific front: "+$id)
    Check ((Value $card 'rules') -eq [string]$nativeCard.text) ("Rules text mismatch: "+$id)
    Check ((Value $card 'cost') -eq [string]$nativeCard.cost) ("Cost mismatch: "+$id)
    Check (@($card.abilityIds).Count -eq @($nativeCard.abilities).Count) ("Ability linkage mismatch: "+$id)
    foreach($nativeAbility in @($nativeCard.abilities)){
        $abilityFile=Join-Path $data ('abilities\'+$nativeAbility.id+'.json')
        if(!(Test-Path $abilityFile)){$errs.Add("Missing ability: $($nativeAbility.id)");continue}
        $ability=Get-Content $abilityFile -Raw -Encoding UTF8|ConvertFrom-Json
        Check ($ability.cardId -eq $id) ("Ability card binding mismatch: "+$ability.id)
        Check (@($ability.effects).Count -eq @($nativeAbility.effects).Count) ("Effect count mismatch: "+$ability.id)
        $nEffects=@($nativeAbility.effects)
        $eEffects=@($ability.effects)
        for($i=0;$i -lt [math]::Min($nEffects.Count,$eEffects.Count);$i++){
            Check ($nEffects[$i].kind -eq $eEffects[$i].kind) ("Effect kind mismatch: "+$ability.id)
            Check ([long]$nEffects[$i].amount -eq [long]$eEffects[$i].amount) ("Effect amount mismatch: "+$ability.id)
            Check ([string]$nEffects[$i].resourceId -eq [string]$eEffects[$i].resourceId) ("Effect resource mismatch: "+$ability.id)
            Check ([string]$nEffects[$i].target -eq [string]$eEffects[$i].target) ("Effect target mismatch: "+$ability.id)
        }
        if($nativeAbility.trigger -like 'action:*'){
            Check ($ability.actionId -eq $nativeAbility.trigger.Substring(7)) ("Action activation mismatch: "+$ability.id)
        } else {
            Check ($ability.activationTriggerId -eq $nativeAbility.trigger) ("Trigger activation mismatch: "+$ability.id)
            Check (Test-Path (Join-Path $data ('triggers\'+$nativeAbility.trigger+'.json'))) ("Missing authored trigger: "+$nativeAbility.trigger)
        }
    }
}
foreach($action in $native.actions){
    $file=Join-Path $data ('actions\'+$action.id+'.json')
    if(!(Test-Path $file)){$errs.Add("Missing editor action: "+$action.id);continue}
    $authored=Get-Content $file -Raw -Encoding UTF8|ConvertFrom-Json
    $effect=@($action.effects|Where-Object {$_})|Select-Object -First 1
    $kind=if($effect){$effect.kind}elseif($action.sourceZone){'move_card'}else{'emit_event'}
    Check ($authored.serverEffectKind -eq $kind) ("Wrong action effect: "+$action.id)
    Check ([string]$authored.sourceZoneId -eq [string]$action.sourceZone) ("Wrong source zone: "+$action.id)
    Check ([string]$authored.destinationZoneId -eq [string]$action.destinationZone) ("Wrong destination zone: "+$action.id)
}
foreach($phase in $native.phases){
    $editor=@($flow.nodes|Where-Object {$_.id -eq $phase.id}|Select-Object -First 1)
    if($editor.Count -eq 0){$errs.Add("Missing phase: "+$phase.id);continue}
    $actual=@($editor[0].actionRules|ForEach-Object {$_.actionId}|Sort-Object)
    $expected=@($phase.allowedActions|Sort-Object)
    Check (($actual -join '|') -eq ($expected -join '|')) ("Phase action schedule mismatch: "+$phase.id)
}
foreach($starter in $native.starterDecks){
    $deck=@($game.sampleDecks|Where-Object {$_.id -eq $starter.id}|Select-Object -First 1)
    Check ($deck.Count -eq 1) ("Missing starter: "+$starter.id)
    if($deck.Count -eq 1){
        Check (@($deck[0].cardIds).Count -eq 42) ("Wrong starter card count: "+$starter.id)
        Check ((@($deck[0].cardIds) -join '|') -eq (@($starter.cardIds) -join '|')) ("Starter cards differ: "+$starter.id)
    }
}
$matchLayoutFile=Join-Path $root 'Assets\CardForgeStudio\CardForgeClient\Resources\AstralRift\CardForgeMatchLayout.uxml'
$sceneFile=Join-Path $root 'Assets\CardForgeStudio\CardForgeClient\Scenes\TcgClient.unity'
Check (Test-Path $matchLayoutFile) 'Editable battle layout is missing'
Check (Test-Path $sceneFile) 'Editable battle scene is missing'
if(Test-Path $matchLayoutFile){
    $layoutText=Get-Content $matchLayoutFile -Raw -Encoding UTF8
    try { [xml]$null=$layoutText }
    catch { $errs.Add('Battle layout UXML cannot be parsed: '+$_.Exception.Message) }
    foreach($slot in @('cf-match-root','cf-match-title','cf-match-main','cf-match-field','cf-match-inspector','cf-match-action-host','cf-match-log')){
        Check ($layoutText.Contains('name="'+$slot+'"')) ('Missing editable battle slot '+$slot)
    }
}
if($errs.Count){
    $errs|Select-Object -First 30|ForEach-Object { Write-Output ('ERROR '+$_) }
    throw "$($errs.Count) authored-data mismatch(es)"
}
Write-Output "PARITY_OK: 240 editable cards, 5 type-specific fronts, linked abilities, 11 actions, 5 phases, 3 starter decks and edit-time battle layout verified."
