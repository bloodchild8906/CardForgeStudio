using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TcgServer.Client.Editor
{
    /// <summary>
    /// Creates a real UI Toolkit runtime theme and panel asset before client builds.
    /// A runtime-created PanelSettings without a theme renders broken controls.
    /// </summary>
    public static class CardForgeClientVisualAssets
    {
        public const string Folder = "Assets/CardForgeStudio/CardForgeClient/Resources/CardForgeDemo";
        public const string ThemePath = Folder + "/CardForgeRuntimeTheme.tss";
        public const string PanelPath = Folder + "/CardForgePanelSettings.asset";

        public static PanelSettings Ensure()
        {
            Directory.CreateDirectory(Folder);
            if (!File.Exists(ThemePath))
            {
                File.WriteAllText(ThemePath,
                    "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath, ImportAssetOptions.ForceSynchronousImport);
            }

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null)
                throw new InvalidOperationException("CardForge UI Toolkit runtime theme failed to import: " + ThemePath);

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.name = "CardForgePanelSettings";
                AssetDatabase.CreateAsset(panel, PanelPath);
            }

            panel.themeStyleSheet = theme;
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1600, 900);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();
            return panel;
        }
    }
}
