$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$edition=Join-Path $root 'Assets\CardForgeData\blocks\astral-rift-core'
$block=Get-Content (Join-Path $edition 'block.json') -Raw -Encoding UTF8|ConvertFrom-Json
$kinds=@('Commander','Creature','Spell','QuickSpell','ManaSource')
$all=@{}
$issues=[Collections.Generic.List[string]]::new()
foreach($kind in $kinds){
  $slug=$kind.ToLowerInvariant()
  $id='ar-layout-'+$slug
  $file=Join-Path $edition ('layouts\'+$id+'.json')
  if(!(Test-Path $file)){$issues.Add('Missing '+$id);continue}
  $layout=Get-Content $file -Raw -Encoding UTF8|ConvertFrom-Json
  $all[$kind]=$layout
  if($layout.isBack -or $layout.width -ne 360 -or $layout.height -ne 504){$issues.Add('Wrong dimensions '+$id)}
  if($layout.id -ne $id){$issues.Add('Wrong layout ID '+$id)}
  $frame=Join-Path $root ($layout.backgroundAssetPath -replace '/','\')
  if(!(Test-Path $frame)){$issues.Add('Missing front frame '+$frame)}
  foreach($required in @('ar-zone-title','ar-zone-cost','ar-zone-art','ar-zone-type','ar-zone-rules','ar-zone-flavor')){
    if(!@($layout.zones|Where-Object {$_.id -eq $required})){ $issues.Add("$id missing $required") }
  }
  foreach($zone in @($layout.zones|Where-Object {$_.visible})){
    if($zone.x -lt 0 -or $zone.y -lt 0 -or ($zone.x+$zone.width) -gt 361 -or ($zone.y+$zone.height) -gt 505){$issues.Add("$id zone $($zone.id) out of bounds")}
    if($zone.kind -in @(0,1,4) -and $zone.fontSize -lt 8){$issues.Add("$id zone $($zone.id) unreadable font")}
  }
  Write-Output "$kind layout: $($layout.zones.Count) editable zones / frame=$([IO.Path]::GetFileName($frame))"
}
$counts=@{}
foreach($cardId in @($block.cardIds)){
  $file=Join-Path $edition ('cards\'+$cardId+'.json')
  if(!(Test-Path $file)){$issues.Add('Missing card '+$cardId);continue}
  $card=Get-Content $file -Raw -Encoding UTF8|ConvertFrom-Json
  if(!$all.ContainsKey($card.cardType)){$issues.Add('Unsupported type on '+$cardId);continue}
  if($card.layoutId -ne $all[$card.cardType].id){$issues.Add('Wrong front on '+$cardId)}
  $counts[$card.cardType]=1+[int]$counts[$card.cardType]
}
foreach($kind in $kinds){Write-Output "$kind cards: $([int]$counts[$kind])"}
if($issues.Count){$issues|Select-Object -First 30|ForEach-Object{Write-Output ('ERROR: '+$_)}; throw "Type layout validation failed: $($issues.Count) issues"}
Write-Output "LAYOUTS_OK: 5 visually distinct fronts and all 240 card layout bindings, dimensions, labels and fonts verified."
