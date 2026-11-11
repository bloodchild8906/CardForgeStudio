using UnityEngine.UIElements;

namespace CardForge.EditorUI
{
    /// <summary>Small helpers for creating styled elements from code.</summary>
    public static class UiElementFactory
    {
        /// <summary>Creates a label that carries the given USS class.</summary>
        public static Label CreateLabel(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }
    }
}
