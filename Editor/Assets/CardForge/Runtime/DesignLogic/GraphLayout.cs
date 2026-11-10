using System;
using System.Collections.Generic;
using System.Linq;

namespace CardForge.DesignLogic
{
    /// <summary>Spacing and wrapping rules for <see cref="GraphLayout.Arrange"/>.</summary>
    public sealed class GraphLayoutSettings
    {
        /// <summary>Width of every node.</summary>
        public float NodeWidth { get; set; } = 220f;

        /// <summary>Height of every node.</summary>
        public float NodeHeight { get; set; } = 112f;

        /// <summary>Space between columns.</summary>
        public float HorizontalGap { get; set; } = 80f;

        /// <summary>Space between nodes in one column.</summary>
        public float VerticalGap { get; set; } = 48f;

        /// <summary>Left edge of the arranged graph.</summary>
        public float OriginX { get; set; } = 40f;

        /// <summary>Top edge of the arranged graph.</summary>
        public float OriginY { get; set; } = 40f;

        /// <summary>Columns per row before the layout wraps to a new row. Zero or less disables wrapping.</summary>
        public int MaximumColumns { get; set; } = 5;
    }

    /// <summary>Where one node should be placed.</summary>
    public readonly struct GraphPlacement
    {
        /// <summary>Creates a placement.</summary>
        public GraphPlacement(string id, float x, float y)
        {
            Id = id;
            X = x;
            Y = y;
        }

        /// <summary>Identifier of the node.</summary>
        public string Id { get; }

        /// <summary>Left edge.</summary>
        public float X { get; }

        /// <summary>Top edge.</summary>
        public float Y { get; }
    }

    /// <summary>Layered left-to-right layout for directed graphs. Cycles are tolerated.</summary>
    public static class GraphLayout
    {
        /// <summary>Places every node. The result has one placement per input id, in the same order.</summary>
        public static IReadOnlyList<GraphPlacement> Arrange(IReadOnlyList<string> nodeIds,
            IReadOnlyList<KeyValuePair<string, string>> links, GraphLayoutSettings settings = null)
        {
            settings = settings ?? new GraphLayoutSettings();
            var forwardEdges = BuildForwardEdges(nodeIds, links);
            int[] layerOfNode = AssignLayers(forwardEdges);
            var nodesByLayer = OrderLayers(layerOfNode, forwardEdges);
            return Place(nodeIds, nodesByLayer, settings);
        }

        private static List<int>[] BuildForwardEdges(IReadOnlyList<string> nodeIds, IReadOnlyList<KeyValuePair<string, string>> links)
        {
            var indexById = new Dictionary<string, int>();
            for (int index = 0; index < nodeIds.Count; index++)
            {
                if (nodeIds[index] != null && !indexById.ContainsKey(nodeIds[index])) indexById[nodeIds[index]] = index;
            }
            var edges = Enumerable.Range(0, nodeIds.Count).Select(_ => new List<int>()).ToArray();
            var seen = new HashSet<long>();
            foreach (var link in links)
            {
                if (link.Key == null || link.Value == null) continue;
                if (!indexById.TryGetValue(link.Key, out int from) || !indexById.TryGetValue(link.Value, out int to)) continue;
                if (from != to && seen.Add((long)from * nodeIds.Count + to)) edges[from].Add(to);
            }
            return RemoveBackEdges(edges);
        }

        private static List<int>[] RemoveBackEdges(List<int>[] edges)
        {
            int count = edges.Length;
            var incomingCount = new int[count];
            foreach (var targets in edges) foreach (int target in targets) incomingCount[target]++;
            var state = new int[count];
            var kept = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();

            void Visit(int node)
            {
                state[node] = 1;
                foreach (int target in edges[node])
                {
                    if (state[target] == 1) continue;
                    kept[node].Add(target);
                    if (state[target] == 0) Visit(target);
                }
                state[node] = 2;
            }

            for (int node = 0; node < count; node++) if (incomingCount[node] == 0 && state[node] == 0) Visit(node);
            for (int node = 0; node < count; node++) if (state[node] == 0) Visit(node);
            return kept;
        }

