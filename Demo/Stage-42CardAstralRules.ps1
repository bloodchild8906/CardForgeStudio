$ErrorActionPreference='Stop'
$root='D:\CardForgeStudio'
$path=Join-Path $root 'Assets\StreamingAssets\CardForgeStudio\CardForge\Rules\astral-rift.json'
$pack=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
if(@($pack.cards).Count -ne 240 -or @($pack.starterDecks).Count -ne 4){throw 'Unexpected Astral set; not updating'}
$affinities=@('solar','tidal','verdant','umbral')
foreach($aff in $affinities) {
  $commander='ar-card-'+$aff+'-001'
  $mana='ar-card-'+$aff+'-047'
  $c=@($pack.cards | Where-Object {$_.id -eq $commander})
  $m=@($pack.cards | Where-Object {$_.id -eq $mana})
  if($c.Count -ne 1 -or $m.Count -ne 1){throw "Missing specialty card for $aff"}
  $c[0].cost=3
  $m[0].type='ManaSource - '+$aff
  $m[0].name=$aff.Substring(0,1).ToUpperInvariant()+$aff.Substring(1)+' Eternal Mana Source'
  $m[0].cost=0
  $m[0].power=0
  $m[0].toughness=0
  $deck=@($pack.starterDecks | Where-Object {$_.id -like "*$aff*"})
  if($deck.Count -ne 1){throw "Starter not found for $aff"}
  $ids=@($deck[0].cardIds | Where-Object {$_ -ne $commander -and $_ -ne $mana})
  if($ids.Count -ne 40){throw "Unexpected main deck size for $aff - $($ids.Count)"}
  $deck[0].cardIds=@($ids)+@($commander,$mana)
}
$pack.startingDeck=@($pack.starterDecks[0].cardIds)
$pack | Add-Member -NotePropertyName 'deckRules' -NotePropertyValue ([pscustomobject]@{
 minCards=40; maxCards=120; maxCopiesPerCard=3;allowedCardTypes=@()
 typeLimits=@(
   [pscustomobject]@{cardType='Command';minCount=1;maxCount=1}
   [pscustomobject]@{cardType='ManaSource';minCount=1;maxCount=1}
 );cardLimits=@()
}) -Force
$commandZone=@($pack.zones|Where-Object {$_.id -eq 'command'})[0]
$commandZone.allowedCardTypes=@('Command')
$commandZone.capacity=1
$commandZone|Add-Member deckStartCardType 'Command' -Force
$commandZone|Add-Member returnOnLeave $true -Force
$commandZone|Add-Member immovable $false -Force
$commandZone.initialCardIds=@()
$battlefield=@($pack.zones|Where-Object {$_.id -eq 'battlefield'})[0]
$battlefield.allowedCardTypes=@('Unit','Command')
$pack.zones=@($pack.zones | Where-Object {$_.id -ne 'mana-source'}) + @(
 [pscustomobject]@{
   id='mana-source';name='Mana Source';ownerScope='player';visibility='public'
   ordered=$false;capacity=1;allowedCardTypes=@('ManaSource');initialCardIds=@()
   deckStartCardType='ManaSource';returnOnLeave=$false;immovable=$true
 }
)
$action=@($pack.actions|Where-Object {$_.id -eq 'ar-activate-command'})[0]
$action.name='Cast commander'
$action.sourceZone='command'
$action|Add-Member destinationZone 'battlefield' -Force
$action.costs=@([pscustomobject]@{resourceId='energy';amount=3})
$action.effects=@()
$pack | Add-Member -NotePropertyName 'version' -NotePropertyValue '2.1.0' -Force
$json=ConvertTo-Json -InputObject $pack -Depth 90 -Compress
[IO.File]::WriteAllText($path,$json,[Text.UTF8Encoding]::new($false))
Write-Output ("Updated {0}: {1} cards; starters={2}; list sizes={3}; main=40, special=2" -f $path,$pack.cards.Count,$pack.starterDecks.Count,($pack.starterDecks|ForEach-Object {$_.cardIds.Count} -join ','))
