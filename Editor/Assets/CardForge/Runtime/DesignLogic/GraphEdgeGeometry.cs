using System;

namespace CardForge.DesignLogic
{
    /// <summary>Shape of the curved connections drawn between graph nodes.</summary>
    public static class GraphEdgeGeometry
    {
        /// <summary>Smallest horizontal pull of the curve handles.</summary>
        public const float MinimumBend = 70f;

        private const int HitTestSegments = 32;

        /// <summary>Horizontal handle length of a connection that runs from startX to endX.</summary>
        public static float Bend(float startX, float endX)
        {
            return Math.Max(MinimumBend, Math.Abs(endX - startX) * 0.5f);
        }

        /// <summary>Point on the cubic curve from a to b at parameter t between 0 and 1.</summary>
        public static void PointAt(float startX, float startY, float endX, float endY, float t, out float x, out float y)
        {
            float bend = Bend(startX, endX);
            float firstHandleX = startX + bend;
            float secondHandleX = endX - bend;
            float remaining = 1f - t;
            float startWeight = remaining * remaining * remaining;
            float firstWeight = 3f * remaining * remaining * t;
            float secondWeight = 3f * remaining * t * t;
            float endWeight = t * t * t;
            x = startWeight * startX + firstWeight * firstHandleX + secondWeight * secondHandleX + endWeight * endX;
            y = (startWeight + firstWeight) * startY + (secondWeight + endWeight) * endY;
        }

        /// <summary>Shortest distance from a point to the curve from a to b.</summary>
        public static float DistanceToEdge(float startX, float startY, float endX, float endY, float pointX, float pointY)
        {
            float previousX = startX;
            float previousY = startY;
            float nearest = float.MaxValue;
            for (int segment = 1; segment <= HitTestSegments; segment++)
            {
                PointAt(startX, startY, endX, endY, segment / (float)HitTestSegments, out float currentX, out float currentY);
                nearest = Math.Min(nearest, DistanceToSegment(previousX, previousY, currentX, currentY, pointX, pointY));
                previousX = currentX;
                previousY = currentY;
            }
            return nearest;
        }

        private static float DistanceToSegment(float ax, float ay, float bx, float by, float px, float py)
        {
            float segmentX = bx - ax;
            float segmentY = by - ay;
            float lengthSquared = segmentX * segmentX + segmentY * segmentY;
            float position = lengthSquared < 0.0001f ? 0f : ((px - ax) * segmentX + (py - ay) * segmentY) / lengthSquared;
            position = Math.Min(1f, Math.Max(0f, position));
            float nearestX = ax + segmentX * position;
            float nearestY = ay + segmentY * position;
            float offsetX = px - nearestX;
            float offsetY = py - nearestY;
            return (float)Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
        }
    }
}
