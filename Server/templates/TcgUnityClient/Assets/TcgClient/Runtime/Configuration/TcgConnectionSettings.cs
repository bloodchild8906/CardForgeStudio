using UnityEngine;

namespace TcgServer.Client
{
    [CreateAssetMenu(menuName = "TcgStudio/Connection Settings", fileName = "TcgConnectionSettings")]
    public sealed class TcgConnectionSettings : ScriptableObject
    {
        [SerializeField] private string baseUrl = "https://localhost:5001";
        [SerializeField, Min(0.25f)] private float gamePollIntervalSeconds = 1f;
        [SerializeField, Min(0.5f)] private float lobbyRefreshIntervalSeconds = 5f;

        public string BaseUrl => baseUrl.TrimEnd('/');
        public string GameHubUrl => $"{BaseUrl}/hubs/game";
        public string StateMachineHubUrl => $"{BaseUrl}/hubs/state-machine";
        public float GamePollIntervalSeconds => gamePollIntervalSeconds;
        public float LobbyRefreshIntervalSeconds => lobbyRefreshIntervalSeconds;
    }
}

