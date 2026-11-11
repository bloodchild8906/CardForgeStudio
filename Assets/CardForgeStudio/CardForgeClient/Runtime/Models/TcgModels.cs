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
        public string deckId;
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
    public sealed class MatchmakingResultDto
    {
        public string gameId;
    }

    [Serializable]
    public sealed class CreateGameRequestDto
    {
        public string rulePackId = "mtg-like";
        public string[] playerIds = Array.Empty<string>();
        public bool addAi = true;
        public string deckId;
        public int teamSize = 1; // The number of opposing seats, 1â€“4.
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
        public ClientGameDataDto[] data = Array.Empty<ClientGameDataDto>();
    }

    [Serializable]
    public sealed class ClientGameZoneDto
    {
        public string playerId;
        public string zoneId;
        public CardInstanceDto[] cards = Array.Empty<CardInstanceDto>();
    }

    [Serializable]
    public sealed class ClientGameResourceDto
    {
        public string playerId;
        public string resourceId;
        public long value;
    }

    [Serializable]
    public sealed class ClientGameActionDto
    {
        public string id;
        public string name;
        public string sourceZone;
        public string destinationZone;
        public bool requiresActivePlayer;
        public string target;
    }

    [Serializable]
    public sealed class ClientGameViewDto
    {
        public string id;
        public string rulePackId;
        public string[] players = Array.Empty<string>();
        public string activePlayerId;
        public string currentPhaseId;
        public int turn;
        public bool finished;
        public string winnerPlayerId;
        public string victoryRuleId;
        public ClientGameZoneDto[] zones = Array.Empty<ClientGameZoneDto>();
        public ClientGameResourceDto[] resources = Array.Empty<ClientGameResourceDto>();
        public ClientGameActionDto[] allowedActions = Array.Empty<ClientGameActionDto>();
        public GameEventDto[] events = Array.Empty<GameEventDto>();
        public ClientGameDataDto[] data = Array.Empty<ClientGameDataDto>();
    }

    [Serializable]
    public sealed class ClientGameDataDto
    {
        public string key, value;
    }

    [Serializable]
    public sealed class ExecuteGameActionRequestDto
    {
        public string actionId;
        public string sourceInstanceId;
        public string targetPlayerId;
        public string targetInstanceId;
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
    public sealed class RuleZoneDto
    {
        public string id;
        public string name;
        public int capacity;
        public string ownerScope;
        public string[] allowedCardTypes = Array.Empty<string>();
        public string[] initialCardIds = Array.Empty<string>();
        public string deckStartCardType;
        public bool returnOnLeave;
        public bool immovable;
    }

    [Serializable]
    public sealed class RuleStarterDeckDto
    {
        public string id;
        public string name;
        public string strategy;
        public string[] cardIds = Array.Empty<string>();
    }

    [Serializable]
    public sealed class DeckTypeLimitDto
    {
        public string cardType;
        public int minCount;
        public int maxCount = -1;
    }

    [Serializable]
    public sealed class DeckCardLimitDto
    {
        public string cardId;
        public int minCopies;
        public int maxCopies = -1;
    }

    [Serializable]
    public sealed class DeckRulesDto
    {
        public int minCards = 1;
        public int maxCards = 120;
        public int maxCopiesPerCard = 4;
        public string[] allowedCardTypes = Array.Empty<string>();
        public DeckTypeLimitDto[] typeLimits = Array.Empty<DeckTypeLimitDto>();
        public DeckCardLimitDto[] cardLimits = Array.Empty<DeckCardLimitDto>();
    }

    [Serializable]
    public sealed class RulePackDto
    {
        public string id;
        public string name;
        public string version;
        public string description;
        public CardDefinitionDto[] cards = Array.Empty<CardDefinitionDto>();
        public RuleZoneDto[] zones = Array.Empty<RuleZoneDto>();
        public RuleStarterDeckDto[] starterDecks = Array.Empty<RuleStarterDeckDto>();
        public DeckRulesDto deckRules = new DeckRulesDto();
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
    public sealed class WalletDto
    {
        // Unity JsonUtility cannot deserialize Dictionary; the server supplies a flat list.
        public WalletBalanceDto[] balances = Array.Empty<WalletBalanceDto>();
    }
    [Serializable]
    public sealed class WalletBalanceDto
    {
        public string currency;
        public long amount;
    }
    [Serializable]
    public sealed class StorePurchaseRequestDto
    {
        public string productId;
    }
    [Serializable]
    public sealed class StorePurchaseReceiptDto
    {
        public string productId;
        public string currency;
        public long balance;
        public CardInstanceDto[] cards = Array.Empty<CardInstanceDto>();
        public string assetId;
    }

    [Serializable]
    public sealed class StoreProductDto
    {
        public string id;
        public string name;
        public string description;
        public decimal price;
        public string currency;
        public string assetId;
        public string contentPackId;
        public bool active;
    }

    [Serializable]
    public sealed class PublicEventDto
    {
        public string id;
        public string name;
        public string kind;
        public string description;
        public string startsAt;
        public string endsAt;
        public bool active;
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
