using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace TcgServer.Client.Editor
{
    public static class TcgTemplateProjectSetup
    {
        public const string SettingsPath = "Assets/CardForgeStudio/CardForgeClient/Resources/TcgConnectionSettings.asset";
        public const string ScenePath = "Assets/CardForgeStudio/CardForgeClient/Scenes/TcgClient.unity";
        public const string PanelSettingsPath = "Assets/CardForgeStudio/CardForgeClient/Resources/AstralRift/CardForgePanelSettings.asset";
        public const string EditableArenaPath = "Assets/CardForgeStudio/CardForgeClient/UI/AstralRiftArena.uxml";

        public static void CreateTemplateScene()
        {
            Directory.CreateDirectory("Assets/CardForgeStudio/CardForgeClient/Resources");
            Directory.CreateDirectory("Assets/CardForgeStudio/CardForgeClient/Scenes");
            EnsurePanelSettings();

            var settings = AssetDatabase.LoadAssetAtPath<TcgConnectionSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<TcgConnectionSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
            }

            var panelSettings = CardForgeClientVisualAssets.Ensure();
            // Reopen the generated scene instead of destroying designer changes
            // whenever the demo is prepared again.
            var activeScene = EditorSceneManager.GetActiveScene();
            var scene = string.Equals(activeScene.path, ScenePath,
                    System.StringComparison.OrdinalIgnoreCase)
                ? activeScene
                : File.Exists(ScenePath)
                    ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                    : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = scene.GetRootGameObjects().FirstOrDefault(gameObject => gameObject.name == "CardForge Client");
            if (root == null) root = new GameObject("CardForge Client");
            if (root.GetComponent<TcgClientSession>() == null) root.AddComponent<TcgClientSession>();
            var rest = root.GetComponent<TcgRestClient>() ?? root.AddComponent<TcgRestClient>();
            if (root.GetComponent<TcgGameConnection>() == null) root.AddComponent<TcgGameConnection>();
            var app = root.GetComponent<CardForgeToolkitClient>() ?? root.AddComponent<CardForgeToolkitClient>();
            var document = root.GetComponent<UIDocument>() ?? root.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            AssetDatabase.ImportAsset(EditableArenaPath, ImportAssetOptions.ForceSynchronousImport);
            var arena = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(EditableArenaPath);
            if (arena == null)
                throw new FileNotFoundException("Editable arena UI Toolkit layout could not be loaded.", EditableArenaPath);
            document.visualTreeAsset = arena;

            var designerRoot = root.transform.Find("Arena Presentation (editable in Inspector)");
            if (designerRoot == null)
            {
                var guide = new GameObject("Arena Presentation (editable in Inspector)");
                guide.transform.SetParent(root.transform, false);
                designerRoot = guide.transform;
            }
            if (designerRoot.GetComponent<CardForgeSceneLayout>() == null)
                designerRoot.gameObject.AddComponent<CardForgeSceneLayout>();

            var camera = scene.GetRootGameObjects().Select(gameObject => gameObject.GetComponent<Camera>())
                .FirstOrDefault(component => component != null);
            if (camera == null)
            {
                var cameraObject = new GameObject("Arena Camera");
                camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.08f, 0.06f, 0.11f, 1f);
                camera.orthographic = true;
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            }

            // Both components read server settings through a serialized field, so bind them explicitly.
            Bind(app, "settings", settings);
            Bind(rest, "settings", settings);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureInBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = root;
            Debug.Log($"CardForge client scene created at {ScenePath}");
        }


        public static void EnsurePanelSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (existing != null) return;

            Directory.CreateDirectory(Path.GetDirectoryName(PanelSettingsPath) ?? "Assets/CardForgeStudio/CardForgeClient/Resources/CardForgeDemo");
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.name = "CardForgePanelSettings";
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1600, 900);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = .5f;

            foreach (var guid in AssetDatabase.FindAssets("t:PanelSettings"))
            {
                var candidatePath = AssetDatabase.GUIDToAssetPath(guid);
                if (candidatePath == PanelSettingsPath) continue;
                var candidate = AssetDatabase.LoadAssetAtPath<PanelSettings>(candidatePath);
                if (candidate == null || candidate.themeStyleSheet == null) continue;
                panel.themeStyleSheet = candidate.themeStyleSheet;
                break;
            }

            AssetDatabase.CreateAsset(panel, PanelSettingsPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(PanelSettingsPath, ImportAssetOptions.ForceUpdate);
        }
        public static void ConfigureConnection(string baseUrl, string rulePackId, string packageId, string packageVersion)
        {
            var settings = AssetDatabase.LoadAssetAtPath<TcgConnectionSettings>(SettingsPath);
            if (settings == null)
            {
                Directory.CreateDirectory("Assets/CardForgeStudio/CardForgeClient/Resources");
                settings = ScriptableObject.CreateInstance<TcgConnectionSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            var serialized = new SerializedObject(settings);
            serialized.Update();
            serialized.FindProperty("baseUrl").stringValue = (baseUrl ?? "").TrimEnd('/');
            serialized.FindProperty("defaultRulePackId").stringValue = rulePackId ?? "mtg-like";
            serialized.FindProperty("activePackageId").stringValue = packageId ?? "";
            serialized.FindProperty("activePackageVersion").stringValue = packageVersion ?? "";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        // Batch entry point: Unity -batchmode -executeMethod TcgServer.Client.Editor.TcgTemplateProjectSetup.BatchCreateScene
        public static void BatchCreateScene() => CreateTemplateScene();

        private static void Bind(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.Update();
            var field = serialized.FindProperty(property);
            if (field == null) return;
            field.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        // Append to the project's build scenes instead of replacing them, so the studio's own scenes survive.
        private static void EnsureInBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var existing in scenes)
                if (existing.path == scenePath) return;

            var updated = new EditorBuildSettingsScene[scenes.Length + 1];
            scenes.CopyTo(updated, 0);
            updated[scenes.Length] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = updated;
        }
    }
}

