using UnityEngine.UIElements;

namespace CardForge.EditorUI.Dialogs
{
    /// <summary>Shows one modal overlay at a time above the studio.</summary>
    public sealed class ModalHost
    {
        private readonly VisualElement layer;

        /// <summary>Binds the host to the window's modal layer.</summary>
        public ModalHost(VisualElement layer) => this.layer = layer;

        /// <summary>Replaces any open modal with the given overlay.</summary>
        public void Show(VisualElement overlay)
        {
            layer.Clear();
            layer.Add(overlay);
        }

        /// <summary>Closes the open modal.</summary>
        public void Close() => layer.Clear();
    }
}
