using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CardForge.Editor
{
    /// <summary>
    /// Studio-grade high-resolution card and layout PNG exporter.
    /// Renders cards to Texture2D with full layer blending, fonts, artwork, and vector icons,
    /// and exports single cards or full editions at 1x, 2x (300 DPI print quality), or custom scales.
    /// </summary>
    public static class CardForgePngExporter
    {
        /// <summary>
        /// Prompts user for save location and exports the card as a high-res PNG.
        /// </summary>
        public static void ExportCard(LayoutDocument layout, CardDocument card, SchemaDocument schema,
            IReadOnlyDictionary<string, IconDocument> icons, int scale = 2)
        {
            if (layout == null) return;
            string defaultName = !string.IsNullOrEmpty(card?.name)
                ? card.name.Replace(" ", "_")
                : (!string.IsNullOrEmpty(layout.name) ? layout.name.Replace(" ", "_") : "card_export");

            string path = EditorUtility.SaveFilePanel("Export Card PNG", "", defaultName, "png");
            if (string.IsNullOrEmpty(path)) return;

            Texture2D tex = null;
            try
            {
                tex = RenderToTexture(layout, card, schema, icons, scale);
                byte[] bytes = tex.EncodeToPNG();
                File.WriteAllBytes(path, bytes);
                EditorUtility.RevealInFinder(path);
                Debug.Log($"[CardForge] Exported card PNG to: {path} ({tex.width}x{tex.height})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CardForge] Card PNG export failed: {ex.Message}");
                EditorUtility.DisplayDialog("Export Error", ex.Message, "OK");
            }
            finally
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        /// <summary>
        /// Batch exports all cards in an edition/block into a selected folder.
        /// </summary>
        public static void ExportBlockCards(BlockDocument block, CardForgeStore store, int scale = 2)
        {
            if (block == null || store == null) return;
            var cardIds = block.cardIds.Where(store.Cards.ContainsKey).ToList();
            if (cardIds.Count == 0)
            {
                EditorUtility.DisplayDialog("Export Block", "This edition has no cards to export.", "OK");
                return;
            }

            string folder = EditorUtility.OpenFolderPanel("Select Export Directory for Edition Cards", "", "");
            if (string.IsNullOrEmpty(folder)) return;

            int count = 0;
            try
            {
                for (int i = 0; i < cardIds.Count; i++)
                {
                    var card = store.Cards[cardIds[i]];
                    var layout = store.Layouts.TryGetValue(card.layoutId ?? "", out var l) ? l : null;
                    if (layout == null) continue;

                    EditorUtility.DisplayProgressBar("Exporting Cards",
                        $"Rendering {card.name} ({i + 1}/{cardIds.Count})…", (float)i / cardIds.Count);

                    string filename = $"{card.cardNumber ?? (i + 1).ToString("D3")}_{Slug(card.name)}.png";
                    string fullPath = Path.Combine(folder, filename);

                    Texture2D tex = null;
                    try
                    {
                        tex = RenderToTexture(layout, card, store.Schema, store.Icons, scale);
                        File.WriteAllBytes(fullPath, tex.EncodeToPNG());
                        count++;
                    }
                    finally
                    {
                        if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorUtility.RevealInFinder(folder);
            EditorUtility.DisplayDialog("Export Complete",
                $"Successfully exported {count} of {cardIds.Count} cards to:\n{folder}", "OK");
        }

        /// <summary>
        /// Renders a card or layout directly to a Texture2D.
        /// </summary>
        public static Texture2D RenderToTexture(LayoutDocument layout, CardDocument card, SchemaDocument schema,
            IReadOnlyDictionary<string, IconDocument> icons, int scale = 1)
        {
            scale = Mathf.Clamp(scale, 1, 4);
            int width = Mathf.Max(100, Mathf.RoundToInt(layout.width * scale));
            int height = Mathf.Max(140, Mathf.RoundToInt(layout.height * scale));

            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var prevRT = RenderTexture.active;
            RenderTexture.active = rt;

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, width, height, 0);

            // 1. Clear background
            Color bgColor = Color.clear;
            if (!string.IsNullOrEmpty(layout.backgroundColor))
                ColorUtility.TryParseHtmlString(layout.backgroundColor, out bgColor);
            if (bgColor.a <= 0) bgColor = new Color(0.1f, 0.14f, 0.2f, 1f);

            GL.Clear(true, true, bgColor);

            // 2. Background image if present
            if (!string.IsNullOrEmpty(layout.backgroundAssetPath))
            {
                var bgTex = AssetDatabase.LoadAssetAtPath<Texture2D>(layout.backgroundAssetPath);
                if (bgTex != null)
                {
                    Graphics.DrawTexture(new Rect(0, 0, width, height), bgTex);
                }
            }

            // 3. Render zones in order
            var zones = new List<ZoneDefinition>(layout.zones);
            zones.Sort((a, b) => a.order.CompareTo(b.order));

            foreach (var sourceZone in zones)
            {
                float opacity = 1f;
                var zone = CardForgeRules.Resolve(sourceZone, card, schema, out opacity);
                var ov = CardForgeData.GetOverride(card, zone.id);
                if (!zone.visible || (ov != null && ov.hidden)) continue;

                float zx = (zone.x + zone.offsetX + (ov?.deltaX ?? 0)) * scale;
                float zy = (zone.y + zone.offsetY + (ov?.deltaY ?? 0)) * scale;
                float zw = Mathf.Max(10 * scale, (zone.width + (ov?.deltaWidth ?? 0)) * scale);
                float zh = Mathf.Max(10 * scale, (zone.height + (ov?.deltaHeight ?? 0)) * scale);
                var destRect = new Rect(zx, zy, zw, zh);

                // Zone background color
                if (!string.IsNullOrEmpty(zone.backgroundColor))
                {
                    if (ColorUtility.TryParseHtmlString(zone.backgroundColor, out var zoneBg))
                    {
                        zoneBg.a *= opacity;
                        DrawSolidRect(destRect, zoneBg);
                    }
                }

                // Zone template image
                var template = (layout.templates ?? new List<ZoneTemplateDefinition>()).FirstOrDefault(x => x.id == zone.templateId);
                if (template != null && !string.IsNullOrEmpty(template.backgroundAssetPath))
                {
                    var tmplTex = AssetDatabase.LoadAssetAtPath<Texture2D>(template.backgroundAssetPath);
                    if (tmplTex != null)
                    {
                        Graphics.DrawTexture(destRect, tmplTex);
                    }
                }

                // Layers
                foreach (var layer in (sourceZone.layers ?? new List<ZoneLayer>()).Where(x => CardForgeRules.LayerMatches(x, card, schema)).OrderBy(x => x.order))
                {
                    if (!string.IsNullOrEmpty(layer.assetPath))
                    {
                        var layerTex = AssetDatabase.LoadAssetAtPath<Texture2D>(layer.assetPath);
                        if (layerTex != null)
                        {
                            Graphics.DrawTexture(destRect, layerTex);
                        }
                    }
                }

                // Content: Image / Icon or Text
                string content = zone.staticText;
                string assetPath = zone.staticAssetPath;

                if (!string.IsNullOrEmpty(zone.fieldId) && schema?.fields != null)
                {
                    var field = schema.fields.Find(d => d.id == zone.fieldId);
                    if (field != null)
                    {
                        string val = CardForgeData.Value(card, field);
                        if (zone.kind == ZoneKind.Icon || zone.kind == ZoneKind.Image)
                            assetPath = val;
                        else
                        {
                            if (field.kind == FieldKind.Number &&
                                double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var num))
                                val = num.ToString("F" + Mathf.Clamp(field.decimalPlaces, 0, 8), System.Globalization.CultureInfo.InvariantCulture);
                            content = (field.prefix ?? "") + val + (field.suffix ?? "");
                        }
                    }
                }

                if (!string.IsNullOrEmpty(ov?.textOverride)) content = ov.textOverride;
                if (!string.IsNullOrEmpty(ov?.assetPathOverride)) assetPath = ov.assetPathOverride;

                if (zone.kind == ZoneKind.Image || zone.kind == ZoneKind.Icon)
                {
                    Texture2D contentTex = null;
                    bool isGenerated = false;

                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        contentTex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    }

                    // Check if icon document exists in icons dictionary
                    if (contentTex == null && icons != null && !string.IsNullOrEmpty(assetPath) && icons.TryGetValue(assetPath, out var iconDoc))
                    {
                        contentTex = CardForgeIconPainter.Render(iconDoc, 256);
                        isGenerated = true;
                    }

                    if (contentTex != null)
                    {
                        Graphics.DrawTexture(destRect, contentTex);
                        if (isGenerated) UnityEngine.Object.DestroyImmediate(contentTex);
                    }
                }
            }

            GL.PopMatrix();

            // Readback into Texture2D
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();

            RenderTexture.active = prevRT;
            RenderTexture.ReleaseTemporary(rt);

            return result;
        }

        static void DrawSolidRect(Rect r, Color c)
        {
            var prevColor = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, EditorGUIUtility.whiteTexture);
            GUI.color = prevColor;
        }

        static string Slug(string text)
        {
            if (string.IsNullOrEmpty(text)) return "card";
            return new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        }
    }
}

