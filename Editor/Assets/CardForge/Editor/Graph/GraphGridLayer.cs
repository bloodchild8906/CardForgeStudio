using CardForge.DesignLogic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.Editor.Graph
{
    /// <summary>Background grid that scrolls and scales with the viewport.</summary>
    internal sealed class GraphGridLayer : VisualElement
    {
        private const float MinorSpacing = 24f;
        private const int MajorEvery = 5;
        private const float SmallestVisibleSpacing = 8f;

        private static readonly CustomStyleProperty<Color> MinorColorProperty = new CustomStyleProperty<Color>("--cf-graph-grid");
        private static readonly CustomStyleProperty<Color> MajorColorProperty = new CustomStyleProperty<Color>("--cf-graph-grid-major");

        private readonly GraphViewTransform viewTransform;
        private Color minorColor = new Color(0.47f, 0.63f, 0.75f, 0.07f);
        private Color majorColor = new Color(0.47f, 0.63f, 0.75f, 0.13f);

        public GraphGridLayer(GraphViewTransform viewTransform)
        {
            this.viewTransform = viewTransform;
            pickingMode = PickingMode.Ignore;
            AddToClassList("cf-graph__grid");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.customStyle.TryGetValue(MinorColorProperty, out Color minor)) minorColor = minor;
            if (evt.customStyle.TryGetValue(MajorColorProperty, out Color major)) majorColor = major;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            float width = contentRect.width;
            float height = contentRect.height;
            if (width < 1f || height < 1f) return;
            var painter = context.painter2D;
            painter.lineWidth = 1f;
            bool showMinor = MinorSpacing * viewTransform.Zoom >= SmallestVisibleSpacing;
            if (showMinor) DrawLines(painter, minorColor, width, height, false);
            DrawLines(painter, majorColor, width, height, true);
        }

        private void DrawLines(Painter2D painter, Color color, float width, float height, bool majorOnly)
        {
            painter.strokeColor = color;
            int firstColumn = Mathf.FloorToInt(viewTransform.ToWorldX(0f) / MinorSpacing);
            int lastColumn = Mathf.CeilToInt(viewTransform.ToWorldX(width) / MinorSpacing);
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                if ((column % MajorEvery == 0) != majorOnly) continue;
                float x = Mathf.Round(viewTransform.ToScreenX(column * MinorSpacing)) + 0.5f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, height));
                painter.Stroke();
            }
            int firstRow = Mathf.FloorToInt(viewTransform.ToWorldY(0f) / MinorSpacing);
            int lastRow = Mathf.CeilToInt(viewTransform.ToWorldY(height) / MinorSpacing);
            for (int row = firstRow; row <= lastRow; row++)
            {
                if ((row % MajorEvery == 0) != majorOnly) continue;
                float y = Mathf.Round(viewTransform.ToScreenY(row * MinorSpacing)) + 0.5f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(0f, y));
                painter.LineTo(new Vector2(width, y));
                painter.Stroke();
            }
        }
    }
}
