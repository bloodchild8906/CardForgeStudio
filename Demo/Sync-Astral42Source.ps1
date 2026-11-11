$ErrorActionPreference='Stop'
$root='D:\CardForgeStudio'
$source=Join-Path $root 'Assets\CardForgeStudio\CardForgeData\game-definitions\astral-rift-core.json'
$pack=Get-Content (Join-Path $root 'Assets\StreamingAssets\CardForgeStudio\CardForge\Rules\astral-rift.json') -Raw|ConvertFrom-Json
$game=Get-Content $source -Raw|ConvertFrom-Json
if(@($game.sampleDecks).Count -ne 4){throw 'Source has unexpected sample deck count'}
$game.packageVersion='2.1.0'
$game.defaultDeckId='ar-deck-solar-starter'
$game | Add-Member -NotePropertyName 'deckRules' -NotePropertyValue ([pscustomobject]@{
  minCards=40;maxCards=120;maxCopiesPerCard=3;allowedCardTypes=@()
  typeLimits=@(
     [pscustomobject]@{cardType='Command';minCount=1;maxCount=1}
     [pscustomobject]@{cardType='ManaSource';minCount=1;maxCount=1}
  );cardLimits=@()
}) -Force
foreach($starter in $game.sampleDecks) {
  $found=@($pack.starterDecks|Where-Object {$_.id -eq $starter.id})
  if($found.Count -ne 1){throw "Cannot find starter $($starter.id)"}
  $starter.cardIds=@($found[0].cardIds)
  $starter.minimumCards=40
  $starter.maximumCopiesPerCard=3
}
$game.zones=@($game.zones|Where-Object{$_.id -ne 'mana-source'})
foreach($zone in $game.zones) {
  if($zone.id -eq 'command') {
    $zone.name='Command Zone'
    $zone.capacity=1
    $zone.allowedCardTypes=@('Command')
    $zone.initialCardIds=@()
    $zone|Add-Member -NotePropertyName 'deckStartCardType' -NotePropertyValue 'Command' -Force
    $zone|Add-Member -NotePropertyName 'returnOnLeave' -NotePropertyValue $true -Force
    $zone|Add-Member -NotePropertyName 'immovable' -NotePropertyValue $false -Force
  }
  if($zone.id -eq 'battlefield') {$zone.allowedCardTypes=@('Unit','Command')}
}
$game.zones+= [pscustomobject]@{
  id='mana-source';name='Mana Source';ownerScope='player';visibility='public'
  ordered=$false;capacity=1;allowedCardTypes=@('ManaSource');initialCardIds=@()
  deckStartCardType='ManaSource';returnOnLeave=$false;immovable=$true
}
$placements=@(
  [pscustomobject]@{id='ar-board-self-mana';zoneId='mana-source';side='self';label='Mana Source (permanent)';x=0.81;y=0.48;width=0.16;height=0.12;order=7}
  [pscustomobject]@{id='ar-board-opp-mana';zoneId='mana-source';side='opponent';label='Enemy Mana Source';x=0.02;y=0.28;width=0.16;height=0.12;order=8}
)
$game.boardLayout=@($game.boardLayout|Where-Object{$_.zoneId -ne 'mana-source'})+ $placements
[IO.File]::WriteAllText($source,(ConvertTo-Json -InputObject $game -Depth 80),[Text.UTF8Encoding]::new($false))
$gen=Join-Path $root 'Assets\CardForgeStudio\CardForge\Editor\CardForgeDemoGenerator.cs'
$genSource=[IO.File]::ReadAllText($gen)
if(!$genSource.Contains('public const string Version = "2.0.0";')){throw 'Expected sample generator version missing'}
[IO.File]::WriteAllText($gen,$genSource.Replace('public const string Version = "2.0.0";','public const string Version = "2.1.0";'),[Text.UTF8Encoding]::new($false))
Write-Output 'Updated authored Game Definition and generator to Astral Rift 2.1.0: four 42-card starters, common Commander/Mana zones.'
