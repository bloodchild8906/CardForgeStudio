param([switch]$SkipInstall)
$ErrorActionPreference = 'Stop'

function Test-MongoPort {
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $connection = $client.BeginConnect('127.0.0.1', 27017, $null, $null)
        $open = $connection.AsyncWaitHandle.WaitOne(900)
        if ($open) { $client.EndConnect($connection) }
        $client.Close()
        return $open
    } catch { return $false }
}

# A configured remote deployment should never install a second local database.
$uri = [string]$env:CARDFORGE_MONGODB_URI
if ($uri -and $uri -notmatch '^mongodb://(localhost|127\.0\.0\.1)(:27017)?([/?]|$)') {
    Write-Output 'Using configured external MongoDB; no local installation attempted.'
    return
}
if (Test-MongoPort) {
    Write-Output 'MongoDB is reachable on loopback port 27017.'
    return
}

$service = Get-Service -Name 'MongoDB' -ErrorAction SilentlyContinue
if ($service) {
    try {
        if ($service.Status -ne 'Running') { Start-Service -Name 'MongoDB' -ErrorAction Stop }
    } catch { Write-Warning ("MongoDB service could not start: " + $_.Exception.Message) }
}

function Find-Mongod {
    $root = 'C:\Program Files\MongoDB\Server'
    if (!(Test-Path $root)) { return $null }
    $files = @(Get-ChildItem $root -Recurse -Filter 'mongod.exe' -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) { return $null }
    return $files | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}

$mongod = Find-Mongod
if (!(Test-MongoPort) -and !$mongod) {
    if ($SkipInstall) { throw 'MongoDB Community Server is not installed.' }
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (!$winget) {
        throw 'MongoDB Community Server is missing and winget is unavailable. Install MongoDB.Server from MongoDB or enable Windows Package Manager.'
    }
    Write-Output 'Installing MongoDB Community Server from the winget MongoDB.Server package...'
    & winget install --id MongoDB.Server --exact --source winget --silent --accept-source-agreements --accept-package-agreements --disable-interactivity
    if ($LASTEXITCODE -ne 0) {
        throw "MongoDB installation failed with exit code $LASTEXITCODE. Run the launcher elevated to allow installation, or provide CARDFORGE_MONGODB_URI for an existing MongoDB."
    }
    $mongod = Find-Mongod
    $service = Get-Service -Name 'MongoDB' -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Running') {
        try { Start-Service -Name 'MongoDB' -ErrorAction Stop }
        catch { Write-Warning ("Installed service could not start: " + $_.Exception.Message) }
    }
}

if (!(Test-MongoPort) -and $mongod) {
    # Safe, user-scoped fallback when the Windows service cannot be started.
    # Never binds an unauthenticated local test DB to a public interface.
    $root = Join-Path $env:LOCALAPPDATA 'CardForgeStudio\MongoDB'
    $data = Join-Path $root 'data'
    $log = Join-Path $root 'mongod.log'
    New-Item -ItemType Directory -Force -Path $data | Out-Null
    $args = @('--bind_ip','127.0.0.1','--port','27017','--dbpath',
        ('"' + $data + '"'),'--logpath',('"' + $log + '"'))
    $process = Start-Process -FilePath $mongod -ArgumentList $args -PassThru -WindowStyle Hidden
    Write-Output ("Started local MongoDB PID {0}." -f $process.Id)
}
$deadline = [DateTime]::UtcNow.AddSeconds(35)
while (!(Test-MongoPort)) {
    if ([DateTime]::UtcNow -ge $deadline) {
        throw 'MongoDB did not become available on port 27017. Check the MongoDB service and logs.'
    }
    Start-Sleep -Milliseconds 500
}
Write-Output 'CARDFORGE_MONGODB_READY local=127.0.0.1:27017'
