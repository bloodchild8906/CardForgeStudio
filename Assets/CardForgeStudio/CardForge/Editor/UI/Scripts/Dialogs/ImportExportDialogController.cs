using System;
using System.IO;
using CardForge.EditorUI.Controls;
using UnityEditor;
using UnityEngine.UIElements;

namespace CardForge.EditorUI.Dialogs
{
    /// <summary>Opens the import and export dialogs and reports what the user chose.</summary>
    public sealed class ImportExportDialogController
    {
        private readonly ModalHost modalHost;
        private VisualElement overlay;
        private string selectedImportPath;

        /// <summary>Creates a controller that shows dialogs through the given host.</summary>
        public ImportExportDialogController(ModalHost modalHost) => this.modalHost = modalHost;

        /// <summary>Shows the import dialog.</summary>
        public void ShowImport(string itemNoun, Func<string, ImportPreviewSummary?> analyzeFile, Action<ImportExportResult> onConfirmed)
        {
            Open($"Import {itemNoun}s", "Import");
            selectedImportPath = null;
            overlay.Q("export-section").AddToClassList("cf-hidden");
            var confirmButton = overlay.Q<Button>("confirm-button");
            confirmButton.SetEnabled(false);
            overlay.Q<Button>("dropzone").clicked += () => ChooseImportFile(analyzeFile);
            confirmButton.clicked += () => Confirm(ReadImportResult(), onConfirmed);
        }

        /// <summary>Shows the export dialog.</summary>
        public void ShowExport(string itemNoun, int selectedCount, Action<ImportExportResult> onConfirmed)
        {
            Open($"Export {itemNoun}s", "Export");
            overlay.Q("import-section").AddToClassList("cf-hidden");
            var selectedOnlyToggle = overlay.Q<Toggle>("selected-only");
            selectedOnlyToggle.SetEnabled(selectedCount > 0);
            selectedOnlyToggle.SetValueWithoutNotify(selectedCount > 0);
            overlay.Q<Button>("confirm-button").clicked += () => Confirm(ReadExportResult(), onConfirmed);
        }

        private void Open(string title, string confirmCaption)
        {
            overlay = UiAssetLoader.InstantiateRoot("UXML/Components/ImportExportDialog.uxml", "overlay");
            overlay.Q<Label>("dialog-title").text = title;
            overlay.Q<Button>("confirm-button").text = confirmCaption;
            overlay.Q<Button>("cancel-button").clicked += modalHost.Close;
            overlay.RegisterCallback<ClickEvent>(click =>
            {
                if (click.target == overlay)
                {
                    modalHost.Close();
                }
            });
            modalHost.Show(overlay);
        }

        private void ChooseImportFile(Func<string, ImportPreviewSummary?> analyzeFile)
        {
            var path = EditorUtility.OpenFilePanel("Import", string.Empty, "json,csv");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            selectedImportPath = path;
            var dropzone = overlay.Q<Button>("dropzone");
            dropzone.text = Path.GetFileName(path);
            dropzone.AddToClassList("cf-dropzone--filled");
            overlay.Q<Button>("confirm-button").SetEnabled(true);
            ApplyPreview(analyzeFile?.Invoke(path));
        }

        private void ApplyPreview(ImportPreviewSummary? summary)
        {
            var chips = overlay.Q("import-preview");
            chips.EnableInClassList("cf-hidden", !summary.HasValue);
            if (!summary.HasValue)
            {
                return;
            }
            overlay.Q<StatusPill>("preview-created").text = $"{summary.Value.CreatedCount} new";
            overlay.Q<StatusPill>("preview-changed").text = $"{summary.Value.ChangedCount} changed";
            overlay.Q<StatusPill>("preview-errors").text = $"{summary.Value.ErrorCount} errors";
        }

        private ImportExportResult ReadImportResult()
        {
            return new ImportExportResult
            {
                Mode = ImportExportMode.Import,
                FilePath = selectedImportPath,
                ConflictMode = (ImportConflictMode)overlay.Q<RadioButtonGroup>("conflict-mode").value
            };
        }

        private ImportExportResult ReadExportResult()
        {
            return new ImportExportResult
            {
                Mode = ImportExportMode.Export,
                Format = (ExportFormat)overlay.Q<RadioButtonGroup>("export-format").value,
                SelectedOnly = overlay.Q<Toggle>("selected-only").value,
                IncludeLinkedAssets = overlay.Q<Toggle>("include-assets").value
            };
        }

        private void Confirm(ImportExportResult result, Action<ImportExportResult> onConfirmed)
        {
            modalHost.Close();
            onConfirmed?.Invoke(result);
        }
    }
}
