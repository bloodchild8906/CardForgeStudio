using System;
using System.Collections.Generic;
using CardForge.EditorUI.Dialogs;

namespace CardForge.EditorUI.Grid
{
    /// <summary>Declarative description of a screen; the generic grid controller renders it.</summary>
    public sealed class GridScreenDefinition
    {
        /// <summary>Unique screen id.</summary>
        public string Id { get; set; }
        public Func<GridRowViewModel, UnityEngine.UIElements.VisualElement> BuildPreview { get; set; }
        public Func<IReadOnlyList<SummaryItemDefinition>> BuildSummary { get; set; }

        /// <summary>Caption on the tab.</summary>
        public string TabLabel { get; set; }

        /// <summary>Title in the toolbar.</summary>
        public string Title { get; set; }

        /// <summary>Caption of the primary add button.</summary>
        public string AddButtonLabel { get; set; }

        /// <summary>Singular noun used in status text and dialogs.</summary>
        public string ItemNoun { get; set; }

        /// <summary>How the strip above the grid looks.</summary>
        public SummaryStyle SummaryStyle { get; set; } = SummaryStyle.None;

        /// <summary>Visual shown at the top of the inspector.</summary>
        public InspectorPreview Preview { get; set; } = InspectorPreview.None;

        /// <summary>Items of the summary strip.</summary>
        public IReadOnlyList<SummaryItemDefinition> SummaryItems { get; set; } = Array.Empty<SummaryItemDefinition>();

        /// <summary>Data columns, left to right.</summary>
        public IReadOnlyList<GridColumnDefinition> Columns { get; set; } = Array.Empty<GridColumnDefinition>();

        /// <summary>Sidebar filter groups.</summary>
        public IReadOnlyList<FilterGroupDefinition> FilterGroups { get; set; } = Array.Empty<FilterGroupDefinition>();

        /// <summary>Button captions shown in the inspector; the first is primary.</summary>
        public IReadOnlyList<string> InspectorActions { get; set; } = Array.Empty<string>();

        /// <summary>Where rows come from.</summary>
        public IGridRowSource RowSource { get; set; }

        /// <summary>Optional dry-run analysis used for the import preview chips.</summary>
        public Func<string, ImportPreviewSummary?> ImportAnalyzer { get; set; }
    }
}
