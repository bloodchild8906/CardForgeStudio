using System;
using CardForge.EditorUI;
using NUnit.Framework;
using UnityEngine;

namespace CardForge.CreatorTools.Tests
{
    /// <summary>Deterministic Edit Mode fixtures. Tests never call Ollama or modify user assets.</summary>
    public sealed class CreatorToolsTests
    {
        [Test] public void OllamaJsonResponseProducesClampedRegions()
        {
            const string json = "{\"zones\":[{\"name\":\"Artwork\",\"kind\":\"Image\",\"field\":\"art\","+
                "\"x\":950,\"y\":800,\"w\":400,\"h\":500,\"confidence\":0.96}]}";
            var proposals = CardForgeOllama.ParseProposals(json);
            Assert.AreEqual(1, proposals.zones.Count);
            Assert.AreEqual("art", proposals.zones[0].field);
            Assert.LessOrEqual(proposals.zones[0].x + proposals.zones[0].w, 1000f);
            Assert.LessOrEqual(proposals.zones[0].y + proposals.zones[0].h, 1000f);
        }
        [Test] public void OllamaRejectsEmptyRegions()
        {
            Assert.Throws<FormatException>(() => CardForgeOllama.ParseProposals("{\"zones\":[]}"));
        }
        [Test] public void FontDownloadsAreRestrictedToTrustedHosts()
        {
            Assert.IsTrue(CardForgeFontImporter.SafeDownloadUrl("https://raw.githubusercontent.com/google/fonts/main/ofl/inter/Inter.ttf"));
            Assert.IsFalse(CardForgeFontImporter.SafeDownloadUrl("https://raw.githubusercontent.com.evil.example/font.ttf"));
            Assert.IsFalse(CardForgeFontImporter.SafeDownloadUrl("http://raw.githubusercontent.com/font.ttf"));
        }
        [Test] public void FrameEdgeRefinerAlignsProposedBoxToHighContrastBorder()
        {
            var picture = new Texture2D(120, 160, TextureFormat.RGBA32, false);
            var black = new Color[120 * 160];
            for(int i = 0; i < black.Length; i++) black[i] = Color.black;
            picture.SetPixels(black);
            for (int x = 20; x <= 100; x++) { picture.SetPixel(x, 35, Color.white); picture.SetPixel(x, 125, Color.white); }
            for (int y = 35; y <= 125; y++) { picture.SetPixel(20, y, Color.white); picture.SetPixel(100, y, Color.white); }
            picture.Apply();
            try
            {
                var rects = CardForgeFrameEdgeRefiner.Refine(picture, new [] { new Rect(24, 39, 72, 82) }, .05f);
                Assert.AreEqual(1, rects.Count);
                Assert.LessOrEqual(Mathf.Abs(rects[0].xMin - 20), 2.1f);
                Assert.LessOrEqual(Mathf.Abs(rects[0].xMax - 100), 2.1f);
            }
            finally { UnityEngine.Object.DestroyImmediate(picture); }
        }

        [Test] public void CropProducesCorrectPixels()
        {
            var original = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            original.SetPixels(new Color[16]); original.SetPixel(1, 1, Color.magenta); original.Apply();
            Texture2D cropped = null;
            try
            {
                cropped = CardForgeImageAssets.Crop(original, new RectInt(1, 1, 2, 2));
                Assert.AreEqual(2, cropped.width);
                Assert.AreEqual(2, cropped.height);
                Assert.AreEqual(Color.magenta, cropped.GetPixel(0, 0));
            }
            finally
            {
                if (cropped != null) UnityEngine.Object.DestroyImmediate(cropped);
                UnityEngine.Object.DestroyImmediate(original);
            }
        }
    }
}
