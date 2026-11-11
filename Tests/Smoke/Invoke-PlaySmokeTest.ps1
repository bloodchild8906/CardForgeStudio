<#
.SYNOPSIS
  Native-only Unity Dedicated Server smoke test for Astral Rift.
.DESCRIPTION
  Reuses or launches the native Unity server and verifies authentication, match
  modes, downloadable card artwork and hidden information.
#>
param([int]$Port = 5168)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$launcher = Join-Path $root 'Demo\Start-CardForgeDedicatedServer.ps1'
$exe = Join-Path $root 'Builds\DedicatedServer\CardForgeServer.exe'
if (!(Test-Path $exe)) {
    throw "Build the native Unity Dedicated Server first: CardForge > Server > Build Native Windows Dedicated Server."
}
& $launcher -FirstPort $Port -LastPort $Port -NoClient
$base = "http://127.0.0.1:$Port"
$health = Invoke-RestMethod "$base/api/health" -TimeoutSec 4
if ($health.status -ne 'healthy' -or $health.simulationMode -ne 'Unity Dedicated Server' -or !$health.simulationReady) {
    throw "Only a healthy native Unity server may be tested."
}
Write-Output 'PASS Unity native health check'

$loginBody = @{handle='demo';password='cardforge-demo'} | ConvertTo-Json
$login = Invoke-RestMethod -Method Post -Uri "$base/api/auth/login" -ContentType 'application/json' -Body $loginBody
$headers = @{Authorization = "Bearer $($login.token)"}
if (!$login.user.id) { throw 'Native login failed.' }
Write-Output 'PASS native account authentication'
foreach ($opponents in 1..4) {
    $request = @{rulePackId='astral-rift';playerIds=@($login.user.id);addAi=$true;teamSize=$opponents} | ConvertTo-Json
    $match = Invoke-RestMethod -Method Post -Uri "$base/api/games" -Headers $headers -ContentType 'application/json' -Body $request
    if ($match.players.Count -ne ($opponents+1)) {
        throw "1v$opponents created $($match.players.Count) seats."
    }
    $view = Invoke-RestMethod -Uri "$base/api/games/$($match.id)/client-view" -Headers $headers
    if (!$view.zones) { throw "1v$opponents client-view was empty." }
    $enemy = $view.players[1]
    $hidden = @($view.zones | Where-Object {$_.playerId -eq $enemy -and $_.zoneId -eq 'hand'} | Select-Object -ExpandProperty cards)
    if (@($hidden | Where-Object {$_.cardId -or $_.instanceId}).Count -gt 0) {
        throw "Opponent hidden cards leaked in 1v$opponents."
    }
    Write-Output "PASS 1v$opponents and hidden-zone redaction"
}
$assets = @(Invoke-RestMethod "$base/api/assets/manifest")
if ($assets.Count -lt 12) { throw 'Missing native card artwork assets.' }
Write-Output "PASS $($assets.Count) downloadable server assets"
Write-Output 'Native Unity Dedicated Server smoke test completed successfully.'
