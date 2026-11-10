using System.Collections.Generic;

namespace CardForge.EditorUI.Grid
{
    /// <summary>Supplies rows for a grid, typically backed by the JSON data folder.</summary>
    public interface IGridRowSource
    {
        /// <summary>Loads the current rows.</summary>
        IReadOnlyList<GridRowViewModel> LoadRows();
    }
}
