using System.Security.Cryptography;
using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

public sealed class AuthService(AppState state)
{
    private const int MinimumPasswordLength = 8;
    private const int MaximumPasswordLength = 256;
    private readonly object registrationGate = new();

    public AuthResponse Register(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Handle) || string.IsNullOrWhiteSpace(request.Password) ||
            request.Handle.Trim().Length is < 3 or > 24 ||
            request.Password.Length is < MinimumPasswordLength or > MaximumPasswordLength)
            throw new InvalidOperationException($"Handles must be 3-24 characters and passwords must be {MinimumPasswordLength}-{MaximumPasswordLength} characters.");

        var normalized = request.Handle.Trim().ToLowerInvariant();
        lock (registrationGate)
        {
            if (state.Users.Values.Any(u => u.Handle.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Handle already exists.");

            var handle = request.Handle.Trim();
            var role = ConfiguredAdminHandles().Contains(handle, StringComparer.OrdinalIgnoreCase) ? "admin" : "user";
            var user = new User(Guid.NewGuid().ToString("N"), handle, Hash(request.Password), Role: role);
            state.Users[user.Id] = user;
            state.Inventories[user.Id] = new Inventory([], []);
            state.Wallets[user.Id] = CreateWallet();
            return CreateSession(user);
        }
    }

    public AuthResponse Login(LoginRequest request)
    {
        var user = state.Users.Values.FirstOrDefault(u => u.Handle.Equals(request.Handle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user is null || user.Disabled || !Verify(request.Password, user.PasswordHash)) throw new UnauthorizedAccessException("Invalid credentials.");
        return CreateSession(user);
    }

    public AuthResponse LocalManagementSession()
    {
        const string localManagementId = "local-management";
        var user = state.Users.AddOrUpdate(
            localManagementId,
            _ => new User(localManagementId, "local-operator", string.Empty, Role: "admin"),
            (_, existing) => existing with { Handle = "local-operator", Role = "admin", Disabled = false });
        state.Inventories.TryAdd(user.Id, new Inventory([], []));
        state.Wallets.TryAdd(user.Id, CreateWallet());
        return CreateSession(user);
    }

    public User? FromToken(string? token)
    {
        if (token is null || !state.Sessions.TryGetValue(token, out var id) || !state.SessionActivity.TryGetValue(token, out var lastSeen) ||
            DateTimeOffset.UtcNow - lastSeen > TimeSpan.FromMinutes(state.Settings.SessionTimeoutMinutes) ||
            !state.Users.TryGetValue(id, out var user) || user.Disabled)
        {
            if (token is not null)
                Revoke(token);
            return null;
        }

        state.SessionActivity[token] = DateTimeOffset.UtcNow;
        return user;
    }

    public bool Revoke(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        state.SessionActivity.TryRemove(token, out _);
        return state.Sessions.TryRemove(token, out _);
    }

    public PresenceSnapshot Presence()
    {
        var now = DateTimeOffset.UtcNow;
        var activeSince = now.AddMinutes(-2);
        var staleBefore = now.AddMinutes(-state.Settings.SessionTimeoutMinutes);
        foreach (var stale in state.SessionActivity.Where(pair => pair.Value < staleBefore).ToList())
        {
            state.SessionActivity.TryRemove(stale.Key, out _);
            state.Sessions.TryRemove(stale.Key, out _);
        }
        var players = state.SessionActivity
            .Where(pair => pair.Value >= activeSince && state.Sessions.ContainsKey(pair.Key))
            .Select(pair => (Token: pair.Key, LastSeen: pair.Value, UserId: state.Sessions[pair.Key]))
            .Where(session => !string.Equals(session.UserId, "local-management", StringComparison.Ordinal))
            .GroupBy(session => session.UserId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(session => session.LastSeen).First())
            .Where(session => state.Users.ContainsKey(session.UserId) && !state.Users[session.UserId].Disabled)
            .Select(session =>
            {
                var player = state.Users[session.UserId];
                return new PlayerPresence(player.Id, player.Handle, player.Rating, session.LastSeen);
            })
            .OrderBy(player => player.Handle)
            .ToList();
        return new PresenceSnapshot(players.Count, players, now);
    }

    private AuthResponse CreateSession(User user)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        state.Sessions[token] = user.Id;
        state.SessionActivity[token] = DateTimeOffset.UtcNow;
        return new(token, new(user.Id, user.Handle, user.Rating, user.Role, user.Disabled));
    }
    private static HashSet<string> ConfiguredAdminHandles() =>
        (Environment.GetEnvironmentVariable("TCG_ADMIN_HANDLES") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(handle => handle.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    private PlayerWallet CreateWallet() => new(new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
    {
        [state.Economy.SoftCurrency] = 0,
        [state.Economy.PremiumCurrency] = 0
    });
    private static string Hash(string password) { var salt = RandomNumberGenerator.GetBytes(16); var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 120_000, HashAlgorithmName.SHA256, 32); return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(key)}"; }
    private static bool Verify(string password, string encoded)
    {
        try
        {
            var parts = encoded.Split(':');
            if (parts.Length != 2) return false;
            var key = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[0]), 120_000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(key, Convert.FromBase64String(parts[1]));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
