using UnityEngine;

namespace TcgServer.Client
{
    [CreateAssetMenu(menuName = "TcgStudio/Connection Settings", fileName = "TcgConnectionSettings")]
    public sealed class TcgConnectionSettings : ScriptableObject
    {
        [SerializeField] private string baseUrl = "https://localhost:5001";
        [SerializeField] private string defaultRulePackId = "mtg-like";
        [SerializeField] private string activePackageId = "";
        [SerializeField] private string activePackageVersion = "";
        [SerializeField, Min(0.25f)] private float gamePollIntervalSeconds = 1f;
        [SerializeField, Min(0.5f)] private float lobbyRefreshIntervalSeconds = 5f;

        public string BaseUrl => baseUrl.TrimEnd('/');
        public string DefaultRulePackId => string.IsNullOrWhiteSpace(defaultRulePackId) ? "mtg-like" : defaultRulePackId.Trim();
        public string ActivePackageId => activePackageId ?? string.Empty;
        public string ActivePackageVersion => activePackageVersion ?? string.Empty;
        public string GameHubUrl => $"{BaseUrl}/hubs/game";
        public string StateMachineHubUrl => $"{BaseUrl}/hubs/state-machine";
        public float GamePollIntervalSeconds => gamePollIntervalSeconds;
        public float LobbyRefreshIntervalSeconds => lobbyRefreshIntervalSeconds;
    }
}
