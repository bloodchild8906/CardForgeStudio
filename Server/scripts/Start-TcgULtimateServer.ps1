$ErrorActionPreference = 'Stop'

$scripts = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scripts
$clientProject = Join-Path $root 'src\TcgULtimateServer.App\TcgULtimateServer.App.csproj'
$clientExecutable = Join-Path $root 'src\TcgULtimateServer.App\bin\Debug\net8.0-windows10.0.19041.0\win-x64\TcgULtimateServer.exe'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
	throw 'The .NET SDK is required to run TcgULtimateServer.'
}

Write-Host 'Starting TcgULtimateServer...' -ForegroundColor Green
Write-Host 'Preparing the native management dashboard...' -ForegroundColor Cyan
dotnet build $clientProject -c Debug
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $clientExecutable)) {
	throw "The native management dashboard build did not produce $clientExecutable."
}

Start-Process $clientExecutable -WorkingDirectory $root
