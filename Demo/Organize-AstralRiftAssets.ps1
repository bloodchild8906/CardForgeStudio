param([switch]$SkipGenerator)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$unity=Join-Path $root 'Assets\CardForgeStudio'
$res=Join-Path $unity 'CardForgeClient\Resources'
$old=Join-Path $res 'CardForgeDemo'
$new=Join-Path $res 'AstralRift'
$backup=Join-Path $root ('artifacts\Backups\AstralRift-Repair-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $backup | Out-Null
$rules=Join-Path $root 'Assets\StreamingAssets\CardForge\Rules\astral-rift.json'
if(Test-Path $rules){Copy-Item $rules (Join-Path $backup 'original-rules.json')}
if(Test-Path $old){
  if(Test-Path $new){throw "Refusing to overwrite existing $new"}
  Move-Item -LiteralPath $old -Destination $new
  if(Test-Path ($old+'.meta')){Move-Item -LiteralPath ($old+'.meta') -Destination ($new+'.meta')}
  Write-Output 'Renamed CardForgeDemo -> AstralRift (including Unity folder .meta)'
}
$oldArt=Join-Path $new 'Art'
$newArt=Join-Path $new 'Illustrations'
if(Test-Path $oldArt){
  if(Test-Path $newArt){throw "Refusing to overwrite existing $newArt"}
  Move-Item -LiteralPath $oldArt -Destination $newArt
  if(Test-Path ($oldArt+'.meta')){Move-Item -LiteralPath ($oldArt+'.meta') -Destination ($newArt+'.meta')}
  Write-Output 'Renamed Art -> Illustrations (including Unity folder .meta)'
}
# Fix all Resources.Load lookups and editor asset paths. References in .unity/.asset use GUIDs
# and must not be rewritten merely because the asset path changed.
$paths=@(
  (Join-Path $unity 'CardForgeClient'),
  (Join-Path $root 'Documentation'),
  (Join-Path $root 'Demo')
)
$changed=0
foreach($path in $paths){
  $files=Get-ChildItem $path -Recurse -File -ErrorAction SilentlyContinue |Where-Object {
    $_.Extension -in @('.cs','.ps1','.md','.json','.uxml','.uss','.tss') }
  foreach($f in $files){
    $current=[IO.File]::ReadAllText($f.FullName)
    $next=$current.Replace('AstralRift/Illustrations/','AstralRift/Illustrations/').
      Replace('AstralRift\Illustrations\','AstralRift\Illustrations\').
      Replace('AstralRift/','AstralRift/').
      Replace('AstralRift\','AstralRift\')
    if($next -ne $current){
      [IO.File]::WriteAllText($f.FullName,$next,[Text.UTF8Encoding]::new($false))
      $changed++
    }
  }
}
Write-Output "Updated $changed text files containing old resource paths."
if(-not $SkipGenerator){
  & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Demo\Build-AstralRift3.ps1')
  if($LASTEXITCODE -ne 0){throw "Astral Rift 3 generator failed: $LASTEXITCODE"}
}
$source=Get-Content $rules -Raw -Encoding UTF8 | ConvertFrom-Json
if($source.version -ne '3.0.0' -or $source.cards.Count -ne 240 -or $source.starterDecks.Count -ne 3){
  throw 'Refusing to sync artwork: expected the 240-card Astral Rift 3 rule pack.'
}
$author=Join-Path $unity 'CardForgeData\demo\astral-rift\art'
$shipping=Join-Path $root 'Assets\StreamingAssets\CardForge\Assets'
New-Item -ItemType Directory -Force $shipping |Out-Null
$manifest=New-Object System.Collections.Generic.List[object]
$changedArt=0
foreach($card in $source.cards){
  if($card.id -notmatch '^[a-z0-9][a-z0-9-]*$'){throw "Unsafe card ID $($card.id)"}
  if($card.assetId -ne ('astral-rift.card.'+$card.id)){throw "Bad assetId on $($card.id)"}
  $original=Join-Path $newArt ($card.id+'.png')
  $authored=Join-Path $author ($card.id+'.png')
  $published=Join-Path $shipping ($card.assetId+'.png')
  if(!(Test-Path $original)){throw "Missing high-resolution illustration $original"}
  if(!(Test-Path $authored)){throw "Missing editable card illustration $authored"}
  $stream=[IO.File]::OpenRead($original)
  try{
    $bytes=[byte[]]::new(24)
    if($stream.Read($bytes,0,24) -ne 24 -or
       $bytes[0] -ne 137 -or $bytes[1] -ne 80 -or $bytes[2] -ne 78 -or $bytes[3] -ne 71){
      throw "Invalid PNG header: $original"
    }
    [array]::Reverse($bytes,16,4);[array]::Reverse($bytes,20,4)
    $w=[BitConverter]::ToUInt32($bytes,16)
    $h=[BitConverter]::ToUInt32($bytes,20)
    if($w -lt 500 -or $h -lt 350){throw "Expected a high-resolution original: $original ($w x $h)"}
  } finally { $stream.Dispose() }
  $sha=(Get-FileHash $original -Algorithm SHA256).Hash
  if((Get-FileHash $authored).Hash -ne $sha){Copy-Item -LiteralPath $original -Destination $authored -Force;$changedArt++}
  if(!(Test-Path $published) -or (Get-FileHash $published).Hash -ne $sha){Copy-Item -LiteralPath $original -Destination $published -Force}
  $manifest.Add([pscustomobject]@{
    CardId=$card.id;Name=$card.name;Type=$card.type;AssetId=$card.assetId;
    Source="Assets/CardForgeStudio/CardForgeClient/Resources/AstralRift/Illustrations/$($card.id).png";
    Editable="Assets/CardForgeStudio/CardForgeData/demo/astral-rift/art/$($card.id).png";
    Streamed="Assets/StreamingAssets/CardForge/Assets/$($card.assetId).png";
    Width=$w;Height=$h;SHA256=$sha
  })
}
if(@($manifest|Select-Object -ExpandProperty CardId -Unique).Count -ne 240){throw 'Duplicate card IDs'}
$csv=Join-Path $root 'Documentation\AstralRift-IllustrationManifest.csv'
$manifest|Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8
Write-Output "Illustration references synchronized: $($manifest.Count); editable files updated: $changedArt; manifest: $csv"
Write-Output "Legacy rules backup: $backup"
