param([int]$Limit=240)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=Split-Path $PSScriptRoot -Parent
$source=Join-Path $root 'Assets\CardForgeStudio\CardForgeData\demo\astral-rift\art'
$server=Join-Path $root 'Assets\StreamingAssets\CardForgeStudio\CardForge\Assets'
$pack=Get-Content (Join-Path $root 'Assets\StreamingAssets\CardForgeStudio\CardForge\Rules\astral-rift.json') -Raw -Encoding UTF8|ConvertFrom-Json
$code=Get-Content (Join-Path $PSScriptRoot 'AstralArtRenderer.cs') -Raw -Encoding UTF8
Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing
$i=0
foreach($card in ($pack.cards|Select-Object -First $Limit)){
 $type=($card.type -split ' ')[0]
 [AstralArtRenderer]::Render($card.id,$type,$card.affinity,$source,$server)
 $i++
 if(($i%40)-eq 0){Write-Output ("Art rendered: $i/$Limit")}
}
Write-Output "Rendered $i distinct 420x580 PNG card illustrations."
