# Connection tutorials

These tutorials assume a local Development server at `https://localhost:5001`. Trust the development certificate before connecting. Production clients must use the deployed trusted certificate and must not bypass certificate validation.

## Tutorial 1: open the operator console

1. Run `TcgULtimateServer.cmd` from the repository root.
2. Wait for **Online** in the top status bar.
3. Confirm **Local operator - Admin** appears.
4. Confirm the live player count is visible.
5. Use **Service health** to verify the API and seeded resources.

The console handles its management session automatically. There is no dashboard sign-in form.

## Tutorial 2: register and call the REST API with PowerShell

Register a player:

```powershell
$baseUrl = "https://localhost:5001"
$registration = curl.exe -k -X POST "$baseUrl/api/auth/register" `
  -H "Content-Type: application/json" `
  --data-raw '{"handle":"atlas","password":"Use-A-Real-Secret"}' |
  ConvertFrom-Json

$token = $registration.token
$userId = $registration.user.id
```

Call an authenticated endpoint:

```powershell
curl.exe -k "$baseUrl/api/decks" `
  -H "Authorization: Bearer $token"
```

Check the public live count:

```powershell
curl.exe -k "$baseUrl/api/presence/count"
```

Use `POST /api/auth/login` with the same `handle` and `password` on later sessions.

When the client signs out, revoke the token as well as clearing it locally:

```powershell
curl.exe -k -X DELETE "$baseUrl/api/auth/session" `
  -H "Authorization: Bearer $token"
```

## Tutorial 3: connect with .NET `HttpClient`

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;

var http = new HttpClient
{
    BaseAddress = new Uri("https://localhost:5001")
};

var loginResponse = await http.PostAsJsonAsync("/api/auth/login", new
{
    handle = "atlas",
    password = "Use-A-Real-Secret"
});
loginResponse.EnsureSuccessStatusCode();

var session = await loginResponse.Content.ReadFromJsonAsync<AuthSession>()
    ?? throw new InvalidOperationException("The login response was empty.");

http.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", session.Token);

var decks = await http.GetFromJsonAsync<List<Deck>>("/api/decks");

public sealed record AuthSession(string Token, PublicUser User);
public sealed record PublicUser(string Id, string Handle, int Rating, string Role, bool Disabled);
public sealed record Deck(string Id, string OwnerId, string Name, string RulePackId, List<string> CardIds);
```

Do not add a certificate-validation bypass to shipped code. Trust the development certificate locally and use a normal trusted certificate in production.

## Tutorial 4: save a deck and send a direct message

Discover rule packs and cards:

```powershell
curl.exe -k "$baseUrl/api/rules"
```

Save or update a deck:

```powershell
$deckBody = @{
  id = $null
  name = "Starter Control"
  rulePackId = "mtg-like"
  cardIds = @("spark-mage", "stone-golem", "bolt")
} | ConvertTo-Json -Compress

curl.exe -k -X POST "$baseUrl/api/decks" `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  --data-raw $deckBody
```

Find another player's public ID in `/api/rankings`, then send a direct message:

```powershell
$messageBody = @{
  recipientId = "RECIPIENT_USER_ID"
  body = "Ready for a match?"
} | ConvertTo-Json -Compress

curl.exe -k -X POST "$baseUrl/api/messages" `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  --data-raw $messageBody
```

## Tutorial 5: join matchmaking

List enabled queues:

```powershell
curl.exe -k "$baseUrl/api/matchmaking/queues"
```

Join the ranked queue:

```powershell
$joinBody = @{
  queueId = "ranked"
  region = "global"
  partySize = 1
} | ConvertTo-Json -Compress

curl.exe -k -X POST "$baseUrl/api/matchmaking/join" `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  --data-raw $joinBody
```

Check the current entry:

```powershell
curl.exe -k "$baseUrl/api/matchmaking/me" `
  -H "Authorization: Bearer $token"
```

Leave matchmaking:

```powershell
curl.exe -k -X DELETE "$baseUrl/api/matchmaking/leave" `
  -H "Authorization: Bearer $token"
```

Administrators can inspect entries, run a queue manually, or flush it from **Matchmaking control**.

## Tutorial 6: create and join a game

Create a game for known player IDs:

```powershell
$gameBody = @{
  rulePackId = "mtg-like"
  playerIds = @($userId)
  addAi = $true
} | ConvertTo-Json -Compress

$game = curl.exe -k -X POST "$baseUrl/api/games" `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  --data-raw $gameBody |
  ConvertFrom-Json

$gameId = $game.id
```

Connect a .NET client to the game hub after adding the `Microsoft.AspNetCore.SignalR.Client` package:

```csharp
using Microsoft.AspNetCore.SignalR.Client;

var connection = new HubConnectionBuilder()
    .WithUrl("https://localhost:5001/hubs/game")
    .WithAutomaticReconnect()
    .Build();

