using System.Collections.Generic;
using System.Linq;

namespace CardForge.DesignLogic
{
    /// <summary>Rules for which connections a user may draw between graph nodes.</summary>
    public static class GraphConnectionRules
    {
        /// <summary>True when the connection joins two different nodes and does not already exist.</summary>
        public static bool CanConnect(string fromId, string toId, IEnumerable<KeyValuePair<string, string>> existingLinks)
        {
            if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId) || fromId == toId) return false;
            return !existingLinks.Any(link => link.Key == fromId && link.Value == toId);
        }
    }
}
