using System;
using System.Collections.Generic;
using UnityEngine;

namespace CardForge.EditorUI
{
    /// <summary>
    /// Optional local refinement of AI region boxes against strong frame boundaries.
    /// This contains no inference, file access, UnityEditor APIs, or network dependencies.
    /// </summary>
    public static class CardForgeFrameEdgeRefiner
    {
        /// <summary>
        /// Refines proposed top-left-origin rectangles by scanning nearby pixel color discontinuities.
        /// Suggestions are conservative: weak edges are ignored; all output stays inside the source image.
        /// The readable GPU copy is created once for the whole input batch.
        /// </summary>
        public static List<Rect> Refine(Texture2D source, IReadOnlyList<Rect> proposals, float searchFraction = .017f)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (proposals == null) throw new ArgumentNullException(nameof(proposals));
            var result = new List<Rect>(proposals.Count);
            var readable = CardForgeImageAssets.Readable(source, 1400);
            try
            {
                var pixels = readable.GetPixels32();
                int imageWidth = readable.width, imageHeight = readable.height;
                float sx = imageWidth / (float)source.width;
                float sy = imageHeight / (float)source.height;
                int radiusX = Mathf.Clamp(Mathf.RoundToInt(imageWidth * Mathf.Clamp(searchFraction, 0, .04f)), 1, 36);
                int radiusY = Mathf.Clamp(Mathf.RoundToInt(imageHeight * Mathf.Clamp(searchFraction, 0, .04f)), 1, 36);

                foreach (var proposal in proposals)
                {
                    int left = Mathf.Clamp(Mathf.RoundToInt(proposal.xMin * sx), 1, imageWidth - 2);
                    int right = Mathf.Clamp(Mathf.RoundToInt(proposal.xMax * sx), left + 1, imageWidth - 1);
                    // Convert from card's top-left origin into Texture2D's bottom-left origin.
                    int bottom = Mathf.Clamp(Mathf.RoundToInt((source.height - proposal.yMax) * sy), 1, imageHeight - 2);
                    int top = Mathf.Clamp(Mathf.RoundToInt((source.height - proposal.yMin) * sy), bottom + 1, imageHeight - 1);
                    if (right - left < 3 || top - bottom < 3)
                    {
                        result.Add(proposal);
                        continue;
                    }

                    int newLeft = FindStrongestVertical(pixels, imageWidth, imageHeight, left, bottom, top, radiusX);
                    int newRight = FindStrongestVertical(pixels, imageWidth, imageHeight, right, bottom, top, radiusX);
                    int newBottom = FindStrongestHorizontal(pixels, imageWidth, imageHeight, bottom, left, right, radiusY);
                    int newTop = FindStrongestHorizontal(pixels, imageWidth, imageHeight, top, left, right, radiusY);

                    if (newRight - newLeft < 4) { newLeft = left; newRight = right; }
                    if (newTop - newBottom < 4) { newBottom = bottom; newTop = top; }

                    result.Add(new Rect(newLeft / sx, source.height - newTop / sy,
                        (newRight - newLeft) / sx, (newTop - newBottom) / sy));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(readable); }
            return result;
        }

        static int FindStrongestVertical(Color32[] pixels, int width, int height,
            int original, int startY, int endY, int radius)
        {
            float baseline = VerticalScore(pixels, width, height, original, startY, endY);
            int best = original;
            float highest = baseline;
            for (int x = Mathf.Max(1, original - radius); x <= Mathf.Min(width - 1, original + radius); x++)
            {
                float score = VerticalScore(pixels, width, height, x, startY, endY);
                if (score > highest) { highest = score; best = x; }
            }
            return highest >= 16 && highest > baseline * 1.12f ? best : original;
        }

        static int FindStrongestHorizontal(Color32[] pixels, int width, int height,
            int original, int startX, int endX, int radius)
        {
            float baseline = HorizontalScore(pixels, width, height, original, startX, endX);
            int best = original;
            float highest = baseline;
            for (int y = Mathf.Max(1, original - radius); y <= Mathf.Min(height - 1, original + radius); y++)
            {
                float score = HorizontalScore(pixels, width, height, y, startX, endX);
                if (score > highest) { highest = score; best = y; }
            }
            return highest >= 16 && highest > baseline * 1.12f ? best : original;
        }

        static float VerticalScore(Color32[] pixels, int width, int height, int x, int startY, int endY)
        {
            float sum = 0; int samples = 0;
            int stride = Mathf.Max(1, (endY - startY) / 120);
            for (int y = Mathf.Clamp(startY, 0, height - 1); y < Mathf.Min(height, endY); y += stride)
            {
                var a = pixels[y * width + x];
                var b = pixels[y * width + x - 1];
                sum += Difference(a, b); samples++;
            }
            return samples == 0 ? 0 : sum / samples;
        }

        static float HorizontalScore(Color32[] pixels, int width, int height, int y, int startX, int endX)
        {
            float sum = 0; int samples = 0;
            int stride = Mathf.Max(1, (endX - startX) / 120);
            for (int x = Mathf.Clamp(startX, 0, width - 1); x < Mathf.Min(width, endX); x += stride)
            {
                var a = pixels[y * width + x];
                var b = pixels[(y - 1) * width + x];
                sum += Difference(a, b); samples++;
            }
            return samples == 0 ? 0 : sum / samples;
        }

        static float Difference(Color32 a, Color32 b)
        {
            return (Mathf.Abs((int)a.r - b.r) + Mathf.Abs((int)a.g - b.g) +
                Mathf.Abs((int)a.b - b.b)) / 3f;
        }
    }
}
