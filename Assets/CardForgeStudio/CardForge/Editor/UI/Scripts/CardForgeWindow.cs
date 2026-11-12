using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor.SceneManagement;
using Newtonsoft.Json.Linq;
using CardForge.Editor;
using CardForge.EditorUI.Dialogs;
using CardForge.EditorUI.Navigation;
using CardForge.EditorUI.Screens;
using CardForge.EditorUI.Grid;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.EditorUI
{
    public sealed partial class CardForgeWindow : EditorWindow
    {
        LiveCatalog catalog;
        ThemeController theme;
        ScreenHost screens;
        CardForgeDesignerView designer;
        SidebarNavigation navigation;
        VisualElement host;
        Label status, heading, context;
        Button backButton, newDocumentButton;
        DropdownField blockPicker;
        System.Collections.Generic.List<BlockDocument> blockChoices;
        string activeScreen = "layouts", openedScreen, openedId;

        public static void Open()
        {
            var window = GetWindow<CardForgeWindow>("CardForgeStudio");
            window.minSize = new Vector2(810, 570);
            var icon = CardForgeArtwork.Load("CardForge_AppIcon.png");
            window.titleContent = new GUIContent("CardForgeStudio", icon);
            window.Show();
            if (window.host != null) window.ShowHome();
        }
        public void CreateGUI()
        {
            string previousScreen = designer?.DocumentScreen ?? openedScreen;
            string previousId = designer?.DocumentId ?? openedId;
            designer?.Dispose(); designer = null; rootVisualElement.Clear();
            if (catalog == null) { catalog = new LiveCatalog(); activeScreen = EditorPrefs.GetString("CardForge.LastLibrary", "layouts"); }
            UiAssetLoader.LoadTree("UXML/CardForgeWindow.uxml").CloneTree(rootVisualElement);
            rootVisualElement.Q("brand").Insert(0, CardForgeArtwork.BrandMark());
            status = rootVisualElement.Q<Label>("status-label");
            heading = rootVisualElement.Q<Label>("workspace-title");
            context = rootVisualElement.Q<Label>("workspace-context");
            backButton = rootVisualElement.Q<Button>("editor-back");
            backButton.clicked += CloseCurrentEditor;
            var dashboard = new Button(ShowHome) { text = "DASHBOARD", tooltip = "Return to the main CardForge workbench" };
            dashboard.AddToClassList("cf-dashboard-return");
            rootVisualElement.Q("workspace-header").Insert(0, dashboard);
            newDocumentButton = rootVisualElement.Q<Button>("new-document");
            newDocumentButton.clicked += () => Run(() => OpenDesigner(activeScreen, catalog.Create(activeScreen)));
            host = rootVisualElement.Q("screen-host");
            theme = new ThemeController(rootVisualElement.Q("app-root"));
            var modal = new ModalHost(rootVisualElement.Q("modal-layer"));
            var definitions = catalog.Definitions();
            screens = new ScreenHost(host, definitions, modal);
            navigation = new SidebarNavigation(rootVisualElement.Q("navigation"), definitions);
            // Tools live on the Home workbench; the sidebar contains only content workspaces.
            navigation.Selected += id => Run(() => {
                if (id == "home") ShowHome();
                else if (id == "overview") ShowOverview();
                else if (id == "schema") { activeScreen = "cards"; OpenDesigner("schema", null); }
                else ShowLibrary(id);
            });
            screens.ActionRequested += HandleAction;
            screens.ImportExportConfirmed += (screen, result) => Run(() => Transfer(screen, result));
            blockPicker = new DropdownField { name = "block-picker", tooltip = "Choose the block for layouts and cards" };
            var blockContext = rootVisualElement.Q("block-context");
            var blockLabel = new Label("Active block"); blockLabel.AddToClassList("cf-navigation__caption");
            blockContext.Add(blockLabel); blockContext.Add(blockPicker);
            SyncBlockPicker();
            blockPicker.RegisterCallback<FocusInEvent>(_ => SyncBlockPicker());
            blockPicker.RegisterValueChangedCallback(e => {
                int index = blockPicker.choices.IndexOf(e.newValue);
                if (index < 0 || index >= blockChoices.Count) return;
                catalog.BlockId = blockChoices[index].id;
                if (activeScreen == "overview") ShowOverview();
                else if (activeScreen == "home") ShowHome();
                else ShowLibrary(activeScreen);
            });
            var file = new UnityEditor.UIElements.ToolbarMenu { text = "More options", name = "file-menu-button" };
            file.AddToClassList("cf-file-menu");
            file.menu.AppendAction("Save (Ctrl/Cmd+S)", _ => SaveCurrentDocument(),
                _ => designer == null ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            file.menu.AppendAction("Reload from disk", _ => Run(() => { catalog.Reload(); ShowLibrary(activeScreen); }));
            file.menu.AppendSeparator();
            file.menu.AppendAction("Go to Dashboard", _ => ShowHome());
            file.menu.AppendAction("Go to Overview", _ => ShowOverview());
            file.menu.AppendAction("Project validation", _ => OpenProjectAudit());
            file.menu.AppendSeparator();
            file.menu.AppendAction("Close editor", _ => CloseCurrentEditor(),
                _ => designer == null ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            file.menu.AppendAction("Exit studio", _ => ExitStudio());
            rootVisualElement.Q("file-menu").Add(file);
            var preferences = new UnityEditor.UIElements.ToolbarMenu { text = "Studio settings", name = "studio-settings" };
            preferences.AddToClassList("cf-settings-menu");
            preferences.menu.AppendAction("Switch theme", _ => theme.Toggle());
            preferences.menu.AppendAction("Show artwork", _ => { CardForgeArtwork.Enabled = !CardForgeArtwork.Enabled; CreateGUI(); },
                _ => CardForgeArtwork.Enabled ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            rootVisualElement.Q("preferences").Add(preferences);
            var audit = new Button(OpenProjectAudit) {
                text = "Project health",
                name = "project-health",
                tooltip = "Validate blocks, turn flows, triggers, actions and abilities"
            };
            audit.AddToClassList("cf-exit-button");
            audit.AddToClassList("cf-project-health-button");
            rootVisualElement.Q("preferences").Add(audit);
            rootVisualElement.RegisterCallback<KeyDownEvent>(e => {
                if ((e.ctrlKey || e.commandKey) && e.keyCode == KeyCode.S) {
                    SaveCurrentDocument();
                    e.StopPropagation();
                }
            });
            rootVisualElement.RegisterCallback<KeyDownEvent>(e => {
                if ((e.ctrlKey || e.commandKey) && e.keyCode == KeyCode.K) {
                    OpenQuickOpen();
                    e.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            var exit = new Button(ExitStudio) { text = "Exit studio", name = "exit-studio", tooltip = "Close CardForge and keep Unity open" };
            exit.AddToClassList("cf-exit-button"); rootVisualElement.Q("preferences").Add(exit);
            var quickOpenButton = new Button(OpenQuickOpen) { text = "Quick open  (Ctrl/Cmd+K)", name = "quick-open", tooltip = "Jump to any screen, document or tool" };
            quickOpenButton.AddToClassList("cf-exit-button");
            rootVisualElement.Q("preferences").Insert(0, quickOpenButton);
            if (previousScreen != null) OpenDesigner(previousScreen, previousId);
            else ShowHome();
        }
        public static void OpenHome() => Open();

        void OpenNativePlayScene()
        {
            const string scene = "Assets/CardForgeStudio/CardForgeClient/Scenes/TcgClient.unity";
            if (!File.Exists(scene))
            {
                EditorUtility.DisplayDialog("Game scene not found",
                    "Create the playable client scene using the CardForge sample preparation tool.", "OK");
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Leave Play Mode", "Exit Play Mode before opening another scene.", "OK");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(scene);
        }

        async void VerifyDemoAuthoring()
        {
            var script = Path.GetFullPath("Demo/Validate-AstralRiftAuthoring.ps1");
            if (Application.platform != RuntimePlatform.WindowsEditor || !File.Exists(script))
            {
                EditorUtility.DisplayDialog("Validation unavailable",
                    "The native authoring comparison is available in Demo/Validate-AstralRiftAuthoring.ps1 on Windows.", "OK");
                return;
            }
            status.text = "Checking authoring...";
            try
            {
                var output = await Task.Run(() =>
                {
                    var info = new System.Diagnostics.ProcessStartInfo("powershell.exe")
                    {
                        Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"",
                        WorkingDirectory = Path.GetDirectoryName(Application.dataPath),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (var process = System.Diagnostics.Process.Start(info))
                    {
                        if (process == null) throw new InvalidOperationException("Could not launch authoring validation.");
                        var standard = process.StandardOutput.ReadToEnd();
                        var errors = process.StandardError.ReadToEnd();
                        if (!process.WaitForExit(120000) || process.ExitCode != 0)
                            throw new InvalidOperationException(errors + "\n" + standard);
                        return standard;
                    }
                });
                EditorApplication.delayCall += () =>
                {
                    if (status != null) status.text = "Demo content aligned";
                    EditorUtility.DisplayDialog("Editor rule coverage", output, "OK");
                };
            }
            catch (Exception error)
            {
                EditorApplication.delayCall += () =>
                {
                    if (status != null) status.text = "Authoring parity failed";
                    EditorUtility.DisplayDialog("Editor rule coverage", error.Message, "OK");
                };
            }
        }

        async void SyncDemoAuthoring()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Exit Play Mode",
                    "The authoring database cannot be synchronized while the editor is playing.", "OK");
                return;
            }
            var script = Path.GetFullPath("Demo/Sync-AstralRiftAuthoring.ps1");
            if (Application.platform != RuntimePlatform.WindowsEditor || !File.Exists(script))
            {
                EditorUtility.DisplayDialog("Sync tool unavailable",
                    "The Astral Rift 3 sync tool requires the Windows Unity editor and Demo/Sync-AstralRiftAuthoring.ps1.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Synchronize Astral Rift 3.0",
                    "Rebuild the editable CardForge cards, abilities, actions, starter decks, resources and turn flow from the staged 3.0 rules?\n\nA timestamped ZIP backup is created before any changes.",
                    "SYNC & BACK UP", "CANCEL")) return;
            status.text = "Synchronizing demo...";
            try
            {
                var output = await Task.Run(() =>
                {
                    var info = new System.Diagnostics.ProcessStartInfo("powershell.exe")
                    {
                        Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"",
                        WorkingDirectory = Path.GetDirectoryName(Application.dataPath),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (var process = System.Diagnostics.Process.Start(info))
                    {
                        if (process == null) throw new InvalidOperationException("Unable to run the demo importer.");
                        var standard = process.StandardOutput.ReadToEnd();
                        var errors = process.StandardError.ReadToEnd();
                        if (!process.WaitForExit(180000) || process.ExitCode != 0)
                            throw new InvalidOperationException(errors + "\n" + standard);
                        return standard;
                    }
                });
                EditorApplication.delayCall += () =>
                {
                    AssetDatabase.Refresh();
                    catalog.Reload();
                    ShowHome();
                    EditorUtility.DisplayDialog("Astral Rift 3.0 editor synchronized",
                        "The editor now references the staged game cards, abilities, phase actions and starter decks.\n\n" +
                        output, "OK");
                };
            }
            catch (Exception error)
            {
                EditorApplication.delayCall += () =>
                {
                    if (status != null) status.text = "Sync failed";
                    Debug.LogException(error);
                    EditorUtility.DisplayDialog("Authoring sync failed", error.Message, "OK");
                };
            }
        }

        void RunMenu(string path)
        {
            if (!EditorApplication.ExecuteMenuItem(path))
                EditorUtility.DisplayDialog("Tool unavailable", "No Unity editor command was found at:\n" + path, "OK");
        }

        string CurrentRulesSummary()
        {
            var file = "Assets/StreamingAssets/CardForge/Rules/astral-rift.json";
            if (!File.Exists(file)) return "Rules not staged";
            try
            {
                var root = JObject.Parse(File.ReadAllText(file));
                var cards = (root["cards"] as JArray)?.Count ?? 0;
                var decks = (root["starterDecks"] as JArray)?.Count ?? 0;
                return cards + " published cards · " + decks + " starters";
            }
            catch { return "Rules need attention"; }
        }

        static VisualElement HomeSurface(string surface = "#232B2B", string border = "#48514A")
        {
            var panel = new VisualElement();
            panel.AddToClassList("cf-home__surface");
            panel.style.backgroundColor = ColorFromHex(surface);
            panel.style.borderTopColor = ColorFromHex(border);
            panel.style.borderRightColor = ColorFromHex(border);
            panel.style.borderBottomColor = ColorFromHex(border);
            panel.style.borderLeftColor = ColorFromHex(border);
            return panel;
        }

        static Color ColorFromHex(string value)
        {
            ColorUtility.TryParseHtmlString(value, out var color);
            return color;
        }

        static Label HomeText(string value, int size, string color, bool bold = false)
        {
            var label = new Label(value);
            label.style.fontSize = size;
            label.style.color = ColorFromHex(color);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 7;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        static VisualElement HomeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("cf-home__row");
            row.style.width = Length.Percent(100);
            row.style.minWidth = 0;
            row.style.flexWrap = Wrap.Wrap;
            return row;
        }

        static Button HomeButton(string label, System.Action action, bool primary = false)
        {
            var button = new Button(action) { text = label };
            button.AddToClassList(primary ? "cf-home__primary" : "cf-home__button");
            return button;
        }

        static VisualElement HomeMetric(string title, string value, string note)
        {
            var panel = HomeSurface();
            panel.AddToClassList("cf-home__metric");
            panel.style.minWidth = 230;
            panel.style.flexBasis = 270;
            panel.style.flexGrow = 1;
            panel.style.flexShrink = 1;
            panel.style.maxWidth = 365;
            panel.Add(HomeText(title, 11, "#D5B478", true));
            panel.Add(HomeText(value, 18, "#F1E7D5", true));
            panel.Add(HomeText(note, 12, "#B4C3BA"));
            return panel;
        }

        static void HomeSection(VisualElement scroll, string title, string description,
            params (string label, string description, System.Action action)[] commands)
        {
            scroll.Add(HomeText(title, 19, "#E8CC90", true));
            scroll.Add(HomeText(description, 13, "#B8C5BC"));
            var grid = HomeRow();
            grid.AddToClassList("cf-home__tiles");
            foreach (var command in commands)
            {
                var tile = HomeSurface();
                tile.AddToClassList("cf-home__tile");
                tile.style.flexGrow = 0;
                tile.style.flexBasis = 300;
                tile.style.maxWidth = 340;
                tile.Add(HomeButton(command.label, command.action));
                tile.Add(HomeText(command.description, 12, "#C7CEC4"));
                grid.Add(tile);
            }
            scroll.Add(grid);
        }

        void SyncBlockPicker()
        {
            if (blockPicker == null) return;
            blockChoices = catalog.Store.Blocks.Values.OrderBy(b => b.name).ToList();
            var labels = blockChoices.Select(b => b.code + " · " + b.name).ToList();
            var occurrences = new System.Collections.Generic.Dictionary<string, int>();
            var duplicated = labels.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            for (int i = 0; i < labels.Count; i++) {
                string label = labels[i];
                if (!duplicated.Contains(label)) continue;
                occurrences.TryGetValue(label, out var count); occurrences[label] = ++count;
                labels[i] = label + " (" + count + ")";
            }
            blockPicker.choices = labels;
            int selected = blockChoices.FindIndex(b => b.id == catalog.BlockId);
            blockPicker.SetValueWithoutNotify(selected >= 0 ? labels[selected] : "No blocks");
            blockPicker.SetEnabled(blockChoices.Count > 0);
        }
        void OnDisable() { designer?.Dispose(); }
        static string ScreenTitle(string screen) => StudioScreens.Title(screen);
        void ShowLibrary(string id)
        {
            designer?.Dispose(); designer = null; openedScreen = null; openedId = null;
            rootVisualElement.Q("app-root").EnableInClassList("cf-library-mode", true);
            rootVisualElement.Q("app-root").EnableInClassList("cf-editor-mode", false);
            rootVisualElement.Q("navigation-sidebar").style.display = DisplayStyle.Flex;
            activeScreen = id;
            rootVisualElement.Q<Button>(className: "cf-dashboard-return").style.display = DisplayStyle.Flex;
            EditorPrefs.SetString("CardForge.LastLibrary", id);
            screens.ReloadAll(); screens.Show(id); navigation.MarkActive(id);
            SyncBlockPicker(); heading.text = ScreenTitle(id);
            context.text = catalog.Block?.name ?? "Project library";
            backButton.AddToClassList("cf-hidden");
            rootVisualElement.Q("workspace-header").RemoveFromClassList("cf-hidden");
            newDocumentButton.text = "Create new";
            newDocumentButton.tooltip = "Create a new record in " + ScreenTitle(id);
            newDocumentButton.SetEnabled(id != "schema");
            newDocumentButton.EnableInClassList("cf-hidden", id == "schema");
            status.text = "Library ready";
        }
        void UpdateEditorHeading()
        {
            if (designer == null) return;
            openedScreen = designer.DocumentScreen; openedId = designer.DocumentId;
            if (openedScreen != "schema" && openedScreen != "keywords") activeScreen = openedScreen;
            rootVisualElement.Q("workspace-header").RemoveFromClassList("cf-hidden");
            heading.text = designer.DocumentTitle;
            context.text = ScreenTitle(openedScreen) + (catalog.Block == null ? "" : " / " + catalog.Block.name);
            backButton.text = "Close editor"; backButton.tooltip = "Return to " + ScreenTitle(activeScreen);
            backButton.RemoveFromClassList("cf-hidden");
            newDocumentButton.AddToClassList("cf-hidden");
            navigation.MarkActive(openedScreen == "keywords" ? "abilities" : openedScreen);
            status.text = "Autosave";
        }
        void OpenDesigner(string screen, string id)
        {
            designer?.Dispose(); openedScreen = screen; openedId = id;
            designer = new CardForgeDesignerView(catalog.Store, screen, id, catalog.BlockId);
            rootVisualElement.Q<Button>(className: "cf-dashboard-return").style.display = DisplayStyle.Flex;
            designer.Closed += () => ShowLibrary(activeScreen);
            designer.ContextChanged += UpdateEditorHeading;
            rootVisualElement.Q("app-root").EnableInClassList("cf-library-mode", false);
            rootVisualElement.Q("app-root").EnableInClassList("cf-editor-mode", true);
            rootVisualElement.Q("navigation-sidebar").style.display = DisplayStyle.Flex;
            host.Clear(); host.Add(designer); UpdateEditorHeading(); SyncBlockPicker();
            RecentDocuments.Record(screen, id);
        }
        void CloseCurrentEditor()
        {
            rootVisualElement.focusController?.focusedElement?.Blur();
            ShowLibrary(activeScreen);
        }
        void ExitStudio()
        {
            rootVisualElement.focusController?.focusedElement?.Blur();
            Close();
        }
        void SaveCurrentDocument()
        {
            if (designer == null) return;
            try {
                rootVisualElement.focusController?.focusedElement?.Blur();
                designer.SaveDocument(); status.text = "Saved";
            } catch (Exception e) { status.text = e.Message; EditorUtility.DisplayDialog("Could not save", e.Message, "OK"); }
        }
        void Run(System.Action action)
        {
            try { action();status.text=designer==null?"":"Autosave"; }
            catch(Exception e) { status.text=e.Message;Debug.LogException(e);EditorUtility.DisplayDialog("CardForge",e.Message,"OK"); }
        }
        void HandleAction(GridActionRequest request)=>Run(()=> {
            string screen=request.ScreenId;var rows=request.Rows;
            switch(request.ActionName) {
                case "Create":OpenDesigner(screen,catalog.Create(screen));return;
                case "Duplicate":foreach(var row in rows)catalog.Duplicate(screen,row.Id);break;
                case "Delete":
                    if(!EditorUtility.DisplayDialog("Delete records?",$"Delete {rows.Count} selected record(s) from disk? This cannot be undone.","Delete","Cancel"))return;
                    foreach(var row in rows)catalog.Delete(screen,row.Id);break;
                case "Activate":if(rows.Count==1)catalog.BlockId=rows[0].Id;break;
                case "Validate":if(rows.Count==1){var issues=catalog.Store.Validate(catalog.Store.Blocks[rows[0].Id]);EditorUtility.DisplayDialog("Validation",issues.Count==0?"Block is ready to export.":string.Join("\n",issues),"OK");}return;
                case "Build bundle":if(rows.Count==1)EditorUtility.RevealInFinder(catalog.Store.ExportBlock(catalog.Store.Blocks[rows[0].Id],EditorUserBuildSettings.activeBuildTarget));return;
                case "Export":DataTransfer.Export(catalog,screen,rows.Select(r=>r.Id).ToArray(),false);return;
                default:if(rows.Count==1)OpenDesigner(screen,rows[0].Id);return;
            }
            ShowLibrary(activeScreen);
        });
        static void AddToolNavigation(VisualElement sidebar, string label, System.Action action)
        {
            var button = new Button(action) { text = label, tooltip = "Open " + label };
            button.AddToClassList("cf-navigation__item");
            button.AddToClassList("cf-navigation__tool");
            sidebar.Add(button);
        }
        void OpenQuickOpen()
        {
            rootVisualElement.focusController?.focusedElement?.Blur();
            var commands = new QuickOpenCommands {
                Navigate = id => navigation.Select(id),
                CreateDocument = screen => HandleAction(new GridActionRequest(screen, "Create", Array.Empty<GridRowViewModel>())),
                OpenDocument = (screen, id) => Run(() => OpenDesigner(screen, id)),
                ToolEntries = BuildToolEntries()
            };
            QuickOpen.Show(rootVisualElement.Q("modal-layer"), QuickOpenEntries.Build(catalog, commands));
        }
        System.Collections.Generic.IReadOnlyList<QuickOpen.Entry> BuildToolEntries()
        {
            System.Action refreshLibrary = () => { catalog.Reload(); if (designer == null) ShowLibrary(activeScreen); };
            return new[] {
                new QuickOpen.Entry("Studio", "Overview", "Open the integrated game overview, readiness and starter decks", ShowOverview),
                new QuickOpen.Entry("Tools", "Project health", "Validate blocks, flows, triggers, actions and abilities", OpenProjectAudit),
                new QuickOpen.Entry("Tools", "Reload from disk", "Re-read every JSON document", () => Run(() => { catalog.Reload(); ShowLibrary(activeScreen); })),
                new QuickOpen.Entry("Tools", "Frame Composer", "Extract zones from a frame image", () => CardForgeFrameComposerWindow.Open(catalog.Store, catalog.BlockId, refreshLibrary)),
                new QuickOpen.Entry("Tools", "Icon Set Studio", "Import an atlas or a folder of icons", () => CardForgeIconSetWindow.Open(catalog.Store, refreshLibrary)),
                new QuickOpen.Entry("Tools", "Google Fonts", "Import a font family or a local font", CardForgeFontImporter.Open),
                new QuickOpen.Entry("Tools", "Game Package Publisher", "Compile a versioned .cardforge release for the authoritative server", CardForgeGamePackageWindow.Open),
                new QuickOpen.Entry("Tools", "Ollama AI settings", "Provider, model and session key", CardForgeAiSettings.Open),
                new QuickOpen.Entry("Tools", "Switch theme", "Toggle dark and light", () => theme.Toggle())
            };
        }
        void OpenProjectAudit()
        {
            CardForgeProjectAudit.Show(catalog.Store, (screen, id) => {
                Focus();
                Run(() => OpenDesigner(screen, id));
            });
        }
        void OpenImport(string screen)=>ImportWizard.Open(catalog,screen,()=>ShowLibrary(activeScreen));
        void Transfer(string screen,ImportExportResult result)
        {
            if(result.Mode==ImportExportMode.Import) { ImportWizard.Open(catalog,screen,()=>ShowLibrary(activeScreen),result.FilePath);return; }
            var controllerRows=result.RowIds ?? catalog.Rows(screen).Select(r=>r.Id).ToArray();
            DataTransfer.Export(catalog,screen,controllerRows,result.Format==ExportFormat.Csv,result.IncludeLinkedAssets);
        }
    }
}
