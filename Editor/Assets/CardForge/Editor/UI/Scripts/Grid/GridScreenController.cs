using System;
using System.Collections.Generic;
using System.Linq;
using CardForge.EditorUI.Dialogs;
using CardForge.EditorUI.Inspector;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.EditorUI.Grid
{
    /// <summary>Renders a <see cref="GridScreenDefinition"/> as a searchable, sortable, filterable datagrid with an inspector.</summary>
    public sealed class GridScreenController
    {
        private const float RowHeight = 48f;
        private const long SearchDebounceMilliseconds = 160;

        private readonly GridScreenDefinition definition;
        private readonly ImportExportDialogController dialogController;
        private readonly List<GridRowViewModel> allRows = new List<GridRowViewModel>();
        private readonly List<GridRowViewModel> visibleRows = new List<GridRowViewModel>();
        private readonly MultiColumnListView grid;
        private bool refreshingSelection;
        private IVisualElementScheduledItem searchDebounce;
        private bool filtersVisible;
        private bool inspectorVisible = true;
        private readonly TwoPaneSplitView splitView;
        private readonly ScrollView filterSidebar;
        private readonly Button filtersButton;
        private readonly TextField searchField;
        private readonly VisualElement bulkBar;
        private readonly Label bulkLabel;
        private readonly Label statusBar;
        private readonly SidebarFilterController filters;
        private readonly InspectorController inspector;
        private readonly ListView gallery;
        private readonly List<int> galleryRows = new List<int>();
        private bool galleryMode;
        private int galleryColumns = 3;
        private int selectionAnchor;
        private bool SupportsGallery => definition.Id == "layouts" || definition.Id == "cards" || definition.Id == "icons";

        /// <summary>Raised when an inspector or bulk action is clicked.</summary>
        public event Action<GridActionRequest> ActionRequested;

        /// <summary>Raised when the import or export dialog is confirmed.</summary>
        public event Action<string, ImportExportResult> ImportExportConfirmed;

        /// <summary>Builds the screen and loads its rows.</summary>
        public GridScreenController(GridScreenDefinition definition, ModalHost modalHost)
        {
            this.definition = definition;
            dialogController = new ImportExportDialogController(modalHost);
            Root = UiAssetLoader.InstantiateRoot("UXML/Screens/GridScreen.uxml", "screen-root");
            grid = Root.Q<MultiColumnListView>("grid");
            searchField = Root.Q<TextField>("search-field");
            bulkBar = Root.Q("bulk-bar");
            bulkLabel = Root.Q<Label>("bulk-label");
            statusBar = Root.Q<Label>("statusbar");
            splitView = Root.Q<TwoPaneSplitView>();
            filterSidebar = Root.Q<ScrollView>("sidebar");
            filtersButton = Root.Q<Button>("filters-button");
            filters = new SidebarFilterController(filterSidebar, definition.FilterGroups);
            filterSidebar.AddToClassList("cf-hidden");
            inspector = new InspectorController(Root.Q("inspector"), definition);
            inspector.SelectionCleared += () => grid.ClearSelection();
            gallery = Root.Q<ListView>("gallery");

            Root.Q("main-pane").Insert(0, CardForge.Editor.CardForgeArtwork.Banner(definition.Id, definition.Title));
            ConfigureToolbar();
            ConfigureBulkBar();
            ConfigureShortcuts();
            ConfigureGrid();
            ConfigureGallery();
            SummaryStripBuilder.Build(Root.Q("summary"), definition);
            filters.FiltersChanged += () => { UpdateFiltersButton(); ApplyFilters(); };
            inspector.ActionInvoked += RaiseAction;
            Reload();
        }

        /// <summary>Root element to place in the screen host.</summary>
        public VisualElement Root { get; }

        /// <summary>Reloads rows from the source and refreshes filters and grid.</summary>
        public void Reload()
        {
            allRows.Clear();
            allRows.AddRange(definition.RowSource.LoadRows());
            filters.Rebuild(allRows);
            UpdateFiltersButton();
            if(definition.BuildSummary != null) definition.SummaryItems = definition.BuildSummary();
            SummaryStripBuilder.Build(Root.Q("summary"), definition);
            ApplyFilters();
        }

        private void ConfigureToolbar()
        {
            Root.Q<Label>("toolbar-title").text = definition.Title;
            Root.Q<Label>("toolbar-title").AddToClassList("cf-hidden");
            filtersButton.EnableInClassList("cf-hidden", definition.FilterGroups.Count == 0);
            filtersButton.clicked += () =>
            {
                filtersVisible = !filtersVisible;
                filterSidebar.EnableInClassList("cf-hidden", !filtersVisible);
                UpdateFiltersButton();
            };
            Root.Q<Button>("add-button").text = definition.AddButtonLabel;
            Root.Q<Button>("add-button").clicked += () => RaiseAction("Create", Array.Empty<GridRowViewModel>());
            Root.Q<Button>("import-button").clicked += OpenImportDialog;
            Root.Q<Button>("export-button").clicked += OpenExportDialog;
            searchDebounce = searchField.schedule.Execute(ApplyFilters);
            searchDebounce.Pause();
            searchField.RegisterValueChangedCallback(_ => searchDebounce.ExecuteLater(SearchDebounceMilliseconds));
            searchField.RegisterCallback<KeyDownEvent>(e => {
                if(e.keyCode != KeyCode.Escape || string.IsNullOrEmpty(searchField.value)) return;
                searchField.value = string.Empty;
                e.StopPropagation();
            });
            searchField.tooltip = "Search " + definition.Title.ToLowerInvariant();
            Root.Q<Button>("empty-action").clicked += () => {
                if(allRows.Count == 0) RaiseAction("Create", Array.Empty<GridRowViewModel>());
                else { searchField.SetValueWithoutNotify(""); filters.Clear(); }
            };
        }

        private void UpdateFiltersButton()
        {
            filtersButton.text = filters.ActiveCount == 0 ? "Filters" : $"Filters ({filters.ActiveCount})";
            filtersButton.EnableInClassList("cf-button--selected", filtersVisible || filters.ActiveCount > 0);
        }

        private void ConfigureBulkBar()
        {
            Root.Q<Button>("clear-selection").clicked += () => grid.ClearSelection();
            Root.Q<Button>("bulk-duplicate").clicked += () => RaiseAction("Duplicate", GetSelectedRows());
            Root.Q<Button>("bulk-delete").clicked += () => RaiseAction("Delete", GetSelectedRows());
            Root.Q<Button>("bulk-export").clicked += OpenExportDialog;
        }

        private void ConfigureShortcuts()
        {
            Root.RegisterCallback<KeyDownEvent>(HandleShortcut);
        }

        private void HandleShortcut(KeyDownEvent evt)
        {
            bool command = evt.ctrlKey || evt.commandKey;
            if(command && evt.keyCode == KeyCode.F)
            {
                searchField.Focus();
                evt.StopPropagation();
                return;
            }
            if(IsTypingInTextField(evt.target)) return;
            var selectedRows = GetSelectedRows();
            if(selectedRows.Count == 0) return;
            bool deletePressed = evt.keyCode == KeyCode.Delete || (evt.commandKey && evt.keyCode == KeyCode.Backspace);
            if(deletePressed) RaiseAction("Delete", selectedRows);
            else if(command && evt.keyCode == KeyCode.D) RaiseAction("Duplicate", selectedRows);
            else return;
            evt.StopPropagation();
        }

        private static bool IsTypingInTextField(IEventHandler target)
        {
            var element = target as VisualElement;
            return element != null && (element is TextField || element.GetFirstAncestorOfType<TextField>() != null);
        }

        private void ConfigureGrid()
        {
            grid.sortingMode = ColumnSortingMode.Custom;
            grid.selectionType = SelectionType.Multiple;
            grid.fixedItemHeight = RowHeight;
            grid.itemsSource = visibleRows;
            grid.columns.Add(SelectionColumnFactory.Create(IsRowSelected, SetRowSelected, SetAllRowsSelected));
            foreach (var column in definition.Columns)
            {
                grid.columns.Add(GridColumnFactory.Create(column, index => visibleRows[index]));
            }
            grid.itemsChosen += _ => { var rows = GetSelectedRows(); if(rows.Count == 1) RaiseAction(definition.InspectorActions[0], rows); };
            grid.selectedIndicesChanged += _ => OnSelectionChanged();
            grid.columnSortingChanged += ApplyFilters;
        }

        private void ApplyFilters()
        {
            var selectedIds = GetSelectedRows().Select(r => r.Id).ToHashSet();
            refreshingSelection = true;
            visibleRows.Clear();
            visibleRows.AddRange(allRows.Where(row => filters.Matches(row) && row.MatchesQuery(searchField.value)));
            GridRowSorter.Sort(visibleRows, grid.sortedColumns);
            grid.ClearSelection();
            grid.RefreshItems();
            grid.SetSelection(visibleRows.Select((row, i) => new { row, i }).Where(x => selectedIds.Contains(x.row.Id)).Select(x => x.i));
            refreshingSelection = false;
            RebuildGallery();
            UpdateView();
            OnSelectionChanged();
        }

        private void OnSelectionChanged()
        {
            if(refreshingSelection) return;
            var selectedRows = GetSelectedRows();
            bulkBar.EnableInClassList("cf-hidden", selectedRows.Count < 2);
            bulkLabel.text = $"{selectedRows.Count} selected";
            statusBar.text = $"{visibleRows.Count} of {allRows.Count} {definition.ItemNoun}s" + (selectedRows.Count > 0 ? $"  ·  {selectedRows.Count} selected" : "  ·  Double-click to open  ·  Ctrl/Cmd+K to jump anywhere");
            inspector.Show(selectedRows);
            bool showInspector = selectedRows.Count == 1;
            if (showInspector != inspectorVisible)
            {
                inspectorVisible = showInspector;
                if (showInspector) splitView.UnCollapse();
                else splitView.CollapseChild(1);
            }
            refreshingSelection=true;
            try { grid.RefreshItems(); } finally { refreshingSelection=false; }
            foreach(var tile in gallery.Query<Button>(className:"cf-gallery-card").ToList())
                tile.EnableInClassList("cf-gallery-card--selected", tile.userData is int index && IsRowSelected(index));
        }

        private void ConfigureGallery()
        {
            Root.Q("view-switch").EnableInClassList("cf-hidden", !SupportsGallery);
            galleryMode = SupportsGallery && EditorPrefs.GetBool("CardForge.Gallery." + definition.Id, true);
            gallery.fixedItemHeight = 304;
            gallery.itemsSource = galleryRows;
            gallery.makeItem = () => { var row = new VisualElement(); row.AddToClassList("cf-gallery-row"); return row; };
            gallery.bindItem = (row, rowIndex) => {
                row.Clear();
                for(int column=0; column<galleryColumns; column++) {
                    int index=rowIndex*galleryColumns+column;
                    if(index>=visibleRows.Count) { var spacer=new VisualElement(); spacer.AddToClassList("cf-gallery-spacer");row.Add(spacer);continue; }
                    var item=visibleRows[index];
                    var tile=new Button { name="gallery-item-"+index, userData=index, tooltip=item.DisplayName+" · Double-click to open" };
                    tile.AddToClassList("cf-gallery-card"); tile.EnableInClassList("cf-gallery-card--selected",IsRowSelected(index));
                    if(definition.BuildPreview!=null) tile.Add(definition.BuildPreview(item));
                    var text=new VisualElement { pickingMode=PickingMode.Ignore }; text.AddToClassList("cf-gallery-card__body");
                    text.Add(UiElementFactory.CreateLabel(item.DisplayName,"cf-gallery-card__title"));
                    var detail=definition.Columns.Where(c=>!c.IsPrimary && c.Id!="asset" && c.Id!="status").Take(2)
                        .Select(c=>c.Header+": "+item.GetCell(c.Id).Text);
                    text.Add(UiElementFactory.CreateLabel(string.Join("  ·  ",detail),"cf-gallery-card__detail"));
                    tile.Add(text);
                    tile.RegisterCallback<ClickEvent>(e=> {
                        SelectGalleryItem(index,e.ctrlKey||e.commandKey,e.shiftKey);
                        if(e.clickCount==2) RaiseAction(definition.InspectorActions[0],new[]{item});
                    });
                    tile.RegisterCallback<KeyDownEvent>(e=> {
                        int next=index;
                        if(e.keyCode==KeyCode.Return || e.keyCode==KeyCode.KeypadEnter) RaiseAction(definition.InspectorActions[0],new[]{item});
                        else if(e.keyCode==KeyCode.Space) SelectGalleryItem(index,true,false);
                        else if(e.keyCode==KeyCode.RightArrow) next++;
                        else if(e.keyCode==KeyCode.LeftArrow) next--;
                        else if(e.keyCode==KeyCode.DownArrow) next+=galleryColumns;
                        else if(e.keyCode==KeyCode.UpArrow) next-=galleryColumns;
                        else return;
                        if(next!=index && visibleRows.Count>0) {
                            next=Mathf.Clamp(next,0,visibleRows.Count-1); SelectGalleryItem(next,false,e.shiftKey);
                            gallery.ScrollToItem(next/galleryColumns);
                            gallery.schedule.Execute(()=>gallery.Q<Button>("gallery-item-"+next)?.Focus());
                        }
                        e.StopPropagation();
                    });
                    row.Add(tile);
                }
            };
            gallery.RegisterCallback<GeometryChangedEvent>(e=> {
                int columns=Mathf.Clamp(Mathf.FloorToInt((e.newRect.width-24)/212),1,6);
                if(columns!=galleryColumns) { galleryColumns=columns;RebuildGallery(); }
            });
            Root.Q<Button>("gallery-view").clicked+=()=>SetGalleryMode(true);
            Root.Q<Button>("table-view").clicked+=()=>SetGalleryMode(false);
        }
        private void SelectGalleryItem(int index,bool toggle,bool range)
        {
            if(range) {
                int first=Mathf.Clamp(Math.Min(selectionAnchor,index),0,visibleRows.Count-1);
                int last=Mathf.Clamp(Math.Max(selectionAnchor,index),0,visibleRows.Count-1);
                grid.SetSelection(Enumerable.Range(first,last-first+1));
            } else { if(toggle) SetRowSelected(index,!IsRowSelected(index)); else grid.SetSelection(new[]{index}); selectionAnchor=index; }
        }
        private void SetGalleryMode(bool value)
        {
            galleryMode=SupportsGallery && value;
            EditorPrefs.SetBool("CardForge.Gallery."+definition.Id,galleryMode);
            UpdateView();
        }
        private void RebuildGallery()
        {
            galleryRows.Clear();
            for(int i=0;i<visibleRows.Count;i+=galleryColumns) galleryRows.Add(i/galleryColumns);
            gallery.Rebuild();
        }
        private void UpdateView()
        {
            bool empty=visibleRows.Count==0;
            grid.EnableInClassList("cf-hidden",galleryMode||empty);
            gallery.EnableInClassList("cf-hidden",!galleryMode||empty);
            Root.Q("empty-state").EnableInClassList("cf-hidden",!empty);
            Root.Q<Label>("empty-title").text=allRows.Count==0 ? "Create your first "+definition.ItemNoun : "No matches found";
            Root.Q<Label>("empty-description").text=allRows.Count==0 ? "Start with a new "+definition.ItemNoun+" or import your existing work." : "Try another search or clear your filters to see everything.";
            Root.Q<Button>("empty-action").text=allRows.Count==0 ? definition.AddButtonLabel : "Clear search & filters";
            Root.Q<Button>("gallery-view").EnableInClassList("cf-view-switch--active",galleryMode);
            Root.Q<Button>("table-view").EnableInClassList("cf-view-switch--active",!galleryMode);
        }

        private List<GridRowViewModel> GetSelectedRows()
        {
            return grid.selectedIndices.Where(index => index >= 0 && index < visibleRows.Count).Select(index => visibleRows[index]).ToList();
        }

        private bool IsRowSelected(int index) => grid.selectedIndices.Contains(index);

        private void SetRowSelected(int index, bool selected)
        {
            if (selected)
            {
                grid.AddToSelection(index);
            }
            else
            {
                grid.RemoveFromSelection(index);
            }
        }

        private void SetAllRowsSelected(bool selected)
        {
            if (selected)
            {
                grid.SetSelection(Enumerable.Range(0, visibleRows.Count));
            }
            else
            {
                grid.ClearSelection();
            }
        }

        private void RaiseAction(string actionName, IReadOnlyList<GridRowViewModel> rows)
        {
            ActionRequested?.Invoke(new GridActionRequest(definition.Id, actionName, rows));
        }

        private void OpenImportDialog()
        {
            ImportExportConfirmed?.Invoke(definition.Id, new ImportExportResult { Mode = ImportExportMode.Import });
        }

        private void OpenExportDialog()
        {
            dialogController.ShowExport(definition.ItemNoun, GetSelectedRows().Count, result => { result.RowIds = result.SelectedOnly ? GetSelectedRows().Select(r=>r.Id).ToArray() : allRows.Select(r=>r.Id).ToArray(); ImportExportConfirmed?.Invoke(definition.Id, result); });
        }
    }
}
