param([switch]$RulesOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$rulesFile = Join-Path $root 'Assets\StreamingAssets\CardForge\Rules\astral-rift.json'
$authoringRoot = Join-Path $root 'Assets\CardForgeStudio\CardForgeData'
$source = (Get-Content $rulesFile -Raw -Encoding UTF8 | ConvertFrom-Json)
$gameFile = Join-Path $authoringRoot 'game-definitions\astral-rift-core.json'
$game = (Get-Content $gameFile -Raw -Encoding UTF8 | ConvertFrom-Json)
$blockFile = Join-Path $authoringRoot 'blocks\astral-rift-core\block.json'
$block = (Get-Content $blockFile -Raw -Encoding UTF8 | ConvertFrom-Json)
$cardFolder = Join-Path $authoringRoot 'blocks\astral-rift-core\cards'
$abilityFolder = Join-Path $authoringRoot 'abilities'
$artRoot = 'Assets/CardForgeStudio/CardForgeData/demo/astral-rift/art'
$colors = @('Solar','Tidal','Verdant','Umbral')
$prefixes = @(
  @('Dawnline','Emberglass','Gilded','Aurora','Sunveil','Radiant','Starfall','Ashwing','Cinder','Goldspire','Helios','Firstlight','Daybreak','Flare','Horizon','Pyreborn','Brighthalo','Sunforge','Solarwind','Beacon','Lightkeeper','Luminant','Vowbound','Skyfire','Firecrest','Suncrest','Brightward','Oathkeeper','Solaris','Dawnsong','Firewake','Aurelian','Coronal','Solstice','Lucent','Sunward','Sunlance','Embergate','Goldcrest','Phoenix','Daylily','Blazewing','Firesong','Sunburst','Mirrorglass','Heatwave','Sunglass','Firebrand','Kindled','Flameshard','Daystar','Golden','Lightweaver','Emberward','Firstborn','Ignition','Solarflare','Flamecrest','Starforged','Sunglade'),
  @('Tidebound','Moonlit','Riftwater','Deepcurrent','Azure','Pearl','Undertow','Frostwake','Seafoam','Rainshard','Mistcall','Reefborn','Wavecrest','Glimmerfin','Floodsong','Abyssal','Riverglass','Stormtide','Nacre','Cloudwake','Dewline','Brineglass','Tidewalker','Seasong','Drownlight','Glacial','Mistweaver','Moonwake','Rainborn','Driftwood','Starcurrent','Watershade','Crestfallen','Bluemoon','Tearfall','Tidekeeper','Saltspire','Oceanheart','Rainsilver','Oysterglow','Deepward','Mooncrest','Floodgate','Stormborn','Rivermouth','Pearlward','Shimmerwave','Duskcurrent','Seafern','Glaze','Bluespire','Currentbound','Mirrorlake','Fathom','Tideglass','Deepwell','Moonborn','Waveblade','Aqualume','Stillwater'),
  @('Verdant','Starbloom','Mossborn','Seedship','Rootbound','Thornward','Ironbark','Crownfern','Bloomspire','Wilderleaf','Greenheart','Vineglass','Canopy','Briarwood','Grovekeeper','Sungrove','Wildflower','Leafstorm','Mosslight','Ancientroot','Barkshield','Dewbloom','Forestborn','Thorncall','Bramble','Greenspire','Sapling','Bloomward','Rootsong','Emberleaf','Feral','Ferncrest','Wildseed','Petalborn','Glimmerbark','Leafguard','Elderwood','Mossveil','Rainwood','Sprout','Nettle','Thorncrest','Greenmoss','Rootcrest','Pineglass','Blossom','Bloomforge','Verdure','Amberleaf','Vinelight','Heartwood','Evergreen','Growlight','Mosswood','Orchid','Briarbound','Greencall','Lushwood','Groveborn','Rootspire'),
  @('Umbral','Nullwake','Whisper','Voidglass','Shadowline','Nightfall','Gloomshade','Eventide','Duskborn','Starless','Obsidian','Moonshadow','Gravecold','Riftshade','Nightspire','Phantom','Blackstar','Hollow','Silent','Dustwake','Grimlight','Ravencrest','Shatter','Eclipsed','Ashveil','Voidcall','Spectral','Fadewood','Blackthorn','Hollowstar','Wraithbound','Duskwind','Darktide','Nightward','Shadeglass','Hush','Duskwhisper','Dreadwake','Veilborn','Ghostline','Darkspore','Afterglow','Darkmirror','Shadesong','Inkwell','Nocturne','Sable','Voidward','Dreadspire','Whisperwake','Veilcrest','Oblivion','Gloaming','Blackwake','Shrouded','Voidborn','Darkstar','Shiver','Umbrafire','Grimward')
)
$creatureSuffix = @('Sentinel','Lancer','Seer','Harrier','Warden','Stalker','Squire','Drake','Guardian','Alchemist','Beast','Ranger','Knight','Artificer','Hound','Vanguard','Oracle','Marauder','Archer','Sage','Charger','Keeper','Rider','Raptor','Invoker','Golem','Duelist','Protector','Mystic','Sovereign','Wyrmling','Colossus')
$spellSuffix = @('Convergence','Edict','Revelation','Ascension','Sanctuary','Oath','Cataclysm','Rebirth','Benediction','Reckoning','Memory','Manifest')
$quickSuffix = @('Countercurrent','Spark','Dodge','Feint','Riposte','Blink','Surge','Veil','Deflection','Pulse','Snap','Intercession')
$sourceSuffix = @('Crucible','Nexus','Sanctum')
$strategyName = @('Solar Vanguard','Tidal Archive','Verdant Sanctuary')
$strategyText = @(
 'Aggressive creature tempo, radiant shields and burst damage.',
 'Control through quickspells, card flow and resource denial.',
 'Resilient creature growth, defensive auras and sustained advantage.'
)
function Write-Json($path,$item) {
  $dir = Split-Path $path -Parent
  if (!(Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
  $json = ConvertTo-Json -InputObject $item -Depth 40
  [IO.File]::WriteAllText($path,$json,[Text.UTF8Encoding]::new($false))
}
function E($kind,$resource,$amount,$target='') {
  $e = [ordered]@{kind=$kind}
  if ($resource) { $e['resourceId']=$resource }
  if ($amount -ne $null) { $e['amount']=[long]$amount }
  if ($target) {$e['target']=$target}
  return $e
}
function Effect-Text($e) {
  $n = $e.amount
  switch($e.kind) {
    'modify_resource' {
      switch($e.resourceId) {
        'shield' {return "Gain $n shield"}
        'life' {if ($e.target -eq 'opponent') {return "Deal $(-$n) damage to an enemy player"}; return "Restore $n life"}
        'energy' {return "Gain $n mana"}
        'renown' {return "Gain $n renown"}
        'momentum' {return "Gain $n momentum"}
      }
    }
    'drain_resource' { return "Drain $n enemy mana" }
    'scale_resource' { return "Gain $n shield per friendly creature" }
    'draw' { return "Draw $n card(s)" }
    'mill' { return "Mill $n enemy card(s)" }
    'reclaim' { return "Return $n card(s) from your discard to hand" }
    'destroy_target' { return 'Destroy one opposing creature' }
  }
  return $e.kind
}
function Ability-Effects([int]$seq,[int]$f,[string]$type) {
   if($type -eq 'Commander') {
     switch($f) {
       0 {return @((E 'modify_resource' 'shield' 4),(E 'modify_resource' 'life' -4 'opponent'),(E 'draw' '' 1))}
       1 {return @((E 'draw' '' 3),(E 'drain_resource' 'energy' 2 'opponent'),(E 'modify_resource' 'shield' 1))}
       2 {return @((E 'scale_resource' 'shield' 2),(E 'modify_resource' 'renown' 3),(E 'draw' '' 1))}
       3 {return @((E 'destroy_target' '' 1 'opponent'),(E 'mill' '' 4 'opponent'),(E 'modify_resource' 'shield' 2))}
     }
   }
   if($type -eq 'ManaSource') {
     $pairs = @(
       @((E 'modify_resource' 'shield' 1),(E 'modify_resource' 'renown' 1),(E 'modify_resource' 'life' 1)),
       @((E 'draw' '' 1),(E 'modify_resource' 'shield' 1),(E 'modify_resource' 'renown' 1)),
       @((E 'modify_resource' 'life' 1),(E 'modify_resource' 'shield' 2),(E 'modify_resource' 'energy' 1)),
       @((E 'modify_resource' 'renown' 1),(E 'mill' '' 1 'opponent'),(E 'drain_resource' 'energy' 1 'opponent'))
     )
     return @($pairs[$f][($seq-2)%3])
   }
   $pattern = ($seq + 7*$f) % 15
   $secondary = [int][math]::Floor(($seq+60*$f)/15)
   $amount = 1 + ($secondary % 3)
   switch($pattern) {
     0 {return @((E 'draw' '' 1),(E 'modify_resource' 'shield' $amount))}
     1 {return @((E 'modify_resource' 'shield' (1+$amount)),(E 'modify_resource' 'renown' 1))}
     2 {return @((E 'modify_resource' 'life' (-$amount) 'opponent'),(E 'modify_resource' 'shield' 1))}
     3 {return @((E 'mill' '' $amount 'opponent'),(E 'draw' '' 1))}
     4 {return @((E 'modify_resource' 'renown' $amount),(E 'modify_resource' 'life' 1))}
     5 {return @((E 'drain_resource' 'energy' 1 'opponent'),(E 'modify_resource' 'shield' $amount))}
     6 {return @((E 'scale_resource' 'shield' 1),(E 'modify_resource' 'energy' $amount))}
     7 {return @((E 'reclaim' '' 1),(E 'modify_resource' 'life' $amount))}
     8 {return @((E 'modify_resource' 'energy' $amount),(E 'draw' '' 1))}
     9 {return @((E 'modify_resource' 'life' (-$amount) 'opponent'),(E 'mill' '' 1 'opponent'))}
     10 {return @((E 'modify_resource' 'shield' $amount),(E 'draw' '' 2))}
     11 {return @((E 'modify_resource' 'renown' 1),(E 'scale_resource' 'shield' $amount))}
     12 {return @((E 'mill' '' 2 'opponent'),(E 'modify_resource' 'energy' $amount))}
     13 {return @((E 'modify_resource' 'life' $amount),(E 'draw' '' 1))}
     default {return @((E 'modify_resource' 'shield' 1),(E 'modify_resource' 'life' (-$amount) 'opponent'),(E 'modify_resource' 'renown' 1))}
   }
}
$cards = New-Object System.Collections.Generic.List[object]
$decks = New-Object System.Collections.Generic.List[object]
for($f=0;$f -lt 4;$f++){
 $faction = $colors[$f]
 for($i=1;$i -le 60;$i++){
  $id = 'ar-card-'+$faction.ToLowerInvariant()+'-'+$i.ToString('000')
  $type = if($i -eq 1){'Commander'}elseif($i -le 4){'ManaSource'}elseif($i -le 36){'Creature'}elseif($i -le 48){'Spell'}else{'QuickSpell'}
  $name = if($type -eq 'Commander'){$faction+' '+@('Dawnline Captain','Tidebound Oracle','Elderroot Sovereign','Nullwake Duelist')[$f]} elseif($type -eq 'ManaSource'){$faction+' '+$sourceSuffix[$i-2]+' Mana Source'}else{
    $prefixes[$f][$i-1]+' '+$(if($type -eq 'Creature'){$creatureSuffix[$i-5]}elseif($type -eq 'Spell'){$spellSuffix[$i-37]}else{$quickSuffix[$i-49]})
  }
  $cost = if($type -eq 'ManaSource'){0}elseif($type -eq 'Commander'){3+$f}elseif($type -eq 'Creature'){1+(($i+2*$f)%6)}elseif($type -eq 'Spell'){2+(($i+$f)%4)}else{1+(($i+$f)%3)}
  $power = if($type -in @('Creature','Commander')){1+(($i+$f)%6) + $(if($type -eq 'Commander'){2}else{0})}else{0}
  $tough = if($type -in @('Creature','Commander')){1+(($i*3+$f)%6) + $(if($type -eq 'Commander'){3}else{0})}else{0}
  $rarity = if($type -eq 'Commander'){'Mythic'}elseif($type -eq 'ManaSource'){'Rare'}elseif($i%13 -eq 0){'Mythic'}elseif($i%7 -eq 0){'Rare'}elseif($i%3 -eq 0){'Uncommon'}else{'Common'}
  $effects = @(Ability-Effects $i $f $type)
  $trigger = switch ($type){'Commander'{'action:ar-cast-commander'}'Creature'{'action:ar-play-creature'}'Spell'{'action:ar-cast-spell'}'QuickSpell'{'action:ar-cast-quickspell'}default{'turn_started'}}
  $description = (($effects|ForEach-Object {Effect-Text $_}) -join '. ') + '.'
  if($type -eq 'ManaSource'){$description = 'Permanent. Generates 1 mana each of your turns. Friendly creatures gain +1 power and +1 toughness, plus +1/+1 per Mana Source level. Level up (max 3): pay 2 + current level mana. Passive: '+$description}
  if($type -eq 'Commander'){$description = 'Starts in Command, visible to all. When defeated, returns to Command; each recast costs +2. On cast: '+$description}
  if($type -eq 'QuickSpell'){$description = 'Fast: cast during any player turn. '+$description}
  $ability = [ordered]@{id='ar-ability-unique-'+$id;name=$name+' - '+@('Solar','Tidal','Verdant','Umbral')[$f]+' Signature';trigger=$trigger;conditions=@();effects=$effects}
  $card = [ordered]@{
    id=$id;name=$name;type=$type+' - '+$faction;affinity=$faction;rarity=$rarity;cost=$cost;
    power=$power;toughness=$tough;text=$description;flavor='Within the Astral Rift, no two constellations answer the same call.';
    keywords=$(if($type -eq 'Creature'){ @(@('haste','guard','ranged','')[($i+$f)%4]) | Where-Object {$_}}else{@()});
    abilities=@($ability);assetId='astral-rift.card.'+$id
  }
  $cards.Add($card)
 }
 if($f -lt 3){
  $ids = New-Object System.Collections.Generic.List[string]
  foreach($n in (1,2)){ $ids.Add('ar-card-'+$faction.ToLowerInvariant()+'-'+$n.ToString('000')) }
  foreach($n in 5..28){$ids.Add('ar-card-'+$faction.ToLowerInvariant()+'-'+$n.ToString('000'))}
  foreach($n in 37..44){$ids.Add('ar-card-'+$faction.ToLowerInvariant()+'-'+$n.ToString('000'))}
  foreach($n in 49..56){$ids.Add('ar-card-'+$faction.ToLowerInvariant()+'-'+$n.ToString('000'))}
  if($ids.Count -ne 42 -or (@($ids|Select-Object -Unique)).Count -ne 42){throw 'Starter must contain 42 distinct cards'}
  $decks.Add([ordered]@{id='ar-deck-'+$faction.ToLowerInvariant()+'-starter';name=$strategyName[$f];strategy=$strategyText[$f];cardIds=@($ids)})
 }
}
if($cards.Count -ne 240 -or $decks.Count -ne 3){throw 'Catalog cardinality error'}
$source.version='3.0.0'
foreach($item in $cards){
 $original=$item['keywords']
 $keywordValues=[string[]]@()
 if($original -is [string] -and $original.Length -gt 0){ $keywordValues=[string[]]@($original) }
 elseif($original -is [array]){ $keywordValues=[string[]]@($original|Where-Object {$_}) }
 $item['keywords']=$keywordValues
}$source.cards=$cards.ToArray()
$source.startingDeck=@($decks[0].cardIds)
$source.starterDecks=$decks.ToArray()
$source.startingHand=5
$source | Add-Member -NotePropertyName drawPerTurn -NotePropertyValue 0 -Force
$source.deckRules=[ordered]@{minCards=40;maxCards=120;maxCopiesPerCard=3;allowedCardTypes=@('Creature','Spell','QuickSpell','Commander','ManaSource');typeLimits=@();cardLimits=@()}
foreach($z in $source.zones){
 switch($z.id){
   'deck' {$z.name='Library';$z.visibility='hidden'}
   'hand' {$z.name='Hand';$z.visibility='owner'}
   'battlefield' {$z.name='Battlefield';$z.visibility='public';$z.allowedCardTypes=@('Creature','Commander')}
   'command' {$z.name='Commander';$z.visibility='public';$z.deckStartCardType='Commander';$z.allowedCardTypes=@('Commander');$z.returnOnLeave=$true;$z.immovable=$false}
   'mana-source' {$z.name='Mana Source';$z.visibility='public';$z.deckStartCardType='ManaSource';$z.allowedCardTypes=@('ManaSource');$z.immovable=$true;$z.returnOnLeave=$false}
   'discard' {$z.name='Discard';$z.visibility='public'}
   'void' {$z.name='Exile';$z.visibility='public'}
 }
}
# Game design is a full board: no deprecated Relic or Rift lanes.
$source.zones=@($source.zones|Where-Object {$_.id -notin @('relic-bay','rift')})
foreach($res in $source.resources){
 if($res.id -eq 'energy'){$res.name='Mana';$res.initialValue=0;$res.maximumValue=24}
 if($res.id -eq 'life'){$res.name='Life';$res.initialValue=25;$res.maximumValue=25}
}
function Act($id,$name,$src,$dst,$cost,$active,$kind,$target='') {
 $a=[ordered]@{id=$id;name=$name;sourceZone=$src;destinationZone=$dst;requiresActivePlayer=$active;costs=@();conditions=@();effects=@()}
 if($cost -gt 0){$a.costs=@([ordered]@{resourceId='energy';amount=$cost})}
 if($kind){$effect=[ordered]@{kind=$kind};if($target){$effect.target=$target};$a.effects=@($effect)}
 return $a
}
$actions=@(
 (Act 'ar-play-creature' 'Summon creature' 'hand' 'battlefield' 0 $true ''),
 (Act 'ar-cast-commander' 'Cast commander' 'command' 'battlefield' 0 $true ''),
 (Act 'ar-cast-spell' 'Cast spell' 'hand' 'discard' 0 $true ''),
 (Act 'ar-cast-quickspell' 'Cast quickspell' 'hand' 'discard' 0 $false ''),
 (Act 'ar-level-mana' 'Level up mana source' 'mana-source' '' 0 $true 'level_mana_source'),
 (Act 'ar-attack-player' 'Attack player' 'battlefield' '' 0 $true 'attack_player'),
 (Act 'ar-attack-creature' 'Attack creature' 'battlefield' '' 0 $true 'attack_creature'),
 (Act 'ar-draw' 'Draw one card' '' '' 0 $true 'draw'),
 (Act 'ar-next-phase' 'Next phase' '' '' 0 $true 'advance_phase'),
 (Act 'ar-end-turn' 'End turn' '' '' 0 $true 'end_turn'),
 (Act 'ar-concede' 'Concede' '' '' 0 $true 'finish_game')
)
foreach($a in $actions){ if($a.id -eq 'ar-draw'){$a.effects=@([ordered]@{kind='draw';amount=1})}}
$source.actions=$actions
$source.phases=@(
 [ordered]@{id='ar-ready';name='Ready';allowedActions=@('ar-cast-quickspell','ar-next-phase');automaticAdvance=$false},
 [ordered]@{id='ar-draw-phase';name='Draw';allowedActions=@('ar-draw','ar-cast-quickspell','ar-next-phase');automaticAdvance=$false},
 [ordered]@{id='ar-main';name='Main';allowedActions=@('ar-play-creature','ar-cast-commander','ar-cast-spell','ar-cast-quickspell','ar-level-mana','ar-next-phase');automaticAdvance=$false},
 [ordered]@{id='ar-clash';name='Combat';allowedActions=@('ar-attack-player','ar-attack-creature','ar-cast-quickspell','ar-next-phase');automaticAdvance=$false},
 [ordered]@{id='ar-end';name='End';allowedActions=@('ar-cast-quickspell','ar-end-turn','ar-concede');automaticAdvance=$false}
)
$source.triggers=@()
$source.victoryRules=@([ordered]@{id='ar-life-loss';name='Life reaches zero';kind='resource-threshold';outcome='eliminate';scope='player';resourceId='life';threshold=0;comparison='less-than-or-equal'})
Write-Json $rulesFile $source
# Keep editable designer data aligned with the live rulepack.
$game.packageVersion='3.0.0'
$game.startingHand=5
$game.deckRules=$source.deckRules
$game.sampleDecks=@($decks|ForEach-Object {
 [ordered]@{id=$_.id;name=$_.name;description=$_.strategy;minimumCards=40;maximumCopiesPerCard=3;cardIds=@($_.cardIds);strategy=$_.strategy}
})
$game.zones=$source.zones
$game.resources=$source.resources
$game.victoryRules=$source.victoryRules
$game.boardLayout=@($game.boardLayout|Where-Object {$_.zoneId -notin @('rift','relic-bay')})
Write-Json $gameFile $game
$block.cardIds=@($cards|ForEach-Object {$_.id})
$block.description='240 distinct cards; three 40+2 starters; Commander, Mana Source, Creature, Spell, QuickSpell. Arena-inspired UI Toolkit battlefield.'
Write-Json $blockFile $block
if(-not $RulesOnly){
  $old = Get-Content (Join-Path $cardFolder 'ar-card-dawn-captain.json') -Raw | ConvertFrom-Json
  $fieldIds=@{}
  $keys=@('title','cost','type','rules','power','toughness','art','affinity','affinity_icon','rarity','flavor','collector','active')
  for($i=0;$i -lt $keys.Count;$i++){$fieldIds[$keys[$i]]=$old.values[$i].fieldId}
  $idx=0
  foreach($card in $cards){
    $idx++
    $values=[ordered]@{
      title=$card.name;cost=[string]$card.cost;type=$card.type;rules=$card.text;power=[string]$card.power;
      toughness=[string]$card.toughness;art=$artRoot+'/'+$card.id+'.png';affinity=$card.affinity;
      affinity_icon='Assets/CardForgeStudio/CardForgeData/demo/astral-rift/icons/'+$card.affinity.ToLowerInvariant()+'.png';
      rarity=$card.rarity;flavor=$card.flavor;collector=$idx.ToString('000')+'/240 ARFT';active='true'
    }
    $cardDoc=[ordered]@{version=1;id=$card.id;name=$card.name;cardNumber=$idx.ToString('000');layoutId='ar-layout-front';backLayoutId='ar-layout-back';sourceBlockId='';sourceCardId='';cardType=$card.type.Split(' ')[0];variantId=$card.rarity.ToLowerInvariant();values=@($keys|ForEach-Object {[ordered]@{fieldId=$fieldIds[$_];value=$values[$_]}});zoneOverrides=@();requirements=@();keywordIds=@();abilityIds=@($card.abilities[0].id)}
    Write-Json (Join-Path $cardFolder ($card.id+'.json')) $cardDoc
    $abl=$card.abilities[0]
    $abilityDoc=[ordered]@{version=1;id=$abl.id;name=$abl.name;cardId=$card.id;activationTriggerId=$abl.trigger;actionId=$abl.trigger.Replace('action:','');phaseId='';condition='';operation='Execute server-authoritative ability effects';effect=$card.text;priority=30;scope='Source card';responseTiming='On source activation';responseResolution='Resolve in priority order';responseActionIds=@();causeTriggerIds=@();rules=@();effects=@($abl.effects);conditions=@();graphNodes=@();graphLinks=@()}
    if($card.type.StartsWith('ManaSource')){$abilityDoc.actionId=''}
    Write-Json (Join-Path $abilityFolder ($abl.id+'.json')) $abilityDoc
  }
}
Write-Output ("Astral Rift 3.0: {0} unique card definitions; {1} legal 42-card starters; {2} editable card docs." -f $cards.Count,$decks.Count,$idx)