connection.On<GameState>("gameState", state =>
{
    Console.WriteLine($"Turn {state.Turn}, active player {state.ActivePlayerId}");
});

connection.On<string>("chatMessage", message => Console.WriteLine(message));
connection.On<string>("serverAnnouncement", message => Console.WriteLine(message));

connection.Reconnected += async _ =>
{
    await connection.InvokeAsync("JoinGame", token, gameId);
};

await connection.StartAsync();
await connection.InvokeAsync("JoinGame", token, gameId);
```

The hub sends the current authoritative state immediately after `JoinGame`.

## Tutorial 7: build and send a secure SignalR packet

```csharp
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

static (string Payload, string PacketId, long SentAt, string Signature) SecurePacket(
    string token,
    string scope,
    string plainText)
{
    var source = Encoding.UTF8.GetBytes(plainText);
    using var output = new MemoryStream();
    using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        gzip.Write(source);

    var payload = Convert.ToBase64String(output.ToArray());
    var packetId = Guid.NewGuid().ToString("N");
    var sentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var signedText = $"{scope}.{packetId}.{sentAt}.{payload}";

    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(token));
    var signature = Convert.ToBase64String(
        hmac.ComputeHash(Encoding.UTF8.GetBytes(signedText)));

    return (payload, packetId, sentAt, signature);
}
```

Send game chat:

```csharp
var chat = SecurePacket(token, $"chat:{gameId}", "Good luck!");
await connection.InvokeAsync(
    "ChatMessage",
    token,
    gameId,
    chat.Payload,
    chat.PacketId,
    chat.SentAt,
    chat.Signature);
```

Play a card instance:

```csharp
var play = SecurePacket(token, $"play:{gameId}", cardInstanceId);
await connection.InvokeAsync(
    "PlayCard",
    token,
    gameId,
    play.Payload,
    play.PacketId,
    play.SentAt,
    play.Signature);
```

Create a new packet for every action and retry. Packet IDs cannot be reused.

## Tutorial 8: connect a Unity client

For Unity versions with a compatible .NET profile, keep the authentication DTOs, REST client, secure-packet helper, and SignalR wrapper in a dedicated runtime assembly. Keep the base URL in environment-specific configuration rather than scene code.

A minimal coroutine-based REST login starts like this:

```csharp
using System.Collections;
using System.Text;
using UnityEngine.Networking;

IEnumerator Login(string handle, string password)
{
    var json = JsonUtility.ToJson(new LoginRequest
    {
        handle = handle,
        password = password
    });

    using (var request = new UnityWebRequest(
        "https://localhost:5001/api/auth/login",
        UnityWebRequest.kHttpVerbPOST))
    {
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
            throw new InvalidOperationException(request.error);

        var session = JsonUtility.FromJson<AuthSession>(request.downloadHandler.text);
        // Keep session.token in the in-memory client session service.
    }
}
```

For protected requests, set:

```csharp
request.SetRequestHeader("Authorization", $"Bearer {session.token}");
```

Use a persistent service object for the token, REST client, and SignalR connection. Rejoin the game group after SignalR reconnects, and marshal UI changes back to Unity's main thread. Continue with the complete [Unity client tutorial](unity-client-tutorial.md).

## Tutorial 9: connect to a workflow

Create an instance:

```powershell
$workflowBody = @{
  configurationId = "workflow"
  data = @{ requestedBy = $userId }
  participantIds = @("another-player-id")
} | ConvertTo-Json -Depth 4 -Compress

$instance = curl.exe -k -X POST "$baseUrl/api/state-machines" `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  --data-raw $workflowBody |
  ConvertFrom-Json
```

Connect to `/hubs/state-machine`, subscribe to `stateMachineState`, and call `Join(token, instance.id)`. For a real-time transition, sign the JSON event payload with scope:

```text
transition:{instanceId}:{eventName}
```

REST transitions are also available at `POST /api/state-machines/{id}/transitions` with `{ "event": "start", "data": { ... } }`.

Workflow instances are private to their creator and the listed participants. Administrators can inspect every instance. Use `participantIds` only for players who should be able to read, join, or transition that workflow.

## Troubleshooting

| Symptom | Check |
|---|---|
| Certificate error | Run `dotnet dev-certs https --trust` locally; install a trusted production certificate for deployed clients. |
| `401 Unauthorized` | Verify the bearer header, token spelling, and that the account is not disabled. |
| `403 Forbidden` | The session is valid but lacks the required moderator or administrator role. |
| SignalR connects but receives no game state | Call `JoinGame` after connecting and again after reconnecting. |
| Secure packet rejected | Check clock drift, scope, GZIP/Base64 order, signature input, packet uniqueness, and payload size. |
| Player count remains zero | Make an authenticated request with the player token; presence reflects activity during the last two minutes. |
| API root returns `404` | Expected behavior. Use `/api/health`. |
| Data disappears after restart | Current operational and gameplay state is in memory. Export an archive before shutdown. |
