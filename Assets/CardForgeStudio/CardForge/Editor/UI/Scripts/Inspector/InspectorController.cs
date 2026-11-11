using System;
using System.Collections.Generic;
using System.Linq;
using CardForge.EditorUI.Controls;
using CardForge.EditorUI.Grid;
using UnityEngine.UIElements;

namespace CardForge.EditorUI.Inspector
{
    /// <summary>Fills the right-hand inspector for the current grid selection.</summary>
    public sealed class InspectorController
    {
        private readonly VisualElement container;
        private readonly GridScreenDefinition definition;

        /// <summary>Raised with the action caption and the inspected rows.</summary>
        public event Action<string, IReadOnlyList<GridRowViewModel>> ActionInvoked;
        public event Action SelectionCleared;

        /// <summary>Binds the controller to its container.</summary>
        public InspectorController(VisualElement container, GridScreenDefinition definition)
        {
            this.container = container;
            this.definition = definition;
        }

        /// <summary>Rebuilds the inspector for the given selection.</summary>
        public void Show(IReadOnlyList<GridRowViewModel> rows)
        {
            container.Clear();
            if (rows.Count == 0)
            {
                AddEmptyState();
            }
            else if (rows.Count == 1)
            {
                AddSingleRow(rows[0]);
            }
            else
            {
                AddMultipleRows(rows);
            }
        }

        private void AddEmptyState()
        {
            container.Add(UiElementFactory.CreateLabel("Inspector", "cf-inspector__title"));
            container.Add(UiElementFactory.CreateLabel("Nothing selected", "cf-inspector__subtitle"));
            container.Add(UiElementFactory.CreateLabel($"Select a {definition.ItemNoun} to see its details.", "cf-inspector__empty"));
        }

        private void AddMultipleRows(IReadOnlyList<GridRowViewModel> rows)
        {
            container.Add(UiElementFactory.CreateLabel($"{rows.Count} {definition.ItemNoun}s selected", "cf-inspector__title"));
            container.Add(UiElementFactory.CreateLabel(string.Join(", ", rows.Take(4).Select(row => row.DisplayName)), "cf-inspector__subtitle"));
            container.Add(UiElementFactory.CreateLabel("Use the bulk bar above the grid to act on the selection.", "cf-inspector__empty"));
        }

        private void AddSingleRow(GridRowViewModel row)
        {
            var top=new VisualElement(); top.AddToClassList("cf-inspector__top");
            top.Add(UiElementFactory.CreateLabel("SELECTION", "cf-inspector__eyebrow"));
            var close=new Button(()=>SelectionCleared?.Invoke()) { text="×", tooltip="Clear selection", name="close-inspector" };
            close.AddToClassList("cf-inspector__close");top.Add(close);container.Add(top);
            container.Add(UiElementFactory.CreateLabel(row.DisplayName, "cf-inspector__title"));
            container.Add(UiElementFactory.CreateLabel(definition.ItemNoun, "cf-inspector__subtitle"));
            if (definition.BuildPreview != null) container.Add(definition.BuildPreview(row));
            foreach (var column in definition.Columns.Where(IsDetailColumn))
            {
                container.Add(CreateDetailRow(column, row));
            }
            container.Add(CreateActionRow(row));
        }

        private static bool IsDetailColumn(GridColumnDefinition column)
        {
            return !column.IsPrimary && column.Kind != GridColumnKind.Thumbnail;
        }

        private static VisualElement CreateDetailRow(GridColumnDefinition column, GridRowViewModel row)
        {
            var cell = row.GetCell(column.Id);
            var detailRow = new VisualElement();
            detailRow.AddToClassList("cf-kv");
            detailRow.Add(UiElementFactory.CreateLabel(column.Header, "cf-kv__key"));
            detailRow.Add(column.Kind == GridColumnKind.Pill
                ? new StatusPill { text = cell.Text, Tone = cell.Tone }
                : UiElementFactory.CreateLabel(cell.Text, "cf-kv__value"));
            return detailRow;
        }

        private VisualElement CreateActionRow(GridRowViewModel row)
        {
            var actionRow = new VisualElement();
            actionRow.AddToClassList("cf-inspector__actions");
            for (var index = 0; index < definition.InspectorActions.Count; index++)
            {
                var actionName = definition.InspectorActions[index];
                var button = new Button(() => ActionInvoked?.Invoke(actionName, new[] { row })) { text = actionName };
                button.AddToClassList("cf-button");
                button.EnableInClassList("cf-button--primary", index == 0);
                button.EnableInClassList("cf-button--danger", actionName == "Delete");
                actionRow.Add(button);
            }
            return actionRow;
        }
    }
}
