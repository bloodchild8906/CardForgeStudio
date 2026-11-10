using System;

namespace TcgServer.Client
{
    [Serializable]
    public sealed class HealthDto
    {
        public string service;
        public string status;
        public string version;
        public string serverTime;
    }

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
    public sealed class PresenceCountDto
    {
        public int onlinePlayers;
    }

    [Serializable]
    public sealed class RankingDto
    {
        public int rank;
        public PublicUserDto user;
    }

    [Serializable]
    public sealed class MatchmakingQueueConfigurationDto
    {
        public string id;
        public string name;
        public string mode;
        public int minPlayers;
        public int maxPlayers;
        public int initialRatingRange;
        public int ratingExpansionPerSecond;
        public int maxWaitSeconds;
        public bool botBackfill;
        public bool enabled;
        public string updatedAt;
    }

    [Serializable]
    public sealed class MatchmakingQueueStatusDto
    {
        public MatchmakingQueueConfigurationDto queue;
        public int waitingPlayers;
        public int waitingParties;
        public int longestWaitSeconds;
    }

    [Serializable]
    public sealed class JoinMatchmakingRequestDto
    {
        public string queueId;
        public string region = "global";
        public int partySize = 1;
    }

    [Serializable]
    public sealed class MatchmakingQueueEntryDto
    {
        public string id;
        public string queueId;
        public string userId;
        public int rating;
        public int partySize;
        public string region;
        public string joinedAt;
    }

    [Serializable]
    public sealed class CreateGameRequestDto
    {
        public string rulePackId = "mtg-like";
        public string[] playerIds = Array.Empty<string>();
        public bool addAi = true;
    }

    [Serializable]
    public sealed class GameEventDto
    {
        public string type;
        public string message;
        public string actorId;
        public string at;
    }

    [Serializable]
    public sealed class GameStateDto
    {
        public string id;
        public string rulePackId;
        public string[] players = Array.Empty<string>();
        public string activePlayerId;
        public int turn;
        public bool finished;
        public GameEventDto[] events = Array.Empty<GameEventDto>();
    }

    [Serializable]
    public sealed class CardDefinitionDto
    {
        public string id;
        public string name;
        public string type;
        public int cost;
        public int power;
        public int toughness;
        public string text;
        public string assetId;
    }

    [Serializable]
    public sealed class RulePackDto
    {
        public string id;
        public string name;
        public string version;
        public string description;
        public CardDefinitionDto[] cards = Array.Empty<CardDefinitionDto>();
    }

    [Serializable]
    public sealed class CardInstanceDto
    {
        public string instanceId;
        public string cardId;
        public string ownerId;
    }

    [Serializable]
    public sealed class InventoryDto
    {
        public CardInstanceDto[] cards = Array.Empty<CardInstanceDto>();
        public string[] achievements = Array.Empty<string>();
    }

    [Serializable]
    public sealed class DeckDto
    {
        public string id;
        public string ownerId;
        public string name;
        public string rulePackId;
        public string[] cardIds = Array.Empty<string>();
        public string updatedAt;
        public bool suspended;
    }

    [Serializable]
    public sealed class SaveDeckRequestDto
    {
        public string id;
        public string name;
        public string rulePackId;
        public string[] cardIds = Array.Empty<string>();
    }

    [Serializable]
    public sealed class FriendConnectionDto
    {
        public string id;
        public string userId;
        public string friendUserId;
        public string status;
        public string createdAt;
    }

    [Serializable]
    public sealed class CreateFriendRequestDto
    {
        public string friendUserId;
    }

    [Serializable]
    public sealed class DirectMessageDto
    {
        public string id;
        public string senderId;
        public string recipientId;
        public string body;
        public string sentAt;
        public bool flagged;
        public bool removed;
    }

    [Serializable]
    public sealed class SendMessageRequestDto
    {
        public string recipientId;
        public string body;
    }

    [Serializable]
    public sealed class AssetDescriptorDto
    {
        public string id;
        public string name;
        public string contentType;
        public long length;
        public string sha256;
        public string url;
    }

    public readonly struct TcgApiError
    {
        public TcgApiError(long statusCode, string message)
        {
            StatusCode = statusCode;
            Message = message;
        }

        public long StatusCode { get; }
        public string Message { get; }
        public override string ToString() => StatusCode > 0 ? $"{StatusCode}: {Message}" : Message;
    }

    public static class JsonArrayUtility
    {
        [Serializable]
        private sealed class Wrapper<T>
        {
            public T[] items = Array.Empty<T>();
        }

        public static T[] FromJson<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<T>();

            var wrapper = UnityEngine.JsonUtility.FromJson<Wrapper<T>>($"{{\"items\":{json}}}");
            return wrapper?.items ?? Array.Empty<T>();
        }
    }
}
