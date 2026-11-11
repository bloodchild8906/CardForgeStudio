# Author → publish → native server → play

## Generate or update a game

Use **CardForge > Dashboard** or **CardForge > Generate Sample Game**. CardForge source documents live in `Assets/CardForgeData`. The designers generate a `.cardforge` archive under `GamePackages`. The Game Package Publisher retains package manifests, file hashes and a portable record of the authored set.

`NativeContentPublisher.StagePackage` converts the ZIP's `rules/rule-pack.json` and `rules/victory.json` into native simulation JSON, normalizes action costs/effect parameters, and stages images under `Assets/StreamingAssets/CardForge`. Newly built Unity Dedicated Server players bundle this initial content.

For a **running local demo server**, **CardForge > Play Sample Game** logs in with local demo credentials and submits current rules via `POST /api/admin/rules/import`, then uploads art to `POST /api/admin/assets/{id}`. The native host validates IDs, SHA-256, file sizes, game-rule references and active-match conflicts. No old REST gateway or `Server/templates` client generation is involved. To publish other rulesets, provide a compatible package and privileged native publisher.

## Native server startup

1. In Unity, choose **CardForge > Server > Build Native Windows Dedicated Server**.
2. Start `Demo/Start-CardForgeDedicatedServer.ps1 -NoClient`, or use the dashboard's Play Sample command.
3. Confirm `GET http://127.0.0.1:5168/api/health` shows `status: healthy`, `simulationMode: Unity Dedicated Server` and `simulationReady: true`.
4. Open the UI Toolkit scene `Assets/CardForgeStudio/CardForgeClient/Scenes/TcgClient.unity` and press Play. Editor startup cancels Play until native health is confirmed.

The launcher reuses a healthy native process, otherwise starts the Windows Unity Dedicated Server build on a free configured port. **It will not fall back to ASP.NET Core.**

## Production

Do not use `--demo-mode` on a public interface. Configure a strong admin account using `CARDFORGE_ADMIN_HANDLE` and `CARDFORGE_ADMIN_PASSWORD` on first startup; remove the password from shell history and process environments after initialization. Run behind an HTTPS reverse proxy, monitor logs, restrict publishing, and provision persistent storage for `Application.persistentDataPath`. Deploy externally reachable clients with a proper base URL, not `127.0.0.1`.

## Testing

```powershell
dotnet run --project Tests/CardForge.Simulation.Tests/CardForge.Simulation.Tests.csproj
dotnet run --project Tests/CardForge.NativeServer.Tests/CardForge.NativeServer.Tests.csproj
powershell -ExecutionPolicy Bypass -File Tests/Smoke/Invoke-PlaySmokeTest.ps1
```

The smoke script requires the native Unity server build. Tests cannot certify production security, cross-machine networking or capacity.
