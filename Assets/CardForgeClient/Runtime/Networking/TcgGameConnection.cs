using System;
using System.Threading.Tasks;
using UnityEngine;

#if TCG_SIGNALR
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
#endif

namespace TcgServer.Client
{
    [DisallowMultipleComponent]
    public sealed class TcgGameConnection : MonoBehaviour
    {
#if TCG_SIGNALR
        private readonly ConcurrentQueue<Action> mainThread = new();
        private HubConnection connection;
        private string token;
        private string gameId;
#endif

        public static bool IsAvailable
        {
            get
            {
#if TCG_SIGNALR
                return true;
#else
                return false;
#endif
            }
        }

        public event Action<GameStateDto> GameStateChanged;
        public event Action<string> ChatReceived;
        public event Action<string> AnnouncementReceived;
        public event Action<string> ConnectionStateChanged;

#if TCG_SIGNALR
        private void Update()
        {
            while (mainThread.TryDequeue(out var action))
                action();
        }

        public async Task ConnectAsync(TcgConnectionSettings settings, string sessionToken, string targetGameId)
        {
            await DisconnectAsync();
            token = sessionToken;
            gameId = targetGameId;

            connection = new HubConnectionBuilder()
                .WithUrl(settings.GameHubUrl)
                .WithAutomaticReconnect()
                .Build();

            connection.On<JsonElement>("gameState", element =>
            {
                var state = JsonUtility.FromJson<GameStateDto>(element.GetRawText());
                mainThread.Enqueue(() => GameStateChanged?.Invoke(state));
            });
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
            EnsureConnected();
            var packet = SecurePacketFactory.Create(token, $"chat:{gameId}", message);
            await connection.InvokeAsync(
                "ChatMessage",
                token,
                gameId,
                packet.Payload,
                packet.PacketId,
                packet.SentAtUnixMs,
                packet.Signature);
        }

        public async Task PlayCardAsync(string cardInstanceId)
        {
            EnsureConnected();
            var packet = SecurePacketFactory.Create(token, $"play:{gameId}", cardInstanceId);
            await connection.InvokeAsync(
                "PlayCard",
                token,
                gameId,
                packet.Payload,
                packet.PacketId,
                packet.SentAtUnixMs,
                packet.Signature);
        }

        public async Task DisconnectAsync()
        {
            if (connection == null)
                return;

            await connection.DisposeAsync();
            connection = null;
        }

        private void EnsureConnected()
        {
            if (connection == null || connection.State != HubConnectionState.Connected)
                throw new InvalidOperationException("The game hub is not connected.");
        }

        private async void OnDestroy() => await DisconnectAsync();
#else
        public Task ConnectAsync(TcgConnectionSettings settings, string sessionToken, string targetGameId) =>
            Task.FromException(new InvalidOperationException("Import Microsoft.AspNetCore.SignalR.Client and add the TCG_SIGNALR scripting define first."));

        public Task SendChatAsync(string message) =>
            Task.FromException(new InvalidOperationException("SignalR support is not enabled."));

        public Task PlayCardAsync(string cardInstanceId) =>
            Task.FromException(new InvalidOperationException("SignalR support is not enabled."));

        public Task DisconnectAsync() => Task.CompletedTask;
#endif
    }
}

