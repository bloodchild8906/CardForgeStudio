using UnityEngine;
using UnityEngine.UI;

namespace TcgServer.Client
{
    /// <summary>
    /// Keeps variable-size card hands within their allotted area by overlapping cards
    /// when needed. It never resizes the card hit targets, preserving readability.
    /// </summary>
    [RequireComponent(typeof(RectTransform), typeof(HorizontalLayoutGroup))]
    public sealed class TcgFanLayout : MonoBehaviour
    {
        [SerializeField, Min(16f)] private float cardWidth = 94f;
        [SerializeField] private float spaciousGap = 6f;
        [SerializeField, Min(0f)] private float minimumVisibleEdge = 19f;

        private RectTransform rect;
        private HorizontalLayoutGroup layout;
        private float previousWidth = -1f;
        private int previousCardCount = -1;

        private void Awake()
        {
            rect = GetComponent<RectTransform>();
            layout = GetComponent<HorizontalLayoutGroup>();
        }

        private void LateUpdate()
        {
            var width = rect.rect.width - layout.padding.left - layout.padding.right;
            var count = transform.childCount;
            if (Mathf.Approximately(width, previousWidth) && count == previousCardCount)
                return;
            previousWidth = width;
            previousCardCount = count;

            var spacing = count <= 1 ? spaciousGap :
                Mathf.Min(spaciousGap, (width - count * cardWidth) / (count - 1));
            // Show at least a small clickable slice of each overlapping card.
            spacing = Mathf.Max(-cardWidth + minimumVisibleEdge, spacing);
            if (Mathf.Abs(layout.spacing - spacing) <= .05f) return;
            layout.spacing = spacing;
            LayoutRebuilder.MarkLayoutForRebuild(rect);
        }
    }
}
