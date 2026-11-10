using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CardForge.Editor;
using CardForge.DesignLogic;
using UnityEditor;
using UnityEngine;

namespace CardForge.EditorUI
{
    /// <summary>Readable GPU copy, safe PNG slicing and frame-overlay creation for digital layouts.</summary>
    public static class CardForgeImageAssets
    {
        public sealed class FrameExport
        {
            public string ReferencePath;
            public string OverlayPath;
            public readonly Dictionary<string, string> Regions = new Dictionary<string, string>();
        }

        public static Texture2D Readable(Texture source, int maxDimension = 0)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            float factor = maxDimension > 0 ? Mathf.Min(1, maxDimension / (float)Mathf.Max(source.width, source.height)) : 1;
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * factor));
            int height = Mathf.Max(1, Mathf.RoundToInt(source.height * factor));
            if (source is Texture2D readableSource && readableSource.isReadable && width == source.width && height == source.height)
            {
                // A CPU copy is deterministic in batch/headless runs and avoids an unnecessary GPU round trip.
                var cpuCopy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                cpuCopy.SetPixels32(readableSource.GetPixels32());
                cpuCopy.Apply();
                return cpuCopy;
            }
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readable.Apply();
                return readable;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        public static byte[] EncodeForVision(Texture2D source, int maxDimension)
        {
            var copy = Readable(source, maxDimension);
            try { return copy.EncodeToJPG(85); }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }

        public static string SafeName(string raw)
        {
            return AtlasGeometry.Slug(raw);
        }

        public static string SavePng(Texture2D texture, string unityPath)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            if (!unityPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                !unityPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Images must be saved as PNG assets under Assets/.", nameof(unityPath));
            string absolute = Path.GetFullPath(unityPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllBytes(absolute, texture.EncodeToPNG());
            return unityPath;
        }

        public static Texture2D Crop(Texture2D readable, RectInt area)
        {
            int x = Mathf.Clamp(area.x, 0, readable.width - 1);
            int y = Mathf.Clamp(area.y, 0, readable.height - 1);
            int w = Mathf.Clamp(area.width, 1, readable.width - x);
            int h = Mathf.Clamp(area.height, 1, readable.height - y);
            var result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            result.SetPixels(readable.GetPixels(x, y, w, h)); result.Apply();
            return result;
        }

        public static RectInt PixelRect(ZoneDefinition zone, int width, int height)
        {
            return new RectInt(
                Mathf.Clamp(Mathf.FloorToInt(zone.x), 0, width - 1),
                Mathf.Clamp(height - Mathf.CeilToInt(zone.y + zone.height), 0, height - 1),
                Mathf.Max(1, Mathf.CeilToInt(zone.width)),
                Mathf.Max(1, Mathf.CeilToInt(zone.height)));
        }

        public static FrameExport SaveFrame(Texture2D source, LayoutDocument layout, bool eraseSlots)
        {
            if (source == null || layout == null) throw new ArgumentNullException("Source and layout are required.");
            var readable = Readable(source);
            try
            {
                // Keep each layout independent; re-extracting one frame never overwrites another.
                string folder = CardForgeStore.DataRoot + "/frames/" + SafeName(layout.name) + "-" +
                                layout.id.Substring(0, Math.Min(layout.id.Length, 8));
                var result = new FrameExport
                {
                    ReferencePath = SavePng(readable, folder + "/reference.png")
                };
                var overlay = new Texture2D(readable.width, readable.height, TextureFormat.RGBA32, false);
                overlay.SetPixels32(readable.GetPixels32());
                foreach (var zone in layout.zones ?? new List<ZoneDefinition>())
                {
                    if (zone == null) continue;
                    RectInt rect = PixelRect(zone, readable.width, readable.height);
                    var region = Crop(readable, rect);
                    try
                    {
                        string path = folder + "/regions/" + SafeName(zone.name) + "-" +
                                      zone.id.Substring(0, Math.Min(8, zone.id.Length)) + ".png";
                        result.Regions[zone.id] = SavePng(region, path);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(region); }
                    if (!eraseSlots) continue;
                    // Cut out content panels, preserving outer borders and fixed corner decorations.
                    int border = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(rect.width, rect.height) * .04f));
                    for (int y = rect.yMin + border; y < Mathf.Min(rect.yMax - border, overlay.height); y++)
                        for (int x = rect.xMin + border; x < Mathf.Min(rect.xMax - border, overlay.width); x++)
                            overlay.SetPixel(x, y, Color.clear);
                }
                try
                {
                    overlay.Apply();
                    result.OverlayPath = SavePng(overlay, folder + "/frame-overlay.png");
                }
                finally { UnityEngine.Object.DestroyImmediate(overlay); }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                return result;
            }
            finally { UnityEngine.Object.DestroyImmediate(readable); }
        }

        /// <summary>Imports a single icon; for an atlas, prefer ImportIconFromReadable with one shared readback.</summary>
        public static string ImportIcon(Texture2D source, RectInt rect, string target, bool trimTransparent)
        {
            var readable = Readable(source);
            try { return ImportIconFromReadable(readable, rect, target, trimTransparent); }
            finally { UnityEngine.Object.DestroyImmediate(readable); }
        }

        /// <summary>Extracts a PNG from an already readable atlas, avoiding repeated GPU readbacks.</summary>
        public static string ImportIconFromReadable(Texture2D readable, RectInt rect, string target, bool trimTransparent)
        {
                var piece = Crop(readable, rect);
                try
                {
                    if (!trimTransparent) return SavePng(piece, target);
                    var pixels = piece.GetPixels32();
                    int xMin = piece.width, yMin = piece.height, xMax = -1, yMax = -1;
                    for (int y = 0; y < piece.height; y++)
                        for (int x = 0; x < piece.width; x++)
                            if (pixels[y * piece.width + x].a > 8)
                            {
                                xMin = Mathf.Min(xMin, x); yMin = Mathf.Min(yMin, y);
                                xMax = Mathf.Max(xMax, x); yMax = Mathf.Max(yMax, y);
                            }
                    if (xMax < xMin || yMax < yMin) return SavePng(piece, target);
                    var trimmed = Crop(piece, new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1));
                    try { return SavePng(trimmed, target); }
                    finally { UnityEngine.Object.DestroyImmediate(trimmed); }
                }
                finally { UnityEngine.Object.DestroyImmediate(piece); }
        }
    }
}
