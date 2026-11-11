using CardForge.EditorUI.Controls;
using UnityEngine.UIElements;

namespace CardForge.EditorUI.Grid
{
    /// <summary>Fills the strip above the grid with stat tiles or a phase sequence.</summary>
    internal static class SummaryStripBuilder
    {
        public static void Build(VisualElement strip, GridScreenDefinition definition)
        {
            strip.Clear();
            strip.EnableInClassList("cf-hidden", definition.SummaryStyle == SummaryStyle.None);
            if (definition.SummaryStyle == SummaryStyle.Tiles)
            {
                AddTiles(strip, definition);
            }
            else if (definition.SummaryStyle == SummaryStyle.Sequence)
            {
                AddSequence(strip, definition);
            }
        }

        private static void AddTiles(VisualElement strip, GridScreenDefinition definition)
        {
            foreach (var item in definition.SummaryItems)
            {
                strip.Add(new StatTile { Value = item.Value, Caption = item.Caption });
            }
        }

        private static void AddSequence(VisualElement strip, GridScreenDefinition definition)
        {
            for (var index = 0; index < definition.SummaryItems.Count; index++)
            {
                if (index > 0)
                {
                    strip.Add(UiElementFactory.CreateLabel("→", "cf-phase__arrow"));
                }
                var chip = new VisualElement();
                chip.AddToClassList("cf-phase");
                chip.Add(new Label($"{index + 1}. {definition.SummaryItems[index].Caption}"));
                strip.Add(chip);
            }
        }
    }
}
