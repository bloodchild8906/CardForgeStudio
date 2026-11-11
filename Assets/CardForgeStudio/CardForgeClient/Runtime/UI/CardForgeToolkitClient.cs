using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TcgServer.Client
{
    /// <summary>
    /// Runtime UI Toolkit shell. The REST API remains authoritative for rules,
    /// inventories and match outcomes; no gameplay rules are duplicated here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class CardForgeToolkitClient : MonoBehaviour
    {
        [SerializeField] private TcgConnectionSettings settings;
        private TcgRestClient api;
        private CardForgeRemoteAssets remoteAssets;
        private TcgClientSession session;
        private UIDocument document;
        private VisualElement root, content, sidebar, header, board, actionBar;
        private Label notice, identity;
        private string page = "Play", matchId, lastViewKey, selectedDeckId;
        private RulePackDto rulePack;
        private ClientGameViewDto match;
        private CardInstanceDto selected;
        private ClientGameActionDto targetAction;
        private bool actionBusy;
        private Coroutine polling;
        private readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        private readonly Dictionary<string, Texture2D> artwork = new();

        private static readonly Color Night = ColorFrom("#0B1020");
        private static readonly Color Surface = ColorFrom("#172139");
        private static readonly Color Raised = ColorFrom("#243451");
        private static readonly Color Gold = ColorFrom("#E9BE69");
        private static readonly Color Pale = ColorFrom("#F3F0E9");
        private static readonly Color Soft = ColorFrom("#B2C3DD");

        public void Configure(TcgConnectionSettings value) => settings = value;

        private void Awake()
        {
            if (settings == null) settings = Resources.Load<TcgConnectionSettings>("TcgConnectionSettings");
            if (settings != null)
            {
                // Always use a runtime copy. Standalone builds may be launched against
                // a dynamically selected local server port without mutating the asset.
                settings = Instantiate(settings);
                var serverArgument = Environment.GetCommandLineArgs()
                    .FirstOrDefault(value => value.StartsWith("--server=", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(serverArgument))
                {
                    var candidate = serverArgument.Substring("--server=".Length).Trim();
                    if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                        settings.SetRuntimeBaseUrl(candidate);
                }
            }
            api = GetComponent<TcgRestClient>() ?? gameObject.AddComponent<TcgRestClient>();
            session = GetComponent<TcgClientSession>() ?? gameObject.AddComponent<TcgClientSession>();
            api.Configure(settings);
            remoteAssets = GetComponent<CardForgeRemoteAssets>() ?? gameObject.AddComponent<CardForgeRemoteAssets>();
            remoteAssets.Configure(api, session);
            document = GetComponent<UIDocument>() ?? gameObject.AddComponent<UIDocument>();
            var panel = Resources.Load<PanelSettings>("AstralRift/CardForgePanelSettings");
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panel.referenceResolution = new Vector2Int(1600, 900);
                panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                panel.match = .5f;
            }
            if (panel.themeStyleSheet == null)
            {
                var theme = Resources.Load<ThemeStyleSheet>("AstralRift/CardForgeRuntimeTheme");
                if (theme != null) panel.themeStyleSheet = theme;
                else Debug.LogError("CardForge runtime theme is missing. Build the client after Unity imports CardForgeRuntimeTheme.tss.");
            }
            document.panelSettings = panel;
        }

        private void Start()
        {
            root = document.rootVisualElement;
            root.style.flexGrow = 1;
            root.style.backgroundColor = Night;
            root.style.color = Pale;
            var arenaStyle = Resources.Load<StyleSheet>("AstralRift/CardForgeArena");
            if (arenaStyle != null && !root.styleSheets.Contains(arenaStyle))
                root.styleSheets.Add(arenaStyle);
            BuildArenaShell();
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape || string.IsNullOrEmpty(matchId)) return;
                if (selected == null && targetAction == null) return;
                selected = null;
                targetAction = null;
                ShowMatch();
                evt.StopPropagation();
            });
            Show("Play");
            StartCoroutine(ConnectDemo());
        }

        private IEnumerator ConnectDemo()
        {
            if (settings == null) { SetNotice("Connection settings are missing."); yield break; }
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var ready = false;
                yield return api.Health(_ => ready = true, _ => { });
                if (ready)
                {
                    SetNotice("Connected to CardForge.");
                    yield return api.Login("demo", "cardforge-demo", LoggedIn,
                        error =>
                        {
                            SetNotice("Sign in to play on the native server.");
                            ShowLogin();
                        });
                    yield break;
                }
                SetNotice("Connecting to the game server...");
                yield return new WaitForSeconds(2);
            }
            SetNotice("Server offline. Check the native server, then use Retry connection.");
            Show("Play");
        }

        private void ShowLogin()
        {
            content.Clear();
            var panel = Panel();
            panel.Add(Label("PLAYER ACCOUNT", 23, Gold));
            panel.Add(Label("Sign in or create a CardForge account on the native Unity server.", 13, Soft));
            var handle = new TextField("Handle") { value = "" };
            var password = new TextField("Password") { isPasswordField = true };
            panel.Add(handle);
            panel.Add(password);
            var actions = Row();
            actions.Add(Action("Sign in", () =>
                StartCoroutine(api.Login(handle.value, password.value, LoggedIn,
                    error => SetNotice("Sign-in failed: " + error)))));
            actions.Add(Action("Create account", () =>
                StartCoroutine(api.Register(handle.value, password.value, LoggedIn,
                    error => SetNotice("Registration failed: " + error)))));
            panel.Add(actions);
            content.Add(panel);
        }

        private void LoggedIn(AuthSessionDto auth)
        {
            session.SetSession(auth);
            identity.text = auth.user.handle + "  •  " + auth.user.rating + " rating";
            Show(page);
            StartCoroutine(api.Decks(session.Token, decks =>
            {
                var preferred = decks.FirstOrDefault(deck =>
                    string.Equals(deck.rulePackId, settings.DefaultRulePackId,
                        StringComparison.OrdinalIgnoreCase));
                if (preferred != null && string.IsNullOrWhiteSpace(selectedDeckId))
                {
                    selectedDeckId = preferred.id;
                    selectedDeckName = preferred.name;
                    if (page == "Play") BuildPlayHub();
                }
            }, error => SetNotice("Could not load your saved decks: " + error)));
        }

        private static Color ColorFrom(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var color);
            return color;
        }

        private static Label Label(string value, int size = 13, Color? color = null)
        {
            var label = new Label(value);
            label.style.fontSize = size;
            label.style.color = color ?? Pale;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static Button Action(string title, Action callback)
        {
            var button = new Button(callback) { text = title };
            button.style.height = 38;
            button.style.minWidth = 104;
            button.style.marginRight = 9;
            button.style.marginBottom = 6;
            button.style.paddingLeft = 14;
            button.style.paddingRight = 14;
            button.style.backgroundColor = Raised;
            button.style.color = Pale;
            button.style.borderTopLeftRadius = 7;
            button.style.borderTopRightRadius = 7;
            button.style.borderBottomLeftRadius = 7;
            button.style.borderBottomRightRadius = 7;
            return button;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;
            return row;
        }

        private static VisualElement Panel()
        {
            var panel = new VisualElement();
            panel.style.backgroundColor = Surface;
            panel.style.marginTop = 12;
            panel.style.marginBottom = 10;
            panel.style.paddingTop = 16;
            panel.style.paddingBottom = 16;
            panel.style.paddingLeft = 16;
            panel.style.paddingRight = 16;
            panel.style.borderTopLeftRadius = 10;
            panel.style.borderTopRightRadius = 10;
            panel.style.borderBottomLeftRadius = 10;
            panel.style.borderBottomRightRadius = 10;
            return panel;
        }

        private void SetNotice(string message) { if (notice != null) notice.text = message; }

        private void Show(string tab)
        {
            page = tab;
            foreach (var item in navigation)
                item.Value.EnableInClassList("cf-nav--active", item.Key == tab);
            content.Clear();
            var top = Label(tab.ToUpperInvariant(), 24, Gold);
            top.style.marginBottom = 12;
            content.Add(top);
            switch (tab)
            {
                case "Play": ShowPlay(); break;
                case "Matchmaking": ShowPlay(); break;
                case "Decks": StartCoroutine(LoadDecks()); break;
                case "Inventory": StartCoroutine(LoadInventory()); break;
                case "Friends": StartCoroutine(LoadFriends()); break;
                case "Messages": StartCoroutine(LoadMessages()); break;
                case "Store": StartCoroutine(LoadStore()); break;
                case "News & Events": StartCoroutine(LoadNews()); break;
                case "Profile": ShowProfile(); break;
            }
        }

        private void ShowPlay() => BuildPlayHub();

        private IEnumerator LoadRulePackAndMatch()
        {
            yield return api.GameRulesSync(settings.DefaultRulePackId, value =>
            {
                rulePack = value;
                ShowMatch();
                if (polling != null) StopCoroutine(polling);
                polling = StartCoroutine(PollMatch());
            }, error => SetNotice("Rules unavailable: " + error));
        }

        private IEnumerator PollMatch()
        {
            while (!string.IsNullOrEmpty(matchId))
            {
                yield return api.GameView(session.Token, matchId, value =>
                {
                    match = value;
                    var signature = value.turn + ":" + value.currentPhaseId + ":" + value.activePlayerId +
                        ":" + value.finished + ":" +
                        string.Join("|", value.zones.Select(z => z.playerId + z.zoneId +
                        string.Join(",", z.cards.Select(c => c.instanceId + c.cardId)))) +
                        ":" + string.Join("|", value.allowedActions.Select(a => a.id)) +
                        ":" + string.Join("|", value.resources.Select(r => r.playerId + r.resourceId + r.value)) +
                        ":" + (value.events.LastOrDefault()?.message ?? "");
                    if (signature != lastViewKey)
                    {
                        lastViewKey = signature;
                        if (page == "Play") ShowMatch();
                    }
                }, error => SetNotice("Match refresh: " + error));
                yield return new WaitForSeconds(1f);
            }
        }

        private void ShowMatch() => ShowArenaMatch();

        private void DrawActions()
        {
            if (actionBar == null) return;
            actionBar.Clear();
            if (actionBusy)
            {
                actionBar.Add(Label("RESOLVING ACTION...", 13, CommandTone));
                return;
            }
            if (targetAction != null)
            {
                var hint = Label("SELECT TARGET  /  " + targetAction.name.ToUpperInvariant(), 13, QuickTone);
                hint.style.marginRight = 18;
                actionBar.Add(hint);
                actionBar.Add(UiButton("CANCEL TARGETING", () =>
                {
                    targetAction = null;
                    ShowMatch();
                }));
                return;
            }
            if (match == null) return;
            bool yourTurn = match.activePlayerId == session.User?.id;
            string selectedZone = (match.zones ?? Array.Empty<ClientGameZoneDto>())
                .FirstOrDefault(z => z.playerId == session.User?.id &&
                    z.cards.Any(card => card.instanceId == selected?.instanceId))?.zoneId;

            var status = Label(yourTurn ? "YOUR ACTIONS" : "RESPONSE WINDOW", 11, CommandTone);
            status.style.marginRight = 13;
            actionBar.Add(status);

            var actions = (match.allowedActions ?? Array.Empty<ClientGameActionDto>())
                .Where(a => yourTurn || !a.requiresActivePlayer)
                .Where(a => string.IsNullOrEmpty(a.sourceZone) || a.sourceZone == selectedZone)
                .ToList();

            foreach (var available in actions)
            {
                var action = available;
                bool primary = action.id.IndexOf("next-phase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               action.id.IndexOf("end-turn", StringComparison.OrdinalIgnoreCase) >= 0;
                var control = UiButton(action.name.ToUpperInvariant(), () =>
                {
                    if (action.target == "target_card" || action.target == "target_instance" ||
                        action.target == "target_player")
                    {
                        targetAction = action;
                        ShowMatch();
                    }
                    else Submit(action, selected?.instanceId, null, null);
                }, primary);
                actionBar.Add(control);
            }
            if (actions.Count == 0)
                actionBar.Add(Label(yourTurn
                    ? "Select a card in your hand, battlefield or Command zone."
                    : "Opponent is playing. Select a Quickspell to respond.", 12, Soft));
            if (selected != null)
                actionBar.Add(UiButton("DESELECT", () => { selected = null; ShowMatch(); }));
        }

        private void Submit(ClientGameActionDto action, string source, string targetCard, string targetPlayer)
        {
            if (actionBusy || match == null ||
                (match.activePlayerId != session.User?.id && action.requiresActivePlayer)) return;
            if (!string.IsNullOrEmpty(action.sourceZone) && string.IsNullOrEmpty(source)) return;
            actionBusy = true;
            targetAction = null;
            DrawActions();
            StartCoroutine(api.ExecuteAction(session.Token, matchId, new ExecuteGameActionRequestDto
            {
                actionId = action.id, sourceInstanceId = source, targetInstanceId = targetCard, targetPlayerId = targetPlayer
            }, _ =>
            {
                actionBusy = false; selected = null; lastViewKey = null;
                StartCoroutine(api.GameView(session.Token, matchId, value =>
                { match = value; ShowMatch(); }, error => SetNotice(error.ToString())));
            }, error =>
            {
                actionBusy = false; SetNotice(error.ToString()); DrawActions();
            }));
        }

        private bool RequireAccount()
        {
            if (session?.IsAuthenticated == true) return true;
            SetNotice("Please sign in to continue.");
            ShowLogin();
            return false;
        }

        private IEnumerator LoadDecks()
        {
            if (!RequireAccount()) yield break;
            yield return api.Decks(session.Token, decks =>
            {
                StartCoroutine(api.Inventory(session.Token, session.User.id, inventory =>
                {
                    StartCoroutine(api.GameRulesSync(settings.DefaultRulePackId, rules =>
                    {
                        if (page == "Decks") BuildDeckEditor(decks, inventory, rules, null);
                    }, error => SetNotice(error.ToString())));
                }, error => SetNotice(error.ToString())));
            }, error => SetNotice(error.ToString()));
        }

        private void BuildDeckEditor(DeckDto[] decks, InventoryDto inventory,
            RulePackDto rules, DeckDto editing) => BuildDeckWorkshopV2(decks, inventory, rules, editing);

        private IEnumerator LoadInventory()
        {
            if (!RequireAccount()) yield break;
            yield return api.Inventory(session.Token, session.User.id, inventory =>
            {
                StartCoroutine(api.GameRulesSync(settings.DefaultRulePackId, rules =>
                {
                    if (page == "Inventory") ShowCollectionGallery(inventory, rules);
                }, error => SetNotice("Collection rules unavailable: " + error)));
            }, error => SetNotice("Collection unavailable: " + error));
        }

        private IEnumerator LoadFriends()
        {
            if (!RequireAccount()) yield break;
            yield return api.Friends(session.Token, friends =>
            {
                if (page != "Friends") return;
                var add = Panel();
                var input = new TextField("Friend user ID"); add.Add(input);
                add.Add(Action("Add friend", () =>
                {
                    StartCoroutine(api.AddFriend(session.Token, input.value,
                        _ => Show("Friends"), error => SetNotice(error.ToString())));
                }));
                content.Add(add);
                foreach (var friend in friends)
                {
                    var item = Panel();
                    item.Add(Label(friend.friendUserId + "  •  " + friend.status, 15, Gold));
                    item.Add(Action("Remove", () => StartCoroutine(api.RemoveFriend(session.Token, friend.id,
                        () => Show("Friends"), error => SetNotice(error.ToString())))));
                    content.Add(item);
                }
            }, error => SetNotice(error.ToString()));
        }

        private IEnumerator LoadMessages()
        {
            if (!RequireAccount()) yield break;
            yield return api.Messages(session.Token, messages =>
            {
                if (page != "Messages") return;
                var form = Panel();
                var to = new TextField("Recipient user ID");
                var message = new TextField("Message") { multiline = true };
                form.Add(to); form.Add(message);
                form.Add(Action("Send", () =>
                {
                    StartCoroutine(api.SendMessage(session.Token, to.value, message.value,
                        _ => Show("Messages"), error => SetNotice(error.ToString())));
                }));
                content.Add(form);
                foreach (var item in messages.Reverse())
                {
                    var panel = Panel();
                    panel.Add(Label(item.senderId + " → " + item.recipientId, 12, Gold));
                    panel.Add(Label(item.body));
                    content.Add(panel);
                }
            }, error => SetNotice(error.ToString()));
        }

        private void ShowProfile()
        {
            var user = session?.User;
            var panel = Panel();
            panel.Add(Label(user == null ? "Sign in to view profile" : user.handle, 22, Gold));
            if (user != null)
            {
                panel.Add(Label("Rating: " + user.rating + "\nPlayer ID: " + user.id, 14));
                panel.Add(Action("Refresh profile", () => Show("Profile")));
            }
            content.Add(panel);
        }

        private IEnumerator LoadStore()
        {
            yield return api.StoreProducts(products =>
            {
                if (page != "Store") return;
                content.Add(Label("CardForge storefront", 18, Gold));
                if (RequireAccount())
                    StartCoroutine(api.Wallet(session.Token, wallet =>
                    {
                        if (page != "Store") return;
                        var balances = string.Join("    ", wallet.balances.Select(b => b.currency + ": " + b.amount));
                        var header = Label("Your balance  •  " + balances, 15, Gold);
                        content.Insert(Math.Min(1, content.childCount), header);
                    }, error => SetNotice(error.ToString())));
                if (products.Length == 0) content.Add(Label("No products are currently on sale.", 14, Soft));
                foreach (var product in products)
                {
                    var item = product;
                    var panel = Panel();
                    panel.Add(Label(product.name + "  •  " + product.price + " " + product.currency, 16, Gold));
                    panel.Add(Label(product.description, 13, Soft));
                    if (session?.IsAuthenticated == true)
                        panel.Add(Action("Purchase", () =>
                            StartCoroutine(api.Purchase(session.Token, item.id,
                                receipt =>
                                {
                                    SetNotice("Purchase complete: " + receipt.cards.Length + " cards granted.");
                                    Show("Store");
                                }, error => SetNotice("Purchase rejected: " + error)))));
                    content.Add(panel);
                }
            }, error => SetNotice(error.ToString()));
        }

        private IEnumerator LoadNews()
        {
            yield return api.PublicEvents(events =>
            {
                if (page != "News & Events") return;
                if (events.Length == 0) content.Add(Label("No upcoming events.", 14, Soft));
                foreach (var item in events)
                {
                    var panel = Panel();
                    panel.Add(Label(item.name + "  •  " + item.kind, 17, Gold));
                    panel.Add(Label(item.description, 13, Soft));
                    content.Add(panel);
                }
            }, error => SetNotice(error.ToString()));
        }
    }
}
