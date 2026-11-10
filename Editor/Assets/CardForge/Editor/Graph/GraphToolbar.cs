using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.Editor.Graph
{
    /// <summary>Floating zoom, fit, tidy and add controls shown in the corner of a graph.</summary>
    internal sealed class GraphToolbar : VisualElement
    {
        private readonly Label zoomLabel = new Label("100%") { pickingMode = PickingMode.Ignore };
        private readonly Button addButton;

        public GraphToolbar()
        {
            AddToClassList("cf-graph__toolbar");
            addButton = Tool("+ Add", "Add a node at the centre of the view", () => AddClicked?.Invoke());
            addButton.style.display = DisplayStyle.None;
            Add(addButton);
            Add(Tool("\u2212", "Zoom out", () => ZoomOutClicked?.Invoke()));
            zoomLabel.AddToClassList("cf-graph__zoom");
            Add(zoomLabel);
            Add(Tool("+", "Zoom in", () => ZoomInClicked?.Invoke()));
            Add(Tool("Fit", "Fit everything in view (F)", () => FitClicked?.Invoke()));
            Add(Tool("Tidy", "Arrange nodes left to right", () => TidyClicked?.Invoke()));
        }

        public event Action AddClicked;
        public event Action ZoomInClicked;
        public event Action ZoomOutClicked;
        public event Action FitClicked;
        public event Action TidyClicked;

        public string AddLabel
        {
            set
            {
                addButton.text = value;
                addButton.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        public void ShowZoom(float zoom) => zoomLabel.text = Mathf.RoundToInt(zoom * 100f) + "%";

        private static Button Tool(string text, string tooltip, Action clicked)
        {
            var button = new Button(clicked) { text = text, tooltip = tooltip, focusable = false };
            button.AddToClassList("cf-graph-tool");
            return button;
        }
    }
}
