#requires -Version 5.1
<#
.SYNOPSIS
    Starts the CardForgeStudio demo: a local authoritative server with the Astral Rift
    game package active, seeded demo accounts, and an optional Unity client launch.

.DESCRIPTION
    The script will:
      1. Reuse a healthy demo server on ports 5168-5198 or start a new one.
      2. Import and activate GamePackages/astral-rift-demo-1.0.0.cardforge when needed.
      3. Register the "demo" and "demo-b" accounts and connect them as friends.
      4. Optionally launch a built Unity client executable.

    Server state (package store, audit log, management key) lives under
    artifacts/demo-server, which is git-ignored, so restarts keep the same data.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Demo/Start-CardForgeDemo.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Demo/Start-CardForgeDemo.ps1 -ClientExe "Builds\CardForgeStudio.exe"
#>
param(
    [int]$FirstPort = 5168,
    [int]$LastPort = 5198,
    [string]$PackagePath = "",
    [string]$ClientExe = "",
    [switch]$NoClient,
    [switch]$NoSync
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$stateDir = Join-Path $root 'artifacts\demo-server'
$packageStore = Join-Path $stateDir 'packages'
$auditLog = Join-Path $stateDir 'audit.ndjson'
$keyFile = Join-Path $stateDir 'management-key.txt'
$apiProject = Join-Path $root 'Server\src\TcgULtimateServer.Api\TcgULtimateServer.Api.csproj'
$defaultPackage = Join-Path $root 'GamePackages\astral-rift-demo-1.0.0.cardforge'

if ([string]::IsNullOrWhiteSpace($PackagePath)) { $PackagePath = $defaultPackage }
if (-not (Test-Path $apiProject)) { throw "Server project not found: $apiProject" }
if (-not (Test-Path $PackagePath)) { throw "Game package not found: $PackagePath" }
New-Item -ItemType Directory -Force -Path $stateDir, $packageStore | Out-Null

function Write-Step([string]$message) { Write-Host "==> $message" -ForegroundColor Cyan }
function Write-Ok([string]$message) { Write-Host "    $message" -ForegroundColor Green }
function Write-Warn2([string]$message) { Write-Host "    $message" -ForegroundColor Yellow }

function Test-Health([int]$port) {
    try {
        $health = Invoke-RestMethod -Uri "http://127.0.0.1:$port/api/health" -TimeoutSec 2
        return $health.status -eq 'healthy'
    } catch { return $false }
}

function Get-ManagementToken([int]$port, [string]$key) {
    try {
        $response = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$port/api/auth/local-management" `
            -Headers @{ 'X-Tcg-Management-Key' = $key } -TimeoutSec 5
        return $response.token
    } catch { return $null }
}

function Test-PortFree([int]$port) {
    try {
        $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $port)
        $listener.Start(); $listener.Stop()
        return $true
    } catch { return $false }
}

# --- generate the Unity client files ----------------------------------------
# Mirrors the studio's CardForgeClient runtime sources into the standalone Unity
# template project and points its connection settings at the demo server port.
function Sync-UnityTemplate([string]$url) {
    $sourceRoot = Join-Path $root 'Assets\CardForgeClient\Runtime'
    $templateRoot = Join-Path $root 'Server\templates\TcgUnityClient\Assets\TcgClient\Runtime'
    $synced = 0
    foreach ($relative in @(
        'UI\TcgTemplateApp.cs',
        'Networking\TcgRestClient.cs',
        'Networking\TcgGameConnection.cs',
        'Models\TcgModels.cs',
        'Session\TcgClientSession.cs',
        'Configuration\TcgConnectionSettings.cs',
        'Security\SecurePacketFactory.cs'
    )) {
        $source = Join-Path $sourceRoot $relative
        $target = Join-Path $templateRoot $relative
        if (-not (Test-Path $source)) { continue }
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        Copy-Item $source $target -Force
        $synced++
    }

    $port = ([uri]$url).Port
    $settingsPath = Join-Path $root 'Server\templates\TcgUnityClient\Assets\TcgClient\Resources\TcgConnectionSettings.asset'
    New-Item -ItemType Directory -Force -Path (Split-Path $settingsPath -Parent) | Out-Null
    @"
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: e870cd084b5eb204d9eb029031a85596, type: 3}
  m_Name: TcgConnectionSettings
  m_EditorClassIdentifier: Assembly-CSharp::TcgServer.Client.TcgConnectionSettings
  baseUrl: $url
  defaultRulePackId: astral-rift
  activePackageId: astral-rift-demo
  activePackageVersion: 1.0.0
  gamePollIntervalSeconds: 1
  lobbyRefreshIntervalSeconds: 5
