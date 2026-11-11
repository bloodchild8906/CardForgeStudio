using System.Collections.Generic;

namespace CardForge.EditorUI.Grid
{
    /// <summary>An action the user triggered on one or more rows.</summary>
    public sealed class GridActionRequest
    {
        /// <summary>Creates a request.</summary>
        public GridActionRequest(string screenId, string actionName, IReadOnlyList<GridRowViewModel> rows)
        {
            ScreenId = screenId;
            ActionName = actionName;
            Rows = rows;
        }

        /// <summary>Screen the action came from.</summary>
        public string ScreenId { get; }

        /// <summary>Caption of the action button.</summary>
        public string ActionName { get; }

        /// <summary>Rows the action applies to.</summary>
        public IReadOnlyList<GridRowViewModel> Rows { get; }
    }
}
