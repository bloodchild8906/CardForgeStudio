param(
    [int]$FirstPort = 5168,
    [int]$LastPort = 5198,
    [switch]$NoClient,
    [switch]$Production
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $root 'Builds\DedicatedServer\CardForgeServer.exe'
$state = Join-Path $PSScriptRoot '.state'
New-Item -ItemType Directory $state -Force | Out-Null

if (!(Test-Path $executable)) {
    throw "The native Unity Dedicated Server build is missing. Open CardForgeStudio in Unity and select CardForge > Server > Build Native Windows Dedicated Server. No legacy server fallback is permitted."
}

function NativeHealth([int]$port) {
    try {
        $health = Invoke-RestMethod -Uri ("http://127.0.0.1:{0}/api/health" -f $port) -TimeoutSec 1
        if ($Production -and $health.persistence -ne 'MongoDB') { return $false }
        if (!$Production -and $health.persistence -ne 'Local JSON (demo)') { return $false }
        return $health.status -eq 'healthy' -and
            $health.simulationMode -eq 'Unity Dedicated Server' -and
            $health.simulationReady -eq $true
    } catch { return $false }
}
function PortOpen([int]$port) {
    try {
        $client = New-Object Net.Sockets.TcpClient
        $connection = $client.BeginConnect('127.0.0.1', $port, $null, $null)
        $success = $connection.AsyncWaitHandle.WaitOne(250)
        if ($success) { $client.EndConnect($connection) }
        $client.Close()
        return $success
    } catch { return $false }
}

$port = $null
foreach ($candidate in $FirstPort..$LastPort) {
    if (NativeHealth $candidate) { $port = $candidate; break }
}
if (!$port) {
    foreach ($candidate in $FirstPort..$LastPort) {
        if (!(PortOpen $candidate)) { $port = $candidate; break }
    }
}
if (!$port) { throw "No available native server port found between $FirstPort and $LastPort." }

if (!(NativeHealth $port)) {
    # Demo mode is self-contained and uses user-scoped JSON persistence unless a
    # MongoDB URI is explicitly supplied. Production still requires MongoDB.
    if ($Production) {
        $mongoSetup = Join-Path $PSScriptRoot 'Ensure-MongoDB.ps1'
        if (!(Test-Path $mongoSetup)) { throw "MongoDB prerequisite installer is missing." }
        & $mongoSetup
    } else {
        Write-Output 'Demo persistence: local JSON (MongoDB installation not required).'
    }
    $logFile = Join-Path $state 'cardforge-native.log'
    $arguments = @('-batchmode','-nographics', "--port=$port", '-logFile', ('"' + $logFile + '"'))
    if (!$Production) { $arguments += '--demo-mode' }
    # A demo must remain self-contained even when an old system-wide MongoDB URI
    # is present. Only this child server receives the cleared environment.
    $previousMongoUri = $env:CARDFORGE_MONGODB_URI
    if (!$Production) { Remove-Item Env:CARDFORGE_MONGODB_URI -ErrorAction SilentlyContinue }
    try {
        $proc = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory (Split-Path $executable) -PassThru
    } finally {
        if (!$Production) {
            if ([string]::IsNullOrWhiteSpace([string]$previousMongoUri)) {
                Remove-Item Env:CARDFORGE_MONGODB_URI -ErrorAction SilentlyContinue
            } else {
                $env:CARDFORGE_MONGODB_URI = $previousMongoUri
            }
        }
    }
    [IO.File]::WriteAllText((Join-Path $state 'native-server.pid'), [string]$proc.Id)
    Write-Output ("Started native CardForge server PID {0} on port {1}." -f $proc.Id, $port)
    $until = [DateTime]::UtcNow.AddSeconds(90)
    while (!(NativeHealth $port)) {
        $proc.Refresh()
        if ($proc.HasExited) {
            throw "Native Unity server exited with code $($proc.ExitCode). See $logFile"
        }
        if ([DateTime]::UtcNow -ge $until) {
            throw "Unity native server failed to become healthy. See $logFile"
        }
        Start-Sleep -Milliseconds 500
    }
}
else {
    Write-Output ("Reusing existing native Unity server on port {0}." -f $port)
}

$settings = Join-Path $root 'Assets\CardForgeStudio\CardForgeClient\Resources\TcgConnectionSettings.asset'
if (Test-Path $settings) {
    $yaml = [IO.File]::ReadAllText($settings)
    $yaml = [regex]::Replace($yaml, '(?m)^  baseUrl: .*$',
        ('  baseUrl: http://127.0.0.1:' + $port))
    $yaml = [regex]::Replace($yaml, '(?m)^  defaultRulePackId: .*$',
        '  defaultRulePackId: astral-rift')
    $yaml = [regex]::Replace($yaml, '(?m)^  activePackageId: .*$',
        '  activePackageId: astral-rift')
    $yaml = [regex]::Replace($yaml, '(?m)^  activePackageVersion: .*$',
        '  activePackageVersion: 3.0.0')
    [IO.File]::WriteAllText($settings, $yaml, [Text.UTF8Encoding]::new($false))
}
Write-Output ("CARDFORGE_NATIVE_READY http://127.0.0.1:{0}" -f $port)
if (!$NoClient) {
    $client = Join-Path $root 'Builds\Windows\CardForgeStudio.exe'
    if (Test-Path $client) {
        $existingClient = @(Get-CimInstance Win32_Process -Filter "Name='CardForgeStudio.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.ExecutablePath -and ([IO.Path]::GetFullPath($_.ExecutablePath) -eq [IO.Path]::GetFullPath($client)) } |
            Select-Object -First 1)
        if ($existingClient.Count -gt 0) {
            Write-Output ("Reusing existing CardForge client PID {0}." -f $existingClient[0].ProcessId)
        } else {
            $clientLog = Join-Path $state 'cardforge-client.log'
            Write-Output ("Launching CardForge client against http://127.0.0.1:{0}" -f $port)
            Start-Process -FilePath $client -ArgumentList @(
                ("--server=http://127.0.0.1:{0}" -f $port),
                '--tcg-demo-login',
                '-logFile', ('"' + $clientLog + '"')
            ) -WorkingDirectory (Split-Path $client)
        }
    } else {
        Write-Output 'No standalone client build found. In Unity use CardForge > Demo > Play Astral Rift.'
    }
}
