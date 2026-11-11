<#
.SYNOPSIS
  End-to-end check of the one-click play path, without Unity.
.DESCRIPTION
  Does what CardForge > Play Sample Game does on the server side:
  start the API on a private port and package store, open a local management session, import and activate a
  .cardforge package, register a player, create a game against the AI and play it until it finishes.
  Exit code 0 = every step passed. Nothing is written outside a temp folder; the server is always stopped.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tests\Smoke\Invoke-PlaySmokeTest.ps1
#>
param(
    [string]$Package = (Join-Path $PSScriptRoot '..\..\GamePackages\astral-rift-demo-1.0.0.cardforge'),
    [string]$GameId = 'astral-rift',
    [int]$Port = 5290,
    [int]$StartupSeconds = 240,
    [int]$MaxSteps = 400
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $root 'Server\src\TcgULtimateServer.Api\TcgULtimateServer.Api.csproj'
$Package = (Resolve-Path $Package).Path
$base = "http://127.0.0.1:$Port"
$key = [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')
$work = Join-Path ([IO.Path]::GetTempPath()) ('cardforge-smoke-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$failures = New-Object System.Collections.Generic.List[string]
$server = $null

function Pass($name) { Write-Host "PASS  $name" }
function Fail($name, $detail) { Write-Host "FAIL  $name :: $detail"; $script:failures.Add($name) }

function Call($method, $path, $token, $body) {
    $headers = @{}
    if ($token) { $headers['Authorization'] = "Bearer $token" }
    try {
        if ($null -ne $body) {
            return Invoke-RestMethod -Method $method -Uri ($base + $path) -Headers $headers -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 8 -Compress)
        }
        return Invoke-RestMethod -Method $method -Uri ($base + $path) -Headers $headers
    } catch {
        $text = ''
        if ($_.Exception.Response) {
            try { $text = (New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() } catch { }
        }
        throw "HTTP $method $path -> $($_.Exception.Message) $text"
    }
}

function Stop-Server {
    if ($server -and -not $server.HasExited) {
        & taskkill.exe /T /F /PID $server.Id 2>&1 | Out-Null
    }
}

try {
    # ---- 1. start the server exactly like the editor does -------------------------------------------------
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $base
    $env:TCG_LOCAL_MANAGEMENT_KEY = $key
    $env:TCG_CLIENT_MANAGED_SERVER = 'true'
    $env:TCG_LAUNCH_CLIENT = 'false'
    $env:CARDFORGE_GAME_PACKAGE_STORE = Join-Path $work 'packages'
    $server = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', "`"$project`"", '--configuration', 'Debug', '--no-launch-profile') `
        -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $work 'out.log') -RedirectStandardError (Join-Path $work 'err.log')

    $healthy = $false
    for ($i = 0; $i -lt $StartupSeconds * 2; $i++) {
        if ($server.HasExited) { break }
        try { $h = Invoke-RestMethod -Uri "$base/api/health" -TimeoutSec 2; if ($h) { $healthy = $true; break } } catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $healthy) {
        $tail = ''
        foreach ($f in 'err.log', 'out.log') { $p = Join-Path $work $f; if (Test-Path $p) { $tail += (Get-Content $p -Tail 8 | Out-String) } }
        throw "The server did not become healthy. $tail"
    }
    Pass "server healthy on $base"

    # ---- 2. local management session --------------------------------------------------------------------------
    $session = Invoke-RestMethod -Method Post -Uri "$base/api/auth/local-management" -Headers @{ 'X-Tcg-Management-Key' = $key }
    if (-not $session.token) { throw 'No management token returned.' }
    $admin = $session.token
    Pass 'local management session opened'

    $wrong = $null
    try { $wrong = Invoke-WebRequest -Method Post -Uri "$base/api/auth/local-management" -Headers @{ 'X-Tcg-Management-Key' = 'wrong' } -UseBasicParsing } catch { }
    if ($wrong -and $wrong.StatusCode -lt 400) { Fail 'wrong management key is rejected' 'the server accepted a wrong key' } else { Pass 'wrong management key is rejected' }

    # ---- 3. import and activate the package ---------------------------------------------------------------------
    $respFile = Join-Path $work 'import.json'
    $code = & curl.exe -s -S -o $respFile -w '%{http_code}' -H "Authorization: Bearer $admin" `
        -F "file=@$Package;type=application/vnd.cardforge.game-package+zip" -F 'activate=true' "$base/api/admin/game-packages/import"
    if ([int]$code -ge 200 -and [int]$code -lt 300) { Pass "package imported and activated (HTTP $code)" }
    else { throw "package import failed (HTTP $code): $(Get-Content $respFile -Raw)" }

    $rules = @(Call 'GET' '/api/rules' $null $null)
    $pack = $rules | Where-Object { $_.id -eq $GameId } | Select-Object -First 1
    if ($pack -and @($pack.cards).Count -gt 0) { Pass "rule pack '$GameId' is live with $(@($pack.cards).Count) cards" }
    else { throw "rule pack '$GameId' is not available after activation" }

    # ---- 4. a player, a game against the AI ----------------------------------------------------------------------
    $handle = 'smoke' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $reg = Call 'POST' '/api/auth/register' $null @{ handle = $handle; password = 'Smoke-Test-1234!' }
    $tok = $reg.token; $uid = $reg.user.id
    if (-not $tok -or -not $uid) { throw 'Registration returned no token or user id.' }
    Pass "player registered ($handle)"

    $game = Call 'POST' '/api/games' $tok @{ rulePackId = $GameId; playerIds = @($uid); addAi = $true }
    $gid = $game.id
    if (-not $gid) { throw 'Game creation returned no id.' }
    Pass "game created against the AI ($gid)"

    # ---- 5. play it ----------------------------------------------------------------------------------------------
    $plan = @(
        @{ id = 'ar-rally'; max = 4 }, @{ id = 'ar-strike'; max = 2 }, @{ id = 'ar-channel'; max = 1 },
        @{ id = 'ar-deploy'; max = 1 }, @{ id = 'ar-draw'; max = 1 }, @{ id = 'ar-next-phase'; max = 9 }, @{ id = 'ar-end-turn'; max = 1 }
    )
    $done = @{}; $accepted = 0; $waits = 0; $view = $null; $rejected = 0
    for ($step = 0; $step -lt $MaxSteps; $step++) {
        $view = Call 'GET' "/api/games/$gid/client-view" $tok $null
        if ($view.finished) { break }
        if ($view.activePlayerId -ne $uid) {
            Start-Sleep -Milliseconds 300
            if (++$waits -gt 100) { throw 'The AI never handed the turn back.' }
            continue
        }
        $waits = 0
        $allowed = @($view.allowedActions | ForEach-Object { $_.id })
        $hand = @($view.zones | Where-Object { $_.playerId -eq $uid -and $_.zoneId -eq 'hand' } | ForEach-Object { $_.cards })
        $energy = ($view.resources | Where-Object { $_.playerId -eq $uid -and $_.resourceId -eq 'energy' } | Select-Object -First 1).value
        $ai = @($view.players | Where-Object { $_ -ne $uid }) | Select-Object -First 1
        $acted = $false
        foreach ($candidate in $plan) {
            $id = $candidate.id
            $slot = "$($view.turn)|$id"
            if ($allowed -notcontains $id) { continue }
            if ($done[$slot] -ge $candidate.max) { continue }
            $body = @{ actionId = $id }
            if ($id -eq 'ar-deploy') { if ($hand.Count -eq 0 -or $energy -lt 2) { continue }; $body.sourceInstanceId = $hand[0].instanceId }
            if ($id -eq 'ar-rally' -and $energy -lt 1) { continue }
            if ($id -eq 'ar-strike') { if ($energy -lt 1) { continue }; $body.targetPlayerId = $ai }
            try {
                Call 'POST' "/api/games/$gid/actions" $tok $body | Out-Null
                $done[$slot] = 1 + [int]$done[$slot]; $accepted++; $acted = $true; break
            } catch {
                $done[$slot] = 1 + [int]$done[$slot]; $rejected++
            }
        }
        if (-not $acted) { throw "No usable action in phase '$($view.currentPhaseId)' (allowed: $($allowed -join ', '))." }
    }

    if ($accepted -ge 5) { Pass "$accepted actions accepted by the authoritative server ($rejected rejected while probing)" }
    else { Fail 'actions are accepted' "only $accepted accepted" }
    if ($view -and $view.finished -and $view.winnerPlayerId) { Pass "match finished on turn $($view.turn): winner $($view.winnerPlayerId) by rule '$($view.victoryRuleId)'" }
    else { Fail 'match reaches a result' "not finished after $MaxSteps steps (turn $($view.turn), phase $($view.currentPhaseId))" }
}
catch {
    Fail 'smoke test aborted' $_.Exception.Message
    $errLog = Join-Path $work 'err.log'
    if (Test-Path $errLog) { $t = Get-Content $errLog -Tail 6 | Out-String; if ($t.Trim()) { Write-Host "--- server stderr (tail)"; Write-Host $t } }
}
finally {
    Stop-Server
    Start-Sleep -Milliseconds 500
    try { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue } catch { }
}

Write-Host ''
if ($failures.Count -eq 0) { Write-Host 'CardForge play smoke test: all steps passed.'; exit 0 }
Write-Host "CardForge play smoke test: $($failures.Count) failed - $($failures -join '; ')"
exit 1
