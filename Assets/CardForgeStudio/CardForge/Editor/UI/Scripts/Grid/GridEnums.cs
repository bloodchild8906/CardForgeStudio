namespace CardForge.EditorUI.Grid
{
    /// <summary>How a column renders its cell.</summary>
    public enum GridColumnKind
    {
        Text,
        Pill,
        Thumbnail
    }

    /// <summary>How the strip above the grid presents its items.</summary>
    public enum SummaryStyle
    {
        None,
        Tiles,
        Sequence
    }

    /// <summary>Optional visual shown at the top of the inspector.</summary>
    public enum InspectorPreview
    {
        None,
        CardSurface
    }
}
