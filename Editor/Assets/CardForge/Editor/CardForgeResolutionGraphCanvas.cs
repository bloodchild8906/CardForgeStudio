using System;
using System.Collections.Generic;
using System.Linq;
using CardForge.Editor.Graph;
using UnityEngine.UIElements;

namespace CardForge.Editor
{
    /// <summary>Action and ability resolution graph, built on <see cref="GraphCanvasView"/>.</summary>
    public sealed class CardForgeResolutionGraphCanvas : VisualElement
    {
        private const string StateChangeBadge = "State change";
        private const string EmitsTriggersBadge = "Emits triggers";

        private readonly GraphCanvasView graph = new GraphCanvasView { AddNodeLabel = "+ Step" };
        private List<ResolutionGraphNode> nodes = new List<ResolutionGraphNode>();
        private Action<ResolutionGraphNode> selectedCallback;
        private Action<ResolutionGraphNode> movedCallback;

        /// <summary>Creates the canvas and subscribes to the shared graph events.</summary>
        public CardForgeResolutionGraphCanvas()
        {
            AddToClassList("workflow-canvas");
            Add(graph);
            graph.NodeSelected += OnNodeSelected;
            graph.LinkSelected += (fromId, _) => OnNodeSelected(fromId);
            graph.NodesMoved += OnNodesMoved;
            graph.ConnectRequested += (fromId, toId) => ConnectRequested?.Invoke(fromId, toId);
            graph.DisconnectRequested += (fromId, toId) => DisconnectRequested?.Invoke(fromId, toId);
            graph.AddNodeRequested += (x, y) => AddRequested?.Invoke(x, y);
        }

        /// <summary>Key under which pan and zoom are remembered between redraws.</summary>
        public string ViewKey
        {
            set => graph.ViewKey = value;
        }

        /// <summary>Raised when the user drags a connection from one step to another.</summary>
        public event Action<string, string> ConnectRequested;

        /// <summary>Raised when the user deletes a connection.</summary>
        public event Action<string, string> DisconnectRequested;

        /// <summary>Raised when the user asks for a new step at a world position.</summary>
        public event Action<float, float> AddRequested;

        /// <summary>Draws a graph. Pan and zoom survive repeated calls and editor redraws.</summary>
        public void Draw(List<ResolutionGraphNode> graphNodes, List<ResolutionGraphLink> graphLinks,
            string selectedId, Action<ResolutionGraphNode> onSelected, Action<ResolutionGraphNode> onMoved)
        {
            nodes = graphNodes ?? new List<ResolutionGraphNode>();
            selectedCallback = onSelected;
            movedCallback = onMoved;
            var links = (graphLinks ?? new List<ResolutionGraphLink>()).Select(ToLinkData).ToList();
            graph.SetContent(nodes.Select(ToNodeData).ToList(), links, selectedId);
        }

        private static GraphNodeData ToNodeData(ResolutionGraphNode node)
        {
            return new GraphNodeData
            {
                Id = node.id,
                X = node.x,
                Y = node.y,
                Kicker = node.tone ?? "process",
                Title = node.title,
                Detail = node.detail,
                Tone = ToneOf(node.tone),
                Badges = BadgesOf(node)
            };
        }

        private static GraphNodeTone ToneOf(string tone)
        {
            if (tone == "trigger") return GraphNodeTone.Trigger;
            return tone == "effect" ? GraphNodeTone.Effect : GraphNodeTone.Process;
        }

        private static IReadOnlyList<string> BadgesOf(ResolutionGraphNode node)
        {
            var badges = new List<string>();
            if (node.causesStateChange) badges.Add(StateChangeBadge);
            if (node.emitsTriggers) badges.Add(EmitsTriggersBadge);
            return badges;
        }

        private static GraphLinkData ToLinkData(ResolutionGraphLink link) => new GraphLinkData { FromId = link.from, ToId = link.to, Label = link.label };

        private void OnNodeSelected(string nodeId)
        {
            var node = nodes.FirstOrDefault(candidate => candidate.id == nodeId);
            if (node != null) selectedCallback?.Invoke(node);
        }

        private void OnNodesMoved(IReadOnlyList<GraphNodeMove> moves)
        {
            ResolutionGraphNode last = null;
            foreach (var move in moves)
            {
                var node = nodes.FirstOrDefault(candidate => candidate.id == move.Id);
                if (node == null) continue;
                node.x = move.X;
                node.y = move.Y;
                last = node;
            }
            if (last != null) movedCallback?.Invoke(last);
        }
    }
}
