namespace CardForge.EditorUI.Dialogs
{
    /// <summary>Whether the dialog imports or exports.</summary>
    public enum ImportExportMode
    {
        Import,
        Export
    }

    /// <summary>How an import treats items that already exist.</summary>
    public enum ImportConflictMode
    {
        Merge,
        ReplaceAll,
        SkipDuplicates
    }

    /// <summary>File format chosen for an export.</summary>
    public enum ExportFormat
    {
        Json,
        Csv,
        PrintSheet
    }

    /// <summary>Dry-run counts shown as chips before an import is confirmed.</summary>
    public readonly struct ImportPreviewSummary
    {
        /// <summary>Creates a summary.</summary>
        public ImportPreviewSummary(int createdCount, int changedCount, int errorCount)
        {
            CreatedCount = createdCount;
            ChangedCount = changedCount;
            ErrorCount = errorCount;
        }

        /// <summary>Items that would be created.</summary>
        public int CreatedCount { get; }

        /// <summary>Items that would be changed.</summary>
        public int ChangedCount { get; }

        /// <summary>Items that failed validation.</summary>
        public int ErrorCount { get; }
    }

    /// <summary>The user's choices when the dialog was confirmed.</summary>
    public sealed class ImportExportResult
    {
        /// <summary>Import or export.</summary>
        public ImportExportMode Mode { get; set; }

        /// <summary>Source file for imports.</summary>
        public string FilePath { get; set; }
        public string[] RowIds { get; set; }

        /// <summary>Conflict handling for imports.</summary>
        public ImportConflictMode ConflictMode { get; set; }

        /// <summary>Format for exports.</summary>
        public ExportFormat Format { get; set; }

        /// <summary>True to export only the selected rows.</summary>
        public bool SelectedOnly { get; set; }

        /// <summary>True to bundle linked assets with the export.</summary>
        public bool IncludeLinkedAssets { get; set; }
    }
}
