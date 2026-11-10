using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace CardForge.EditorUI.Grid
{
    /// <summary>Builds the filter sidebar from row data and answers whether a row passes the active filters.</summary>
    public sealed class SidebarFilterController
    {
        private const string AllValue = "";

        private readonly ScrollView sidebar;
        private readonly IReadOnlyList<FilterGroupDefinition> groups;
        private readonly Dictionary<string, string> activeValueByColumnId = new Dictionary<string, string>();
        private IReadOnlyList<GridRowViewModel> rows = Array.Empty<GridRowViewModel>();

        /// <summary>Number of groups with an applied value.</summary>
        public int ActiveCount => activeValueByColumnId.Count(pair => pair.Value != AllValue);

        /// <summary>Raised after the user picks a filter option.</summary>
        public event Action FiltersChanged;

        /// <summary>Hides the sidebar when there are no groups.</summary>
        public SidebarFilterController(ScrollView sidebar, IReadOnlyList<FilterGroupDefinition> groups)
        {
            this.sidebar = sidebar;
            this.groups = groups;
            sidebar.EnableInClassList("cf-hidden", groups.Count == 0);
        }

        /// <summary>Recomputes options and counts from the given rows.</summary>
        public void Rebuild(IReadOnlyList<GridRowViewModel> sourceRows)
        {
            rows = sourceRows;
            sidebar.Clear();
            // Discard values removed from the current dataset instead of retaining an invisible filter.
            foreach (var column in activeValueByColumnId.Keys.ToList())
            {
                var selected = activeValueByColumnId[column];
                if (selected != AllValue && !rows.Any(row => row.GetCell(column).Text == selected))
                    activeValueByColumnId.Remove(column);
            }
            if (ActiveCount > 0)
            {
                var clear = new Button(Clear) { text = "Clear filters" };
                clear.AddToClassList("cf-filter-clear");
                sidebar.Add(clear);
            }
            foreach (var group in groups)
            {
                AddGroup(group);
            }
        }

        /// <summary>True when the row satisfies every active filter.</summary>
        public bool Matches(GridRowViewModel row)
        {
            return activeValueByColumnId.All(pair => pair.Value == AllValue || row.GetCell(pair.Key).Text == pair.Value);
        }

        /// <summary>Removes applied filters and refreshes the library.</summary>
        public void Clear()
        {
            activeValueByColumnId.Clear();
            Rebuild(rows);
            FiltersChanged?.Invoke();
        }

        private void AddGroup(FilterGroupDefinition group)
        {
            sidebar.Add(UiElementFactory.CreateLabel(group.Title.ToUpperInvariant(), "cf-sidebar__group-title"));
            AddItem(group.ColumnId, AllValue, "All", rows.Count);
            var valueGroups = rows
                .GroupBy(row => row.GetCell(group.ColumnId).Text)
                .Where(valueGroup => valueGroup.Key.Length > 0)
                .OrderByDescending(valueGroup => valueGroup.Count());
            foreach (var valueGroup in valueGroups)
            {
                AddItem(group.ColumnId, valueGroup.Key, valueGroup.Key, valueGroup.Count());
            }
        }

        private void AddItem(string columnId, string value, string caption, int count)
        {
            var item = new VisualElement();
            item.AddToClassList("cf-filter-item");
            item.EnableInClassList("cf-filter-item--active", GetActiveValue(columnId) == value);
            item.Add(UiElementFactory.CreateLabel(caption, "cf-filter-item__label"));
            item.Add(UiElementFactory.CreateLabel(count.ToString(), "cf-filter-item__count"));
            item.RegisterCallback<ClickEvent>(_ => Select(columnId, value));
            sidebar.Add(item);
        }

        private string GetActiveValue(string columnId)
        {
            return activeValueByColumnId.TryGetValue(columnId, out var value) ? value : AllValue;
        }

        private void Select(string columnId, string value)
        {
            activeValueByColumnId[columnId] = value;
            Rebuild(rows);
            FiltersChanged?.Invoke();
        }
    }
}
