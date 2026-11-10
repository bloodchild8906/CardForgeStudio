using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CardForge.Editor
{
    /// <summary>Procedural icon painter. Editable vectors are stored as JSON; PNG is an exported asset.</summary>
    public static class CardForgeIconPainter
    {
        public static readonly string[] Shapes = { "Circle", "Ring", "Square", "Diamond", "Triangle", "Star", "Bolt", "Shield", "Cross" };
        public static Texture2D Render(IconDocument icon, int resolution = 128)
        {
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            var pixels = new Color[resolution * resolution];
            foreach (var layer in icon.layers)
            {
                if (!layer.visible) continue;
                Color paint;
                if (!ColorUtility.TryParseHtmlString(layer.color, out paint)) paint = Color.white;
                paint.a *= Mathf.Clamp01(layer.opacity);
                float s = Mathf.Max(.01f, layer.size);
                float radians = layer.rotation * Mathf.Deg2Rad;
                float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
                for (int y = 0; y < resolution; ++y)
                for (int x = 0; x < resolution; ++x)
                {
                    float u = ((x + .5f) / resolution - layer.x) / s;
                    float v = ((y + .5f) / resolution - (1f - layer.y)) / s;
                    float a = cos * u + sin * v;
                    float b = -sin * u + cos * v;
                    if (!Inside(layer.shape, a, b)) continue;
                    int index = y * resolution + x;
                    float outA = paint.a + pixels[index].a * (1f - paint.a);
                    if (outA <= 0) continue;
                    Color background = pixels[index];
                    pixels[index] = new Color(
                        (paint.r * paint.a + background.r * background.a * (1f - paint.a)) / outA,
                        (paint.g * paint.a + background.g * background.a * (1f - paint.a)) / outA,
                        (paint.b * paint.a + background.b * background.a * (1f - paint.a)) / outA,
                        outA);
                }
            }
            texture.SetPixels(pixels); texture.Apply(); texture.filterMode = FilterMode.Bilinear;
            return texture;
        }
        static bool Inside(string shape, float x, float y)
        {
            switch (shape)
            {
                case "Circle": return x * x + y * y <= .25f;
                case "Ring": { float d = x * x + y * y; return d <= .25f && d >= .115f; }
                case "Square": return Mathf.Abs(x) <= .5f && Mathf.Abs(y) <= .5f;
                case "Diamond": return Mathf.Abs(x) + Mathf.Abs(y) <= .5f;
                case "Triangle": return Polygon(x, y, new [] { new Vector2(0,.56f), new Vector2(-.52f,-.45f), new Vector2(.52f,-.45f) });
                case "Bolt": return Polygon(x, y, new [] { new Vector2(.05f,.52f), new Vector2(-.32f,.03f), new Vector2(-.06f,.03f), new Vector2(-.18f,-.51f), new Vector2(.34f,.14f), new Vector2(.08f,.14f) });
                case "Shield": return Polygon(x, y, new [] { new Vector2(-.46f,.36f), new Vector2(0,.51f), new Vector2(.46f,.36f), new Vector2(.38f,-.22f), new Vector2(0,-.54f), new Vector2(-.38f,-.22f) });
                case "Cross": return (Mathf.Abs(x) <= .14f && Mathf.Abs(y) <= .5f) || (Mathf.Abs(y) <= .14f && Mathf.Abs(x) <= .5f);
                case "Star":
                    var verts = new Vector2[10];
                    for (int i = 0; i < 10; i++)
                    {
                        float t = Mathf.PI * .5f + i * Mathf.PI / 5f;
                        float r = i % 2 == 0 ? .52f : .23f;
                        verts[i] = new Vector2(Mathf.Cos(t) * r, Mathf.Sin(t) * r);
                    }
                    return Polygon(x, y, verts);
                default: return false;
            }
        }
        static bool Polygon(float x, float y, Vector2[] vertices)
        {
            bool inside = false;
            int j = vertices.Length - 1;
            for (int i = 0; i < vertices.Length; j = i++)
            {
                var a = vertices[i]; var b = vertices[j];
                if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }
        public static void SavePng(IconDocument icon)
        {
            icon.assetPath = CardForgeStore.IconImagePath(icon.id);
            var preview = Render(icon, 256);
            var bytes = preview.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(preview);
            string absolute = Path.Combine(Path.GetDirectoryName(Application.dataPath), icon.assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllBytes(absolute, bytes);
            AssetDatabase.ImportAsset(icon.assetPath, ImportAssetOptions.ForceUpdate);
        }
    }
}
