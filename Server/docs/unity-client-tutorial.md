# Unity client tutorial

For the complete implementation sequence, release gates, and feature matrix, see the [Unity client product roadmap](unity-client-roadmap.md).

This tutorial builds a Unity game-client foundation for TcgULtimateServer. It covers project layout, REST authentication, bearer requests, matchmaking, SignalR game events, secure action packets, reconnect behavior, and deployment checks.

A ready-to-open implementation is available at [`templates/TcgUnityClient`](../templates/TcgUnityClient/README.md).

## 1. Choose a compatible Unity configuration

Use a Unity project with a modern .NET compatibility profile. In **Project Settings > Player > Other Settings**, choose the broadest cross-platform .NET Standard profile supported by the Unity version unless a required managed library needs the .NET Framework profile.

Official references:

- [Unity .NET profile support](https://docs.unity3d.com/2023.2/Documentation/Manual/dotnetProfileSupport.html)
- [ASP.NET Core SignalR .NET client](https://learn.microsoft.com/aspnet/core/signalr/dotnet-client)

Add `Microsoft.AspNetCore.SignalR.Client` and all of its required dependencies through the package-ingestion workflow approved for the Unity project. Do not copy only the top-level DLL. Test every target platform, especially IL2CPP builds, because managed dependency and AOT support can differ by target.

## 2. Recommended project layout

```text
Assets/
  TcgClient/
    Runtime/
      Configuration/
        TcgConnectionSettings.cs
      Dtos/
        TcgDtos.cs
      Networking/
        TcgRestClient.cs
        TcgGameConnection.cs
        SecurePacketFactory.cs
      Session/
        TcgClientSession.cs
      TcgClient.Runtime.asmdef
    Scenes/
      Login.unity
      Lobby.unity
      Game.unity
```

Keep networking independent from scene UI. Login screens should call the session service; game views should subscribe to session and connection events.

## 3. Create connection settings

```csharp
using UnityEngine;

[CreateAssetMenu(menuName = "TcgStudio/Connection Settings")]
public sealed class TcgConnectionSettings : ScriptableObject
{
    [SerializeField] private string baseUrl = "https://localhost:5001";

    public string BaseUrl => baseUrl.TrimEnd('/');
    public string GameHubUrl => $"{BaseUrl}/hubs/game";
    public string StateMachineHubUrl => $"{BaseUrl}/hubs/state-machine";
}
```

Create separate assets for local development, test, staging, and production. Do not hard-code production hosts in gameplay scripts.

## 4. Add basic DTOs

`JsonUtility` works for these simple object responses. Use a project-standard JSON library for endpoint responses containing dictionaries or top-level arrays.

```csharp
using System;

[Serializable]
public sealed class LoginRequestDto
{
    public string handle;
    public string password;
}

[Serializable]
public sealed class PublicUserDto
{
    public string id;
    public string handle;
    public int rating;
    public string role;
    public bool disabled;
}

[Serializable]
public sealed class AuthSessionDto
{
    public string token;
    public PublicUserDto user;
}

[Serializable]
public sealed class JoinMatchmakingRequestDto
{
    public string queueId;
    public string region = "global";
    public int partySize = 1;
}

public sealed class GameStateDto
{
    public string Id { get; set; } = string.Empty;
    public string RulePackId { get; set; } = string.Empty;
    public string ActivePlayerId { get; set; } = string.Empty;
    public int Turn { get; set; }
    public bool Finished { get; set; }
}
```

The SignalR client uses properties on `GameStateDto`; REST examples using `JsonUtility` use serializable fields.

## 5. Build a coroutine REST client

```csharp
using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed class TcgRestClient : MonoBehaviour
{
    [SerializeField] private TcgConnectionSettings settings;

    public IEnumerator Login(
        string handle,
        string password,
        Action<AuthSessionDto> completed,
        Action<string> failed)
    {
        var body = JsonUtility.ToJson(new LoginRequestDto
        {
            handle = handle,
            password = password
        });

        yield return SendJson(
            UnityWebRequest.kHttpVerbPOST,
            "/api/auth/login",
            body,
            null,
            json => completed(JsonUtility.FromJson<AuthSessionDto>(json)),
            failed);
    }

    public IEnumerator Register(
        string handle,
        string password,
        Action<AuthSessionDto> completed,
        Action<string> failed)
    {
        var body = JsonUtility.ToJson(new LoginRequestDto
        {
            handle = handle,
            password = password
        });

        yield return SendJson(
            UnityWebRequest.kHttpVerbPOST,
            "/api/auth/register",
            body,
            null,
            json => completed(JsonUtility.FromJson<AuthSessionDto>(json)),
            failed);
    }

    public IEnumerator Get(
        string path,
        string token,
        Action<string> completed,
        Action<string> failed)
    {
        yield return SendJson(
            UnityWebRequest.kHttpVerbGET,
            path,
            null,
            token,
            completed,
            failed);
    }

    public IEnumerator Post(
        string path,
        string json,
        string token,
        Action<string> completed,
        Action<string> failed)
    {
        yield return SendJson(
            UnityWebRequest.kHttpVerbPOST,
            path,
            json,
            token,
            completed,
            failed);
    }

    private IEnumerator SendJson(
        string method,
        string path,
        string json,
        string token,
        Action<string> completed,
        Action<string> failed)
    {
        using (var request = new UnityWebRequest(settings.BaseUrl + path, method))
        {
            if (json != null)
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));

            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Accept", "application/json");
            if (json != null)
                request.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrWhiteSpace(token))
                request.SetRequestHeader("Authorization", $"Bearer {token}");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
                completed(request.downloadHandler.text);
            else
                failed($"{request.responseCode}: {request.downloadHandler.text}");
        }
    }
}
```

Trust the local development certificate on the development machine. Never ship a certificate handler that accepts every certificate.

## 6. Keep session state in memory

```csharp
using UnityEngine;

public sealed class TcgClientSession : MonoBehaviour
{
    public static TcgClientSession Instance { get; private set; }

    public string Token { get; private set; }
    public PublicUserDto User { get; private set; }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void SetSession(AuthSessionDto session)
    {
        Token = session.token;
        User = session.user;
    }

    public void ClearSession()
    {
        Token = null;
        User = null;
    }
}
```

Do not store the bearer token in `PlayerPrefs`. If persistent sessions are added later, use the operating system's protected credential storage and a server-side token-expiry or refresh design.

## 7. Login from a scene

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class LoginController : MonoBehaviour
{
    [SerializeField] private TcgRestClient client;
    [SerializeField] private TMP_InputField handle;
    [SerializeField] private TMP_InputField password;
    [SerializeField] private TMP_Text status;

    public void SignIn()
    {
        status.text = "Connecting...";
        StartCoroutine(client.Login(
            handle.text.Trim(),
            password.text,
            session =>
            {
                TcgClientSession.Instance.SetSession(session);
                SceneManager.LoadScene("Lobby");
            },
            error => status.text = error));
    }
}
```

Disable the sign-in button while the request is active to prevent duplicate requests.

## 8. Join matchmaking

```csharp
var joinRequest = new JoinMatchmakingRequestDto
{
    queueId = "ranked",
    region = "global",
    partySize = 1
};

StartCoroutine(restClient.Post(
    "/api/matchmaking/join",
    JsonUtility.ToJson(joinRequest),
    TcgClientSession.Instance.Token,
    responseJson => Debug.Log(responseJson),
    error => Debug.LogError(error)));
```

Poll `GET /api/matchmaking/me` at a measured interval while queued. Stop polling when the player leaves the lobby, is matched, signs out, or the request returns an authentication error.

The default server queues are `ranked` and `casual`, but clients should populate queue choices from `GET /api/matchmaking/queues` instead of hard-coding the list.

## 9. Create the secure packet helper

```csharp
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

public readonly struct SecurePacket
{
    public SecurePacket(string payload, string packetId, long sentAt, string signature)
    {
        Payload = payload;
        PacketId = packetId;
        SentAt = sentAt;
        Signature = signature;
    }

    public string Payload { get; }
    public string PacketId { get; }
    public long SentAt { get; }
    public string Signature { get; }
}

public static class SecurePacketFactory
{
    public static SecurePacket Create(string token, string scope, string plainText)
    {
        var source = Encoding.UTF8.GetBytes(plainText);
        byte[] compressed;

        using (var output = new MemoryStream())
        {
            using (var gzip = new GZipStream(
                output,
                CompressionLevel.Optimal,
                leaveOpen: true))
            {
                gzip.Write(source, 0, source.Length);
            }

            compressed = output.ToArray();
        }

        var payload = Convert.ToBase64String(compressed);
        var packetId = Guid.NewGuid().ToString("N");
        var sentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var signedText = $"{scope}.{packetId}.{sentAt}.{payload}";

        byte[] signatureBytes;
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(token)))
            signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedText));

        return new SecurePacket(
            payload,
            packetId,
            sentAt,
            Convert.ToBase64String(signatureBytes));
    }
}
```

Generate a new secure packet for every action. The server rejects repeated packet IDs, timestamps outside the 30-second window, incorrect scopes, invalid signatures, oversized payloads, and rate-limit violations.

## 10. Add the SignalR game connection

SignalR callbacks may run away from Unity's main thread. Queue scene and UI work for `Update`.

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using UnityEngine;

public sealed class TcgGameConnection : MonoBehaviour
{
    [SerializeField] private TcgConnectionSettings settings;

    private readonly ConcurrentQueue<Action> mainThread = new();
    private HubConnection connection;
    private string token;
    private string gameId;

    public event Action<GameStateDto> GameStateChanged;
    public event Action<string> ChatReceived;
    public event Action<string> AnnouncementReceived;
    public event Action<string> ConnectionStateChanged;

    private void Update()
    {
        while (mainThread.TryDequeue(out var action))
            action();
    }

    public async Task ConnectAsync(string sessionToken, string targetGameId)
    {
        token = sessionToken;
        gameId = targetGameId;

        connection = new HubConnectionBuilder()
            .WithUrl(settings.GameHubUrl)
            .WithAutomaticReconnect()
            .Build();

        connection.On<GameStateDto>("gameState", state =>
            mainThread.Enqueue(() => GameStateChanged?.Invoke(state)));
        connection.On<string>("chatMessage", message =>
            mainThread.Enqueue(() => ChatReceived?.Invoke(message)));
        connection.On<string>("serverAnnouncement", message =>
            mainThread.Enqueue(() => AnnouncementReceived?.Invoke(message)));

        connection.Reconnecting += _ =>
        {
            mainThread.Enqueue(() => ConnectionStateChanged?.Invoke("Reconnecting"));
            return Task.CompletedTask;
        };

        connection.Reconnected += async _ =>
        {
            await connection.InvokeAsync("JoinGame", token, gameId);
            mainThread.Enqueue(() => ConnectionStateChanged?.Invoke("Connected"));
        };

        connection.Closed += _ =>
        {
            mainThread.Enqueue(() => ConnectionStateChanged?.Invoke("Disconnected"));
            return Task.CompletedTask;
        };

        await connection.StartAsync();
        await connection.InvokeAsync("JoinGame", token, gameId);
        mainThread.Enqueue(() => ConnectionStateChanged?.Invoke("Connected"));
    }

    public async Task SendChatAsync(string message)
    {
        var packet = SecurePacketFactory.Create(token, $"chat:{gameId}", message);
        await connection.InvokeAsync(
            "ChatMessage",
            token,
            gameId,
            packet.Payload,
            packet.PacketId,
            packet.SentAt,
            packet.Signature);
    }

    public async Task PlayCardAsync(string cardInstanceId)
    {
        var packet = SecurePacketFactory.Create(token, $"play:{gameId}", cardInstanceId);
        await connection.InvokeAsync(
            "PlayCard",
            token,
            gameId,
            packet.Payload,
            packet.PacketId,
            packet.SentAt,
            packet.Signature);
    }

    private async void OnDestroy()
    {
        if (connection != null)
            await connection.DisposeAsync();
    }
}
```

