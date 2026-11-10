using Microsoft.AspNetCore.SignalR;

namespace TcgULtimateServer.Api.Services;

public sealed class StateMachineHub(AuthService auth, AdminService admin, StateMachineService machines, SecurePacketService packets) : Hub
{
    public async Task Join(string token, string instanceId)
    {
        var user = auth.FromToken(token) ?? throw new HubException("Unauthorized");
        try
        {
            var instance = machines.GetInstance(instanceId);
            if (!machines.CanAccess(instance, user.Id, admin.IsAdmin(user)))
                throw new HubException("Workflow access denied.");
            await Groups.AddToGroupAsync(Context.ConnectionId, instanceId);
            await Clients.Caller.SendAsync("stateMachineState", instance);
        }
        catch (KeyNotFoundException exception)
        {
            throw new HubException(exception.Message);
        }
    }

    public async Task Transition(string token, string instanceId, string eventName, string compressedData, string packetId, long sentAtUnixMs, string signature)
    {
        var user = auth.FromToken(token) ?? throw new HubException("Unauthorized");
        try
        {
            var instance = machines.GetInstance(instanceId);
            if (!machines.CanAccess(instance, user.Id, admin.IsAdmin(user)))
                throw new HubException("Workflow access denied.");
            var data = packets.Validate(user, token, $"transition:{instanceId}:{eventName}", packetId, sentAtUnixMs, compressedData, signature);
            var request = new Models.TransitionStateMachineRequest(
                eventName,
                System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(data));
            var updatedInstance = machines.Transition(instanceId, user.Id, request);
            await Clients.Group(instanceId).SendAsync("stateMachineState", updatedInstance);
        }
        catch (KeyNotFoundException exception)
        {
            throw new HubException(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw new HubException(exception.Message);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new HubException("Transition data must be valid JSON.");
        }
    }
}
