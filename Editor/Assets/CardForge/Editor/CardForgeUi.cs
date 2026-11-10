using System;
using UnityEngine.UIElements;

namespace CardForge.Editor
{
    /// <summary>Reusable UI Toolkit-only primitives shared by every CardForge workspace.</summary>
    public static class CardForgeUi
    {
        public static Label Heading(string text)
        {
            var label = new Label(text);
            label.AddToClassList("section-title");
            return label;
        }

        public static Label Description(string text)
        {
            var label = new Label(text);
            label.AddToClassList("muted");
            return label;
        }

        public static Label Eyebrow(string text)
        {
            var label = new Label(text);
            label.AddToClassList("section-kicker");
            return label;
        }

        public static VisualElement Divider()
        {
            var divider = new VisualElement();
            divider.AddToClassList("ui-divider");
            return divider;
        }

        public static VisualElement Surface(string title, string description = null)
        {
            var surface = new VisualElement();
            surface.AddToClassList("ui-surface");
            surface.Add(Heading(title));
            if (!string.IsNullOrEmpty(description)) surface.Add(Description(description));
            return surface;
        }

        public static VisualElement IndexHeader(string eyebrow, string title, string description)
        {
            var header = new VisualElement();
            header.AddToClassList("ui-index-header");
            header.Add(Eyebrow(eyebrow));
            header.Add(Heading(title));
            header.Add(Description(description));
            return header;
        }

        public static VisualElement EmptyState(string title, string description)
        {
            var state = new VisualElement();
            state.AddToClassList("ui-empty-state");
            state.Add(Heading(title));
            state.Add(Description(description));
            return state;
        }

        public static VisualElement ActionRow()
        {
            var row = new VisualElement();
            row.AddToClassList("ui-action-row");
            return row;
        }

        public static Button Primary(string label, Action action)
        {
            var button = new Button(action) { text = label };
            button.AddToClassList("primary-button");
            return button;
        }

        public static Button Secondary(string label, Action action)
        {
            var button = new Button(action) { text = label };
            button.AddToClassList("ghost-button");
            return button;
        }

        public static Button Back(string label, Action action)
        {
            var button = new Button(action) { text = "← " + label };
            button.AddToClassList("small-button");
            button.AddToClassList("ui-back-button");
            return button;
        }
    }
}
