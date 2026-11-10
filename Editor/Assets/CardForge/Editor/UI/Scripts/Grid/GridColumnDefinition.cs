namespace CardForge.EditorUI.Grid
{
    /// <summary>Describes one data column of a grid screen.</summary>
    public sealed class GridColumnDefinition
    {
        /// <summary>Column id used for the thumbnail column.</summary>
        public const string ThumbnailColumnId = "thumbnail";

        private GridColumnDefinition(string id, string header, float width, GridColumnKind kind, bool isPrimary)
        {
            Id = id;
            Header = header;
            Width = width;
            Kind = kind;
            IsPrimary = isPrimary;
        }

        /// <summary>Key into <see cref="GridRowViewModel"/> cells.</summary>
        public string Id { get; }

        /// <summary>Header caption.</summary>
        public string Header { get; }

        /// <summary>Initial width in pixels.</summary>
        public float Width { get; }

        /// <summary>Rendering kind.</summary>
        public GridColumnKind Kind { get; }

        /// <summary>True for the bold, stretching name column.</summary>
        public bool IsPrimary { get; }

        /// <summary>The name column.</summary>
        public static GridColumnDefinition Primary(string header)
        {
            return new GridColumnDefinition(GridRowViewModel.NameColumnId, header, 220f, GridColumnKind.Text, true);
        }

        /// <summary>A plain text column.</summary>
        public static GridColumnDefinition Text(string id, string header, float width)
        {
            return new GridColumnDefinition(id, header, width, GridColumnKind.Text, false);
        }

        /// <summary>A status pill column.</summary>
        public static GridColumnDefinition Pill(string id, string header, float width)
        {
            return new GridColumnDefinition(id, header, width, GridColumnKind.Pill, false);
        }

        /// <summary>A mini card thumbnail column.</summary>
        public static GridColumnDefinition Thumbnail()
        {
            return new GridColumnDefinition(ThumbnailColumnId, string.Empty, 56f, GridColumnKind.Thumbnail, false);
        }
    }
}
