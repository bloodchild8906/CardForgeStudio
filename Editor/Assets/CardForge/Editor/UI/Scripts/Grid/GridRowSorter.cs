using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.UIElements;

namespace CardForge.EditorUI.Grid
{
    /// <summary>Sorts rows by the primary sort column chosen in the grid header.</summary>
    internal static class GridRowSorter
    {
        public static void Sort(List<GridRowViewModel> rows, IEnumerable<SortColumnDescription> descriptions)
        {
            var primary = descriptions?.FirstOrDefault();
            if (primary == null)
            {
                return;
            }
            var direction = primary.direction == SortDirection.Descending ? -1 : 1;
            rows.Sort((left, right) => direction * Compare(left.GetCell(primary.columnName).Text, right.GetCell(primary.columnName).Text));
        }

        private static int Compare(string left, string right)
        {
            var bothNumeric = double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber)
                              & double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber);
            return bothNumeric
                ? leftNumber.CompareTo(rightNumber)
                : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
