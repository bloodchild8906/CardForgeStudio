using System;
using CardForge.Editor;
using CardForge.EditorUI;
using TcgServer.Client.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.Learn
{
    /// <summary>The UI Toolkit home for authoring, sample generation, local Play, publishing, and learning.</summary>
    public sealed class CardForgeDashboardWindow : EditorWindow
    {
        Label statusLabel;
        Button generateButton;
        Button playButton;

        [MenuItem("CardForge/Dashboard")]
        public static void Open()
        {
            var window = GetWindow<CardForgeDashboardWindow>();
            var icon = CardForgeArtwork.Load("CardForge_AppIcon.png");
            window.titleContent = new GUIContent("CardForgeStudio", icon);
            window.minSize = new Vector2(1040, 680);
            window.Show();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.backgroundColor = Hex("#0C121B");
            rootVisualElement.style.color = Hex("#EAF0F7");

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.contentContainer.style.paddingLeft = 28;
            scroll.contentContainer.style.paddingRight = 28;
            scroll.contentContainer.style.paddingTop = 24;
            scroll.contentContainer.style.paddingBottom = 30;
            rootVisualElement.Add(scroll);

            scroll.Add(BuildHeader());
            scroll.Add(BuildPlayPanel());

            var columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.flexWrap = Wrap.Wrap;
            columns.style.marginTop = 18;
            columns.Add(BuildGroup("DESIGN", new[]
            {
                new ToolEntry("CardForge Studio", "Create cards, layouts, icons, abilities, actions, triggers, and turn flows.", CardForgeWindow.Open),
                new ToolEntry("Frame Composer", "Extract an editable layout from frame artwork.", () => CardForgeFrameComposerWindow.Open()),
                new ToolEntry("Icon Set Studio", "Import, slice, and organize portable game icons.", () => CardForgeIconSetWindow.Open()),
                new ToolEntry("Google Fonts", "Import licensed typefaces for card layouts.", CardForgeFontImporter.Open)
            }));
            columns.Add(BuildGroup("RULES & RELEASES", new[]
            {
                new ToolEntry("Game Package Publisher", "Validate and export an immutable .cardforge release.", CardForgeGamePackageWindow.Open),
                new ToolEntry("Project Health", "Open the Studio and inspect structural and reference validation.", CardForgeWindow.Open),
                new ToolEntry("AI Provider Settings", "Configure the optional vision provider used by creator tools.", CardForgeAiSettings.Open),
                new ToolEntry("Regenerate Sample", "Refresh Astral Rift content, decks, artwork, package, and client scene.", GenerateDemo)
            }));
            columns.Add(BuildGroup("PLAY & LEARN", new[]
            {
                new ToolEntry("Learn and Templates", "Follow the guided track or create the smaller Starter Duel project.", CardForgeLearnWindow.Open),
                new ToolEntry("Create Client Scene", "Create or refresh the Unity client without changing game content.", CreateClientScene),
                new ToolEntry("Start Server Dashboard", "Open the native server management experience for manual operations.", CardForgeLessons.StartServer),
                new ToolEntry("Open Repository", "Browse the server, shared contract, documentation, packages, and exports.", OpenRepository)
            }));
            scroll.Add(columns);

            var paths = Card("PROJECT CONNECTIONS");
            paths.Add(Body("Unity project\n" + CardForgeLearnPaths.ProjectRoot));
            paths.Add(Body("Server\n" + (CardForgeLearnPaths.ServerFolder ?? "Not found")));
            paths.Add(Body("Game packages\n" + (CardForgeLearnPaths.GamePackagesFolder ?? "Not found")));
            paths.style.marginTop = 8;
            scroll.Add(paths);

            CardForgePlayWorkflow.StatusChanged -= OnWorkflowStatus;
            CardForgePlayWorkflow.StatusChanged += OnWorkflowStatus;
            OnWorkflowStatus(CardForgePlayWorkflow.Status);
        }

        void OnDisable() => CardForgePlayWorkflow.StatusChanged -= OnWorkflowStatus;

        VisualElement BuildHeader()
        {
            var header = Card(null);
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingTop = 22;
            header.style.paddingBottom = 22;
            var logo = CardForgeArtwork.Load("CardForge_LogoMark.png");
            if (logo != null)
            {
                var image = new Image { image = logo, scaleMode = ScaleMode.ScaleToFit };
                image.style.width = 72; image.style.height = 72; image.style.marginRight = 20;
                header.Add(image);
            }
            var copy = new VisualElement { style = { flexGrow = 1 } };
            copy.Add(Eyebrow("CARDFORGE STUDIO · AUTHOR · SYNC · PLAY"));
            var title = new Label("Build the game, then press Play.");
            title.style.fontSize = 30; title.style.unityFontStyleAndWeight = FontStyle.Bold; title.style.marginTop = 5; title.style.marginBottom = 7;
            copy.Add(title);
            copy.Add(Body("Generate Sample now creates a complete Astral Rift project. Unity Play validates it, starts an isolated local server when necessary, uploads and activates the current package, and launches the configured client scene."));
            header.Add(copy);
            return header;
        }

        VisualElement BuildPlayPanel()
        {
            var panel = Card(null);
            panel.style.marginTop = 16;
            panel.style.borderLeftWidth = 4;
            panel.style.borderLeftColor = Hex("#55A7F2");

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            var copy = new VisualElement { style = { flexGrow = 1, marginRight = 20 } };
            copy.Add(Eyebrow("ONE-CLICK DEVELOPMENT LOOP"));
            var title = new Label("Astral Rift playable sample");
            title.style.fontSize = 22; title.style.unityFontStyleAndWeight = FontStyle.Bold; title.style.marginTop = 4; title.style.marginBottom = 5;
            copy.Add(title);
            copy.Add(Body("12 authored cards · 2 legal 24-card decks · 5 phases · 14 actions · keywords and abilities · 4 victory paths · portable art and frames"));
            row.Add(copy);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            generateButton = PrimaryButton("1  Generate Sample", GenerateDemo, false);
            playButton = PrimaryButton("2  Play Sample", CardForgePlayWorkflow.PlaySample, true);
            actions.Add(generateButton); actions.Add(playButton); row.Add(actions); panel.Add(row);

            statusLabel = new Label();
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            statusLabel.style.marginTop = 14;
            statusLabel.style.paddingTop = 10; statusLabel.style.paddingBottom = 10; statusLabel.style.paddingLeft = 12; statusLabel.style.paddingRight = 12;
            statusLabel.style.backgroundColor = Hex("#101A27");
            statusLabel.style.borderTopLeftRadius = 5; statusLabel.style.borderTopRightRadius = 5; statusLabel.style.borderBottomLeftRadius = 5; statusLabel.style.borderBottomRightRadius = 5;
            panel.Add(statusLabel);
            return panel;
        }

        VisualElement BuildGroup(string title, ToolEntry[] tools)
        {
            var group = new VisualElement();
            group.style.flexGrow = 1;
            group.style.flexBasis = 300;
            group.style.minWidth = 300;
            group.style.marginRight = 12;
            group.style.marginBottom = 12;
            group.Add(Eyebrow(title));
            foreach (var tool in tools)
            {
                var card = Card(null);
                card.style.marginTop = 8;
                var name = new Label(tool.Title);
                name.style.fontSize = 15; name.style.unityFontStyleAndWeight = FontStyle.Bold; name.style.marginBottom = 4;
                card.Add(name); card.Add(Body(tool.Description));
                var open = new Button(() => RunAction(tool.Action)) { text = "Open" };
                open.style.marginTop = 10; open.style.height = 28; open.style.alignSelf = Align.FlexStart;
                card.Add(open); group.Add(card);
            }
            return group;
        }

        static VisualElement Card(string eyebrow)
        {
            var card = new VisualElement();
            card.style.backgroundColor = Hex("#151E2A");
            card.style.paddingLeft = 18; card.style.paddingRight = 18; card.style.paddingTop = 16; card.style.paddingBottom = 16;
            card.style.borderTopLeftRadius = 7; card.style.borderTopRightRadius = 7; card.style.borderBottomLeftRadius = 7; card.style.borderBottomRightRadius = 7;
            card.style.borderTopWidth = 1; card.style.borderRightWidth = 1; card.style.borderBottomWidth = 1; card.style.borderLeftWidth = 1;
            card.style.borderTopColor = Hex("#2C3A4D"); card.style.borderRightColor = Hex("#2C3A4D"); card.style.borderBottomColor = Hex("#2C3A4D"); card.style.borderLeftColor = Hex("#2C3A4D");
            if (!string.IsNullOrEmpty(eyebrow)) card.Add(Eyebrow(eyebrow));
            return card;
        }

        static Label Eyebrow(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 10; label.style.letterSpacing = 1.4f; label.style.color = Hex("#75B8F5"); label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        static Label Body(string text)
        {
            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal; label.style.color = Hex("#AAB6C5"); label.style.fontSize = 12;
            return label;
        }

        static Button PrimaryButton(string text, Action action, bool accent)
        {
            var button = new Button(action) { text = text };
            button.style.height = 38; button.style.minWidth = 150; button.style.marginLeft = 8;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.backgroundColor = Hex(accent ? "#287BC1" : "#253348");
            button.style.color = Hex("#F4F8FC");
            return button;
        }

        public static void CreateClientScene()
        {
            if (!EditorUtility.DisplayDialog("Create or Refresh Client Scene", "This creates or replaces Assets/CardForgeClient/Scenes/TcgClient.unity and adds it to the build settings. Continue?", "Continue", "Cancel")) return;
            RunAction(TcgTemplateProjectSetup.CreateTemplateScene);
        }

        static void GenerateDemo()
        {
            if (!EditorUtility.DisplayDialog("Generate Complete Sample", "Create or refresh Astral Rift content, artwork, two starter decks, its .cardforge release, and the configured Unity client scene?", "Generate", "Cancel")) return;
            RunAction(() =>
            {
                var output = CardForgePlayWorkflow.GenerateSample();
                EditorUtility.DisplayDialog("Sample ready", "Astral Rift is ready. Press Unity Play or use Play Sample.\n\nRelease package:\n" + output, "OK");
            });
        }

        static void OpenRepository()
        {
            var root = CardForgeLearnPaths.RepoRoot;
            if (string.IsNullOrEmpty(root))
            {
                EditorUtility.DisplayDialog("Repository not found", "Open the Unity project from the CardForgeStudio repository root.", "OK");
                return;
            }
            EditorUtility.RevealInFinder(root);
        }

        void OnWorkflowStatus(string value)
        {
            if (statusLabel != null) statusLabel.text = value;
            if (generateButton != null) generateButton.SetEnabled(!CardForgePlayWorkflow.IsRunning);
            if (playButton != null) playButton.SetEnabled(!CardForgePlayWorkflow.IsRunning);
            Repaint();
        }

        static void RunAction(Action action)
        {
            try { action(); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("CardForge action failed", exception.Message, "OK");
            }
        }

        static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString(value, out var color);
            return color;
        }

        readonly struct ToolEntry
        {
            public readonly string Title;
            public readonly string Description;
            public readonly Action Action;
            public ToolEntry(string title, string description, Action action) { Title = title; Description = description; Action = action; }
        }
    }
}