"@ | Set-Content -Path $settingsPath -Encoding ascii
    Write-Ok "Generated $synced Unity client files in Server\templates\TcgUnityClient (server $url)."
}

# --- management key -------------------------------------------------------
$managementKey = $null
if (Test-Path $keyFile) { $managementKey = (Get-Content $keyFile -Raw).Trim() }
if ([string]::IsNullOrWhiteSpace($managementKey)) {
    $managementKey = [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')
    Set-Content -Path $keyFile -Value $managementKey -NoNewline
}

# --- find or start the server ----------------------------------------------
$baseUrl = $null
$token = $null
$serverProcess = $null

foreach ($port in $FirstPort..$LastPort) {
    if (-not (Test-Health $port)) { continue }
    $candidate = Get-ManagementToken $port $managementKey
    if ($candidate) {
        $baseUrl = "http://127.0.0.1:$port"
        $token = $candidate
        Write-Ok "Reusing demo server at $baseUrl"
        break
    }
    Write-Warn2 "Port $port is serving a different server instance; skipping."
}

if (-not $baseUrl) {
    $port = $null
    foreach ($candidate in $FirstPort..$LastPort) {
        if (Test-PortFree $candidate) { $port = $candidate; break }
    }
    if (-not $port) { throw "No free port between $FirstPort and $LastPort." }

    Write-Step "Starting the authoritative server on http://127.0.0.1:$port ..."
    # Process-scoped variables are inherited by the child dotnet process.
    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Development', 'Process')
    [Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', "http://127.0.0.1:$port", 'Process')
    [Environment]::SetEnvironmentVariable('TCG_LOCAL_MANAGEMENT_KEY', $managementKey, 'Process')
    [Environment]::SetEnvironmentVariable('TCG_CLIENT_MANAGED_SERVER', 'true', 'Process')
    [Environment]::SetEnvironmentVariable('TCG_LAUNCH_CLIENT', 'false', 'Process')
    [Environment]::SetEnvironmentVariable('CARDFORGE_GAME_PACKAGE_STORE', $packageStore, 'Process')
    [Environment]::SetEnvironmentVariable('TCG_AUDIT_LOG_PATH', $auditLog, 'Process')
    $serverProcess = Start-Process -FilePath 'dotnet' -ArgumentList @(
        'run', '--project', "`"$apiProject`"", '--configuration', 'Debug', '--no-launch-profile'
    ) -WorkingDirectory $root -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $stateDir 'server-stdout.log') `
        -RedirectStandardError (Join-Path $stateDir 'server-stderr.log')

    $ready = $false
    for ($i = 0; $i -lt 360; $i++) {
        if ($serverProcess.HasExited) { throw "The server exited during startup. Check Server logs." }
        if (Test-Health $port) { $ready = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "The server did not become healthy within 3 minutes (first build can be slow). See artifacts/demo-server/server-*.log" }

    $baseUrl = "http://127.0.0.1:$port"
    $token = Get-ManagementToken $port $managementKey
    if (-not $token) { throw "The server is healthy but rejected the management key." }
    Write-Ok "Server ready at $baseUrl"
}

# --- generate the Unity client files ----------------------------------------
if (-not $NoSync) { Sync-UnityTemplate $baseUrl }

# --- activate the game package ----------------------------------------------
$packages = Invoke-RestMethod -Uri "$baseUrl/api/admin/game-packages" -Headers @{ Authorization = "Bearer $token" }
$active = $packages | Where-Object { $_.packageId -eq 'astral-rift-demo' -and $_.active } | Select-Object -First 1
if ($active) {
    Write-Ok "Package already active: $($active.name) $($active.version)"
} else {
    Write-Step "Importing and activating $(Split-Path $PackagePath -Leaf) ..."
    $fileBytes = [IO.File]::ReadAllBytes($PackagePath)
    $boundary = [guid]::NewGuid().ToString('N')
    $enc = [Text.Encoding]::GetEncoding('iso-8859-1')
    $body = New-Object IO.MemoryStream
    $writer = New-Object IO.StreamWriter($body, $enc)
    $writer.Write("--$boundary`r`nContent-Disposition: form-data; name=`"activate`"`r`n`r`ntrue`r`n")
    $writer.Write("--$boundary`r`nContent-Disposition: form-data; name=`"file`"; filename=`"$(Split-Path $PackagePath -Leaf)`"`r`nContent-Type: application/vnd.cardforge.game-package+zip`r`n`r`n")
    $writer.Flush()
    $body.Write($fileBytes, 0, $fileBytes.Length)
    $writer.Write("`r`n--$boundary--`r`n")
    $writer.Flush()
    $body.Position = 0
    $response = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/admin/game-packages/import" `
        -Headers @{ Authorization = "Bearer $token" } `
        -ContentType "multipart/form-data; boundary=$boundary" `
        -Body $body.ToArray()
    Write-Ok "Package active: $($response.name) $($response.version)"
}

# --- demo accounts -----------------------------------------------------------
function Register-IfMissing([string]$handle, [string]$password) {
    try {
        Invoke-RestMethod -Method Post -Uri "$baseUrl/api/auth/register" `
            -ContentType 'application/json' `
            -Body (@{ handle = $handle; password = $password } | ConvertTo-Json) | Out-Null
        Write-Ok "Registered account '$handle'."
    } catch {
        Write-Ok "Account '$handle' already exists."
    }
}

function Login-User([string]$handle, [string]$password) {
    try {
        return Invoke-RestMethod -Method Post -Uri "$baseUrl/api/auth/login" `
            -ContentType 'application/json' `
            -Body (@{ handle = $handle; password = $password } | ConvertTo-Json)
    } catch { return $null }
}

Register-IfMissing 'demo' 'cardforge-demo'
Register-IfMissing 'demo-b' 'cardforge-demo-b'

$demoA = Login-User 'demo' 'cardforge-demo'
$demoB = Login-User 'demo-b' 'cardforge-demo-b'
if ($demoA -and $demoB) {
    try {
        Invoke-RestMethod -Method Post -Uri "$baseUrl/api/friends" `
            -Headers @{ Authorization = "Bearer $($demoA.token)" } `
            -ContentType 'application/json' `
            -Body (@{ friendUserId = $demoB.user.id } | ConvertTo-Json) | Out-Null
        Write-Ok "demo and demo-b are friends."
    } catch { Write-Warn2 "Friend link skipped (already friends or unavailable)." }
    try {
        Invoke-RestMethod -Method Post -Uri "$baseUrl/api/messages" `
            -Headers @{ Authorization = "Bearer $($demoB.token)" } `
            -ContentType 'application/json' `
            -Body (@{ recipientId = $demoA.user.id; body = 'Welcome to the CardForge Studio demo! Try Play vs AI from the lobby.' } | ConvertTo-Json) | Out-Null
        Write-Ok "Welcome message sent to demo."
    } catch { Write-Warn2 "Welcome message skipped." }
}

# --- launch the client -------------------------------------------------------
Write-Step "Demo server is ready at $baseUrl"
Write-Host "    Accounts:  demo / cardforge-demo  (or Quick demo sign-in)" -ForegroundColor White
Write-Host "              demo-b / cardforge-demo-b" -ForegroundColor White

if (-not $NoClient) {
    $exe = $ClientExe
    if ([string]::IsNullOrWhiteSpace($exe)) {
        foreach ($candidate in @(
            (Join-Path $root 'Builds\Windows\CardForgeStudio.exe'),
            (Join-Path $root 'Builds\Windows\TcgULtimateClient.exe'),
            (Join-Path $root 'Server\templates\TcgUnityClient\Builds\Windows\TcgULtimateClient.exe')
        )) {
            if (Test-Path $candidate) { $exe = $candidate; break }
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($exe) -and (Test-Path $exe)) {
        Write-Step "Launching client: $exe"
        Start-Process -FilePath $exe -ArgumentList '--tcg-demo-login'
    } else {
        Write-Warn2 "No client executable found. Open Server\templates\TcgUnityClient in Unity (Tools > TcgStudio > Create or Refresh Client Scene, then Play), or pass -ClientExe."
    }
}

if ($serverProcess) {
    Write-Host "`nPress Ctrl+C to stop the demo server (PID $($serverProcess.Id))." -ForegroundColor DarkGray
    Wait-Process -Id $serverProcess.Id
}
