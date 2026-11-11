using System;
using System.Collections.Generic;
using System.Linq;
using CardForge.DesignLogic;

namespace CardForge.Editor
{
    /// <summary>Graph editing gestures (connect, disconnect, add) for turn flows, actions and abilities.</summary>
    public sealed partial class CardForgeDesignerView
    {
        string _graphNodeId;

        void WireFlowCanvas(CardForgeWorkflowCanvas canvas)
        {
            canvas.ConnectRequested += ConnectFlowNodes;
            canvas.DisconnectRequested += DisconnectFlowNodes;
            canvas.AddRequested += AddFlowNodeAt;
        }

        void ConnectFlowNodes(string fromId, string toId)
        {
            if (Flow == null || !GraphConnectionRules.CanConnect(fromId, toId, Flow.links.Select(link => new KeyValuePair<string, string>(link.from, link.to)))) return;
            Flow.links.Add(new TurnFlowLink { from = fromId, to = toId });
            _flowNodeId = fromId;
            SaveFlow();
            Render();
        }

        void DisconnectFlowNodes(string fromId, string toId)
        {
            if (Flow == null || Flow.links.RemoveAll(link => link.from == fromId && link.to == toId) == 0) return;
            SaveFlow();
            Render();
        }

        void AddFlowNodeAt(float x, float y)
        {
            if (Flow == null) return;
            var node = new TurnFlowNode { name = "New phase", phase = "Main", x = x, y = y };
            Flow.nodes.Add(node);
            _flowNodeId = node.id;
            SaveFlow();
            Render();
        }

        void ShowResolutionGraph(CardForgeResolutionGraphCanvas canvas, List<ResolutionGraphNode> nodes,
            List<ResolutionGraphLink> links, Action save, Action refreshInspector)
        {
            canvas.ConnectRequested += (fromId, toId) =>
            {
                if (!GraphConnectionRules.CanConnect(fromId, toId, links.Select(link => new KeyValuePair<string, string>(link.from, link.to)))) return;
                links.Add(new ResolutionGraphLink { from = fromId, to = toId });
                _graphNodeId = fromId;
                save();
                Render();
            };
            canvas.DisconnectRequested += (fromId, toId) =>
            {
                if (links.RemoveAll(link => link.from == fromId && link.to == toId) == 0) return;
                save();
                Render();
            };
            canvas.AddRequested += (x, y) =>
            {
                var step = new ResolutionGraphNode { title = "New rule step", x = x, y = y };
                nodes.Add(step);
                _graphNodeId = step.id;
                save();
                Render();
            };
            canvas.Draw(nodes, links, _graphNodeId, step =>
            {
                _graphNodeId = step.id;
                refreshInspector();
            }, _ => save());
        }
    }
}
