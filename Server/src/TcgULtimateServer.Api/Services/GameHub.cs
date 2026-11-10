using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using TcgULtimateServer.Api.Models;
namespace TcgULtimateServer.Api.Services;
public sealed class GameHub(AppState state, AuthService auth, RulesEngine rules, BotService bots, SecurePacketService packets) : Hub
{
    public async Task JoinGame(string token, string gameId)
    {
        var user = auth.FromToken(token) ?? throw new HubException("Unauthorized");
        if (!state.Games.TryGetValue(gameId, out var game) || !game.Players.Contains(user.Id, StringComparer.Ordinal))
            throw new HubException("Game access denied.");
        await Groups.AddToGroupAsync(Context.ConnectionId, gameId);
        await Clients.Caller.SendAsync("gameState", game);
    }

    public async Task ChatMessage(string token, string gameId, string compressedMessage, string packetId, long sentAtUnixMs, string signature)
    {
        var user = auth.FromToken(token) ?? throw new HubException("Unauthorized");
        var message = packets.Validate(user, token, $"chat:{gameId}", packetId, sentAtUnixMs, compressedMessage, signature);
        if (!state.Games.TryGetValue(gameId, out var game) || !game.Players.Contains(user.Id, StringComparer.Ordinal))
            throw new HubException("Game access denied.");
        var item = $"{user.Handle}: {message}";
        var channel = state.Chat.GetOrAdd(gameId, _ => []);
        lock (channel) channel.Add(item);
        await Clients.Group(gameId).SendAsync("chatMessage", item);
    }
    public async Task PlayCard(string token, string gameId, string compressedCardInstanceId, string packetId, long sentAtUnixMs, string signature) { var user = auth.FromToken(token) ?? throw new HubException("Unauthorized"); var cardInstanceId = packets.Validate(user, token, $"play:{gameId}", packetId, sentAtUnixMs, compressedCardInstanceId, signature, 256); if (!state.Games.TryGetValue(gameId, out var game) || !game.Players.Contains(user.Id)) throw new HubException("Game access denied."); rules.Play(game, user.Id, cardInstanceId); if (game.ActivePlayerId.StartsWith("bot-")) bots.TakeTurn(game, game.ActivePlayerId); await Clients.Group(gameId).SendAsync("gameState", game); }
    public async Task ExecuteAction(string token, string gameId, string compressedAction, string packetId, long sentAtUnixMs, string signature)
    {
        var user = auth.FromToken(token) ?? throw new HubException("Unauthorized");
        var json = packets.Validate(user, token, $"action:{gameId}", packetId, sentAtUnixMs, compressedAction, signature, 16 * 1024);
        var action = JsonSerializer.Deserialize<ExecuteGameActionRequest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new HubException("Invalid action payload.");
        if (!state.Games.TryGetValue(gameId, out var game) || !game.Players.Contains(user.Id)) throw new HubException("Game access denied.");
        try { rules.Execute(game, user.Id, action); }
        catch (InvalidOperationException exception) { throw new HubException(exception.Message); }
        if (game.ActivePlayerId.StartsWith("bot-", StringComparison.Ordinal)) bots.TakeTurn(game, game.ActivePlayerId);
        await Clients.Group(gameId).SendAsync("gameState", game);
    }
}
