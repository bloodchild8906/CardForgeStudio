using System;
using System.IO;
using Newtonsoft.Json.Linq;
using CardForge.Editor;
using CardForge.EditorUI.Dialogs;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.EditorUI
{
    public sealed partial class CardForgeWindow
    {
        // This is the primary Studio experience. Specialist editors are launched
        // from here; source-control, file operations and diagnostics stay secondary.
        private void ShowHome()
        {
            if (host == null || catalog == null) return;
            designer?.Dispose();
            designer = null;
            openedScreen = null;
            openedId = null;
            activeScreen = "home";

            var app = rootVisualElement.Q<VisualElement>("app-root");
            app.EnableInClassList("cf-library-mode", true);
            app.EnableInClassList("cf-editor-mode", false);
            rootVisualElement.Q<VisualElement>("navigation-sidebar").style.display = DisplayStyle.Flex;
            heading.text = "Dashboard";
            context.text = "Astral Rift  /  Studio workspace";
            status.text = "PROJECT WORKSPACE";
            navigation.MarkActive("home");
            rootVisualElement.Q<Button>(className: "cf-dashboard-return").style.display = DisplayStyle.None;
            backButton.AddToClassList("cf-hidden");
            newDocumentButton.AddToClassList("cf-hidden");
            host.Clear();
            var workspace = HubPanel("cf-dashboard-workspace");
            workspace.style.flexGrow = 1;
            workspace.style.minHeight = 0;
            host.Add(workspace);
            workspace.Add(CardForge.Editor.CardForgeArtwork.Banner("home", "Dashboard"));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("cf-hub");
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;
            scroll.style.width = Length.Percent(100);
            scroll.style.maxWidth = Length.Percent(100);
            scroll.contentContainer.style.paddingLeft = 20;
            scroll.contentContainer.style.paddingRight = 20;
            scroll.contentContainer.style.paddingTop = 18;
            scroll.contentContainer.style.paddingBottom = 32;
            scroll.contentContainer.style.width = Length.Percent(100);
            scroll.contentContainer.style.maxWidth = Length.Percent(100);
            scroll.contentContainer.style.alignSelf = Align.Stretch;
            scroll.contentContainer.style.alignItems = Align.FlexStart;
            workspace.Add(scroll);
            var page = new VisualElement();
            page.AddToClassList("cf-hub__page");
            page.style.width = 1160;
            page.style.maxWidth = 1160;
            page.style.alignSelf = Align.FlexStart;
            page.style.flexShrink = 0;
            scroll.Add(page);

            JObject rules = null;
            try
            {
                const string rulesPath = "Assets/StreamingAssets/CardForge/Rules/astral-rift.json";
                if (File.Exists(rulesPath)) rules = JObject.Parse(File.ReadAllText(rulesPath));
            }
            catch (Exception exception) when (exception is IOException || exception is Newtonsoft.Json.JsonException)
            {
                Debug.LogWarning("CardForge rules preview is unavailable: " + exception.Message);
            }

            int cards = (rules?["cards"] as JArray)?.Count ?? 0;
            int decks = (rules?["starterDecks"] as JArray)?.Count ?? 0;
            int phases = (rules?["phases"] as JArray)?.Count ?? 0;
            int actions = (rules?["actions"] as JArray)?.Count ?? 0;

            // Same compact toolbar and workspace hierarchy used by the card,
            // action and ability editors. This is not a separate game menu.
            var hero = HubPanel("cf-hub__hero");
            var heroRow = HubRow("cf-hub__hero-row");
            var copy = new VisualElement();
            copy.AddToClassList("cf-hub__hero-copy");
            copy.Add(HubLabel("ACTIVE PROJECT  /  ASTRAL RIFT", "cf-hub__eyebrow"));
            copy.Add(HubLabel("Create, test and publish", "cf-hub__title"));
            copy.Add(HubLabel("Manage the game from one workspace. Use the same tools and rules editors throughout Studio.",
                "cf-hub__intro"));
            heroRow.Add(copy);
            var actionsRow = HubRow("cf-hub__hero-actions");
            actionsRow.Add(HubButton("PLAY DEMO", () => RunMenu("CardForge/Play Sample Game"), "cf-hub__cta"));
            actionsRow.Add(HubButton("EDIT CARDS", () => ShowLibrary("cards"), "cf-hub__cta-alt"));
            actionsRow.Add(HubButton("GENERATE DEMO", () => RunMenu("CardForge/Generate Sample Game"), "cf-hub__cta-alt"));
            heroRow.Add(actionsRow);
            hero.Add(heroRow);
            page.Add(hero);
            // Overview is the game command centre, not a second admin dashboard.
            // The status and deck artwork are derived from the staged game files.
            var pulse = BuildGameOverview(rules);
            page.Add(pulse);

            page.Add(HubSectionHeading("EDITOR TOOLS", "The same card, layout and rules editors used across CardForge Studio."));

            var creativeRow = HubRow("cf-hub__primary-grid");
            var cardsPanel = HubPanel("cf-hub__path");
            cardsPanel.AddToClassList("cf-hub__path-cards");
            cardsPanel.Add(HubLabel("01  /  CARD FORGE", "cf-hub__eyebrow"));
            cardsPanel.Add(HubLabel("Cards & presentation", "cf-hub__path-title"));
            cardsPanel.Add(HubLabel("Create creatures, spells, commanders and mana sources with their own frames, artwork and abilities.", "cf-hub__path-desc"));
            var cardLinks = HubRow("cf-hub__link-row");
            cardLinks.Add(HubButton("CARD DESIGNER", () => ShowLibrary("cards"), "cf-hub__path-button"));
            cardLinks.Add(HubButton("CARD LAYOUTS", () => ShowLibrary("layouts"), "cf-hub__path-button"));
            cardLinks.Add(HubButton("BLOCKS & SETS", () => ShowLibrary("blocks"), "cf-hub__path-button"));
            cardLinks.Add(HubButton("FRAME COMPOSER", () => CardForgeFrameComposerWindow.Open(), "cf-hub__path-button"));
            foreach (var shortcut in cardLinks.Children())
            {
                shortcut.style.flexBasis = Length.Percent(44);
                shortcut.style.maxWidth = Length.Percent(49);
            }
            cardsPanel.Add(cardLinks);
            creativeRow.Add(cardsPanel);

            var rulesPanel = HubPanel("cf-hub__path");
            rulesPanel.AddToClassList("cf-hub__path-rules");
            rulesPanel.Add(HubLabel("02  /  RULES ENGINE", "cf-hub__eyebrow"));
            rulesPanel.Add(HubLabel("Rules & gameplay", "cf-hub__path-title"));
            rulesPanel.Add(HubLabel("Author the exact actions, triggers and reactions your game server executes — with readable flow diagrams.", "cf-hub__path-desc"));
            var ruleLinks = HubRow("cf-hub__link-row");
            ruleLinks.Add(HubButton("TURN FLOW", () => ShowLibrary("turnflow"), "cf-hub__path-button"));
            ruleLinks.Add(HubButton("ACTIONS", () => ShowLibrary("actions"), "cf-hub__path-button"));
            ruleLinks.Add(HubButton("ABILITIES", () => ShowLibrary("abilities"), "cf-hub__path-button"));
            ruleLinks.Add(HubButton("TRIGGERS", () => ShowLibrary("triggers"), "cf-hub__path-button"));
            foreach (var shortcut in ruleLinks.Children())
            {
                shortcut.style.flexBasis = Length.Percent(44);
                shortcut.style.maxWidth = Length.Percent(49);
            }
            rulesPanel.Add(ruleLinks);
            creativeRow.Add(rulesPanel);
            page.Add(creativeRow);

            page.Add(HubSectionHeading("TEST & PUBLISH", "Prepare the editable Unity scene, test the server and publish your game."));
            var journey = HubRow("cf-hub__journey");
            journey.Add(HubStep("01", "PREPARE", "Generate the demo and fill the editable Unity scene.",
                "GENERATE DEMO", () => RunMenu("CardForge/Generate Sample Game")));
            journey.Add(HubStep("02", "TEST", "Inspect the arena layout, then play against a rival.",
                "PLAY IN UNITY", () => RunMenu("CardForge/Play Sample Game")));
            journey.Add(HubStep("03", "PUBLISH", "Validate and export a .cardforge edition for your players.",
                "PUBLISH PACKAGE", CardForgeGamePackageWindow.Open));
            page.Add(journey);

            var tools = new Foldout { text = "MORE CREATOR TOOLS  /  Art, UI, diagnostics and publishing", value = false };
            tools.AddToClassList("cf-hub__more");
            var toolLinks = HubRow("cf-hub__more-grid");
            toolLinks.Add(HubButton("EDIT MATCH LAYOUT", () => OpenHubLayout(
                "Assets/CardForgeStudio/CardForgeClient/Resources/AstralRift/CardForgeMatchLayout.uxml"), "cf-hub__tool"));
            toolLinks.Add(HubButton("EDIT GAME UI", () => OpenHubLayout(
                "Assets/CardForgeStudio/CardForgeClient/UI/AstralRiftArena.uxml"), "cf-hub__tool"));
            toolLinks.Add(HubButton("EDIT SCENE", OpenNativePlayScene, "cf-hub__tool"));
            toolLinks.Add(HubButton("IMPORT ICONS", () => CardForgeIconSetWindow.Open(), "cf-hub__tool"));
            toolLinks.Add(HubButton("DATA SCHEMA", () => OpenDesigner("schema", null), "cf-hub__tool"));
            toolLinks.Add(HubButton("AI SETTINGS", CardForgeAiSettings.Open, "cf-hub__tool"));
            toolLinks.Add(HubButton("VERIFY CONTENT", VerifyDemoAuthoring, "cf-hub__tool"));
            toolLinks.Add(HubButton("BUILD SERVER", () => RunMenu("CardForge/Server/Build Native Windows Dedicated Server"), "cf-hub__tool"));
            toolLinks.Add(HubButton("BUILD CLIENT", () => RunMenu("CardForge/Demo/Build Windows Client"), "cf-hub__tool"));
            toolLinks.Add(HubButton("PUBLISH PACKAGE", CardForgeGamePackageWindow.Open, "cf-hub__tool"));
            toolLinks.Add(HubButton("PROJECT HEALTH", OpenProjectAudit, "cf-hub__tool"));
            tools.Add(toolLinks);
            page.Add(tools);


            // ScrollView's vertical content can use its children's preferred
            // width rather than its visible viewport. Explicitly size each
            // row from the real viewport to prevent clipped columns.
            float lastViewportWidth = -1f;
            void FitDashboard(float viewportWidth)
            {
                if (float.IsNaN(viewportWidth) || viewportWidth < 260f ||
                    Mathf.Abs(lastViewportWidth - viewportWidth) < 1f) return;
                lastViewportWidth = viewportWidth;
                // Unity 7 on high-DPI Windows reports a logical viewport
                // that renders at the editor's pixels-per-point scale.
                // Size the workbench for the actual visible pixel area;
                // otherwise cards and artwork run off the right edge.
                var pixelsPerPoint = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
                var safeViewport = viewportWidth / pixelsPerPoint;
                scroll.contentContainer.style.width = safeViewport;
                scroll.contentContainer.style.maxWidth = safeViewport;
                var pageWidth = Mathf.Min(1000f, Mathf.Max(270f, safeViewport - 60f));
                var innerWidth = pageWidth;
                page.style.width = pageWidth;
                page.style.maxWidth = pageWidth;
                page.style.marginLeft = Mathf.Max(0f, (safeViewport - pageWidth) / 2f - 20f);
                hero.style.width = innerWidth;
                pulse.style.width = innerWidth;
                creativeRow.style.width = innerWidth;
                journey.style.width = innerWidth;
                tools.style.width = innerWidth;
                heroRow.style.width = Mathf.Max(260f, innerWidth - 32f);

                int creativeColumns = innerWidth >= 820f ? 2 : 1;
                float creativeWidth = creativeColumns == 2
                    ? (innerWidth - 32f) / 2f : innerWidth - 12f;
                foreach (var panel in creativeRow.Children())
                {
                    panel.style.flexGrow = 0;
                    panel.style.flexShrink = 0;
                    panel.style.flexBasis = creativeWidth;
                    panel.style.width = creativeWidth;
                    panel.style.minWidth = 0;
                    panel.style.maxWidth = creativeWidth;
                }

                int stepColumns = innerWidth >= 850f ? 3 :
                    innerWidth >= 560f ? 2 : 1;
                float stepWidth = (innerWidth - 12f * stepColumns) / stepColumns;
                foreach (var step in journey.Children())
                {
                    step.style.flexGrow = 0;
                    step.style.flexShrink = 0;
                    step.style.flexBasis = stepWidth;
                    step.style.width = stepWidth;
                    step.style.minWidth = 0;
                    step.style.maxWidth = stepWidth;
                }
                // Overview has independent responsive columns (readiness + decks).
                // No spreadsheet-like fixed-stat grid is left to clip on resize.
            }
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(evt =>
                FitDashboard(evt.newRect.width));
            scroll.schedule.Execute(() =>
            {
                FitDashboard(scroll.contentViewport.resolvedStyle.width);
                scroll.scrollOffset = Vector2.zero;
            }).ExecuteLater(40);

        }

        private void OpenHubLayout(string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(assetPath);
            if (asset == null)
            {
                EditorUtility.DisplayDialog("UI layout not found", "Missing editable UI asset:\n" + assetPath, "OK");
                return;
            }
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            if (!AssetDatabase.OpenAsset(asset))
                EditorUtility.DisplayDialog("Layout selected", "Open the selected UXML with UI Builder to edit its hierarchy and style.", "OK");
        }

        private static VisualElement HubPanel(string className)
        {
            var panel = new VisualElement();
            panel.AddToClassList(className);
            return panel;
        }

        private static VisualElement HubRow(string className)
        {
            var row = new VisualElement();
            row.AddToClassList(className);
            return row;
        }

        private static Label HubLabel(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            label.style.whiteSpace = WhiteSpace.Normal;
            switch (className)
            {
                case "cf-hub__title":
                    label.style.fontSize = 20;
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                    label.style.marginTop = 4;
                    label.style.marginBottom = 4;
                    break;
                case "cf-hub__section-title":
                    label.style.fontSize = 17;
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                    break;
                case "cf-hub__path-title":
                    label.style.fontSize = 17;
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                    break;
                case "cf-hub__stat-value":
                    label.style.fontSize = 26;
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                    break;
            }
            return label;
        }

        private static Button HubButton(string title, Action callback, string className)
        {
            var button = new Button(callback) { text = title };
            button.AddToClassList(className);
            return button;
        }

        private static VisualElement HubSectionHeading(string title, string subtitle)
        {
            var box = HubPanel("cf-hub__section-heading");
            box.Add(HubLabel(title, "cf-hub__section-title"));
            box.Add(HubLabel(subtitle, "cf-hub__section-subtitle"));
            return box;
        }

        private static VisualElement HubStat(string amount, string label, string note)
        {
            var cell = HubPanel("cf-hub__stat");
            var row = HubRow("cf-hub__stat-line");
            row.Add(HubLabel(amount, "cf-hub__stat-value"));
            row.Add(HubLabel(label, "cf-hub__stat-title"));
            cell.Add(row);
            cell.Add(HubLabel(note, "cf-hub__stat-note"));
            return cell;
        }

        private static VisualElement HubStep(string index, string title, string detail, string button, Action action)
        {
            var panel = HubPanel("cf-hub__step");
            var titleRow = HubRow("cf-hub__step-head");
            titleRow.Add(HubLabel(index, "cf-hub__step-number"));
            titleRow.Add(HubLabel(title, "cf-hub__step-title"));
            panel.Add(titleRow);
            panel.Add(HubLabel(detail, "cf-hub__step-detail"));
            panel.Add(HubButton(button, action, "cf-hub__step-action"));
            return panel;
        }
    }
}
