param([switch]$Preview)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$data=Join-Path $root 'Assets\CardForgeData'
$rulesPath=Join-Path $root 'Assets\StreamingAssets\CardForge\Rules\astral-rift.json'
$rules=Get-Content $rulesPath -Raw -Encoding UTF8 | ConvertFrom-Json
if($rules.version -ne '3.0.0' -or @($rules.cards).Count -ne 240 -or @($rules.starterDecks).Count -ne 3){
    throw 'Expected Astral Rift 3.0 with 240 cards and three starter decks.'
}
$blockFile=Join-Path $data 'blocks\astral-rift-core\block.json'
$gameFile=Join-Path $data 'game-definitions\astral-rift-core.json'
$flowFile=Join-Path $data 'turn-flows\ar-standard-turn.json'
$block=Get-Content $blockFile -Raw -Encoding UTF8 | ConvertFrom-Json
$game=Get-Content $gameFile -Raw -Encoding UTF8 | ConvertFrom-Json
$schema=Get-Content (Join-Path $data 'schema.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$nativeIds=@($rules.cards | ForEach-Object { $_.id })
$missing=@($nativeIds | Where-Object { $_ -notin @($block.cardIds) })
Write-Output "Astral Rift 3.0 authoring: $($rules.cards.Count) live cards; $($missing.Count) missing from editor block; $($rules.actions.Count) live actions."
if($Preview){exit 0}
$backupDir=Join-Path $root 'artifacts\backups'
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
$archive=Join-Path $backupDir ('astral-authoring-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.zip')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$paths=@(
    (Join-Path $data 'blocks\astral-rift-core'),
    (Join-Path $data 'game-definitions'),
    (Join-Path $data 'turn-flows'),
    (Join-Path $data 'actions'),
    (Join-Path $data 'abilities'),
    (Join-Path $data 'triggers'),
    (Join-Path $data 'keywords')
)
Compress-Archive -Path $paths -DestinationPath $archive -CompressionLevel Optimal
Write-Output "Backup created: $archive"
$utf8=[Text.UTF8Encoding]::new($false)
function WriteJson([string]$path,$value){
    $folder=Split-Path $path -Parent
    if(!(Test-Path $folder)){New-Item -ItemType Directory -Path $folder -Force|Out-Null}
    [IO.File]::WriteAllText($path,(ConvertTo-Json -InputObject $value -Depth 90),$utf8)
}
function SetField($card,[string]$key,[string]$value) {
    $schemaField=@($schema.fields | Where-Object {$_.key -eq $key} | Select-Object -First 1)
    if($schemaField.Count -eq 0){return}
    $slot=@($card.values | Where-Object {$_.fieldId -eq $schemaField[0].id} | Select-Object -First 1)
    if($slot.Count -gt 0){$slot[0].value=$value}
    else{$card.values=@($card.values)+[pscustomobject]@{fieldId=$schemaField[0].id;value=$value}}
}
$cardFolder=Join-Path $data 'blocks\astral-rift-core\cards'
$cardTemplate=Get-Content (Join-Path $cardFolder 'ar-card-solar-057.json') -Raw -Encoding UTF8 |ConvertFrom-Json
$abilityFolder=Join-Path $data 'abilities'
$abilityTemplate=Get-Content (Join-Path $abilityFolder 'ar-ability-unique-ar-card-solar-001.json') -Raw -Encoding UTF8|ConvertFrom-Json
$actionFolder=Join-Path $data 'actions'
$actionTemplate=Get-Content (Join-Path $actionFolder 'ar-deploy.json') -Raw -Encoding UTF8|ConvertFrom-Json
$allNativeTriggers=@($rules.cards|ForEach-Object {$_.abilities}|ForEach-Object {$_.trigger}|Where-Object {$_ -and !$_.StartsWith('action:')}|Select-Object -Unique)
foreach($triggerName in $allNativeTriggers){
    if($triggerName -notmatch '^[a-z0-9_-]+$'){throw "Unsafe trigger ID $triggerName"}
    $triggerPath=Join-Path $data ('triggers\'+$triggerName+'.json')
    if(!(Test-Path $triggerPath)){
        WriteJson $triggerPath ([ordered]@{version=1;id=$triggerName;name=$triggerName.Replace('_',' ');eventType=$triggerName.Replace('_',' ');source='Any object';fromZone='';toZone='';orientation='';rules=@();condition='';responseWindow='Immediate response';priority=0;causeActionIds=@();causeAbilityIds=@();responseActionIds=@()})
    }
}
$allKeywords=@($rules.cards|ForEach-Object {$_.keywords}|Where-Object {$_}|Select-Object -Unique)
foreach($keyword in $allKeywords){
    if($keyword -notmatch '^[a-z0-9-]+$'){continue}
    $path=Join-Path $data ('keywords\ar-keyword-'+$keyword+'.json')
    if(!(Test-Path $path)){
        WriteJson $path ([ordered]@{version=1;id='ar-keyword-'+$keyword;name=([cultureinfo]::InvariantCulture.TextInfo.ToTitleCase($keyword));rulesText=$keyword;reminderText='';abilityIds=@();tags=@()})
    }
}
$index=0
foreach($native in $rules.cards){
    $index++
    $id=[string]$native.id
    if($id -notmatch '^ar-card-[a-z]+-[0-9]{3}$'){throw "Unsafe card ID $id"}
    $cardPath=Join-Path $cardFolder ($id+'.json')
    $card=if(Test-Path $cardPath){Get-Content $cardPath -Raw -Encoding UTF8|ConvertFrom-Json}else{ConvertTo-Json $cardTemplate -Depth 50 |ConvertFrom-Json}
    $kind=([string]$native.type -split '\s*-\s*')[0]
    $card.id=$id
    $card.name=[string]$native.name
    $card.cardNumber=$index.ToString('000')
    $card.cardType=$kind
    $card.variantId=([string]$native.rarity).ToLowerInvariant()
    $card.values=@($card.values)
    $card.abilityIds=@($native.abilities | ForEach-Object {$_.id})
    $card.keywordIds=@($native.keywords|Where-Object {$_ -and $_ -match '^[a-z0-9-]+$'}|ForEach-Object {'ar-keyword-'+$_})
    if(!(Test-Path $cardPath)){$card.requirements=@();$card.zoneOverrides=@()}
    SetField $card 'title' ([string]$native.name)
    SetField $card 'cost' ([string]$native.cost)
    SetField $card 'type' ([string]$native.type)
    SetField $card 'rules' ([string]$native.text)
    SetField $card 'power' ([string]$native.power)
    SetField $card 'toughness' ([string]$native.toughness)
    SetField $card 'affinity' ([string]$native.affinity)
    SetField $card 'rarity' ([string]$native.rarity)
    SetField $card 'flavor' ([string]$native.flavor)
    SetField $card 'collector' ($index.ToString('000')+'/240  ARFT')
    SetField $card 'active' 'true'
    SetField $card 'art' ('Assets/CardForgeStudio/CardForgeClient/Resources/AstralRift/Illustrations/'+$id+'.png')
    WriteJson $cardPath $card
    foreach($nativeAbility in @($native.abilities)){
        $aid=[string]$nativeAbility.id
        if($aid -notmatch '^ar-ability-unique-ar-card-[a-z]+-[0-9]{3}$'){throw "Unsafe ability ID $aid"}
        $path=Join-Path $abilityFolder ($aid+'.json')
        $ability=if(Test-Path $path){Get-Content $path -Raw -Encoding UTF8|ConvertFrom-Json}else{ConvertTo-Json $abilityTemplate -Depth 50|ConvertFrom-Json}
        $ability.id=$aid
        $ability.name=[string]$nativeAbility.name
        $ability.cardId=$id
        $trigger=[string]$nativeAbility.trigger
        $ability.actionId=if($trigger.StartsWith('action:')){$trigger.Substring(7)}else{''}
        $ability.activationTriggerId=if(!$trigger.StartsWith('action:')){$trigger}else{''}
        $ability.condition=''
        $ability.effect=[string]$native.text
        $ability.operation='Resolve structured server effects'
        $ability.responseTiming='On '+$trigger
        $ability.rules=@()
        $ability.effects=@($nativeAbility.effects)
        $ability.conditions=@($nativeAbility.conditions)
        $ability.responseActionIds=@()
        $ability.causeTriggerIds=@()
        $ability.graphNodes=@()
        $ability.graphLinks=@()
        WriteJson $path $ability
    }
}
foreach($native in @($rules.actions)){
    $id=[string]$native.id
    if($id -notmatch '^[a-z0-9-]+$'){throw "Unsafe action ID $id"}
    $path=Join-Path $actionFolder ($id+'.json')
    $a=if(Test-Path $path){Get-Content $path -Raw -Encoding UTF8|ConvertFrom-Json}else{ConvertTo-Json $actionTemplate -Depth 50|ConvertFrom-Json}
    $a.id=$id;$a.name=[string]$native.name
    $a.sourceZoneId=[string]$native.sourceZone
    $a.destinationZoneId=[string]$native.destinationZone
    $a.requiresActivePlayer=[bool]$native.requiresActivePlayer
    $a.targets=[string]$native.target
    $a.triggerId=''
    $a.causeTriggerIds=@()
    $a.graphNodes=@()
    $a.graphLinks=@()
    $effects=@($native.effects|Where-Object {$_})
    $effect=if($effects.Count -gt 0){$effects[0]}else{$null}
    $a.serverEffectKind=if($effect){[string]$effect.kind}elseif($a.sourceZoneId){'move_card'}else{'emit_event'}
    $a.effect=[string]$native.name
    $a.resolution='Validate phase, source, targets and cost; apply the server effect; evaluate victory.'
    $a.condition='Server phase and source validation'
    $a.resourceId=if($effect){[string]$effect.resourceId}else{''}
    $a.effectAmount=if($effect -and $effect.amount){[int]$effect.amount}else{0}
    $a.effectTarget=if($effect){[string]$effect.target}else{''}
    $a.eventType=if($effect){[string]$effect.eventType}else{''}
    $a.conditionKind='';$a.conditionKey='';$a.conditionValue=''
    $a.costResourceId='';$a.costAmount=0
    foreach($cost in @($native.costs|Where-Object {$_})){
        if($cost.resourceId){$a.costResourceId=[string]$cost.resourceId;$a.costAmount=[int]$cost.amount;break}
    }
    $a.cost=if($a.costAmount -gt 0){"$($a.costAmount) $($a.costResourceId)"}else{'Card cost or no fixed cost'}
    WriteJson $path $a
}
$flow=Get-Content $flowFile -Raw -Encoding UTF8 |ConvertFrom-Json
$flow.name='Astral Rift 3.0 - five phase turn'
$flow.description='Ready, Draw, Main, Combat, End. Actions scheduled by the authoritative rules.'
$oldNodes=@($flow.nodes)
$nodes=@()
$links=@()
$phaseIndex=0
foreach($phase in $rules.phases){
    $prev=@($oldNodes|Where-Object {$_.id -eq $phase.id}|Select-Object -First 1)
    $phaseRules=@()
    foreach($actionId in @($phase.allowedActions)){
        $phaseRules+= [ordered]@{id=('ar-rule-'+$phase.id+'-'+$actionId);actionId=$actionId;availability='During '+$phase.name;condition='';priority=100;resolutionOrder='Server priority';optional=$false;beforeTriggerIds=@();afterTriggerIds=@()}
    }
    $nodes+= [ordered]@{id=$phase.id;name=$phase.name;phase=$phase.name;check='Follow server rules for legal actions';resolution='Resolve submitted actions and responses';x=($phaseIndex*275+60);y=80;optional=[bool]$phase.automaticAdvance;actionRules=@($phaseRules)}
    if($phaseIndex -gt 0){$links+= [ordered]@{from=$rules.phases[$phaseIndex-1].id;to=$phase.id;label='next phase'}}
    $phaseIndex++
}
if($rules.phases.Count -gt 1){
    $links+= [ordered]@{from=$rules.phases[$rules.phases.Count-1].id;to=$rules.phases[0].id;label='next turn'}
}
$flow.nodes=@($nodes);$flow.links=@($links);WriteJson $flowFile $flow
$game.packageId='astral-rift'
$game.gameId='astral-rift'
$game.packageVersion='3.0.0'
$game.gameSystem=[string]$rules.gameSystem
$game.minimumPlayers=2;$game.maximumPlayers=5
$game.startingHand=if($rules.startingHand){[int]$rules.startingHand}else{5}
$game.turnFlowId='ar-standard-turn'
$game.zones=@($rules.zones|ForEach-Object {
    [ordered]@{id=$_.id;name=$_.name;ownerScope=$_.ownerScope;visibility=$_.visibility;ordered=[bool]$_.ordered;capacity=[int]$_.capacity;allowedCardTypes=@($_.allowedCardTypes);initialCardIds=@($_.initialCardIds);deckStartCardType=[string]$_.deckStartCardType;returnOnLeave=[bool]$_.returnOnLeave;immovable=[bool]$_.immovable}
})
$game.resources=@($rules.resources|ForEach-Object {
    [ordered]@{id=$_.id;name=$_.name;scope=$_.scope;initialValue=[int]$_.initialValue;minimumValue=[int]$_.minimumValue;maximumValue=[int]$_.maximumValue}
})
$game.boardLayout=@($game.boardLayout|Where-Object {$_.zoneId -in @($rules.zones|ForEach-Object {$_.id})})
$game.victoryRules=@($rules.victoryRules|ForEach-Object {
    [ordered]@{id=$_.id;name=$_.name;kind=$_.kind;outcome=$_.outcome;scope=$_.scope;resourceId=$_.resourceId;zoneId=$_.zoneId;comparison=$_.comparison;threshold=[int]$_.threshold;description=$_.description}
})
$game.deckRules.minCards=[int]$rules.deckRules.minCards
$game.deckRules.maxCards=[int]$rules.deckRules.maxCards
$game.deckRules.maxCopiesPerCard=[int]$rules.deckRules.maxCopiesPerCard
$game.deckRules.allowedCardTypes=@($rules.deckRules.allowedCardTypes)
$game.deckRules.typeLimits=@([ordered]@{cardType='Commander';minCount=1;maxCount=1},[ordered]@{cardType='ManaSource';minCount=1;maxCount=1})
$game.deckRules.cardLimits=@()
$game.sampleDecks=@($rules.starterDecks|ForEach-Object {
    [ordered]@{id=$_.id;name=$_.name;description=$_.strategy;strategy=$_.strategy;minimumCards=40;maximumCopiesPerCard=3;cardIds=@($_.cardIds)}
})
$game.defaultDeckId=$game.sampleDecks[0].id
WriteJson $gameFile $game
$block.cardIds=@($nativeIds)
$block.description='Astral Rift 3.0: 240 original cards, three 42-card unique starter decks, Commander, Mana Source, five phases and server-authoritative effects.'
WriteJson $blockFile $block
$checkBlock=Get-Content $blockFile -Raw -Encoding UTF8|ConvertFrom-Json
$checkGame=Get-Content $gameFile -Raw -Encoding UTF8|ConvertFrom-Json
if(@($checkBlock.cardIds).Count -ne 240 -or @($checkGame.sampleDecks).Count -ne 3 -or $checkGame.packageVersion -ne '3.0.0'){
    throw 'Authoring sync verification failed; restore from backup.'
}
Write-Output "SYNC_OK: 240 editable cards, 240 native abilities, $(@($rules.actions).Count) native actions, 5 phases, 3 starters."
Write-Output "BACKUP: $archive"
