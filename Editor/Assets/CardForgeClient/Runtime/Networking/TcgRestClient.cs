using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace TcgServer.Client
{
    [DisallowMultipleComponent]
    public sealed class TcgRestClient : MonoBehaviour
    {
        [SerializeField] private TcgConnectionSettings settings;

        public TcgConnectionSettings Settings => settings;

        public void Configure(TcgConnectionSettings value) => settings = value;

        public IEnumerator Health(Action<HealthDto> completed, Action<TcgApiError> failed) =>
            GetJson("/api/health", null, json => completed(JsonUtility.FromJson<HealthDto>(json)), failed);

        public IEnumerator Presence(Action<PresenceCountDto> completed, Action<TcgApiError> failed) =>
            GetJson("/api/presence/count", null, json => completed(JsonUtility.FromJson<PresenceCountDto>(json)), failed);

        public IEnumerator Register(string handle, string password, Action<AuthSessionDto> completed, Action<TcgApiError> failed) =>
            Authenticate("/api/auth/register", handle, password, completed, failed);

        public IEnumerator Login(string handle, string password, Action<AuthSessionDto> completed, Action<TcgApiError> failed) =>
            Authenticate("/api/auth/login", handle, password, completed, failed);

        public IEnumerator Logout(string token, Action completed, Action<TcgApiError> failed) =>
            SendJson(UnityWebRequest.kHttpVerbDELETE, "/api/auth/session", null, token, _ => completed(), failed);

        public IEnumerator Rankings(Action<RankingDto[]> completed, Action<TcgApiError> failed) =>
            GetJson("/api/rankings", null, json => completed(JsonArrayUtility.FromJson<RankingDto>(json)), failed);

        public IEnumerator MatchmakingQueues(Action<MatchmakingQueueStatusDto[]> completed, Action<TcgApiError> failed) =>
            GetJson("/api/matchmaking/queues", null, json => completed(JsonArrayUtility.FromJson<MatchmakingQueueStatusDto>(json)), failed);

        public IEnumerator JoinMatchmaking(string token, string queueId, Action<MatchmakingQueueEntryDto> completed, Action<TcgApiError> failed)
        {
            var request = new JoinMatchmakingRequestDto { queueId = queueId };
            return SendJson(
                UnityWebRequest.kHttpVerbPOST,
                "/api/matchmaking/join",
                JsonUtility.ToJson(request),
                token,
                json => completed(JsonUtility.FromJson<MatchmakingQueueEntryDto>(json)),
                failed);
        }

        public IEnumerator MyMatchmaking(string token, Action<MatchmakingQueueEntryDto[]> completed, Action<TcgApiError> failed) =>
            GetJson("/api/matchmaking/me", token, json => completed(JsonArrayUtility.FromJson<MatchmakingQueueEntryDto>(json)), failed);

        public IEnumerator LeaveMatchmaking(string token, Action completed, Action<TcgApiError> failed) =>
            SendJson(UnityWebRequest.kHttpVerbDELETE, "/api/matchmaking/leave", null, token, _ => completed(), failed);

        public IEnumerator CreateAiGame(string token, string userId, Action<GameStateDto> completed, Action<TcgApiError> failed)
        {
            var request = new CreateGameRequestDto
            {
                playerIds = new[] { userId },
                addAi = true
            };
            return SendJson(
                UnityWebRequest.kHttpVerbPOST,
                "/api/games",
                JsonUtility.ToJson(request),
                token,
                json => completed(JsonUtility.FromJson<GameStateDto>(json)),
                failed);
        }

        public IEnumerator Game(string token, string gameId, Action<GameStateDto> completed, Action<TcgApiError> failed) =>
            GetJson($"/api/games/{UnityWebRequest.EscapeURL(gameId)}", token, json => completed(JsonUtility.FromJson<GameStateDto>(json)), failed);

        public IEnumerator Games(string token, Action<GameStateDto[]> completed, Action<TcgApiError> failed) =>
            GetJson("/api/games", token, json => completed(JsonArrayUtility.FromJson<GameStateDto>(json)), failed);

        public IEnumerator Rules(Action<RulePackDto[]> completed, Action<TcgApiError> failed) =>
            GetJson("/api/rules", null, json => completed(JsonArrayUtility.FromJson<RulePackDto>(json)), failed);

        public IEnumerator AssetManifest(Action<AssetDescriptorDto[]> completed, Action<TcgApiError> failed) =>
            GetJson("/api/assets/manifest", null, json => completed(JsonArrayUtility.FromJson<AssetDescriptorDto>(json)), failed);

        public IEnumerator Inventory(string token, string userId, Action<InventoryDto> completed, Action<TcgApiError> failed) =>
            GetJson($"/api/inventory/{UnityWebRequest.EscapeURL(userId)}", token, json => completed(JsonUtility.FromJson<InventoryDto>(json)), failed);

        public IEnumerator Decks(string token, Action<DeckDto[]> completed, Action<TcgApiError> failed) =>
            GetJson("/api/decks", token, json => completed(JsonArrayUtility.FromJson<DeckDto>(json)), failed);

        public IEnumerator SaveDeck(string token, SaveDeckRequestDto deck, Action<DeckDto> completed, Action<TcgApiError> failed) =>
            SendJson(UnityWebRequest.kHttpVerbPOST, "/api/decks", JsonUtility.ToJson(deck), token, json => completed(JsonUtility.FromJson<DeckDto>(json)), failed);

        public IEnumerator DeleteDeck(string token, string deckId, Action completed, Action<TcgApiError> failed) =>
            SendJson(UnityWebRequest.kHttpVerbDELETE, $"/api/decks/{UnityWebRequest.EscapeURL(deckId)}", null, token, _ => completed(), failed);

        public IEnumerator Friends(string token, Action<FriendConnectionDto[]> completed, Action<TcgApiError> failed) =>
            GetJson("/api/friends", token, json => completed(JsonArrayUtility.FromJson<FriendConnectionDto>(json)), failed);

        public IEnumerator AddFriend(string token, string friendUserId, Action<FriendConnectionDto> completed, Action<TcgApiError> failed) =>
            SendJson(UnityWebRequest.kHttpVerbPOST, "/api/friends", JsonUtility.ToJson(new CreateFriendRequestDto { friendUserId = friendUserId }), token, json => completed(JsonUtility.FromJson<FriendConnectionDto>(json)), failed);

        public IEnumerator RemoveFriend(string token, string connectionId, Action completed, Action<TcgApiError> failed) =>
            SendJson(UnityWebRequest.kHttpVerbDELETE, $"/api/friends/{UnityWebRequest.EscapeURL(connectionId)}", null, token, _ => completed(), failed);

        public IEnumerator Messages(string token, Action<DirectMessageDto[]> completed, Action<TcgApiError> failed) =>
            GetJson("/api/messages", token, json => completed(JsonArrayUtility.FromJson<DirectMessageDto>(json)), failed);

        public IEnumerator SendMessage(string token, string recipientId, string body, Action<DirectMessageDto> completed, Action<TcgApiError> failed) =>
            SendJson(
                UnityWebRequest.kHttpVerbPOST,
                "/api/messages",
                JsonUtility.ToJson(new SendMessageRequestDto { recipientId = recipientId, body = body }),
                token,
                json => completed(JsonUtility.FromJson<DirectMessageDto>(json)),
                failed);

        private IEnumerator Authenticate(string path, string handle, string password, Action<AuthSessionDto> completed, Action<TcgApiError> failed)
        {
            var request = new LoginRequestDto { handle = handle, password = password };
            return SendJson(
                UnityWebRequest.kHttpVerbPOST,
                path,
                JsonUtility.ToJson(request),
                null,
                json => completed(JsonUtility.FromJson<AuthSessionDto>(json)),
                failed);
        }

        private IEnumerator GetJson(string path, string token, Action<string> completed, Action<TcgApiError> failed) =>
            SendJson(UnityWebRequest.kHttpVerbGET, path, null, token, completed, failed);

        private IEnumerator SendJson(
            string method,
            string path,
            string json,
            string token,
            Action<string> completed,
            Action<TcgApiError> failed)
        {
            if (settings == null)
            {
                failed(new TcgApiError(0, "Connection settings are not assigned."));
                yield break;
            }

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

                var responseText = request.downloadHandler?.text ?? string.Empty;
                var success = request.result == UnityWebRequest.Result.Success && request.responseCode is >= 200 and < 300;
                if (success)
                    completed(responseText);
                else
                    failed(new TcgApiError(request.responseCode, string.IsNullOrWhiteSpace(responseText) ? request.error : responseText));
            }
        }
    }
}
