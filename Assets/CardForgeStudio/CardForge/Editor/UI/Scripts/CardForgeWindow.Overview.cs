using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardForge.EditorUI
{
    public sealed partial class CardForgeWindow
    {
        [MenuItem("CardForge/Studio/Overview", priority = 30)]
        public static void OpenOverview()
        {
            var window = GetWindow<CardForgeWindow>("CardForgeStudio");
            window.minSize = new Vector2(810, 570);
            window.Show();
            window.Focus();
            if (window.host != null) window.ShowOverview();
            else EditorApplication.delayCall += () =>
            {
                if (window != null) window.ShowOverview();
            };
        }

        /// <summary>Studio Overview shares the editor shell, navigation and visual language.</summary>
        private void ShowOverview()
        {
            if (host == null || catalog == null) return;
            designer?.Dispose();
            designer = null;
            openedScreen = null;
            openedId = null;
            activeScreen = "overview";

            var app = rootVisualElement.Q<VisualElement>("app-root");
            app.EnableInClassList("cf-library-mode", true);
            app.EnableInClassList("cf-editor-mode", false);
            rootVisualElement.Q<VisualElement>("navigation-sidebar").style.display = DisplayStyle.Flex;
            heading.text = "Overview";
            context.text = (catalog.Block?.name ?? "Astral Rift") + "  /  Studio workspace";
            status.text = "PROJECT OVERVIEW";
            navigation.MarkActive("overview");
            rootVisualElement.Q<Button>(className: "cf-dashboard-return").style.display = DisplayStyle.None;
            backButton.AddToClassList("cf-hidden");
            newDocumentButton.AddToClassList("cf-hidden");
            host.Clear();

            var workspace = HubPanel("cf-overview-workspace");
            workspace.style.flexGrow = 1;
            workspace.style.minHeight = 0;
            host.Add(workspace);
            workspace.Add(CardForge.Editor.CardForgeArtwork.Banner("home", "Overview"));

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("cf-overview-scroll");
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;
            scroll.style.width = Length.Percent(100);
            scroll.contentContainer.style.alignItems = Align.FlexStart;
            workspace.Add(scroll);
            var page = HubPanel("cf-overview-page");
            page.style.width = 980;
            page.style.flexShrink = 0;
            scroll.Add(page);

            var shortNav = HubRow("cf-overview-shortcuts");
            shortNav.Add(HubButton("DASHBOARD", ShowHome, "cf-overview-shortcut"));
            shortNav.Add(HubButton("CARDS", () => ShowLibrary("cards"), "cf-overview-shortcut"));
            shortNav.Add(HubButton("CARD LAYOUTS", () => ShowLibrary("layouts"), "cf-overview-shortcut"));
            shortNav.Add(HubButton("TURN FLOW", () => ShowLibrary("turnflow"), "cf-overview-shortcut"));
            shortNav.Add(HubButton("ACTIONS", () => ShowLibrary("actions"), "cf-overview-shortcut"));
            shortNav.Add(HubButton("ABILITIES", () => ShowLibrary("abilities"), "cf-overview-shortcut"));
            page.Add(shortNav);

            JObject rules = null;
            try
            {
                const string rulesPath = "Assets/StreamingAssets/CardForge/Rules/astral-rift.json";
                if (File.Exists(rulesPath)) rules = JObject.Parse(File.ReadAllText(rulesPath));
            }
            catch (Exception exception) when (exception is IOException ||
                exception is Newtonsoft.Json.JsonException)
            {
                Debug.LogWarning("Cannot read Overview rules: " + exception.Message);
            }

            var overview = BuildGameOverview(rules);
            overview.AddToClassList("cf-hub__overview--studio");
            page.Add(overview);

            float lastWidth = -1;
            void FitOverview(float width)
            {
                if (float.IsNaN(width) || width < 250 || Mathf.Abs(width - lastWidth) < 1) return;
                lastWidth = width;
                // Follow the same high-DPI logical pixel rule as the Dashboard.
                var available = width / Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
                var pageWidth = Mathf.Min(980f, Mathf.Max(260f, available - 36f));
                scroll.contentContainer.style.width = available;
                scroll.contentContainer.style.maxWidth = available;
                page.style.width = pageWidth;
                page.style.maxWidth = pageWidth;
                page.style.marginLeft = Mathf.Max(0f, (available - pageWidth) / 2f - 10f);
            }
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(e =>
                FitOverview(e.newRect.width));
            scroll.schedule.Execute(() => FitOverview(scroll.contentViewport.resolvedStyle.width))
                .ExecuteLater(40);
        }

        private VisualElement BuildGameOverview(JObject rules)
        {
            var wrap = HubPanel("cf-hub__overview");
            var headingRow = HubRow("cf-hub__overview-head");
            var headingCopy = new VisualElement();
            headingCopy.style.flexGrow = 1;
            headingCopy.Add(HubLabel("THE WORLD YOU'RE BUILDING", "cf-hub__eyebrow"));
            headingCopy.Add(HubLabel("Astral Rift  /  Overview", "cf-hub__overview-title"));
            headingRow.Add(headingCopy);
            var toolbarActions = HubRow("cf-overview-heading-actions");
            if (activeScreen != "overview")
                toolbarActions.Add(HubButton("OPEN OVERVIEW", ShowOverview, "cf-overview-heading-link"));
            toolbarActions.Add(HubButton("CHECK RULES", VerifyDemoAuthoring, "cf-overview-heading-link"));
            headingRow.Add(toolbarActions);
            wrap.Add(headingRow);

            var cards = rules?["cards"] as JArray;
            var decks = rules?["starterDecks"] as JArray;
            var phases = rules?["phases"] as JArray;
            var actions = rules?["actions"] as JArray;
            int cardCount = cards?.Count ?? 0;
            int deckCount = decks?.Count ?? 0;
            int phaseCount = phases?.Count ?? 0;
            int actionCount = actions?.Count ?? 0;
            int artworkCount = 0;
            string artFolder = "Assets/CardForgeStudio/CardForgeClient/Resources/AstralRift/Illustrations";
            if (cards != null)
            {
                artworkCount = cards.Count(c =>
                {
                    var id = (string)c["id"];
                    return !string.IsNullOrWhiteSpace(id) &&
                           File.Exists(Path.Combine(artFolder, id + ".png"));
                });
            }
            int completeDecks = decks?.Count(deck =>
            {
                var ids = deck["cardIds"] as JArray;
                return ids != null && ids.Count == 42 &&
                    ids.Select(c => (string)c).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 42;
            }) ?? 0;
            string[] types = { "commander", "creature", "spell", "quickspell", "manasource" };
            int frontCount = types.Count(type => File.Exists(
                "Assets/CardForgeData/blocks/astral-rift-core/layouts/ar-layout-" + type + ".json"));
            int authoringCount = 0;
            try
            {
                const string blockPath =
                    "Assets/CardForgeData/blocks/astral-rift-core/block.json";
                if (File.Exists(blockPath))
                {
                    var block = JObject.Parse(File.ReadAllText(blockPath));
                    authoringCount = (block["cardIds"] as JArray)?.Count ?? 0;
                }
            }
            catch (Exception error) when (error is IOException ||
                error is Newtonsoft.Json.JsonException)
            {
                Debug.LogWarning("Astral Rift authoring overview unavailable: " + error.Message);
            }
            bool sceneReady = File.Exists("Assets/CardForgeStudio/CardForgeClient/Scenes/TcgClient.unity") &&
                              File.Exists("Assets/CardForgeStudio/CardForgeClient/UI/AstralRiftArena.uxml");
            bool nativeServer = File.Exists("Builds/DedicatedServer/CardForgeServer.exe");
            bool windowsClient = File.Exists("Builds/Windows/CardForgeStudio.exe");
            bool rulesReady = cardCount == 240 && authoringCount == 240 &&
                              completeDecks == 3 && phaseCount == 5 && actionCount == 11 &&
                              artworkCount == 240 && frontCount == 5;

            var highlight = HubPanel("cf-hub__overview-banner");
            var highlightText = new VisualElement();
            highlightText.style.flexGrow = 1;
            highlightText.Add(HubLabel(rulesReady ? "RULES CONTENT COMPLETE" : "A FEW PIECES NEED ATTENTION",
                "cf-hub__overview-status"));
            highlightText.Add(HubLabel(
                cardCount + " cards  •  " + completeDecks + " battle-ready decks  •  " +
                artworkCount + " illustrations  •  " + frontCount + " unique card fronts",
                "cf-hub__overview-summary"));
            highlight.Add(highlightText);
            highlight.Add(HubButton("OPEN THE ARENA", () => RunMenu("CardForge/Play Sample Game"),
                "cf-hub__overview-play"));
            wrap.Add(highlight);

            var body = HubRow("cf-hub__overview-body");
            var readiness = HubPanel("cf-hub__overview-readiness");
            readiness.Add(HubLabel("READY FOR THE NEXT BATTLE?", "cf-hub__overview-block-title"));
            readiness.Add(HubLabel("A quick look at the pieces that make your game playable.",
                "cf-hub__overview-explainer"));
            readiness.Add(HubReadiness("CARDS & DECKS",
                cardCount == 240 && completeDecks == 3 && authoringCount == 240,
                cardCount + "/240 cards  •  " + completeDecks + "/3 starters  •  " +
                authoringCount + " editable", () => ShowLibrary("cards")));
            readiness.Add(HubReadiness("ILLUSTRATIONS & FRAMES",
                artworkCount == 240 && frontCount == 5,
                artworkCount + "/240 art  •  " + frontCount + "/5 layouts",
                () => ShowLibrary("layouts")));
            readiness.Add(HubReadiness("PLAYABLE UNITY SCENE", sceneReady,
                sceneReady ? "Editable arena layout connected" : "Create the game scene",
                OpenNativePlayScene));
            var serverRow = HubReadiness("LIVE DEDICATED SERVER", false,
                nativeServer ? "Checking connection..." : "Build the server to enable play",
                () => RunMenu(nativeServer
                    ? "CardForge/Play Sample Game"
                    : "CardForge/Server/Build Native Windows Dedicated Server"));
            readiness.Add(serverRow);
            if (nativeServer)
            {
                var liveNote = serverRow.Q<Label>(className: "cf-hub__ready-note");
                var liveIndicator = serverRow.Q<Label>(className: "cf-hub__ready-indicator");
                _ = UpdateServerReadinessAsync(liveNote, liveIndicator);
            }
            readiness.Add(HubReadiness("WINDOWS GAME BUILD", windowsClient,
                windowsClient ? "Desktop client executable found" : "Not built yet",
                () => RunMenu("CardForge/Demo/Build Windows Client")));
            body.Add(readiness);

            var factions = HubPanel("cf-hub__overview-factions");
            var deckHeader = HubRow("cf-hub__overview-deck-head");
            deckHeader.Add(HubLabel("CHOOSE YOUR ALLEGIANCE", "cf-hub__overview-block-title"));
            factions.Add(deckHeader);
            factions.Add(HubLabel("Three distinct strategies. All 42-card starter decks are editable.",
                "cf-hub__overview-explainer"));
            var deckRow = HubRow("cf-hub__deck-row");
            string[] themes = { "solar", "tidal", "verdant" };
            if (decks != null)
            {
                int index = 0;
                foreach (var deck in decks.Take(3))
                {
                    var name = (string)deck["name"] ?? "Starter deck";
                    var description = (string)deck["strategy"] ?? "";
                    var ids = deck["cardIds"] as JArray;
                    var artId = ids?.FirstOrDefault()?.ToString() ?? "";
                    var theme = themes[Math.Min(index, themes.Length - 1)];
                    var card = HubPanel("cf-hub__deck");
                    card.AddToClassList("cf-hub__deck-" + theme);
                    var image = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        artFolder + "/" + artId + ".png");
                    if (image != null)
                    {
                        var portrait = new Image { image = image, scaleMode = ScaleMode.ScaleAndCrop };
                        portrait.AddToClassList("cf-hub__deck-art");
                        card.Add(portrait);
                    }
                    card.Add(HubLabel(name.ToUpperInvariant(), "cf-hub__deck-name"));
                    card.Add(HubLabel(description, "cf-hub__deck-description"));
                    card.Add(HubLabel((ids?.Count ?? 0) + " CARDS  /  COMMANDER + MANA SOURCE",
                        "cf-hub__deck-count"));
                    var deckName = name;
                    var deckCardIds = ids?.Select(id => (string)id)
                        .Where(id => !string.IsNullOrWhiteSpace(id)).ToArray() ?? Array.Empty<string>();
                    card.Add(HubButton("VIEW THIS DECK", () =>
                    {
                        ShowLibrary("cards");
                        screens.ShowOnly("cards", deckCardIds, deckName + " starter deck");
                    }, "cf-hub__deck-action"));
                    deckRow.Add(card);
                    index++;
                }
            }
            factions.Add(deckRow);
            body.Add(factions);
            wrap.Add(body);
            return wrap;
        }

        private static async Task UpdateServerReadinessAsync(Label statusLabel, Label indicator)
        {
            bool healthy = false;
            string detail = "Server build present; currently offline. Use Play to launch it.";
            try
            {
                var address = "http://127.0.0.1:5168";
                const string settingsPath =
                    "Assets/CardForgeStudio/CardForgeClient/Resources/TcgConnectionSettings.asset";
                if (File.Exists(settingsPath))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(
                        File.ReadAllText(settingsPath), @"(?m)^\s*baseUrl:\s*(https?://\S+)");
                    if (match.Success) address = match.Groups[1].Value.TrimEnd('/');
                }
                // The game is authoritative on the dedicated server, so a
                // build on disk does not mean that a playable server is running.
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) })
                using (var response = await client.GetAsync(address + "/api/health"))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        var health = JObject.Parse(await response.Content.ReadAsStringAsync());
                        healthy = (string)health["status"] == "healthy" &&
                                  (bool?)health["simulationReady"] == true &&
                                  (string)health["simulationMode"] == "Unity Dedicated Server";
                        detail = healthy ? "Online at " + address + "  •  match simulation ready"
                            : "Server responded, but its game simulation is not ready";
                    }
                }
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException ||
                e is IOException || e is Newtonsoft.Json.JsonException)
            {
                detail = "Server offline  •  use Play to start the native host";
            }
            if (statusLabel?.panel == null || indicator?.panel == null) return;
            statusLabel.text = detail;
            indicator.text = healthy ? "●" : "◇";
            indicator.style.color = healthy
                ? new Color(0.62f, 0.87f, 0.69f)
                : new Color(0.98f, 0.72f, 0.42f);
        }

        private static VisualElement HubReadiness(string title, bool ready, string detail, Action onClick)
        {
            var row = HubRow("cf-hub__ready-row");
            var icon = HubLabel(ready ? "●" : "◇", "cf-hub__ready-indicator");
            icon.style.color = ready
                ? new Color(0.62f, 0.87f, 0.69f, 1f)
                : new Color(0.98f, 0.72f, 0.42f, 1f);
            row.Add(icon);
            var text = new VisualElement();
            text.style.flexGrow = 1;
            text.style.minWidth = 0;
            text.Add(HubLabel(title, "cf-hub__ready-title"));
            text.Add(HubLabel(detail, "cf-hub__ready-note"));
            row.Add(text);
            if (onClick != null)
            {
                var open = HubButton("OPEN", onClick, "cf-hub__ready-link");
                row.Add(open);
            }
            return row;
        }
    }
}
