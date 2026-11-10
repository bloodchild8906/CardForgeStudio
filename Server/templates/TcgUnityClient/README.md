# TcgULtimateClient Unity project template

This is a ready-to-open Unity client foundation for the TcgULtimateServer in this repository.

## Included

- Professional runtime sample UI using Unity uGUI.
- Server health and connection status.
- Player registration and login.
- In-memory bearer session management.
- Live online-player count.
- Collection, achievement, and deck summaries.
- Friends and direct messages.
- Active and recorded game browser.
- Enabled matchmaking queues and queue status.
- Rankings.
- Join ranked or casual matchmaking.
- Leave matchmaking.
- Create a game against server AI.
- Public game-state polling and recent event display.
- Secure GZIP, Base64, and HMAC-SHA256 packet generation.
- Optional SignalR game state, chat, announcements, and card actions.
- Editor command that creates or refreshes the sample scene and connection settings.

## Open the project

1. Start the TcgULtimateServer WinUI console from the repository root.
2. Trust the local HTTPS certificate if needed:

   ```powershell
   dotnet dev-certs https --trust
   ```

3. Add this folder to Unity Hub:

   ```text
   D:\TcgServer\templates\TcgUnityClient
   ```

4. Open it with Unity `7000.0.0a16` or allow a newer compatible editor to upgrade the project.
5. If the scene is not already present, choose **Tools > TcgStudio > Create or Refresh Client Scene**.
6. Open `Assets/TcgClient/Scenes/TcgClient.unity`.
7. Enter Play Mode.

## Sample workflow

1. Register two development accounts or sign into an existing account.
2. Review online players, matchmaking queues, and rankings in the lobby.
3. Join ranked or casual matchmaking.
4. Open the WinUI **Matchmaking control** workspace to inspect or run the queue.
5. Or select **Create vs AI** for an immediate game.
6. The Game page polls the authoritative game state over REST.
7. Enable SignalR to receive immediate game state, game chat, and announcements.

## Connection settings

The editor setup creates:

```text
Assets/TcgClient/Resources/TcgConnectionSettings.asset
```

The default base URL is:

```text
https://localhost:5001
```

Create separate settings assets for development, test, staging, and production. Do not ship a development URL or certificate bypass in a production client.

## Enable SignalR

The project compiles and runs with REST polling before SignalR dependencies are installed.

To enable real-time communication:

1. Import `Microsoft.AspNetCore.SignalR.Client` and all required dependencies using the Unity project's approved managed-package workflow.
2. Confirm the assemblies are compatible with the selected Unity API compatibility level and all target platforms.
3. Add `TCG_SIGNALR` to **Project Settings > Player > Scripting Define Symbols**.
4. Let Unity recompile.
5. Enter Play Mode, create or open a game, and select **Connect SignalR**.

Do not add the define until the package and dependencies are available; the optional source is excluded otherwise.

## Folder guide

```text
Assets/TcgClient/
  Editor/          Sample-scene generator
  Resources/       Generated connection settings
  Runtime/
    Configuration/ Server environment settings
    Models/        REST and game DTOs
    Networking/    REST and optional SignalR transports
    Security/      Secure action packet builder
    Session/       In-memory player session
    UI/            Runtime sample client interface
  Scenes/          Generated playable scene
```

## Extend the template

- Replace the runtime-built uGUI with the production UI design system.
- Add deck, collection, friends, and direct-message screens using the existing REST client pattern.
- Populate card visuals from `/api/rules` and `/api/assets/manifest`.
- Add a board presenter that maps authoritative `GameStateDto` data to card GameObjects.
- Add card selection and pass the owned card-instance ID to `PlayCardAsync`.
- Add reconnect UX and initial SignalR start retry.
- Add protected platform credential storage only if persistent sessions are required.
- Add environment-specific build automation and certificate trust for test devices.

## Security notes

- Player sign-in is server-managed.
- The Unity client never uses the local management key.
- Bearer tokens remain in memory and are cleared on sign-out.
- State-changing SignalR actions use unique, timestamped secure packets.
- Never disable certificate validation in a release build.
- Treat server game state as authoritative.

See the complete repository guide at `docs/unity-client-tutorial.md`.

The complete phased product plan is in `docs/unity-client-roadmap.md`.
