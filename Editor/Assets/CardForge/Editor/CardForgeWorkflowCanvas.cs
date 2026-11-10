using System;
using System.Collections.Generic;
using System.Linq;
using CardForge.Editor.Graph;
using UnityEngine.UIElements;

namespace CardForge.Editor
{
    /// <summary>Turn flow view: phases as nodes, connections between them, built on <see cref="GraphCanvasView"/>.</summary>
    public sealed class CardForgeWorkflowCanvas : VisualElement
    {
        private readonly GraphCanvasView graph = new GraphCanvasView { AddNodeLabel = "+ Phase" };
        private TurnFlowDocument flow;
        private Action<TurnFlowNode> selectedCallback;
        private Action<TurnFlowNode> movedCallback;

        /// <summary>Creates the canvas and subscribes to the shared graph events.</summary>
        public CardForgeWorkflowCanvas()
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

        /// <summary>Resolves an action id to a display name.</summary>
        public Func<string, string> ActionLabel;

        /// <summary>Builds the badge text for one scheduled action.</summary>
        public Func<PhaseActionRule, string> ActionRuleLabel;

        /// <summary>Raised when the user drags a connection from one phase to another.</summary>
        public event Action<string, string> ConnectRequested;

        /// <summary>Raised when the user deletes a connection.</summary>
        public event Action<string, string> DisconnectRequested;

        /// <summary>Raised when the user asks for a new phase at a world position.</summary>
        public event Action<float, float> AddRequested;

        /// <summary>Scales and centres the view so every phase is visible.</summary>
        public void FitToContent() => graph.FitToContent();

        /// <summary>Draws a flow. Pan and zoom survive repeated calls and editor redraws.</summary>
        public void Draw(TurnFlowDocument document, string selectedId, Action<TurnFlowNode> select, Action<TurnFlowNode> moved)
        {
            flow = document;
            selectedCallback = select;
            movedCallback = moved;
            graph.ViewKey = document == null ? null : "flow:" + document.id;
            var nodes = document == null ? new List<GraphNodeData>() : document.nodes.Select(ToNodeData).ToList();
            var links = document == null ? new List<GraphLinkData>() : document.links.Select(ToLinkData).ToList();
            graph.SetContent(nodes, links, selectedId);
        }

        private GraphNodeData ToNodeData(TurnFlowNode node)
        {
            return new GraphNodeData
            {
                Id = node.id,
                X = node.x,
                Y = node.y,
                Kicker = node.phase,
                Title = node.name,
                Detail = node.check,
                Footer = node.optional ? "OPTIONAL" : node.resolution,
                Tone = GraphNodeTone.Phase,
                Badges = (node.actionRules ?? new List<PhaseActionRule>()).Select(BadgeText).ToList()
            };
        }

        private string BadgeText(PhaseActionRule schedule)
        {
            if (ActionRuleLabel != null) return ActionRuleLabel(schedule);
            return ActionLabel == null ? schedule.actionId : ActionLabel(schedule.actionId);
        }

        private static GraphLinkData ToLinkData(TurnFlowLink link) => new GraphLinkData { FromId = link.from, ToId = link.to, Label = link.label };

        private void OnNodeSelected(string nodeId)
        {
            var node = flow?.nodes.FirstOrDefault(candidate => candidate.id == nodeId);
            if (node != null) selectedCallback?.Invoke(node);
        }

        private void OnNodesMoved(IReadOnlyList<GraphNodeMove> moves)
        {
            if (flow == null) return;
            TurnFlowNode last = null;
            foreach (var move in moves)
            {
                var node = flow.nodes.FirstOrDefault(candidate => candidate.id == move.Id);
                if (node == null) continue;
                node.x = move.X;
                node.y = move.Y;
                last = node;
            }
            if (last != null) movedCallback?.Invoke(last);
        }
    }
}
