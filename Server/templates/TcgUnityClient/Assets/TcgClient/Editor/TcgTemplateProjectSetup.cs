using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TcgServer.Client.Editor
{
    public static class TcgTemplateProjectSetup
    {
        private const string SettingsPath = "Assets/TcgClient/Resources/TcgConnectionSettings.asset";
        private const string ScenePath = "Assets/TcgClient/Scenes/TcgClient.unity";

        [MenuItem("Tools/TcgStudio/Create or Refresh Client Scene")]
        public static void CreateTemplateScene()
        {
            Directory.CreateDirectory("Assets/TcgClient/Resources");
            Directory.CreateDirectory("Assets/TcgClient/Scenes");

            var settings = AssetDatabase.LoadAssetAtPath<TcgConnectionSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<TcgConnectionSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Tcg Client Template");
            root.AddComponent<TcgClientSession>();
            root.AddComponent<TcgRestClient>();
            root.AddComponent<TcgGameConnection>();
            var app = root.AddComponent<TcgTemplateApp>();

            var appObject = new SerializedObject(app);
            appObject.Update();
            appObject.FindProperty("settings").objectReferenceValue = settings;
            appObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(app);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "TcgStudio";
            PlayerSettings.productName = "TcgULtimateClient";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.tcgstudio.ultimateclient");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = root;
            Debug.Log($"TcgULtimateClient template scene created at {ScenePath}");
        }
    }
}
