# Getting started

## Requirements

- Windows 10 version 1809 or later, or Windows 11.
- .NET 8 SDK for development and source launches.
- A trusted local ASP.NET Core development certificate for HTTPS development.
- The repository checked out to a writable folder.

Trust the local HTTPS certificate once on a development machine:

```powershell
dotnet dev-certs https --trust
```

## Recommended launch: native operations console

From the repository root, run:

```powershell
.\TcgULtimateServer.cmd
```

Or launch the PowerShell script directly:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Start-TcgULtimateServer.ps1
```

The launcher builds the current source, opens the WinUI console, starts the API without a visible terminal, waits for `/api/health`, and opens a local management session. Closing the console stops the API process it owns.

The console should show:

- **Online** in the service status area.
- **Local operator - Admin** in the session area.
- The live player count in the top bar.
- Enabled administrator workspaces in the navigation pane.

## Run the API directly

Use this option for API development or a separate client process:

```powershell
dotnet run --project .\src\TcgULtimateServer.Api\TcgULtimateServer.Api.csproj
```

The Development launch profile listens on:

- `https://localhost:5001`
- `http://localhost:5000` with redirection to HTTPS

When `TCG_LAUNCH_CLIENT=true`, a direct Development launch also opens the WinUI client.

## Verify the service

```powershell
curl.exe -k https://localhost:5001/api/health
```

Expected response shape:

```json
{
  "service": "TcgULtimateServer",
  "status": "healthy",
  "version": "1.0.0.0",
  "serverTime": "2026-01-01T12:00:00Z"
}
```

`-k` is suitable only for a local development certificate. Production clients must validate the deployed certificate normally.

## Build and publish

Build the complete solution:

```powershell
dotnet build .\TcgULtimateServer.slnx -c Debug
```

Publish the WinUI app for 64-bit Windows:

```powershell
dotnet publish .\src\TcgULtimateServer.App\TcgULtimateServer.App.csproj -p:PublishProfile=win-x64
```

Published output is written to `artifacts/publish/win-x64`.

## Configuration

| Variable | Purpose | Guidance |
|---|---|---|
| `ASPNETCORE_URLS` | Overrides HTTP/HTTPS listener URLs | Use an HTTPS production endpoint or bind behind a trusted reverse proxy. |
| `ASPNETCORE_ENVIRONMENT` | Selects ASP.NET Core environment | Use `Development` only on trusted developer machines. |
| `TCG_ADMIN_HANDLES` | Comma-separated handles that receive the admin role when registered | Set before registration; do not expose it to clients. |
| `TCG_AUDIT_LOG_PATH` | Durable JSONL audit-log path | Point to a protected, backed-up location. |
| `TCG_LOCAL_MANAGEMENT_KEY` | Secret used for the loopback management session | The WinUI host generates one when not supplied. Never expose it remotely. |
| `TCG_LAUNCH_CLIENT` | Opens the WinUI client after direct API startup | Intended for local development. |
| `TCG_CLIENT_MANAGED_SERVER` | Marks an API process as owned by the WinUI client | Set by the desktop host automatically. |

## Seeded content

A new process starts with:

- The `mtg-like` example rule pack.
- Ranked and casual matchmaking queues.
- Example card definitions and addressable assets.
- Empty player, game, direct-message, and transaction collections until clients use them.

## Data lifecycle

The current implementation keeps gameplay and operational entities in process memory. Restarting the API resets that state. The exceptions are data explicitly written to disk, including the configured JSONL audit log and files saved from generated reports or archives.

Before a planned restart:

1. Open **Reports & archives**.
2. Generate the required JSON or CSV reports.
3. Create a `full` archive when a complete snapshot is required.
4. Verify the archive checksum.
5. Save the archive to approved durable storage.

## Next steps

- Connect a player client with [Connection tutorials](connection-tutorials.md).
- Review network contracts in [Communications protocol](communications-protocol.md).
- Learn every administrator workspace in [Feature documentation](features/README.md).

