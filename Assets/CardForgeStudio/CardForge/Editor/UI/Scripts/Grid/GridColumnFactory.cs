using System;
using CardForge.EditorUI.Controls;
using UnityEngine.UIElements;

namespace CardForge.EditorUI.Grid
{
    /// <summary>Creates MultiColumnListView columns from column definitions.</summary>
    internal static class GridColumnFactory
    {
        public static Column Create(GridColumnDefinition definition, Func<int, GridRowViewModel> rowAt)
        {
            return new Column
            {
                name = definition.Id,
                title = definition.Header,
                width = definition.Width,
                stretchable = definition.IsPrimary,
                sortable = definition.Kind != GridColumnKind.Thumbnail,
                makeCell = () => CreateCell(definition),
                bindCell = (element, index) => BindCell(element, definition, rowAt(index).GetCell(definition.Id))
            };
        }

        private static VisualElement CreateCell(GridColumnDefinition definition)
        {
            switch (definition.Kind)
            {
                case GridColumnKind.Pill:
                    return new StatusPill();
                case GridColumnKind.Thumbnail:
                    var thumbnail = new VisualElement();
                    thumbnail.AddToClassList("cf-thumb");
                    return thumbnail;
                default:
                    var label = new Label();
                    label.AddToClassList("cf-cell");
                    label.EnableInClassList("cf-cell--primary", definition.IsPrimary);
                    return label;
            }
        }

        private static void BindCell(VisualElement element, GridColumnDefinition definition, GridCell cell)
        {
            switch (definition.Kind)
            {
                case GridColumnKind.Pill:
                    var pill = (StatusPill)element;
                    pill.text = cell.Text;
                    pill.Tone = cell.Tone;
                    pill.EnableInClassList("cf-hidden", cell.Text.Length == 0);
                    break;
                case GridColumnKind.Thumbnail:
                    element.EnableInClassList("cf-thumb--back", cell.Text == "back");
                    break;
                default:
                    ((Label)element).text = cell.Text;
                    break;
            }
        }
    }
}
