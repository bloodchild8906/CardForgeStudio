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
        private string page = "Play", matchId, lastViewKey, selectedDeckId, pendingRecipientId;
        private RulePackDto rulePack;
        private ClientGameViewDto match;
        private CardInstanceDto selected;
        private ClientGameActionDto targetAction;
        private bool actionBusy;
        private Coroutine polling;
        private readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        private readonly Dictionary<string, Texture2D> artwork = new();

        private static readonly Color Night = ColorFrom("#14101C");
        private static readonly Color Surface = ColorFrom("#211A2C");
        private static readonly Color Raised = ColorFrom("#44304A");
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
            RegisterRuntimeShortcuts();
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

        // Shared game-facing layout for secondary screens: scrollable content,
        // readable hierarchy and a consistent illustration-friendly surface.
        private ScrollView BeginGameScreen(string eyebrow, string heading, string description)
        {
            content.Clear();
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;
            scroll.AddToClassList("cf-scroll");
            var hero = UiSurface(true);
            hero.style.marginBottom = 15;
            hero.Add(Label(eyebrow, 11, CommandTone));
            var title = Label(heading, 29, Pale);
            title.style.marginTop = 5;
            title.style.marginBottom = 7;
            hero.Add(title);
            hero.Add(Label(description, 14, Soft));
            scroll.Add(hero);
            content.Add(scroll);
            return scroll;
        }

        private static VisualElement GameScreenCard(string heading, string description)
        {
            var card = UiSurface();
            card.style.marginBottom = 12;
            card.Add(Label(heading, 17, Pale));
            if (!string.IsNullOrWhiteSpace(description))
            {
                var detail = Label(description, 13, Soft);
                detail.style.marginTop = 6;
                card.Add(detail);
            }
            return card;
        }

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
                case "Decks": ShowLoadingState("Decks"); StartCoroutine(LoadDecks()); break;
                case "Inventory": ShowLoadingState("Inventory"); StartCoroutine(LoadInventory()); break;
                case "Friends": StartCoroutine(LoadFriends()); break;
                case "Messages": StartCoroutine(LoadMessages()); break;
                case "Store": StartCoroutine(LoadStore()); break;
                case "News & Events": StartCoroutine(LoadNews()); break;
                case "Profile": ShowProfile(); break;
            }
        }

        // Returning to Play must resume the current match, not strand it behind the lobby.
        private void ShowPlay()
        {
            if (!string.IsNullOrEmpty(matchId)) ShowArenaMatch();
            else BuildPlayHub();
        }

        private IEnumerator LoadRulePackAndMatch()
        {
            yield return api.GameRulesSync(settings.DefaultRulePackId, value =>
            {
                rulePack = value;
                ShowMatch();
                if (polling != null) StopCoroutine(polling);
                polling = StartCoroutine(PollMatch());
            }, error =>
            {
                SetNotice("Rules unavailable: " + error);
                ShowMatchLoadError("Could not load this match's rules. Check the server connection and retry.");
            });
        }

        private void ShowMatchLoadError(string message)
        {
            if (page != "Play" || string.IsNullOrEmpty(matchId) || match != null) return;
            content.Clear();
            var panel = UiSurface(true);
            panel.style.maxWidth = 740;
            panel.Add(Label("THE ARENA IS UNAVAILABLE", 24, QuickTone));
            panel.Add(Label(message, 14, Soft));
            var controls = Flex(true);
            controls.style.flexWrap = Wrap.Wrap;
            controls.style.marginTop = 16;
            controls.Add(UiButton("RETRY MATCH", () => StartCoroutine(LoadRulePackAndMatch()), true));
            controls.Add(UiButton("BACK TO PLAY", () =>
            {
                matchId = null;
                if (polling != null) { StopCoroutine(polling); polling = null; }
                Show("Play");
            }));
            panel.Add(controls);
            content.Add(panel);
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
                }, error =>
                {
                    SetNotice("Match refresh: " + error);
                    ShowMatchLoadError("Could not retrieve the match. Retry or return to your decks.");
                });
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
            if (!string.IsNullOrEmpty(action.sourceZone) && string.IsNullOrEmpty(source))
            {
                SetNotice("Select a card from " + action.sourceZone + " to " + action.name.ToLowerInvariant() + ".");
                return;
            }
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
                    }, error => ShowLoadError("Decks", "Rules: " + error)));
                }, error => ShowLoadError("Decks", "Inventory: " + error)));
            }, error => ShowLoadError("Decks", "Saved decks: " + error));
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
                }, error => ShowLoadError("Inventory", "Rules: " + error)));
            }, error => ShowLoadError("Inventory", "Collection: " + error));
        }

        private IEnumerator LoadFriends()
        {
            if (!RequireAccount()) yield break;
            yield return api.Friends(session.Token, friends =>
            {
                if (page != "Friends") return;
                var screen = BeginGameScreen("YOUR ALLIES", "The Fellowship",
                    "Find familiar faces, forge alliances and challenge your friends.");
                var invite = GameScreenCard("Invite a player", "Ask a friend for their Player ID, then add them here.");
                var input = new TextField("Player ID");
                input.AddToClassList("cf-textfield");
                invite.Add(input);
                invite.Add(UiButton("ADD ALLY", () =>
                {
                    if (string.IsNullOrWhiteSpace(input.value))
                    {
                        SetNotice("Enter your friend's Player ID first.");
                        return;
                    }
                    StartCoroutine(api.AddFriend(session.Token, input.value.Trim(),
                        _ => Show("Friends"), error => SetNotice("Could not add ally: " + error)));
                }, true));
                screen.Add(invite);
                screen.Add(Label("YOUR ALLIES  •  " + (friends?.Length ?? 0), 17, CommandTone));
                if (friends == null || friends.Length == 0)
                    screen.Add(GameScreenCard("Your party awaits", "Share your Player ID from the Profile screen to make your first connection."));
                foreach (var friend in friends ?? Array.Empty<FriendConnectionDto>())
                {
                    var entry = friend;
                    var otherId = friend.userId == session.User.id ? friend.friendUserId : friend.userId;
                    var card = GameScreenCard("ALLY  •  " + otherId,
                        "Connection: " + friend.status);
                    var actions = Flex(true);
                    actions.style.flexWrap = Wrap.Wrap;
                    actions.Add(UiButton("SEND MESSAGE", () =>
                    {
                        pendingRecipientId = otherId;
                        Show("Messages");
                    }, true));
                    actions.Add(UiButton("REMOVE", () => StartCoroutine(api.RemoveFriend(
                        session.Token, entry.id, () => Show("Friends"),
                        error => SetNotice("Could not remove ally: " + error)))));
                    card.Add(actions);
                    screen.Add(card);
                }
            }, error => SetNotice("Could not load your allies: " + error));
        }

        private IEnumerator LoadMessages()
        {
            if (!RequireAccount()) yield break;
            yield return api.Messages(session.Token, messages =>
            {
                if (page != "Messages") return;
                var screen = BeginGameScreen("RAVEN POST", "Messages",
                    "Keep in touch with your allies between battles.");
                var compose = GameScreenCard("Send a message", "Choose a recipient and write a note.");
                var to = new TextField("Recipient Player ID") { value = pendingRecipientId ?? "" };
                var message = new TextField("Your message") { multiline = true };
                to.AddToClassList("cf-textfield");
                message.AddToClassList("cf-textfield");
                message.style.minHeight = 75;
                compose.Add(to);
                compose.Add(message);
                compose.Add(UiButton("SEND MESSAGE", () =>
                {
                    if (string.IsNullOrWhiteSpace(to.value) || string.IsNullOrWhiteSpace(message.value))
                    {
                        SetNotice("Choose a recipient and write a message first.");
                        return;
                    }
                    StartCoroutine(api.SendMessage(session.Token, to.value.Trim(), message.value.Trim(),
                        _ =>
                        {
                            pendingRecipientId = null;
                            Show("Messages");
                        }, error => SetNotice("Message not sent: " + error)));
                }, true));
                screen.Add(compose);
                screen.Add(Label("RECENT MESSAGES", 17, CommandTone));
                if (messages == null || messages.Length == 0)
                    screen.Add(GameScreenCard("No messages yet", "Your conversations will appear here once an ally writes to you."));
                foreach (var item in (messages ?? Array.Empty<DirectMessageDto>()).Reverse())
                {
                    var outgoing = item.senderId == session.User.id;
                    var other = outgoing ? item.recipientId : item.senderId;
                    var card = GameScreenCard((outgoing ? "TO  " : "FROM  ") + other, item.body);
                    card.style.borderLeftWidth = 3;
                    card.style.borderLeftColor = outgoing ? CommandTone : ManaTone;
                    screen.Add(card);
                }
            }, error => SetNotice("Messages unavailable: " + error));
        }

        private void ShowProfile()
        {
            var user = session?.User;
            var screen = BeginGameScreen("YOUR JOURNEY", user == null ? "The Adventurer" : user.handle,
                user == null ? "Sign in to begin your story." : "Your victories, collection and allies in the Astral Rift.");
            if (user == null)
            {
                var card = GameScreenCard("Your story begins here", "Connect to the native game server to load your profile.");
                card.Add(UiButton("SIGN IN", ShowLogin, true));
                screen.Add(card);
                return;
            }
            var stats = Flex(true);
            stats.style.flexWrap = Wrap.Wrap;
            stats.style.marginBottom = 10;
            var rank = GameScreenCard("RATING", user.rating.ToString("N0"));
            rank.style.flexGrow = 1;
            rank.style.minWidth = 180;
            rank.style.marginRight = 10;
            var decks = GameScreenCard("YOUR DECKS", "Forge a new strategy or refine an old favorite.");
            decks.style.flexGrow = 1;
            decks.style.minWidth = 220;
            decks.Add(UiButton("OPEN DECKS", () => Show("Decks"), true));
            stats.Add(rank);
            stats.Add(decks);
            screen.Add(stats);
            var share = GameScreenCard("Play with friends",
                "Give an ally your Player ID so they can add you.");
            share.Add(Label(user.id, 13, CommandTone));
            share.Add(UiButton("COPY PLAYER ID", () =>
            {
                GUIUtility.systemCopyBuffer = user.id;
                SetNotice("Player ID copied. Share it with an ally.");
            }, true));
            screen.Add(share);
            var controls = Flex(true);
            controls.style.flexWrap = Wrap.Wrap;
            controls.Add(UiButton("MY COLLECTION", () => Show("Inventory")));
            controls.Add(UiButton("FIND ALLIES", () => Show("Friends")));
            controls.Add(UiButton("ENTER BATTLE", () => Show("Play"), true));
            screen.Add(controls);
        }

        private IEnumerator LoadStore()
        {
            yield return api.StoreProducts(products =>
            {
                if (page != "Store") return;
                var screen = BeginGameScreen("THE TRADING POST", "Astral Bazaar",
                    "Discover new card packs and expand your collection with in-game gold.");
                if (session?.IsAuthenticated == true)
                {
                    StartCoroutine(api.Wallet(session.Token, wallet =>
                    {
                        if (page != "Store") return;
                        var balances = wallet?.balances ?? Array.Empty<WalletBalanceDto>();
                        var balance = string.Join("  •  ",
                            balances.Select(b => b.amount + " " + b.currency.ToUpperInvariant()));
                        var purse = GameScreenCard("YOUR PURSE", string.IsNullOrEmpty(balance) ? "No currency yet" : balance);
                        screen.Insert(Math.Min(1, screen.childCount), purse);
                    }, error => SetNotice("Wallet unavailable: " + error)));
                }
                else
                {
                    var welcome = GameScreenCard("Sign in to trade",
                        "Connect your player account to buy packs with earned gold.");
                    welcome.Add(UiButton("SIGN IN", ShowLogin, true));
                    screen.Add(welcome);
                }
                screen.Add(Label("AVAILABLE WARES", 17, CommandTone));
                if (products == null || products.Length == 0)
                    screen.Add(GameScreenCard("The stalls are quiet",
                        "The bazaar has no packs available right now. Check back after the next event."));
                var grid = Flex(true);
                grid.style.flexWrap = Wrap.Wrap;
                foreach (var product in products ?? Array.Empty<StoreProductDto>())
                {
                    var item = product;
                    var card = GameScreenCard(item.name, item.description);
                    card.style.flexGrow = 1;
                    card.style.flexBasis = 280;
                    card.style.minWidth = 255;
                    card.style.marginRight = 12;
                    var price = Label(item.price + " " + item.currency.ToUpperInvariant(), 18, CommandTone);
                    price.style.marginTop = 12;
                    price.style.marginBottom = 8;
                    card.Add(price);
                    if (session?.IsAuthenticated == true)
                        card.Add(UiButton("BUY PACK", () =>
                            StartCoroutine(api.Purchase(session.Token, item.id,
                                receipt =>
                                {
                                    SetNotice("Pack opened! " + (receipt.cards?.Length ?? 0) +
                                        " cards added to your collection.");
                                    Show("Store");
                                }, error => SetNotice("Trade declined: " + error))), true));
                    grid.Add(card);
                }
                screen.Add(grid);
            }, error => SetNotice("Bazaar unavailable: " + error));
        }

        private IEnumerator LoadNews()
        {
            yield return api.PublicEvents(events =>
            {
                if (page != "News & Events") return;
                var screen = BeginGameScreen("WHISPERS FROM THE RIFT", "Tales & Events",
                    "Discover seasonal challenges, new encounters and happenings across the Rift.");
                if (events == null || events.Length == 0)
                    screen.Add(GameScreenCard("The stars are quiet",
                        "There are no active events at the moment. Return soon for fresh challenges."));
                foreach (var item in events ?? Array.Empty<PublicEventDto>())
                {
                    var card = GameScreenCard(item.name, item.description);
                    var tag = Label(item.kind.ToUpperInvariant() + " EVENT", 11, QuickTone);
                    card.Insert(0, tag);
                    screen.Add(card);
                }
                screen.Add(UiButton("BACK TO BATTLE", () => Show("Play"), true));
            }, error => SetNotice("Events unavailable: " + error));
        }
    }
}
