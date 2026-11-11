using System;
using System.Collections.Generic;

namespace CardForge.Simulation
{
    [Serializable]
    public sealed class HostCreateRequest
    {
        public string rulePackId;
        public string[] humans = Array.Empty<string>();
        public int opponents = 1;
        public bool fillWithBots = true;
        public int difficulty = 2;
        public int seed;
        public bool useSeed;
        public HostDeck[] decks = Array.Empty<HostDeck>();
    }
    [Serializable]
    public sealed class HostDeck
    {
        public string playerId;
        public string[] cardIds = Array.Empty<string>();
    }
    [Serializable]
    public sealed class HostActionRequest
    {
        public string actorId;
        public SimActionRequest action;
    }
    [Serializable]
    public sealed class HostError
    {
        public string error;
        public HostError(string message) { error = message; }
    }
    [Serializable]
    public sealed class HostMatches
    {
        public List<SimMatch> matches = new List<SimMatch>();
    }
}
