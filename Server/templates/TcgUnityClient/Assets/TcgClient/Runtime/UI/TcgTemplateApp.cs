using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TcgServer.Client
{
    [DisallowMultipleComponent]
    public sealed class TcgTemplateApp : MonoBehaviour
    {
        [SerializeField] private TcgConnectionSettings settings;

        private static readonly Color Background = Hex("0D121B");
        private static readonly Color Surface = Hex("151C27");
        private static readonly Color SurfaceRaised = Hex("1E2735");
        private static readonly Color Divider = Hex("374354");
        private static readonly Color Accent = Hex("5294D6");
        private static readonly Color Foreground = Hex("EBF0F6");
        private static readonly Color Muted = Hex("A0ABB9");
        private static readonly Color Danger = Hex("E16570");

        private TcgRestClient rest;
        private TcgClientSession session;
        private TcgGameConnection gameConnection;
        private Canvas interfaceCanvas;
        private Font font;

        private GameObject loginPage;
        private GameObject lobbyPage;
        private GameObject collectionPage;
        private GameObject socialPage;
        private GameObject gamesPage;
        private GameObject gamePage;
        private Text serviceStatus;
        private Text operatorStatus;
        private Text loginStatus;
        private Text lobbyStatus;
        private Text lobbySummary;
        private Text queueSummary;
        private Text rankingSummary;
        private Text inventorySummary;
        private Text deckSummary;
        private Text friendSummary;
        private Text messageSummary;
        private Text gameBrowserSummary;
        private Text featureStatus;
        private Text socialStatus;
        private Text gameBrowserStatus;
        private Text gameStatus;
        private Text gameSummary;
        private Text realtimeSummary;
        private InputField handleInput;
        private InputField passwordInput;
        private InputField gameIdInput;
        private InputField chatInput;
        private InputField friendIdInput;
        private InputField recipientIdInput;
        private InputField directMessageInput;
        private Coroutine gamePolling;
        private string currentGameId;

        private void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (settings == null)
                settings = Resources.Load<TcgConnectionSettings>("TcgConnectionSettings");
            session = GetComponent<TcgClientSession>() ?? gameObject.AddComponent<TcgClientSession>();
            rest = GetComponent<TcgRestClient>() ?? gameObject.AddComponent<TcgRestClient>();
            gameConnection = GetComponent<TcgGameConnection>() ?? gameObject.AddComponent<TcgGameConnection>();
            rest.Configure(settings);
            BuildInterface();
            BindRealtimeEvents();
            Debug.Log("TcgULtimateClient interface initialized.");
        }

        private void Start()
        {
            ShowPage(loginPage);
            CheckHealth();
            var screenshotPath = CommandLineValue("--tcg-screenshot");
            StartCoroutine(LogInterfaceState());
            if (!string.IsNullOrWhiteSpace(screenshotPath))
                StartCoroutine(CaptureScreenshot(screenshotPath));
        }

        private IEnumerator LogInterfaceState()
        {
            yield return new WaitForSeconds(1f);
            Canvas.ForceUpdateCanvases();
            var canvasRect = interfaceCanvas.GetComponent<RectTransform>().rect;
            var hasUiShader = Shader.Find("UI/Default") != null;
            Debug.Log($"TcgULtimateClient canvas: active={interfaceCanvas.gameObject.activeInHierarchy}, enabled={interfaceCanvas.enabled}, root={interfaceCanvas.isRootCanvas}, mode={interfaceCanvas.renderMode}, size={canvasRect.width}x{canvasRect.height}, children={interfaceCanvas.transform.childCount}, login={loginPage.activeInHierarchy}, shader={hasUiShader}.");
        }

        private static string CommandLineValue(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
                    return arguments[index + 1];
            }

            return null;
        }

        private static IEnumerator CaptureScreenshot(string path)
        {
            yield return new WaitForSeconds(2f);
            Canvas.ForceUpdateCanvases();
            yield return new WaitForEndOfFrame();
            var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Destroy(texture);
        }

        private void CheckHealth()
        {
            serviceStatus.text = "CONNECTING";
            StartCoroutine(rest.Health(
                health =>
                {
                    serviceStatus.text = $"{health.status?.ToUpperInvariant()} · API {health.version}";
                    serviceStatus.color = Accent;
                },
                error =>
                {
                    serviceStatus.text = "SERVER UNAVAILABLE";
                    serviceStatus.color = Danger;
                    loginStatus.text = error.ToString();
                }));
        }

        private void Authenticate(bool register)
        {
            var handle = handleInput.text.Trim();
            var password = passwordInput.text;
            if (string.IsNullOrWhiteSpace(handle) || string.IsNullOrWhiteSpace(password))
            {
                loginStatus.text = "Enter a handle and password.";
                return;
            }

            loginStatus.text = register ? "Creating account..." : "Signing in...";
            var operation = register
                ? rest.Register(handle, password, OnAuthenticated, OnAuthenticationFailed)
                : rest.Login(handle, password, OnAuthenticated, OnAuthenticationFailed);
            StartCoroutine(operation);
        }

        private void OnAuthenticated(AuthSessionDto auth)
        {
            session.SetSession(auth);
            passwordInput.text = string.Empty;
            operatorStatus.text = $"{auth.user.handle.ToUpperInvariant()} · RATING {auth.user.rating}";
            ShowPage(lobbyPage);
            RefreshLobby();
        }

        private void OnAuthenticationFailed(TcgApiError error) => loginStatus.text = error.ToString();

        private void RefreshLobby()
        {
            if (!session.IsAuthenticated)
                return;

            lobbyStatus.text = "Refreshing live server data...";
            lobbySummary.text = $"PLAYER\n{session.User.handle}\n\nRATING\n{session.User.rating:N0}\n\nROLE\n{session.User.role.ToUpperInvariant()}";

            StartCoroutine(rest.Presence(
                presence => lobbyStatus.text = $"{presence.onlinePlayers:N0} player{(presence.onlinePlayers == 1 ? string.Empty : "s")} online",
                error => lobbyStatus.text = error.ToString()));

            StartCoroutine(rest.MatchmakingQueues(
                queues =>
                {
                    queueSummary.text = queues.Length == 0
                        ? "No enabled matchmaking queues."
                        : string.Join("\n", queues.Select(item =>
                            $"{item.queue.name}  ·  {item.waitingPlayers} waiting  ·  longest {item.longestWaitSeconds}s"));
                },
                error => queueSummary.text = error.ToString()));

            StartCoroutine(rest.Rankings(
                rankings =>
                {
                    rankingSummary.text = rankings.Length == 0
                        ? "No ranked players."
                        : string.Join("\n", rankings.Take(8).Select(item =>
                            $"#{item.rank,-3} {item.user.handle,-18} {item.user.rating,5:N0}"));
                },
                error => rankingSummary.text = error.ToString()));

            StartCoroutine(rest.MyMatchmaking(
                session.Token,
                entries =>
                {
                    if (entries.Length > 0)
                        lobbyStatus.text = $"Queued in {entries[0].queueId} since {entries[0].joinedAt}";
                },
                _ => { }));
        }

        private void JoinQueue(string queueId)
        {
            if (!RequireSession())
                return;

            lobbyStatus.text = $"Joining {queueId}...";
            StartCoroutine(rest.JoinMatchmaking(
                session.Token,
                queueId,
                entry =>
                {
                    lobbyStatus.text = $"Queued in {entry.queueId} · {entry.region}";
                    RefreshLobby();
                },
                error => lobbyStatus.text = error.ToString()));
        }

        private void LeaveQueue()
        {
            if (!RequireSession())
                return;

            StartCoroutine(rest.LeaveMatchmaking(
                session.Token,
                () =>
                {
                    lobbyStatus.text = "Left matchmaking.";
                    RefreshLobby();
                },
                error => lobbyStatus.text = error.ToString()));
        }

        private void CreateAiGame()
        {
            if (!RequireSession())
                return;

            lobbyStatus.text = "Creating game against AI...";
            StartCoroutine(rest.CreateAiGame(
                session.Token,
                session.User.id,
                game => OpenGame(game.id),
                error => lobbyStatus.text = error.ToString()));
        }

        private void RefreshCollection()
        {
            if (!RequireSession())
                return;

            featureStatus.text = "Refreshing collection and decks...";
            StartCoroutine(rest.Inventory(
                session.Token,
                session.User.id,
                inventory =>
                {
                    var cards = inventory.cards ?? Array.Empty<CardInstanceDto>();
                    var achievements = inventory.achievements ?? Array.Empty<string>();
                    var groupedCards = cards
                        .GroupBy(card => card.cardId)
                        .OrderBy(group => group.Key)
                        .Select(group => $"{group.Key,-24} x{group.Count()}");
                    inventorySummary.text = cards.Length == 0
                        ? "No cards in this collection yet."
                        : $"{cards.Length:N0} owned cards\n{achievements.Length:N0} achievements\n\n{string.Join("\n", groupedCards.Take(14))}";
                    featureStatus.text = $"Collection synchronized at {DateTime.Now:T}";
                },
                error => featureStatus.text = error.ToString()));

            StartCoroutine(rest.Decks(
                session.Token,
                decks => deckSummary.text = decks.Length == 0
                    ? "No saved decks. Build the first deck from owned cards."
                    : string.Join("\n\n", decks.Take(10).Select(deck =>
                        $"{deck.name}\n{deck.cardIds.Length} cards · {deck.rulePackId} · {(deck.suspended ? "SUSPENDED" : "READY")}")),
                error => deckSummary.text = error.ToString()));
        }

        private void RefreshSocial()
        {
            if (!RequireSession())
                return;

            socialStatus.text = "Refreshing friends and messages...";
            StartCoroutine(rest.Friends(
                session.Token,
                friends => friendSummary.text = friends.Length == 0
                    ? "No friends yet. Add one using their player ID."
                    : string.Join("\n", friends.Take(12).Select(friend =>
                        $"{OtherPlayer(friend)} · {friend.status} · connection {friend.id}")),
                error => socialStatus.text = error.ToString()));
            StartCoroutine(rest.Messages(
                session.Token,
                messages =>
                {
                    messageSummary.text = messages.Length == 0
                        ? "No direct messages."
                        : string.Join("\n\n", messages.Take(10).Select(message =>
                            $"{(message.senderId == session.User.id ? "TO" : "FROM")} {(message.senderId == session.User.id ? message.recipientId : message.senderId)}\n{message.body}"));
                    socialStatus.text = $"Social data synchronized at {DateTime.Now:T}";
                },
                error => socialStatus.text = error.ToString()));
        }

        private string OtherPlayer(FriendConnectionDto friend) =>
            friend.userId == session.User.id ? friend.friendUserId : friend.userId;

        private void AddFriend()
        {
            if (!RequireSession())
                return;

            var playerId = friendIdInput.text.Trim();
            if (string.IsNullOrWhiteSpace(playerId))
            {
                socialStatus.text = "Enter a player ID to add.";
                return;
            }

            StartCoroutine(rest.AddFriend(
                session.Token,
                playerId,
                _ =>
                {
                    friendIdInput.text = string.Empty;
                    RefreshSocial();
                },
                error => socialStatus.text = error.ToString()));
        }

        private void SendDirectMessage()
        {
            if (!RequireSession())
                return;

            var recipientId = recipientIdInput.text.Trim();
            var body = directMessageInput.text.Trim();
            if (string.IsNullOrWhiteSpace(recipientId) || string.IsNullOrWhiteSpace(body))
            {
                socialStatus.text = "Enter a recipient player ID and message.";
                return;
            }

            StartCoroutine(rest.SendMessage(
                session.Token,
                recipientId,
                body,
                _ =>
                {
                    directMessageInput.text = string.Empty;
                    RefreshSocial();
                },
                error => socialStatus.text = error.ToString()));
        }

        private void RefreshGameBrowser()
        {
            if (!RequireSession())
                return;

            gameBrowserStatus.text = "Refreshing active and recorded games...";
            StartCoroutine(rest.Games(
                session.Token,
                games =>
                {
                    gameBrowserSummary.text = games.Length == 0
                        ? "No games are currently available."
                        : string.Join("\n\n", games.OrderByDescending(game => game.turn).Take(14).Select(game =>
                            $"{game.id}\nTurn {game.turn} · {(game.finished ? "FINISHED" : "ACTIVE")} · {string.Join(", ", game.players)}"));
                    gameBrowserStatus.text = $"Game browser synchronized at {DateTime.Now:T}";
                },
                error => gameBrowserStatus.text = error.ToString()));
        }

        private void WatchGame()
        {
            var gameId = gameIdInput.text.Trim();
            if (string.IsNullOrWhiteSpace(gameId))
            {
                gameStatus.text = "Enter a game ID.";
                return;
            }

            OpenGame(gameId);
        }

        private void OpenGame(string gameId)
        {
            currentGameId = gameId;
            gameIdInput.text = gameId;
            ShowPage(gamePage);
            if (gamePolling != null)
                StopCoroutine(gamePolling);
            gamePolling = StartCoroutine(PollGame());
        }

        private IEnumerator PollGame()
        {
            while (gamePage.activeSelf && !string.IsNullOrWhiteSpace(currentGameId))
            {
                yield return rest.Game(
                    session.Token,
                    currentGameId,
                    RenderGame,
                    error => gameStatus.text = error.ToString());
                yield return new WaitForSeconds(settings == null ? 1f : settings.GamePollIntervalSeconds);
            }
        }

        private void RenderGame(GameStateDto game)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"GAME  {game.id}");
            builder.AppendLine($"RULES  {game.rulePackId}");
            builder.AppendLine($"TURN   {game.turn}");
            builder.AppendLine($"ACTIVE {game.activePlayerId}");
            builder.AppendLine($"STATE  {(game.finished ? "FINISHED" : "ACTIVE")}");
            builder.AppendLine($"PLAYERS {string.Join(", ", game.players ?? Array.Empty<string>())}");
            builder.AppendLine();
            builder.AppendLine("RECENT EVENTS");
            foreach (var gameEvent in (game.events ?? Array.Empty<GameEventDto>()).TakeLast(8))
                builder.AppendLine($"{gameEvent.type,-14} {gameEvent.message}");

            gameStatus.text = $"Last synchronized {DateTime.Now:T}";
            gameSummary.text = builder.ToString();
        }

        private async void ConnectRealtime()
        {
            if (!RequireSession() || string.IsNullOrWhiteSpace(currentGameId))
            {
                realtimeSummary.text = "Open a game before connecting SignalR.";
                return;
            }

            try
            {
                realtimeSummary.text = "Connecting real-time channel...";
                await gameConnection.ConnectAsync(settings, session.Token, currentGameId);
            }
            catch (Exception exception)
            {
                realtimeSummary.text = exception.Message;
            }
        }

        private async void SendChat()
        {
            var message = chatInput.text.Trim();
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                await gameConnection.SendChatAsync(message);
                chatInput.text = string.Empty;
            }
            catch (Exception exception)
            {
                realtimeSummary.text = exception.Message;
            }
        }

        private void BindRealtimeEvents()
        {
            gameConnection.GameStateChanged += RenderGame;
            gameConnection.ChatReceived += message => realtimeSummary.text = message;
            gameConnection.AnnouncementReceived += message => realtimeSummary.text = $"ANNOUNCEMENT · {message}";
            gameConnection.ConnectionStateChanged += state => realtimeSummary.text = $"SignalR · {state}";
        }

        private bool RequireSession()
        {
            if (session.IsAuthenticated)
                return true;

            ShowPage(loginPage);
            loginStatus.text = "Sign in first.";
            return false;
        }

        private void SignOut()
        {
            var token = session.Token;
            _ = gameConnection.DisconnectAsync();
            session.Clear();
            operatorStatus.text = "NOT SIGNED IN";
            currentGameId = null;
            if (gamePolling != null)
                StopCoroutine(gamePolling);
            ShowPage(loginPage);
            if (!string.IsNullOrWhiteSpace(token))
                StartCoroutine(rest.Logout(token, () => { }, _ => { }));
        }

        private void ShowPage(GameObject page)
        {
            loginPage.SetActive(page == loginPage);
            lobbyPage.SetActive(page == lobbyPage);
            collectionPage.SetActive(page == collectionPage);
            socialPage.SetActive(page == socialPage);
            gamesPage.SetActive(page == gamesPage);
            gamePage.SetActive(page == gamePage);
        }

        private void BuildInterface()
        {
            EnsureEventSystem();
            var canvas = CreateCanvas();
            CreatePanel(canvas.transform, "Background", Background, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            BuildHeader(canvas.transform);
            loginPage = CreatePage(canvas.transform, "Login Page");
            lobbyPage = CreatePage(canvas.transform, "Lobby Page");
            collectionPage = CreatePage(canvas.transform, "Collection Page");
            socialPage = CreatePage(canvas.transform, "Social Page");
            gamesPage = CreatePage(canvas.transform, "Games Page");
            gamePage = CreatePage(canvas.transform, "Game Page");
            BuildLoginPage(loginPage.transform);
            BuildLobbyPage(lobbyPage.transform);
            BuildCollectionPage(collectionPage.transform);
            BuildSocialPage(socialPage.transform);
            BuildGamesPage(gamesPage.transform);
            BuildGamePage(gamePage.transform);
        }

        private void BuildHeader(Transform parent)
        {
            var header = CreatePanel(parent, "Header", Surface, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -76), Vector2.zero);
            var layout = header.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 13, 13);
            layout.spacing = 18;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;

            var brand = CreateText(header.transform, "TCG ULTIMATE CLIENT\nPLAYER CONNECTION TEMPLATE", 20, Foreground, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetLayout(brand.gameObject, 360, 50);
            serviceStatus = CreateText(header.transform, "CONNECTING", 13, Muted, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetLayout(serviceStatus.gameObject, 280, 50, 1);
            operatorStatus = CreateText(header.transform, "NOT SIGNED IN", 12, Muted, FontStyle.Bold, TextAnchor.MiddleRight);
            SetLayout(operatorStatus.gameObject, 260, 50);
        }

        private void BuildLoginPage(Transform page)
        {
            CreateTitle(page, "Connect to TcgULtimateServer", "Register a test player or sign in with a server-managed account.");
            var card = CreateCard(page, 470);
            handleInput = CreateInput(card.transform, "Handle", false);
            passwordInput = CreateInput(card.transform, "Password", true);
            var buttons = CreateButtonRow(card.transform);
            CreateButton(buttons.transform, "Sign in", () => Authenticate(false), Accent);
            CreateButton(buttons.transform, "Register", () => Authenticate(true), SurfaceRaised);
            CreateButton(buttons.transform, "Retry server", CheckHealth, SurfaceRaised);
            loginStatus = CreateText(card.transform, "Use the local development server at https://localhost:5001.", 13, Muted, FontStyle.Normal, TextAnchor.UpperLeft);
            SetLayout(loginStatus.gameObject, -1, 70);
        }

        private void BuildLobbyPage(Transform page)
        {
            CreateTitle(page, "Player lobby", "Review live service data, join matchmaking, or create a game against AI.");
            var columns = CreateHorizontalArea(page, 520);
            var identityCard = CreateCard(columns.transform, 410, 0.8f);
            lobbySummary = CreateText(identityCard.transform, "PLAYER", 18, Foreground, FontStyle.Bold, TextAnchor.UpperLeft);
            SetLayout(lobbySummary.gameObject, -1, 150);
            lobbyStatus = CreateText(identityCard.transform, "Waiting for player data...", 13, Accent, FontStyle.Bold, TextAnchor.UpperLeft);
            SetLayout(lobbyStatus.gameObject, -1, 60);
            var identityButtons = CreateButtonRow(identityCard.transform);
            CreateButton(identityButtons.transform, "Refresh", RefreshLobby, SurfaceRaised);
            CreateButton(identityButtons.transform, "Ranked", () => JoinQueue("ranked"), Accent);
            CreateButton(identityButtons.transform, "Casual", () => JoinQueue("casual"), SurfaceRaised);
            CreateButton(identityButtons.transform, "Leave queue", LeaveQueue, SurfaceRaised);
            CreateButton(identityButtons.transform, "Create vs AI", CreateAiGame, Accent);
            CreateButton(identityButtons.transform, "Sign out", SignOut, Danger);

            var featureButtons = CreateButtonRow(identityCard.transform);
            CreateButton(featureButtons.transform, "Collection & decks", () =>
            {
                ShowPage(collectionPage);
                RefreshCollection();
            }, SurfaceRaised);
            CreateButton(featureButtons.transform, "Friends & messages", () =>
            {
                ShowPage(socialPage);
                RefreshSocial();
            }, SurfaceRaised);
            CreateButton(featureButtons.transform, "Game browser", () =>
            {
                ShowPage(gamesPage);
                RefreshGameBrowser();
            }, SurfaceRaised);

            var dataCard = CreateCard(columns.transform, 410, 1.4f);
            CreateSectionLabel(dataCard.transform, "MATCHMAKING");
            queueSummary = CreateText(dataCard.transform, "Loading queues...", 13, Foreground, FontStyle.Normal, TextAnchor.UpperLeft);
            SetLayout(queueSummary.gameObject, -1, 100);
            CreateSectionLabel(dataCard.transform, "RANKINGS");
            rankingSummary = CreateText(dataCard.transform, "Loading rankings...", 13, Foreground, FontStyle.Normal, TextAnchor.UpperLeft);
            SetLayout(rankingSummary.gameObject, -1, 210);
        }

        private void BuildCollectionPage(Transform page)
        {
            CreateTitle(page, "Collection & decks", "Inspect owned cards, achievements, and server-backed deck records.");
            var actions = CreateButtonRow(page);
            CreateButton(actions.transform, "Back to lobby", () => ShowPage(lobbyPage), SurfaceRaised);
            CreateButton(actions.transform, "Refresh", RefreshCollection, Accent);
            featureStatus = CreateText(actions.transform, "Ready", 13, Muted, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetLayout(featureStatus.gameObject, -1, 42, 1);

            var columns = CreateHorizontalArea(page, 520);
            var inventoryCard = CreateCard(columns.transform, 500);
            CreateSectionLabel(inventoryCard.transform, "COLLECTION");
            inventorySummary = CreateText(inventoryCard.transform, "Loading collection...", 13, Foreground, FontStyle.Normal, TextAnchor.UpperLeft);
            SetLayout(inventorySummary.gameObject, -1, 430);
            var decksCard = CreateCard(columns.transform, 500);
            CreateSectionLabel(decksCard.transform, "DECKS");
            deckSummary = CreateText(decksCard.transform, "Loading decks...", 13, Foreground, FontStyle.Normal, TextAnchor.UpperLeft);
            SetLayout(deckSummary.gameObject, -1, 430);
        }

        private void BuildSocialPage(Transform page)
        {
            CreateTitle(page, "Friends & messages", "Manage player connections and server-backed direct messages.");
            var actions = CreateButtonRow(page);
            CreateButton(actions.transform, "Back to lobby", () => ShowPage(lobbyPage), SurfaceRaised);
            CreateButton(actions.transform, "Refresh", RefreshSocial, Accent);
            socialStatus = CreateText(actions.transform, "Ready", 13, Muted, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetLayout(socialStatus.gameObject, -1, 42, 1);
            var columns = CreateHorizontalArea(page, 520);

            var friendsCard = CreateCard(columns.transform, 500);
            CreateSectionLabel(friendsCard.transform, "FRIENDS");
            friendIdInput = CreateInput(friendsCard.transform, "Player ID", false);
            var friendActions = CreateButtonRow(friendsCard.transform);
            CreateButton(friendActions.transform, "Add friend", AddFriend, Accent);
            friendSummary = CreateText(friendsCard.transform, "Loading friends...", 13, Foreground, FontStyle.Normal, TextAnchor.UpperLeft);
            SetLayout(friendSummary.gameObject, -1, 330);

            var messagesCard = CreateCard(columns.transform, 500);
            CreateSectionLabel(messagesCard.transform, "DIRECT MESSAGES");
            recipientIdInput = CreateInput(messagesCard.transform, "Recipient player ID", false);
            directMessageInput = CreateInput(messagesCard.transform, "Message", false);
            var messageActions = CreateButtonRow(messagesCard.transform);
            CreateButton(messageActions.transform, "Send message", SendDirectMessage, Accent);
            messageSummary = CreateText(messagesCard.transform, "Loading messages...", 13, Foreground, FontStyle.Normal, TextAnchor.UpperLeft);
            SetLayout(messageSummary.gameObject, -1, 270);
        }

        private void BuildGamesPage(Transform page)
        {
            CreateTitle(page, "Game browser", "Open active games and completed recordings by their server game ID.");
            var actions = CreateButtonRow(page);
            CreateButton(actions.transform, "Back to lobby", () => ShowPage(lobbyPage), SurfaceRaised);
            CreateButton(actions.transform, "Refresh", RefreshGameBrowser, Accent);
            var browserIdInput = CreateInput(actions.transform, "Game ID", false, 360);
            CreateButton(actions.transform, "Open game", () =>
            {
                var id = browserIdInput.text.Trim();
                if (!string.IsNullOrWhiteSpace(id))
                    OpenGame(id);
            }, Accent);
            gameBrowserStatus = CreateText(actions.transform, "Ready", 13, Muted, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetLayout(gameBrowserStatus.gameObject, -1, 42, 1);

            var card = CreateCard(page, 510);
            CreateSectionLabel(card.transform, "ACTIVE & RECORDED GAMES");
            gameBrowserSummary = CreateText(card.transform, "Loading games...", 13, Foreground, FontStyle.Normal, TextAnchor.UpperLeft);
            SetLayout(gameBrowserSummary.gameObject, -1, 440);
        }

        private void BuildGamePage(Transform page)
        {
            CreateTitle(page, "Game session", "Poll public game state immediately, then enable SignalR for chat and real-time actions.");
            var controls = CreateButtonRow(page);
            gameIdInput = CreateInput(controls.transform, "Game ID", false, 360);
            CreateButton(controls.transform, "Watch game", WatchGame, Accent);
            CreateButton(controls.transform, "Back to lobby", () => ShowPage(lobbyPage), SurfaceRaised);
            CreateButton(controls.transform, "Connect SignalR", ConnectRealtime, SurfaceRaised);

            var gameCard = CreateCard(page, 380);
            gameStatus = CreateText(gameCard.transform, "Enter a game ID or create a game from the lobby.", 13, Accent, FontStyle.Bold, TextAnchor.UpperLeft);
            SetLayout(gameStatus.gameObject, -1, 32);
            gameSummary = CreateText(gameCard.transform, "No game loaded.", 14, Foreground, FontStyle.Normal, TextAnchor.UpperLeft);
            SetLayout(gameSummary.gameObject, -1, 230);
            realtimeSummary = CreateText(gameCard.transform, TcgGameConnection.IsAvailable ? "SignalR package detected." : "REST polling ready · SignalR optional package not enabled.", 13, Muted, FontStyle.Normal, TextAnchor.UpperLeft);
            SetLayout(realtimeSummary.gameObject, -1, 44);
            var chat = CreateButtonRow(gameCard.transform);
            chatInput = CreateInput(chat.transform, "Game chat", false, 420);
            CreateButton(chat.transform, "Send chat", SendChat, Accent);
        }

        private GameObject CreateCanvas()
        {
            var cameraObject = new GameObject("Tcg UI Camera", typeof(Camera));
            DontDestroyOnLoad(cameraObject);
            var uiCamera = cameraObject.GetComponent<Camera>();
            uiCamera.clearFlags = CameraClearFlags.SolidColor;
            uiCamera.backgroundColor = Background;
            uiCamera.orthographic = true;
            uiCamera.nearClipPlane = 0.01f;
            uiCamera.farClipPlane = 10f;

            var canvasObject = new GameObject("Tcg Client UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasObject);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = uiCamera;
            canvas.planeDistance = 1f;
            interfaceCanvas = canvas;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 0.5f;
            return canvasObject;
        }

        private GameObject CreatePage(Transform parent, string name)
        {
            var page = CreatePanel(parent, name, Background, Vector2.zero, Vector2.one, new Vector2(26, 28), new Vector2(-26, -96));
            var layout = page.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 12, 12);
            layout.spacing = 16;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            return page;
        }

        private void CreateTitle(Transform parent, string title, string subtitle)
        {
            var area = CreatePanel(parent, title + " Header", Surface, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            SetLayout(area, -1, 96);
            var layout = area.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(22, 22, 14, 12);
            layout.spacing = 3;
            CreateText(area.transform, title, 28, Foreground, FontStyle.Bold, TextAnchor.MiddleLeft);
            CreateText(area.transform, subtitle, 14, Muted, FontStyle.Normal, TextAnchor.MiddleLeft);
        }

        private GameObject CreateCard(Transform parent, float height, float flexibleWidth = 1f)
        {
            var card = CreatePanel(parent, "Card", Surface, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            SetLayout(card, -1, height, flexibleWidth);
            var layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 18, 18);
            layout.spacing = 12;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            return card;
        }

        private GameObject CreateHorizontalArea(Transform parent, float height)
        {
            var area = new GameObject("Columns", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            area.transform.SetParent(parent, false);
            SetLayout(area, -1, height);
            var layout = area.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 16;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            return area;
        }

        private GameObject CreateButtonRow(Transform parent)
        {
            var row = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            SetLayout(row, -1, 46);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;
            return row;
        }

        private Button CreateButton(Transform parent, string label, Action clicked, Color color)
        {
            var buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            SetLayout(buttonObject, Mathf.Max(112, label.Length * 9 + 28), 42);
            buttonObject.GetComponent<Image>().color = color;
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.AddListener(() => clicked());
            var text = CreateText(buttonObject.transform, label, 13, Foreground, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 10);
            return button;
        }

        private InputField CreateInput(Transform parent, string placeholder, bool password, float width = -1)
        {
            var inputObject = new GameObject(placeholder, typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            inputObject.transform.SetParent(parent, false);
            SetLayout(inputObject, width, 46, width < 0 ? 1 : 0);
            inputObject.GetComponent<Image>().color = SurfaceRaised;

            var input = inputObject.GetComponent<InputField>();
            input.targetGraphic = inputObject.GetComponent<Image>();
            input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;

            var value = CreateText(inputObject.transform, string.Empty, 14, Foreground, FontStyle.Normal, TextAnchor.MiddleLeft);
            Stretch(value.rectTransform, 14);
            input.textComponent = value;

            var hint = CreateText(inputObject.transform, placeholder, 14, Muted, FontStyle.Italic, TextAnchor.MiddleLeft);
            Stretch(hint.rectTransform, 14);
            input.placeholder = hint;
            return input;
        }

        private void CreateSectionLabel(Transform parent, string text)
        {
            var label = CreateText(parent, text, 11, Accent, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetLayout(label.gameObject, -1, 24);
        }

        private Text CreateText(Transform parent, string value, int size, Color color, FontStyle style, TextAnchor anchor)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.text = value;
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static GameObject CreatePanel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = color;
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return panel;
        }

        private static void SetLayout(GameObject target, float width, float height, float flexibleWidth = 0)
        {
            var layout = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            if (width >= 0)
                layout.preferredWidth = width;
            if (height >= 0)
                layout.preferredHeight = height;
            layout.flexibleWidth = flexibleWidth;
        }

        private static void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, 4);
            rect.offsetMax = new Vector2(-padding, -4);
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null)
                return;

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out var color);
            return color;
        }
    }
}
