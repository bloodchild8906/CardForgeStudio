using CardForge.EditorUI;
using TcgServer.Client.Editor;
using UnityEditor;

namespace CardForge.Learn
{
    // Compatibility entry point: there is one Studio workbench, not two dashboards.
    // Existing Learn references still reach the same integrated window.
    public sealed class CardForgeDashboardWindow : EditorWindow
    {
        [MenuItem("CardForge/Dashboard")]
        public static void Open() => CardForgeWindow.OpenHome();

        public static void CreateClientScene()
        {
            if (!EditorUtility.DisplayDialog("Create or refresh client scene",
                "This refreshes the playable Unity client scene and its build entry.",
                "REFRESH SCENE", "CANCEL")) return;
            TcgTemplateProjectSetup.CreateTemplateScene();
        }

        void OnEnable() => EditorApplication.delayCall += RedirectLegacyWindow;
        void OnDisable() => EditorApplication.delayCall -= RedirectLegacyWindow;
        void RedirectLegacyWindow()
        {
            if (this == null) return;
            Close();
            CardForgeWindow.OpenHome();
        }
    }
}
