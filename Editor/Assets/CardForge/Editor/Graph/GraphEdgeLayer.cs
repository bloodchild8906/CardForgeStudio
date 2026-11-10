using System.Collections.Generic;
using CardForge.DesignLogic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.Editor.Graph
{
    /// <summary>Gives the edge layer the current rectangle of any node, in world space.</summary>
    internal interface IGraphGeometry
    {
        bool TryGetNodeRect(string nodeId, out Rect rect);
    }

    /// <summary>Draws the curved connections, their labels and the connection being dragged.</summary>
    internal sealed class GraphEdgeLayer : VisualElement
    {
        private const float LineWidth = 2f;
        private const float SelectedLineWidth = 3.5f;
        private const float ArrowLength = 11f;
        private const float ArrowHalfWidth = 6f;

        private static readonly CustomStyleProperty<Color> EdgeColorProperty = new CustomStyleProperty<Color>("--cf-graph-edge");
        private static readonly CustomStyleProperty<Color> ActiveColorProperty = new CustomStyleProperty<Color>("--cf-graph-edge-active");
        private static readonly CustomStyleProperty<Color> SelectedColorProperty = new CustomStyleProperty<Color>("--cf-graph-edge-selected");

        private readonly IGraphGeometry geometry;
        private readonly List<Label> labels = new List<Label>();
        private IReadOnlyList<GraphLinkData> links = new List<GraphLinkData>();
        private Vector2? previewStart;
        private Vector2? previewEnd;
        private Color edgeColor = new Color(0.31f, 0.55f, 0.54f);
        private Color activeColor = new Color(0.39f, 0.78f, 0.73f);
        private Color selectedColor = new Color(0.83f, 0.70f, 0.43f);

        public GraphEdgeLayer(IGraphGeometry geometry)
        {
            this.geometry = geometry;
            pickingMode = PickingMode.Ignore;
            AddToClassList("cf-graph__edges");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        public string HighlightedNodeId { get; set; }

        public int SelectedLinkIndex { get; set; } = -1;

        public void SetLinks(IReadOnlyList<GraphLinkData> newLinks)
        {
            links = newLinks ?? new List<GraphLinkData>();
            foreach (var label in labels) label.RemoveFromHierarchy();
            labels.Clear();
            foreach (var link in links)
            {
                var label = new Label(link.Label ?? string.Empty) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("cf-graph-edge-label");
                label.style.display = string.IsNullOrEmpty(link.Label) ? DisplayStyle.None : DisplayStyle.Flex;
                labels.Add(label);
                Add(label);
            }
            Refresh();
        }

        public void SetPreview(Vector2? start, Vector2? end)
        {
            previewStart = start;
            previewEnd = end;
            MarkDirtyRepaint();
        }

        public void Refresh()
        {
            for (int index = 0; index < links.Count; index++)
            {
                if (!TryGetAnchors(links[index], out Vector2 start, out Vector2 end)) continue;
                GraphEdgeGeometry.PointAt(start.x, start.y, end.x, end.y, 0.5f, out float x, out float y);
                labels[index].style.left = x;
                labels[index].style.top = y;
                labels[index].EnableInClassList("cf-graph-edge-label--selected", index == SelectedLinkIndex);
                labels[index].EnableInClassList("cf-graph-edge-label--active", IsTouchingHighlightedNode(links[index]));
            }
            MarkDirtyRepaint();
        }

        public int HitTest(Vector2 worldPoint, float tolerance)
        {
            int nearestIndex = -1;
            float nearestDistance = tolerance;
            for (int index = 0; index < links.Count; index++)
            {
                if (!TryGetAnchors(links[index], out Vector2 start, out Vector2 end)) continue;
                float distance = GraphEdgeGeometry.DistanceToEdge(start.x, start.y, end.x, end.y, worldPoint.x, worldPoint.y);
                if (distance > nearestDistance) continue;
                nearestDistance = distance;
                nearestIndex = index;
            }
            return nearestIndex;
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.customStyle.TryGetValue(EdgeColorProperty, out Color edge)) edgeColor = edge;
            if (evt.customStyle.TryGetValue(ActiveColorProperty, out Color active)) activeColor = active;
            if (evt.customStyle.TryGetValue(SelectedColorProperty, out Color selected)) selectedColor = selected;
            MarkDirtyRepaint();
        }

        private bool TryGetAnchors(GraphLinkData link, out Vector2 start, out Vector2 end)
        {
            start = default;
            end = default;
            if (!geometry.TryGetNodeRect(link.FromId, out Rect fromRect) || !geometry.TryGetNodeRect(link.ToId, out Rect toRect)) return false;
            start = new Vector2(fromRect.xMax, fromRect.center.y);
            end = new Vector2(toRect.xMin, toRect.center.y);
            return true;
        }

        private bool IsTouchingHighlightedNode(GraphLinkData link)
        {
            return HighlightedNodeId != null && (link.FromId == HighlightedNodeId || link.ToId == HighlightedNodeId);
        }

        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            for (int index = 0; index < links.Count; index++)
            {
                if (!TryGetAnchors(links[index], out Vector2 start, out Vector2 end)) continue;
                bool selected = index == SelectedLinkIndex;
                Color color = selected ? selectedColor : IsTouchingHighlightedNode(links[index]) ? activeColor : edgeColor;
                DrawCurve(painter, start, end, color, selected ? SelectedLineWidth : LineWidth, true);
            }
            if (previewStart.HasValue && previewEnd.HasValue) DrawCurve(painter, previewStart.Value, previewEnd.Value, activeColor, LineWidth, false);
        }

        private static void DrawCurve(Painter2D painter, Vector2 start, Vector2 end, Color color, float width, bool arrow)
        {
            float bend = GraphEdgeGeometry.Bend(start.x, end.x);
            painter.lineWidth = width;
            painter.strokeColor = color;
            painter.BeginPath();
            painter.MoveTo(start);
            painter.BezierCurveTo(start + Vector2.right * bend, end - Vector2.right * bend, end);
            painter.Stroke();
            if (!arrow) return;
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(end);
            painter.LineTo(end + new Vector2(-ArrowLength, -ArrowHalfWidth));
            painter.LineTo(end + new Vector2(-ArrowLength, ArrowHalfWidth));
            painter.ClosePath();
            painter.Fill();
        }
    }
}
