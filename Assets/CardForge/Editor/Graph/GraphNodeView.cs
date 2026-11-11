using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.Editor.Graph
{
    /// <summary>Whether a node would accept the connection currently being dragged.</summary>
    internal enum GraphDropState
    {
        None,
        Accepts,
        Rejects
    }

    /// <summary>Visual for one graph node: text, badges and the two connection ports.</summary>
    internal sealed class GraphNodeView : VisualElement
    {
        public const float DefaultWidth = 220f;
        public const float DefaultHeight = 112f;
        private const int MaximumVisibleBadges = 4;

        private readonly VisualElement outputPort = new VisualElement();

        public GraphNodeView(GraphNodeData data)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("cf-graph-node");
            AddToClassList("cf-graph-node--" + data.Tone.ToString().ToLowerInvariant());
            Add(Text("cf-graph-node__kicker", (data.Kicker ?? string.Empty).ToUpperInvariant()));
            Add(Text("cf-graph-node__title", string.IsNullOrEmpty(data.Title) ? "Untitled" : data.Title));
            if (!string.IsNullOrEmpty(data.Detail)) Add(Text("cf-graph-node__detail", data.Detail));
            AddBadges(data.Badges);
            if (!string.IsNullOrEmpty(data.Footer)) Add(Text("cf-graph-node__footer", data.Footer));
            Add(Port("cf-graph-port--in", new VisualElement()));
            Add(Port("cf-graph-port--out", outputPort));
        }

        public float MeasuredWidth => IsMeasured(layout.width) ? layout.width : DefaultWidth;

        public float MeasuredHeight => IsMeasured(layout.height) ? layout.height : DefaultHeight;

        public void SetSelected(bool selected) => EnableInClassList("cf-graph-node--selected", selected);

        public void SetOutputPortHot(bool hot) => outputPort.EnableInClassList("cf-graph-port--hot", hot);

        public void SetDropState(GraphDropState state)
        {
            EnableInClassList("cf-graph-node--accepts", state == GraphDropState.Accepts);
            EnableInClassList("cf-graph-node--rejects", state == GraphDropState.Rejects);
        }

        private static bool IsMeasured(float value) => !float.IsNaN(value) && value >= 1f;

        private static Label Text(string className, string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }

        private static VisualElement Port(string className, VisualElement port)
        {
            port.pickingMode = PickingMode.Ignore;
            port.AddToClassList("cf-graph-port");
            port.AddToClassList(className);
            return port;
        }

        private void AddBadges(IReadOnlyList<string> badges)
        {
            if (badges == null || badges.Count == 0) return;
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("cf-graph-node__badges");
            for (int index = 0; index < badges.Count && index < MaximumVisibleBadges; index++) row.Add(Text("cf-graph-badge", badges[index]));
            if (badges.Count > MaximumVisibleBadges) row.Add(Text("cf-graph-badge", "+" + (badges.Count - MaximumVisibleBadges) + " more"));
            Add(row);
        }
    }
}
