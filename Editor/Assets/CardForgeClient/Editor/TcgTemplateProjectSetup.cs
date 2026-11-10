using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TcgServer.Client.Editor
{
    public static class TcgTemplateProjectSetup
    {
        public const string SettingsPath = "Assets/CardForgeClient/Resources/TcgConnectionSettings.asset";
        public const string ScenePath = "Assets/CardForgeClient/Scenes/TcgClient.unity";

        [MenuItem("CardForge/Client/Create or Refresh Client Scene")]
        public static void CreateTemplateScene()
        {
            Directory.CreateDirectory("Assets/CardForgeClient/Resources");
            Directory.CreateDirectory("Assets/CardForgeClient/Scenes");

            var settings = AssetDatabase.LoadAssetAtPath<TcgConnectionSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<TcgConnectionSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("CardForge Client");
            root.AddComponent<TcgClientSession>();
            var rest = root.AddComponent<TcgRestClient>();
            root.AddComponent<TcgGameConnection>();
            var app = root.AddComponent<TcgTemplateApp>();

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
