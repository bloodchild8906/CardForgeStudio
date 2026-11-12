using UnityEngine;

namespace TcgServer.Client
{
    /// <summary>
    /// Scene-authored presentation values. The UXML on the UIDocument controls
    /// hierarchy; these Inspector values are applied to the runtime shell.
    /// Gameplay and turn authority remain on the native server.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CardForgeSceneLayout : MonoBehaviour
    {
        [Header("Arena colors")]
        [SerializeField] private Color arenaBackground = new Color(0.08f, 0.06f, 0.11f, 1f);
        [SerializeField] private Color headerBackground = new Color(0.14f, 0.11f, 0.18f, 1f);
        [SerializeField] private Color trimColor = new Color(0.62f, 0.47f, 0.31f, 1f);

        [Header("Screen spacing")]
        [SerializeField, Range(60f, 120f)] private float headerMinimumHeight = 70f;
        [SerializeField, Range(4f, 32f)] private float pagePadding = 12f;

        public Color ArenaBackground => arenaBackground;
        public Color HeaderBackground => headerBackground;
        public Color TrimColor => trimColor;
        public float HeaderMinimumHeight => headerMinimumHeight;
        public float PagePadding => pagePadding;

        private void OnValidate()
        {
            headerMinimumHeight = Mathf.Clamp(headerMinimumHeight, 60f, 120f);
            pagePadding = Mathf.Clamp(pagePadding, 4f, 32f);
        }
    }
}
