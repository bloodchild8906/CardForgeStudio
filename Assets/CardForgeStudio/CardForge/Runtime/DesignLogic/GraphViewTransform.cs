using System;

namespace CardForge.DesignLogic
{
    /// <summary>Pan and zoom state of a graph viewport, with the conversions between screen and world space.</summary>
    public sealed class GraphViewTransform
    {
        /// <summary>Smallest allowed zoom factor.</summary>
        public const float MinimumZoom = 0.35f;

        /// <summary>Largest allowed zoom factor.</summary>
        public const float MaximumZoom = 1.75f;

        /// <summary>Current zoom factor; 1 shows the graph at its natural size.</summary>
        public float Zoom { get; private set; } = 1f;

        /// <summary>Horizontal screen offset of the world origin.</summary>
        public float PanX { get; private set; }

        /// <summary>Vertical screen offset of the world origin.</summary>
        public float PanY { get; private set; }

        /// <summary>Replaces the whole view state, clamping the zoom.</summary>
        public void Set(float zoom, float panX, float panY)
        {
            Zoom = Clamp(zoom);
            PanX = panX;
            PanY = panY;
        }

        /// <summary>Moves the view by a screen-space distance.</summary>
        public void PanBy(float deltaX, float deltaY)
        {
            PanX += deltaX;
            PanY += deltaY;
        }

        /// <summary>Multiplies the zoom while keeping the world point under the anchor fixed on screen.</summary>
        public void ZoomAt(float factor, float anchorX, float anchorY)
        {
            ZoomTo(Zoom * factor, anchorX, anchorY);
        }

        /// <summary>Sets the zoom while keeping the world point under the anchor fixed on screen.</summary>
        public void ZoomTo(float zoom, float anchorX, float anchorY)
        {
            float worldX = ToWorldX(anchorX);
            float worldY = ToWorldY(anchorY);
            Zoom = Clamp(zoom);
            PanX = anchorX - worldX * Zoom;
            PanY = anchorY - worldY * Zoom;
        }

        /// <summary>Scales and centres a world rectangle inside the view. Never enlarges beyond natural size.</summary>
        public void FitTo(float contentLeft, float contentTop, float contentWidth, float contentHeight,
            float viewWidth, float viewHeight, float padding)
        {
            float safeWidth = Math.Max(contentWidth, 1f);
            float safeHeight = Math.Max(contentHeight, 1f);
            float availableWidth = Math.Max(viewWidth - padding * 2f, 1f);
            float availableHeight = Math.Max(viewHeight - padding * 2f, 1f);
            Zoom = Clamp(Math.Min(1f, Math.Min(availableWidth / safeWidth, availableHeight / safeHeight)));
            PanX = (viewWidth - safeWidth * Zoom) / 2f - contentLeft * Zoom;
            PanY = (viewHeight - safeHeight * Zoom) / 2f - contentTop * Zoom;
        }

        /// <summary>Converts a screen x coordinate to world space.</summary>
        public float ToWorldX(float screenX) => (screenX - PanX) / Zoom;

        /// <summary>Converts a screen y coordinate to world space.</summary>
        public float ToWorldY(float screenY) => (screenY - PanY) / Zoom;

        /// <summary>Converts a world x coordinate to screen space.</summary>
        public float ToScreenX(float worldX) => worldX * Zoom + PanX;

        /// <summary>Converts a world y coordinate to screen space.</summary>
        public float ToScreenY(float worldY) => worldY * Zoom + PanY;

        private static float Clamp(float zoom) => Math.Min(MaximumZoom, Math.Max(MinimumZoom, zoom));
    }
}
