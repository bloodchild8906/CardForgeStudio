using System;
using System.Collections.Generic;
using System.Linq;

namespace CardForge.EditorUI.Grid
{
    /// <summary>Read-only row shown in a grid and inspected on the right.</summary>
    public sealed class GridRowViewModel
    {
        /// <summary>Column id every row fills with its display name.</summary>
        public const string NameColumnId = "name";

        private readonly Dictionary<string, GridCell> cellsByColumnId;

        private GridRowViewModel(string id, string displayName, Dictionary<string, GridCell> cellsByColumnId)
        {
            Id = id;
            DisplayName = displayName;
            this.cellsByColumnId = cellsByColumnId;
        }

        /// <summary>Stable identifier of the underlying record.</summary>
        public string Id { get; }

        /// <summary>Human readable name.</summary>
        public string DisplayName { get; }

        /// <summary>Creates a row; the name cell is added automatically.</summary>
        public static GridRowViewModel Create(string id, string displayName, params (string columnId, GridCell cell)[] cells)
        {
            var cellsByColumnId = cells.ToDictionary(entry => entry.columnId, entry => entry.cell);
            cellsByColumnId[NameColumnId] = GridCell.FromText(displayName);
            return new GridRowViewModel(id, displayName, cellsByColumnId);
        }

        /// <summary>Returns the cell for a column, or an empty cell when absent.</summary>
        public GridCell GetCell(string columnId)
        {
            return cellsByColumnId.TryGetValue(columnId, out var cell) ? cell : GridCell.FromText(string.Empty);
        }

        /// <summary>True when any cell contains the query, ignoring case.</summary>
        public bool MatchesQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }
            return cellsByColumnId.Values.Any(cell => cell.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
