# Tests

`CardForgeStudio.IntegrationTests` is a dependency-free executable test assembly. It covers valid import/activation/runtime victory, immutable version collisions, SHA-256 tampering, ZIP path traversal, live-match activation protection, and durable version/activation restoration.

Run it with:

```powershell
dotnet run --project Tests/CardForgeStudio.IntegrationTests/CardForgeStudio.IntegrationTests.csproj -c Release
```

Validate a real editor-produced archive with:

```powershell
dotnet run --project Tests/CardForgeStudio.IntegrationTests/CardForgeStudio.IntegrationTests.csproj -c Release -- --package GamePackages/core-1.0.0.cardforge
```

Unity EditMode tests remain under `Assets/CardForge/Tests` and run through Unity Test Framework.
