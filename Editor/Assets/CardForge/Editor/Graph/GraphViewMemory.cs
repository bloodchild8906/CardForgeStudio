using System.Collections.Generic;

namespace CardForge.Editor.Graph
{
    /// <summary>Remembers pan and zoom per graph so a redraw of the editor keeps the user's place.</summary>
    internal static class GraphViewMemory
    {
        private static readonly Dictionary<string, float[]> StatesByKey = new Dictionary<string, float[]>();

        public static void Save(string key, float zoom, float panX, float panY)
        {
            if (!string.IsNullOrEmpty(key)) StatesByKey[key] = new[] { zoom, panX, panY };
        }

        public static bool TryLoad(string key, out float zoom, out float panX, out float panY)
        {
            zoom = 1f;
            panX = 0f;
            panY = 0f;
            if (string.IsNullOrEmpty(key) || !StatesByKey.TryGetValue(key, out var state)) return false;
            zoom = state[0];
            panX = state[1];
            panY = state[2];
            return true;
        }
    }
}
