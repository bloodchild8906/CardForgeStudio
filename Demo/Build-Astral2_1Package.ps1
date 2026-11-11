$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root='D:\CardForgeStudio'
$oldPath=Join-Path $root 'GamePackages\astral-rift-demo-2.0.0.cardforge'
$newPath=Join-Path $root 'GamePackages\astral-rift-demo-2.1.0.cardforge'
$rulesPath=Join-Path $root 'Assets\StreamingAssets\CardForgeStudio\CardForge\Rules\astral-rift.json'
$rules=Get-Content $rulesPath -Raw|ConvertFrom-Json
if($rules.cards.Count -ne 240 -or $rules.starterDecks.Count -ne 4){throw 'Invalid staged ruleset'}
$utf8=[Text.UTF8Encoding]::new($false)
$replace=New-Object 'System.Collections.Generic.Dictionary[string,byte[]]' ([StringComparer]::OrdinalIgnoreCase)
function Put([string]$path,[string]$json) {$script:replace[$path]=$utf8.GetBytes($json)}
$rules.version='2.1.0'
Put 'rules/rule-pack.json' (ConvertTo-Json -InputObject $rules -Depth 95 -Compress)
Put 'zones/game-zones.json' (ConvertTo-Json -InputObject ([pscustomobject]@{version=1;zones=$rules.zones}) -Depth 55 -Compress)

$archive=[IO.Compression.ZipFile]::OpenRead($oldPath)
try {
 function ReadEntry([string]$name) {
   $entry=$archive.GetEntry($name)
   if(!$entry){throw "Missing package entry $name"}
   $reader=[IO.StreamReader]::new($entry.Open())
   try{return $reader.ReadToEnd()}finally{$reader.Dispose()}
 }
 foreach($starter in $rules.starterDecks) {
   $entryPath='decks/'+$starter.id+'.json'
   $obj=ReadEntry $entryPath | ConvertFrom-Json
   $obj.cardIds=@($starter.cardIds)
   $obj.minimumCards=40
   $obj.maximumCopiesPerCard=3
   Put $entryPath (ConvertTo-Json -InputObject $obj -Depth 35 -Compress)
 }
 $affinities=@('solar','tidal','verdant','umbral')
 foreach($aff in $affinities) {
   foreach($suffix in @('001','047')) {
     $id='ar-card-'+$aff+'-'+$suffix
     $name='cards/'+$id+'.json'
     $obj=ReadEntry $name|ConvertFrom-Json
     if($suffix -eq '001') {
       $cost='3'
     }else{
       $newCard=@($rules.cards|Where-Object {$_.id -eq $id})[0]
       $obj.name=$newCard.name
       $obj.cardType='ManaSource'
       $cost='0'
     }
     foreach($value in $obj.values) {
       if($value.fieldId -eq '7e1150461ebe451eb04d2d9733b3ef71' -and $suffix -eq '047')
         {$value.value=$obj.name}
       if($value.fieldId -eq '35b35a270df44204ad87fc10ea4b1d25')
         {$value.value=$cost}
       if($value.fieldId -eq '114ef7b3b56b4be89bc41f0dc8ffca5e' -and $suffix -eq '047')
         {$value.value='ManaSource - '+$aff}
     }
     Put $name (ConvertTo-Json -InputObject $obj -Depth 65)
   }
   $manaAbilityPath='abilities/ar-ability-unique-ar-card-'+$aff+'-047.json'
   $manaAbility=ReadEntry $manaAbilityPath | ConvertFrom-Json
   $manaAbility.actionId=''
   $manaAbility.activationTriggerId='turn_started'
   $manaAbility.responseTiming='At the start of its controller''s turn'
   Put $manaAbilityPath (ConvertTo-Json -InputObject $manaAbility -Depth 70)
 }
 $actionPath='actions/ar-activate-command.json'
 $action=ReadEntry $actionPath | ConvertFrom-Json
 $action.name='Cast commander'
 $action.effect='Cast your Commander from Command to the battlefield for three energy.'
 $action.cost='3 energy'
 $action.costAmount=3
 $action.costResourceId='energy'
 $action.serverEffectKind='move_card'
 $action.sourceZoneId='command'
 $action.destinationZoneId='battlefield'
 Put $actionPath (ConvertTo-Json -InputObject $action -Depth 70)

 $manifest=ReadEntry 'manifest.json' | ConvertFrom-Json
 $manifest.version='2.1.0'
 $manifest.createdUtc=[DateTimeOffset]::UtcNow.ToString('o')
 $sha=[Security.Cryptography.SHA256]::Create()
 try {
   foreach($file in $manifest.files) {
     $entryName=[string]$file.path
     if($replace.ContainsKey($entryName)) {
       $bytes=$replace[$entryName]
       $file.sha256=[BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','')
       $file.length=[long]$bytes.Length
     }
   }
 }finally{$sha.Dispose()}
 Put 'manifest.json' (ConvertTo-Json -InputObject $manifest -Depth 80 -Compress)
 $out=[IO.File]::Open($newPath,[IO.FileMode]::Create,[IO.FileAccess]::Write)
 try {
   $zip=[IO.Compression.ZipArchive]::new($out,[IO.Compression.ZipArchiveMode]::Create,$true)
   try {
     foreach($original in $archive.Entries) {
       $entry=$zip.CreateEntry($original.FullName,[IO.Compression.CompressionLevel]::Optimal)
       $entry.LastWriteTime=[DateTimeOffset]::new(1980,1,1,0,0,0,[TimeSpan]::Zero)
       $stream=$entry.Open()
       try {
         if($replace.ContainsKey($original.FullName)) {
           $bytes=$replace[$original.FullName]
           $stream.Write($bytes,0,$bytes.Length)
         }else{
           $input=$original.Open()
           try{$input.CopyTo($stream)}finally{$input.Dispose()}
         }
       }finally{$stream.Dispose()}
     }
   }finally{$zip.Dispose()}
 }finally{$out.Dispose()}
}finally{$archive.Dispose()}
$verify=[IO.Compression.ZipFile]::OpenRead($newPath)
try {
 $reader=[IO.StreamReader]::new($verify.GetEntry('manifest.json').Open())
 try{$check=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
 if($check.version -ne '2.1.0' -or $check.files.Count -lt 780){throw 'Package manifest mismatch'}
 $digest=[Security.Cryptography.SHA256]::Create()
 try{
   foreach($file in $check.files){
     $item=$verify.GetEntry($file.path)
     if(!$item){throw "Missing file $($file.path)"}
     $mem=New-Object IO.MemoryStream
     try{
       $input=$item.Open();try{$input.CopyTo($mem)}finally{$input.Dispose()}
       $bytes=$mem.ToArray()
       $hash=[BitConverter]::ToString($digest.ComputeHash($bytes)).Replace('-','')
       if($bytes.Length -ne $file.length -or $hash -ne $file.sha256){throw "Hash mismatch for $($file.path)"}
     }finally{$mem.Dispose()}
   }
 }finally{$digest.Dispose()}
 Write-Output ('Built '+$newPath+': '+$check.files.Count+' verified content entries, '+$rules.cards.Count+' cards, '+$rules.starterDecks.Count+' starter decks.')
}finally{$verify.Dispose()}
