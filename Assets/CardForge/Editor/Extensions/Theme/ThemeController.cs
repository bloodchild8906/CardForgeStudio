using UnityEditor;
using UnityEngine.UIElements;

namespace CardForge.EditorUI
{
    /// <summary>Switches the studio between the dark and light token sets.</summary>
    public sealed class ThemeController
    {
        private const string DarkClassName = "cf-theme-dark";
        private const string LightClassName = "cf-theme-light";

        private readonly VisualElement themeTarget;
        private bool isDark;

        /// <summary>Applies the theme matching the current editor skin.</summary>
        public ThemeController(VisualElement themeTarget)
        {
            this.themeTarget = themeTarget;
            Apply(EditorPrefs.GetBool("CardForge.Theme.Dark", EditorGUIUtility.isProSkin));
        }

        /// <summary>Flips between dark and light.</summary>
        public void Toggle() => Apply(!isDark);

        private void Apply(bool useDark)
        {
            isDark = useDark;
            EditorPrefs.SetBool("CardForge.Theme.Dark", useDark);
            themeTarget.EnableInClassList(DarkClassName, useDark);
            themeTarget.EnableInClassList(LightClassName, !useDark);
        }
    }
}
