namespace CardForge.EditorUI.Grid
{
    /// <summary>A sidebar filter group whose options come from the distinct values of a column.</summary>
    public sealed class FilterGroupDefinition
    {
        /// <summary>Creates a filter group.</summary>
        public FilterGroupDefinition(string title, string columnId)
        {
            Title = title;
            ColumnId = columnId;
        }

        /// <summary>Heading shown above the options.</summary>
        public string Title { get; }

        /// <summary>Column whose values become options.</summary>
        public string ColumnId { get; }
    }

    /// <summary>One tile or phase chip in the summary strip.</summary>
    public sealed class SummaryItemDefinition
    {
        /// <summary>Creates a summary item.</summary>
        public SummaryItemDefinition(string value, string caption)
        {
            Value = value;
            Caption = caption;
        }

        /// <summary>Headline value; empty for sequence chips.</summary>
        public string Value { get; }

        /// <summary>Caption or phase name.</summary>
        public string Caption { get; }
    }
}