`WithAutomaticReconnect` handles dropped established connections. Add a separate retry loop around the initial `StartAsync` call if the client should wait through server startup or a temporary offline state.

## 11. Bind the game scene

At scene start:

1. Load the selected `gameId` from the lobby flow.
2. Subscribe the board presenter to `GameStateChanged`.
3. Subscribe chat UI to `ChatReceived`.
4. Subscribe a global notification banner to `AnnouncementReceived`.
5. Call `ConnectAsync` with the in-memory token and game ID.
6. Disable card input unless the connection is active and the server state says it is the local player's turn.
7. Send only card-instance IDs owned by the local player.
8. Render the state returned by the server instead of predicting authoritative outcomes.

## 12. Add workflow connections when needed

Use a second `HubConnection` for `settings.StateMachineHubUrl`. Subscribe to `stateMachineState`, call `Join(token, instanceId)`, and sign transitions with:

```text
transition:{instanceId}:{eventName}
```

The compressed payload is the transition event JSON. Rejoin the instance after reconnecting.

## 13. Error and sign-out behavior

- `401`: clear the local session and return to the login scene.
- `403`: show that the account lacks permission; do not retry continuously.
- `404`: refresh the lobby or game list because the selected entity no longer exists.
- `400`: display a user-safe validation message and log the detailed response for development.
- Network failure: show offline/reconnecting state and disable state-changing controls.
- Sign out: stop polling, dispose SignalR connections, clear the in-memory token, and load the login scene.