        private static int[] AssignLayers(List<int>[] edges)
        {
            int count = edges.Length;
            var layers = new int[count];
            var incomingCount = new int[count];
            foreach (var targets in edges) foreach (int target in targets) incomingCount[target]++;
            var ready = new Queue<int>(Enumerable.Range(0, count).Where(node => incomingCount[node] == 0));
            while (ready.Count > 0)
            {
                int node = ready.Dequeue();
                foreach (int target in edges[node])
                {
                    layers[target] = Math.Max(layers[target], layers[node] + 1);
                    if (--incomingCount[target] == 0) ready.Enqueue(target);
                }
            }
            return layers;
        }

        private static List<List<int>> OrderLayers(int[] layerOfNode, List<int>[] edges)
        {
            int layerCount = layerOfNode.Length == 0 ? 0 : layerOfNode.Max() + 1;
            var nodesByLayer = Enumerable.Range(0, layerCount).Select(_ => new List<int>()).ToList();
            for (int node = 0; node < layerOfNode.Length; node++) nodesByLayer[layerOfNode[node]].Add(node);

            var predecessors = Enumerable.Range(0, layerOfNode.Length).Select(_ => new List<int>()).ToArray();
            for (int node = 0; node < edges.Length; node++) foreach (int target in edges[node]) predecessors[target].Add(node);

            var position = new float[layerOfNode.Length];
            foreach (var layer in nodesByLayer) for (int slot = 0; slot < layer.Count; slot++) position[layer[slot]] = slot;
            for (int layerIndex = 1; layerIndex < nodesByLayer.Count; layerIndex++)
            {
                var layer = nodesByLayer[layerIndex];
                var ordered = layer.OrderBy(node => AveragePosition(predecessors[node], position, position[node])).ThenBy(node => node).ToList();
                nodesByLayer[layerIndex] = ordered;
                for (int slot = 0; slot < ordered.Count; slot++) position[ordered[slot]] = slot;
            }
            return nodesByLayer;
        }

        private static float AveragePosition(List<int> neighbours, float[] position, float fallback)
        {
            return neighbours.Count == 0 ? fallback : neighbours.Average(neighbour => position[neighbour]);
        }

        private static IReadOnlyList<GraphPlacement> Place(IReadOnlyList<string> nodeIds,
            List<List<int>> nodesByLayer, GraphLayoutSettings settings)
        {
            int columns = settings.MaximumColumns <= 0 ? int.MaxValue : settings.MaximumColumns;
            float rowPitch = settings.NodeHeight + settings.VerticalGap;
            int bandCount = nodesByLayer.Count == 0 ? 0 : (nodesByLayer.Count - 1) / columns + 1;
            var bandTop = new float[bandCount];
            var bandRows = new int[bandCount];
            for (int layer = 0; layer < nodesByLayer.Count; layer++)
            {
                int band = layer / columns;
                bandRows[band] = Math.Max(bandRows[band], nodesByLayer[layer].Count);
            }
            float top = settings.OriginY;
            for (int band = 0; band < bandCount; band++)
            {
                bandTop[band] = top;
                top += bandRows[band] * rowPitch + settings.VerticalGap;
            }

            var x = new float[nodeIds.Count];
            var y = new float[nodeIds.Count];
            for (int layer = 0; layer < nodesByLayer.Count; layer++)
            {
                int band = layer / columns;
                float centring = (bandRows[band] - nodesByLayer[layer].Count) * rowPitch / 2f;
                for (int slot = 0; slot < nodesByLayer[layer].Count; slot++)
                {
                    int node = nodesByLayer[layer][slot];
                    x[node] = settings.OriginX + (layer % columns) * (settings.NodeWidth + settings.HorizontalGap);
                    y[node] = bandTop[band] + centring + slot * rowPitch;
                }
            }
            return Enumerable.Range(0, nodeIds.Count).Select(node => new GraphPlacement(nodeIds[node], x[node], y[node])).ToList();
        }
    }
}
