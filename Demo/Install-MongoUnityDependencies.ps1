param([switch]$Restore)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$packageRoot = Join-Path $env:USERPROFILE '.nuget\packages'
$map = [ordered]@{
  'mongodb.driver' = @('3.13.0','netstandard2.1','MongoDB.Driver.dll')
  'mongodb.bson' = @('3.13.0','netstandard2.1','MongoDB.Bson.dll')
  'dnsclient' = @('1.6.1','netstandard2.1','DnsClient.dll')
  'sharpcompress' = @('0.48.1','netstandard2.1','SharpCompress.dll')
  'snappier' = @('1.3.1','netstandard2.0','Snappier.dll')
  'zstdsharp.port' = @('0.7.3','netstandard2.1','ZstdSharp.dll')
  'microsoft.extensions.logging.abstractions' = @('2.0.0','netstandard2.0','Microsoft.Extensions.Logging.Abstractions.dll')
  'microsoft.bcl.asyncinterfaces' = @('8.0.0','netstandard2.1','Microsoft.Bcl.AsyncInterfaces.dll')
}
$folder = Join-Path $root 'Assets\CardForgeStudio\Plugins\CardForgeMongo'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$missing = @($map.Keys | Where-Object {
    $value=$map[$_]; !(Test-Path (Join-Path $packageRoot "$_\$($value[0])\lib\$($value[1])\$($value[2])"))
})
if ($missing.Count -gt 0) {
    if (!$Restore) {
        throw "MongoDB driver dependency packages are missing: $($missing -join ', '). Run this script with -Restore."
    }
    & dotnet restore (Join-Path $root 'Shared\CardForge.MongoDependencies\CardForge.MongoDependencies.csproj') --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'MongoDB .NET dependencies could not be restored.' }
}
foreach ($pkg in $map.Keys) {
    $value = $map[$pkg]
    $source = Join-Path $packageRoot "$pkg\$($value[0])\lib\$($value[1])\$($value[2])"
    if (!(Test-Path $source)) { throw "MongoDB dependency not available: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $folder $value[2]) -Force
}
Write-Output "MongoDB .NET driver 3.13.0 installed in Unity: $folder"