## 14. Platform and build checklist

- Test managed SignalR dependencies in Editor and in every player target.
- Preserve required SignalR and JSON types for IL2CPP/AOT when linker stripping removes dynamically accessed types.
- Use HTTPS/WSS endpoints in every non-local environment.
- Never accept all TLS certificates in a release build.
- Keep tokens out of logs, analytics, crash messages, `PlayerPrefs`, and scene assets.
- Keep server and client clocks synchronized.
- Rejoin SignalR groups after reconnecting.
- Treat all server state as authoritative.
- Cap chat input before sending; the server decompressed-payload limit is 2 KB.
- Generate a new packet ID and timestamp for every action.

## 15. End-to-end test

1. Start the WinUI server console.
2. Run the Unity login scene and register two test users.
3. Confirm the WinUI live player count increases after authenticated requests.
4. Join the `ranked` or `casual` queue from both clients.
5. Use **Matchmaking control** to inspect or run the queue if needed.
6. Open the resulting game from both clients.
7. Verify each client receives `gameState`.
8. Send chat and confirm it appears in both Unity clients and **Live chat center**.
9. Play a card and confirm both clients receive the updated authoritative state.
10. Broadcast a server announcement and confirm Unity displays it.
11. Temporarily disconnect one client, reconnect it, and confirm it rejoins the game group.
