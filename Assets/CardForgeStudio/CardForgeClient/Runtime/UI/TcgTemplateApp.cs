using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace TcgServer.Client
{
    [DisallowMultipleComponent]
#if UNITY_6000_5_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed class TcgTemplateApp : MonoBehaviour
    {
        [SerializeField] private TcgConnectionSettings settings;

        private static readonly Color Background = Hex("0A0D18");
        private static readonly Color Surface = Hex("131A30");
        private static readonly Color SurfaceRaised = Hex("1C2547");
        private static readonly Color Divider = Hex("2E3A66");
        private static readonly Color Accent = Hex("E3B23C");
        private static readonly Color AccentAlt = Hex("5B8DEF");
        private static readonly Color Ink = Hex("1B1408");
        private static readonly Color Foreground = Hex("F2EDE3");
        private static readonly Color Muted = Hex("9AA3C0");
        private static readonly Color Danger = Hex("E16570");

        // MTG-style mana gem colors: white, blue, black, red, green, colorless.
        private static readonly Color[] GemColors =
        {
            Hex("F2EDE3"), Hex("4A90E2"), Hex("8E7BD8"), Hex("D95F4B"), Hex("57A05A"), Hex("9AA3C0")
        };
        private static Sprite circleSprite;
        private static Sprite battlefieldSprite;
        private static readonly Dictionary<string, Sprite> DemoArtCache = new();

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
        private Text zoneSummary;
        private Text actionStatus;
        private Text realtimeSummary;
        private InputField handleInput;
        private InputField passwordInput;
        private InputField gameIdInput;
        private InputField chatInput;
        private InputField friendIdInput;
        private InputField recipientIdInput;
        private InputField directMessageInput;
        private Coroutine gamePolling;
        private float nextLobbyRefresh;
        private string currentGameId;
        private GameObject actionButtonRow;
        private ClientGameViewDto currentGameView;
        private Dictionary<string, CardDefinitionDto> cardDefinitions = new();
        private string loadedRulePackId;
        private string loadingRulePackId;
        private string lastZoneSignature;
        private string lastActionSignature;
        private GameObject playerHandContainer;
        private GameObject playerFieldContainer;
        private GameObject opponentHandContainer;
        private GameObject opponentFieldContainer;
        private GameObject opponentAvatar;
        private GameObject playerAvatar;
        private Text turnPhaseText;
        private Text playerLifeText;
        private Text opponentLifeText;
        private Text playerEnergyText, opponentEnergyText, playerRenownText, opponentRenownText;
        private Dictionary<string, GameObject> cardObjects = new();
        private GameObject cardInspector;
        private GameObject resultPanel;
        private GameObject matchLogPanel;
        private Text matchLogText;
        private Text resultTitle;
        private HashSet<string> previouslyRenderedCards = new();
        private CardInstanceDto selectedCard;
        private ClientGameActionDto pendingTargetAction;
        private bool actionInFlight;
        private Text eventBanner;
        private string lastEventKey;
        private long? previousPlayerLife, previousOpponentLife;
        private bool autoStartDemoMatch;

        private void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (settings == null)
                settings = Resources.Load<TcgConnectionSettings>("TcgConnectionSettings");
#if UNITY_EDITOR
            // Never mutate the project's saved connection asset during automatic port discovery.
            if (settings != null) settings = Instantiate(settings);
#endif
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
            autoStartDemoMatch = Environment.GetCommandLineArgs().Any(arg =>
                string.Equals(arg, "--tcg-demo-login", StringComparison.OrdinalIgnoreCase));
#if UNITY_EDITOR
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "TcgClient" &&
                settings != null && settings.DefaultRulePackId == "astral-rift")
                autoStartDemoMatch = true;
#endif
            if (autoStartDemoMatch) StartCoroutine(BeginDemoMatchWhenReady());
        }

        private void Update()
        {
            HandleMatchShortcuts();
            if (lobbyPage == null || !lobbyPage.activeSelf || !session.IsAuthenticated)
                return;
            if (Time.unscaledTime < nextLobbyRefresh)
                return;

            nextLobbyRefresh = Time.unscaledTime + (settings == null ? 5f : settings.LobbyRefreshIntervalSeconds);
            RefreshLobby(false);
        }

        private void ToggleMatchLog()
        {
            if (matchLogPanel == null) return;
            var show = !matchLogPanel.activeSelf;
            matchLogPanel.SetActive(show);
            if (show && cardInspector != null) cardInspector.SetActive(false);
            else if (!show && selectedCard != null) ShowCardInspector();
        }

        private void HandleMatchShortcuts()
        {
#if ENABLE_INPUT_SYSTEM
            if (gamePage == null || !gamePage.activeSelf) return;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;
            if (EventSystem.current?.currentSelectedGameObject?.GetComponentInParent<InputField>() != null)
                return;
            if (keyboard.lKey.wasPressedThisFrame) ToggleMatchLog();
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (matchLogPanel != null && matchLogPanel.activeSelf)
                {
                    ToggleMatchLog();
                    return;
                }
                if (pendingTargetAction != null) pendingTargetAction = null;
                else
                {
                    selectedCard = null;
                    if (cardInspector != null) cardInspector.SetActive(false);
                    UpdateCardVisualStates();
                }
                RebuildActionButtons(currentGameView, true);
            }
            // Space advances the phase only when the server explicitly offers that action.
            if (keyboard.spaceKey.wasPressedThisFrame && !actionInFlight &&
                pendingTargetAction == null && currentGameView?.activePlayerId == session.User?.id)
            {
                var advance = (currentGameView.allowedActions ?? Array.Empty<ClientGameActionDto>())
                    .FirstOrDefault(action => string.IsNullOrWhiteSpace(action.sourceZone) &&
                        (action.id.IndexOf("advance-phase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         action.id.IndexOf("next-phase", StringComparison.OrdinalIgnoreCase) >= 0));
                if (advance != null) ExecuteAction(advance);
            }
#endif
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

        private IEnumerator BeginDemoMatchWhenReady()
        {
            // Starting a scene should work after a fresh reboot, even if the server
            // is still publishing or has moved away from an occupied default port.
            for (var attempt = 0; attempt < 90; attempt++)
            {
                if (settings == null) yield break;
                var rulePath = "/api/rules/" + UnityWebRequest.EscapeURL(settings.DefaultRulePackId);
                var foundServer = false;

                using (var request = UnityWebRequest.Get(settings.BaseUrl + rulePath))
                {
                    request.timeout = 2;
                    yield return request.SendWebRequest();
                    foundServer = request.result == UnityWebRequest.Result.Success &&
                        request.responseCode == 200;
                }

#if UNITY_EDITOR
                if (!foundServer)
                {
                    for (var port = 5168; port <= 5198 && !foundServer; port++)
                    {
                        var url = "http://127.0.0.1:" + port;
                        if (settings.BaseUrl == url) continue;
                        using (var request = UnityWebRequest.Get(url + rulePath))
                        {
                            request.timeout = 1;
                            yield return request.SendWebRequest();
                            foundServer = request.result == UnityWebRequest.Result.Success &&
                                request.responseCode == 200;
                            if (foundServer)
                            {
                                settings.SetRuntimeBaseUrl(url);
                                rest.Configure(settings);
                            }
                        }
                    }
                }
#endif

                if (foundServer)
                {
                    CheckHealth();
                    QuickDemoLogin();
                    yield break;
                }
                if (serviceStatus != null) serviceStatus.text = "STARTING ASTRAL RIFT SERVER...";
                yield return new WaitForSeconds(2f);
            }
            autoStartDemoMatch = false;
            if (loginStatus != null)
                loginStatus.text = "The Astral Rift server did not become available. Start the demo launcher and try again.";
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
            nextLobbyRefresh = Time.unscaledTime + (settings == null ? 5f : settings.LobbyRefreshIntervalSeconds);
            ShowPage(lobbyPage);
            RefreshLobby();
            if (autoStartDemoMatch)
            {
                autoStartDemoMatch = false;
                CreateAiGame();
            }
        }

        private void OnAuthenticationFailed(TcgApiError error) => loginStatus.text = error.ToString();

        private const string DemoHandle = "demo";
        private const string DemoPassword = "cardforge-demo";

        private void QuickDemoLogin()
        {
            handleInput.text = DemoHandle;
            passwordInput.text = DemoPassword;
            loginStatus.text = "Quick demo sign-in...";
            StartCoroutine(rest.Login(
                DemoHandle,
                DemoPassword,
                OnAuthenticated,
                _ => StartCoroutine(RegisterDemoAccount())));
        }

        private IEnumerator RegisterDemoAccount()
        {
            loginStatus.text = "Demo account not found — registering...";
            yield return rest.Register(
                DemoHandle,
                DemoPassword,
                OnAuthenticated,
                error => loginStatus.text = "Demo sign-in failed: " + error);
        }

        private void RefreshLobby() => RefreshLobby(true);

        private void RefreshLobby(bool announce)
        {
            if (!session.IsAuthenticated)
                return;

            if (announce)
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

            lobbyStatus.text = "Creating a match against the server AI...";
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
            selectedCard = null;
            pendingTargetAction = null;
            actionInFlight = false;
            lastEventKey = null;
            previousPlayerLife = null;
            previousOpponentLife = null;
            if (cardInspector != null) cardInspector.SetActive(false);
            if (resultPanel != null) resultPanel.SetActive(false);
            if (matchLogPanel != null) matchLogPanel.SetActive(false);
            previouslyRenderedCards.Clear();
            lastZoneSignature = null;
            lastActionSignature = null;
            loadedRulePackId = null;
            loadingRulePackId = null;
            currentGameView = null;
            cardDefinitions.Clear();
            gameIdInput.text = gameId;
            ShowPage(gamePage);
            if (gamePolling != null)
                StopCoroutine(gamePolling);
            gamePolling = StartCoroutine(PollGame());
            if (TcgGameConnection.IsAvailable)
                ConnectRealtime();
            else
                realtimeSummary.text = "REST polling active · SignalR package not enabled.";
        }

        private IEnumerator PollGame()
        {
            while (gamePage.activeSelf && !string.IsNullOrWhiteSpace(currentGameId))
            {
                yield return rest.GameView(
                    session.Token,
                    currentGameId,
                    RenderGame,
                    error => gameStatus.text = error.ToString());
                yield return new WaitForSeconds(settings == null ? 1f : settings.GamePollIntervalSeconds);
            }
        }

        private void RenderGame(ClientGameViewDto game)
        {
            if (game == null || game.id != currentGameId) return;
            currentGameView = game;
            turnPhaseText.text = $"TURN {game.turn} · PHASE {game.currentPhaseId?.ToUpperInvariant() ?? "DRAW"} · {(game.finished ? "FINISHED" : "ACTIVE")}";
            if (game.finished) turnPhaseText.text += $" · WINNER {game.winnerPlayerId?.ToUpperInvariant()}";
            if (resultPanel != null)
            {
                resultPanel.SetActive(game.finished);
                if (game.finished && matchLogPanel != null) matchLogPanel.SetActive(false);
                if (game.finished && resultTitle != null)
                {
                    var won = game.winnerPlayerId == session.User?.id;
                    resultTitle.text = won ? "VICTORY" : "DEFEAT";
                    resultTitle.color = won ? Accent : Danger;
                }
            }
            if (matchLogText != null)
                matchLogText.text = string.Join("\n", (game.events ?? Array.Empty<GameEventDto>())
                    .TakeLast(9).Select(e => "• " + (e.message ?? e.type ?? "Event")));
            var latestEvent = (game.events ?? Array.Empty<GameEventDto>()).LastOrDefault();
            if (latestEvent != null && eventBanner != null)
            {
                var eventKey = latestEvent.at + ":" + latestEvent.type + ":" + latestEvent.message;
                if (eventKey != lastEventKey)
                {
                    lastEventKey = eventKey;
                    eventBanner.text = latestEvent.message ?? latestEvent.type;
                    StartCoroutine(FlashEventBanner());
                }
            }

            if (loadedRulePackId == game.rulePackId && cardDefinitions.Count > 0)
            {
                RefreshBattlefield(game);
                RebuildActionButtons(game);
            }
            else if (loadingRulePackId != game.rulePackId)
            {
                loadingRulePackId = game.rulePackId;
                StartCoroutine(FetchRulePackAndRender(game));
            }
        }

        private IEnumerator FetchRulePackAndRender(ClientGameViewDto game)
        {
            yield return rest.GameRulesSync(
                game.rulePackId,
                rulePack =>
                {
                    if (currentGameId != game.id) return;
                    cardDefinitions = (rulePack.cards ?? Array.Empty<CardDefinitionDto>()).ToDictionary(c => c.id, c => c);
                    loadedRulePackId = game.rulePackId;
                    loadingRulePackId = null;
                    lastZoneSignature = null;
                    RefreshBattlefield(game);
                    RebuildActionButtons(game);
                },
                error =>
                {
                    if (currentGameId != game.id) return;
                    loadingRulePackId = null;
                    realtimeSummary.text = "Failed to load rule pack: " + error;
                    RefreshBattlefield(game);
                    RebuildActionButtons(game);
                });
        }

        private void RefreshBattlefield(ClientGameViewDto game)
        {
            var zoneSignature = string.Join("|", (game.zones ?? Array.Empty<ClientGameZoneDto>())
                .Select(zone => zone.playerId + ":" + zone.zoneId + ":" + string.Join(",",
                    (zone.cards ?? Array.Empty<CardInstanceDto>()).Select(card => card.instanceId + ":" + card.cardId))));
            var shouldRebuild = zoneSignature != lastZoneSignature;
            if (shouldRebuild)
            {
                foreach (var obj in cardObjects.Values) Destroy(obj);
                cardObjects.Clear();
                lastZoneSignature = zoneSignature;
            }

            var userId = session.User?.id;
            if (string.IsNullOrWhiteSpace(userId)) return;

            var opponentId = (game.players ?? Array.Empty<string>()).FirstOrDefault(p => p != userId);
            if (string.IsNullOrWhiteSpace(opponentId)) return;

            if (shouldRebuild)
            {
                RenderZone(opponentHandContainer, game, opponentId, "hand", hidden: true);
                RenderZone(opponentFieldContainer, game, opponentId, "battlefield", hidden: false);
                RenderZone(playerHandContainer, game, userId, "hand", hidden: false);
                RenderZone(playerFieldContainer, game, userId, "battlefield", hidden: false);
                previouslyRenderedCards = new HashSet<string>(cardObjects.Keys);
            }

            var resources = (game.resources ?? Array.Empty<ClientGameResourceDto>()).FirstOrDefault(r => r.playerId == userId && r.resourceId == "life");
            if (resources != null && resources.resourceId == "life")
            {
                if (previousPlayerLife.HasValue && previousPlayerLife.Value != resources.value)
                    StartCoroutine(FlashAvatar(playerAvatar, resources.value < previousPlayerLife.Value));
                previousPlayerLife = resources.value;
                playerLifeText.text = resources.value.ToString();
            }
            var oppResources = (game.resources ?? Array.Empty<ClientGameResourceDto>()).FirstOrDefault(r => r.playerId == opponentId && r.resourceId == "life");
            if (oppResources != null && oppResources.resourceId == "life")
            {
                if (previousOpponentLife.HasValue && previousOpponentLife.Value != oppResources.value)
                    StartCoroutine(FlashAvatar(opponentAvatar, oppResources.value < previousOpponentLife.Value));
                previousOpponentLife = oppResources.value;
                opponentLifeText.text = oppResources.value.ToString();
            }
            var allResources = game.resources ?? Array.Empty<ClientGameResourceDto>();
            var energy = allResources.FirstOrDefault(r => r.playerId == userId && r.resourceId == "energy");
            var oppEnergy = allResources.FirstOrDefault(r => r.playerId == opponentId && r.resourceId == "energy");
            var renown = allResources.FirstOrDefault(r => r.playerId == userId && r.resourceId == "renown");
            var oppRenown = allResources.FirstOrDefault(r => r.playerId == opponentId && r.resourceId == "renown");
            playerEnergyText.text = "ENERGY  " + (energy?.value ?? 0);
            opponentEnergyText.text = "ENERGY  " + (oppEnergy?.value ?? 0);
            playerRenownText.text = "RENOWN  " + (renown?.value ?? 0);
            opponentRenownText.text = "RENOWN  " + (oppRenown?.value ?? 0);
            UpdateCardVisualStates();
            if (selectedCard != null)
            {
                var stillVisible = (game.zones ?? Array.Empty<ClientGameZoneDto>()).Any(z =>
                    (z.cards ?? Array.Empty<CardInstanceDto>()).Any(c => c.instanceId == selectedCard.instanceId));
                if (!stillVisible) { selectedCard = null; if (cardInspector != null) cardInspector.SetActive(false); }
                else if (shouldRebuild) ShowCardInspector();
            }
        }

        private void RenderZone(GameObject container, ClientGameViewDto game, string playerId, string zoneId, bool hidden)
        {
            var zone = (game.zones ?? Array.Empty<ClientGameZoneDto>()).FirstOrDefault(z => z.playerId == playerId && z.zoneId == zoneId);
            var cards = zone?.cards ?? Array.Empty<CardInstanceDto>();

            for (var index = 0; index < cards.Length; index++)
            {
                var card = cards[index];
                var key = string.IsNullOrWhiteSpace(card.instanceId)
                    ? playerId + ":" + zoneId + ":hidden:" + index
                    : card.instanceId;
                var cardObj = CreateCardObject(card, hidden);
                cardObj.transform.SetParent(container.transform, false);
                cardObjects[key] = cardObj;
                if (!previouslyRenderedCards.Contains(key)) StartCoroutine(AnimateCardArrival(cardObj));
            }
        }

        private GameObject CreateCardObject(CardInstanceDto card, bool hidden)
        {
            var panel = CreatePanel(null, "Card", SurfaceRaised, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            SetLayout(panel, 94, 134);
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 7, 7);
            layout.spacing = 2;
            layout.childAlignment = TextAnchor.MiddleCenter;

            if (hidden)
            {
                panel.GetComponent<Image>().color = Hex("293958");
                CreateText(panel.transform, "◆", 28, Accent, FontStyle.Bold, TextAnchor.MiddleCenter);
                CreateText(panel.transform, "ASTRAL RIFT", 9, Foreground, FontStyle.Bold, TextAnchor.MiddleCenter);
                return panel;
            }

            var def = cardDefinitions.GetValueOrDefault(card.cardId);
            var name = def?.name ?? card.cardId;
            var cost = def?.cost ?? 0;
            var power = def?.power ?? 0;
            var toughness = def?.toughness ?? 0;

            var topRow = CreateHorizontalArea(panel.transform, 16);
            CreateText(topRow.transform, cost.ToString(), 14, Accent, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetLayout(topRow.transform.GetChild(0).gameObject, 24, 16);

            var artwork = Resources.Load<Texture2D>("AstralRift/Illustrations/" + card.cardId);
            if (artwork != null)
            {
                if (!DemoArtCache.TryGetValue(card.cardId, out var sprite))
                {
                    sprite = Sprite.Create(artwork, new Rect(0, 0, artwork.width, artwork.height), new Vector2(0.5f, 0.5f));
                    DemoArtCache[card.cardId] = sprite;
                }
                var artPanel = new GameObject("Card illustration", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                artPanel.transform.SetParent(panel.transform, false);
                var artImage = artPanel.GetComponent<Image>();
                artImage.sprite = sprite;
                artImage.preserveAspect = true;
                SetLayout(artPanel, -1, 46);
            }
            var nameText = CreateText(panel.transform, name, 11, Foreground, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetLayout(nameText.gameObject, -1, artwork != null ? 28 : 64);
            CreateText(panel.transform, def?.type?.ToUpperInvariant() ?? "CARD", 9, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);

            if (power > 0 || toughness > 0)
            {
                var stats = CreateText(panel.transform, $"{power}/{toughness}", 10, Foreground, FontStyle.Bold, TextAnchor.MiddleCenter);
                SetLayout(stats.gameObject, -1, 12);
            }

            var button = panel.AddComponent<Button>();
            button.targetGraphic = panel.GetComponent<Image>();
            button.onClick.AddListener(() => OnCardClicked(card));
            return panel;
        }

        private void OnCardClicked(CardInstanceDto card)
        {
            if (currentGameView == null || !session.IsAuthenticated || actionInFlight) return;

            // Target selection is explicit. Never automatically choose the first card in a zone.
            if (pendingTargetAction != null)
            {
                if (pendingTargetAction.target == "target_player")
                {
                    actionStatus.text = "Click a player avatar to target that player.";
                    return;
                }
                var action = pendingTargetAction;
                pendingTargetAction = null;
                UpdateCardVisualStates();
                ExecuteAction(action, selectedCard?.instanceId, card.instanceId);
                return;
            }

            selectedCard = card;
            UpdateCardVisualStates();
            ShowCardInspector();
            RebuildActionButtons(currentGameView, true);
            if (cardObjects.TryGetValue(card.instanceId, out var visual))
                StartCoroutine(PulseSelectedCard(visual));
        }

        private void OnAvatarClicked(bool self)
        {
            if (pendingTargetAction == null || pendingTargetAction.target != "target_player" ||
                currentGameView == null || session.User == null) return;
            var targetId = self ? session.User.id :
                (currentGameView.players ?? Array.Empty<string>()).FirstOrDefault(p => p != session.User.id);
            if (string.IsNullOrWhiteSpace(targetId)) return;
            var action = pendingTargetAction;
            pendingTargetAction = null;
            UpdateCardVisualStates();
            ExecuteAction(action, selectedCard?.instanceId, null, targetId);
        }

        private void UpdateCardVisualStates()
        {
            foreach (var item in cardObjects)
            {
                if (item.Value == null) continue;
                var image = item.Value.GetComponent<Image>();
                if (image == null) continue;
                var isSelected = selectedCard != null && selectedCard.instanceId == item.Key;
                var ownCard = (currentGameView?.zones ?? Array.Empty<ClientGameZoneDto>()).Any(z =>
                    z.playerId == session.User?.id &&
                    (z.cards ?? Array.Empty<CardInstanceDto>()).Any(c => c.instanceId == item.Key));
                var selectable = ownCard && currentGameView?.activePlayerId == session.User?.id &&
                    !actionInFlight;
                var isBattlefield = (currentGameView?.zones ?? Array.Empty<ClientGameZoneDto>()).Any(z =>
                    z.zoneId == "battlefield" &&
                    (z.cards ?? Array.Empty<CardInstanceDto>()).Any(c => c.instanceId == item.Key));
                var targetable = pendingTargetAction != null &&
                    pendingTargetAction.target != "target_player" && isBattlefield;
                image.color = targetable ? Accent : isSelected ? Hex("3F716B") :
                    selectable ? Hex("283C59") : SurfaceRaised;
            }
            if (playerAvatar != null && opponentAvatar != null)
            {
                var playerTarget = pendingTargetAction?.target == "target_player";
                playerAvatar.GetComponent<Image>().color = playerTarget ? Hex("315F8C") : SurfaceRaised;
                opponentAvatar.GetComponent<Image>().color = playerTarget ? Hex("315F8C") : SurfaceRaised;
            }
        }

        private IEnumerator FlashAvatar(GameObject avatar, bool damage)
        {
            if (avatar == null) yield break;
            var image = avatar.GetComponent<Image>();
            if (image == null) yield break;
            var previous = image.color;
            image.color = damage ? Danger : AccentAlt;
            for (var i = 0; i < 12; ++i)
            {
                if (image == null) yield break;
                image.color = Color.Lerp(damage ? Danger : AccentAlt, previous, i / 11f);
                yield return null;
            }
            if (image != null) image.color = previous;
        }

        private void ShowCardInspector()
        {
            if (cardInspector == null || selectedCard == null ||
                (matchLogPanel != null && matchLogPanel.activeSelf)) return;
            var card = selectedCard;
            var definition = cardDefinitions.GetValueOrDefault(card.cardId);
            cardInspector.SetActive(true);
            foreach (Transform child in cardInspector.transform)
                Destroy(child.gameObject);

            var header = CreateText(cardInspector.transform, definition?.name ?? card.cardId, 17, Accent, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetLayout(header.gameObject, -1, 36);
            var image = Resources.Load<Texture2D>("AstralRift/Illustrations/" + card.cardId);
            if (image != null)
            {
                if (!DemoArtCache.TryGetValue(card.cardId, out var sprite))
                {
                    sprite = Sprite.Create(image, new Rect(0, 0, image.width, image.height), new Vector2(.5f, .5f));
                    DemoArtCache[card.cardId] = sprite;
                }
                var art = new GameObject("Artwork", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                art.transform.SetParent(cardInspector.transform, false);
                art.GetComponent<Image>().sprite = sprite;
                art.GetComponent<Image>().preserveAspect = true;
                SetLayout(art, -1, 112);
            }
            var stats = definition == null ? "" :
                (definition.type ?? "Card") + "  |  Cost " + definition.cost +
                "  |  " + definition.power + "/" + definition.toughness;
            var detail = CreateText(cardInspector.transform, stats, 12, Foreground, FontStyle.Bold, TextAnchor.UpperLeft);
            SetLayout(detail.gameObject, -1, 34);
            var rules = CreateText(cardInspector.transform,
                string.IsNullOrWhiteSpace(definition?.text) ? "Select an available action to play this card." : definition.text,
                12, Foreground, FontStyle.Normal, TextAnchor.UpperLeft);
            SetLayout(rules.gameObject, -1, 82);
            CreateButton(cardInspector.transform, "Close preview", () => { selectedCard = null; cardInspector.SetActive(false); UpdateCardVisualStates(); RebuildActionButtons(currentGameView, true); }, SurfaceRaised);
        }

        private IEnumerator AnimateCardArrival(GameObject visual)
        {
            if (visual == null) yield break;
            var rect = visual.transform as RectTransform;
            if (rect == null) yield break;
            var canvasGroup = visual.GetComponent<CanvasGroup>() ?? visual.AddComponent<CanvasGroup>();
            var originalScale = rect.localScale;
            for (var i = 0; i <= 12; i++)
            {
                if (rect == null || canvasGroup == null) yield break;
                var progress = i / 12f;
                rect.localScale = originalScale * Mathf.Lerp(.84f, 1f, progress);
                canvasGroup.alpha = Mathf.SmoothStep(0f, 1f, progress);
                yield return null;
            }
            if (rect != null) rect.localScale = originalScale;
            if (canvasGroup != null) canvasGroup.alpha = 1f;
        }

        private IEnumerator PulseSelectedCard(GameObject visual)
        {
            if (visual == null) yield break;
            var rect = visual.transform as RectTransform;
            if (rect == null) yield break;
            var startingScale = rect.localScale;
            for (var i = 0; i < 10; i++)
            {
                if (rect == null) yield break;
                rect.localScale = startingScale * (1f + Mathf.Sin(i / 9f * Mathf.PI) * .11f);
                yield return null;
            }
            if (rect != null) rect.localScale = startingScale;
        }

        private IEnumerator FlashEventBanner()
        {
            if (eventBanner == null) yield break;
            eventBanner.color = Accent;
            yield return new WaitForSeconds(.8f);
            if (eventBanner != null) eventBanner.color = Muted;
        }

        private void RebuildActionButtons(ClientGameViewDto game, bool force = false)
        {
            if (actionButtonRow == null || game == null) return;
            var actionSignature = game.id + ":" + game.finished + ":" + game.activePlayerId + ":" +
                selectedCard?.instanceId + ":" + (pendingTargetAction?.id ?? "") + ":" + actionInFlight + ":" +
                string.Join("|", (game.allowedActions ?? Array.Empty<ClientGameActionDto>())
                    .Select(action => action.id + ":" + action.sourceZone + ":" + action.target));
            if (!force && lastActionSignature == actionSignature) return;
            lastActionSignature = actionSignature;
            for (var index = actionButtonRow.transform.childCount - 1; index >= 0; index--)
                Destroy(actionButtonRow.transform.GetChild(index).gameObject);

            if (game.finished)
            {
                CreateButton(actionButtonRow.transform, game.winnerPlayerId == session.User?.id ? "Victory - return to lobby" : "Match ended - return to lobby",
                    () => ShowPage(lobbyPage), Accent);
                return;
            }
            if (actionInFlight)
            {
                CreateButton(actionButtonRow.transform, "Resolving action...", null, SurfaceRaised);
                return;
            }
            if (pendingTargetAction != null)
            {
                CreateButton(actionButtonRow.transform,
                    pendingTargetAction.target == "target_player" ? "Select a player avatar" : "Select a target card", null, AccentAlt);
                CreateButton(actionButtonRow.transform, "Cancel targeting", () =>
                {
                    pendingTargetAction = null;
                    UpdateCardVisualStates();
                    RebuildActionButtons(currentGameView, true);
                }, SurfaceRaised);
                return;
            }
            if (session.User == null || game.activePlayerId != session.User.id)
            {
                CreateButton(actionButtonRow.transform, "Opponent's turn", null, SurfaceRaised);
                return;
            }

            var actions = game.allowedActions ?? Array.Empty<ClientGameActionDto>();
            var selectedZone = selectedCard == null ? null :
                (game.zones ?? Array.Empty<ClientGameZoneDto>()).FirstOrDefault(z =>
                    z.playerId == session.User.id &&
                    (z.cards ?? Array.Empty<CardInstanceDto>()).Any(c => c.instanceId == selectedCard.instanceId))?.zoneId;

            var shown = 0;
            foreach (var action in actions)
            {
                if (!string.IsNullOrWhiteSpace(action.sourceZone) && action.sourceZone != selectedZone)
                    continue;
                if (shown++ >= 5) break;
                var label = string.IsNullOrWhiteSpace(action.name) ? action.id : action.name;
                var selection = selectedCard?.instanceId;
                CreateButton(actionButtonRow.transform, label, () =>
                {
                    if (action.target == "target_card" || action.target == "target_instance" || action.target == "target_player")
                    {
                        pendingTargetAction = action;
                        UpdateCardVisualStates();
                        actionStatus.text = action.target == "target_player" ?
                            "Choose your avatar or the opponent avatar as the target." :
                            "Choose a target card on the battlefield.";
                        RebuildActionButtons(currentGameView, true);
                    }
                    else ExecuteAction(action, string.IsNullOrWhiteSpace(action.sourceZone) ? null : selection);
                }, action.sourceZone == selectedZone && selectedZone != null ? Accent : AccentAlt);
            }
            // Phase progression must remain accessible even when a phase lists many actions.
            var advance = actions.FirstOrDefault(action =>
                string.IsNullOrWhiteSpace(action.sourceZone) &&
                (action.id.IndexOf("next-phase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 action.id.IndexOf("advance-phase", StringComparison.OrdinalIgnoreCase) >= 0));
            if (advance != null && !actions.Take(5).Contains(advance))
            {
                CreateButton(actionButtonRow.transform, "Advance phase", () => ExecuteAction(advance), Accent, Ink);
                shown++;
            }
            if (shown == 0)
                CreateButton(actionButtonRow.transform,
                    selectedCard == null ? "Select a card to see its actions" : "No actions available for this card",
                    null, SurfaceRaised);
            if (selectedCard != null)
                CreateButton(actionButtonRow.transform, "Deselect", () =>
                {
                    selectedCard = null;
                    if (cardInspector != null) cardInspector.SetActive(false);
                    UpdateCardVisualStates();
                    RebuildActionButtons(currentGameView, true);
                }, SurfaceRaised);
        }

        private void ExecuteAction(ClientGameActionDto action, string selectedInstanceId = null, string targetInstanceId = null, string explicitTargetPlayerId = null)
        {
            if (!RequireSession() || currentGameView == null || actionInFlight) return;
            if (!string.IsNullOrWhiteSpace(action.sourceZone) && string.IsNullOrWhiteSpace(selectedInstanceId))
            {
                actionStatus.text = "Select a card in " + action.sourceZone + " first.";
                return;
            }

            var targetPlayerId = action.target == "target_player" ? explicitTargetPlayerId : null;
            if (action.target == "target_player" && string.IsNullOrWhiteSpace(targetPlayerId))
            {
                actionStatus.text = "Choose a player avatar to target.";
                return;
            }
            var matchId = currentGameId;
            actionInFlight = true;
            actionStatus.text = "Resolving " + action.name + "...";
            RebuildActionButtons(currentGameView, true);
            StartCoroutine(rest.ExecuteAction(
                session.Token, matchId,
                new ExecuteGameActionRequestDto
                {
                    actionId = action.id, sourceInstanceId = selectedInstanceId,
                    targetPlayerId = targetPlayerId, targetInstanceId = targetInstanceId
                },
                _ =>
                {
                    if (matchId != currentGameId) return;
                    actionInFlight = false;
                    selectedCard = null;
                    if (cardInspector != null) cardInspector.SetActive(false);
                    actionStatus.text = action.name + " resolved.";
                    StartCoroutine(rest.GameView(session.Token, matchId, RenderGame,
                        error => actionStatus.text = error.ToString()));
                    RebuildActionButtons(currentGameView, true);
                },
                error =>
                {
                    if (matchId != currentGameId) return;
                    actionInFlight = false;
                    actionStatus.text = error.ToString();
                    RebuildActionButtons(currentGameView, true);
                }));
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
            gameConnection.GameStateChanged += game =>
            {
                if (game == null || string.IsNullOrWhiteSpace(game.id) || !session.IsAuthenticated) return;
                StartCoroutine(rest.GameView(session.Token, game.id, RenderGame, error => realtimeSummary.text = error.ToString()));
            };
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
            CreatePanel(canvas.transform, "Header Accent", Accent, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -78), new Vector2(0, -76));
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

            var brand = CreateText(header.transform, "CARDFORGE\nARENA-STYLE LIVE DEMO", 22, Foreground, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetLayout(brand.gameObject, 360, 50);
            serviceStatus = CreateText(header.transform, "CONNECTING", 13, Muted, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetLayout(serviceStatus.gameObject, 280, 50, 1);
            operatorStatus = CreateText(header.transform, "NOT SIGNED IN", 12, Muted, FontStyle.Bold, TextAnchor.MiddleRight);
            SetLayout(operatorStatus.gameObject, 260, 50);
        }

        private void BuildLoginPage(Transform page)
        {
            var hero = CreatePanel(page, "Hero", Surface, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            SetLayout(hero, -1, 130);
            var heroLayout = hero.AddComponent<VerticalLayoutGroup>();
            heroLayout.padding = new RectOffset(28, 28, 16, 10);
            heroLayout.spacing = 4;
            heroLayout.childControlHeight = false;
            heroLayout.childForceExpandHeight = false;
            heroLayout.childControlWidth = true;
            heroLayout.childForceExpandWidth = true;
            CreateText(hero.transform, "CARD FORGE", 44, Accent, FontStyle.Bold, TextAnchor.MiddleLeft);
            CreateText(hero.transform, "ARENA-INSPIRED TCG PLATFORM — AUTHOR IN UNITY, PLAY ON THE AUTHORITATIVE SERVER", 14, Muted, FontStyle.Bold, TextAnchor.MiddleLeft);
            CreateGemRow(hero.transform);

            var columns = CreateHorizontalArea(page, 470);

            var loginCard = CreateCard(columns.transform, 470, 0.85f);
            CreateSectionLabel(loginCard.transform, "PLAYER SIGN-IN");
            handleInput = CreateInput(loginCard.transform, "Handle (3-24 characters)", false);
            passwordInput = CreateInput(loginCard.transform, "Password (8+ characters)", true);
            var buttons = CreateButtonRow(loginCard.transform);
            CreateButton(buttons.transform, "Quick demo sign-in", QuickDemoLogin, Accent, Ink);
            CreateButton(buttons.transform, "Sign in", () => Authenticate(false), AccentAlt);
            CreateButton(buttons.transform, "Register", () => Authenticate(true), SurfaceRaised);
            var secondary = CreateButtonRow(loginCard.transform);
            CreateButton(secondary.transform, "Retry server", CheckHealth, SurfaceRaised);
            loginStatus = CreateText(
                loginCard.transform,
                "Quick demo sign-in uses the \"demo\" account and registers it on first run.\nServer: " + (settings == null ? "not configured" : settings.BaseUrl),
                13, Muted, FontStyle.Normal, TextAnchor.UpperLeft);
            SetLayout(loginStatus.gameObject, -1, 80);

            var tourCard = CreateCard(columns.transform, 470, 1.15f);
            CreateSectionLabel(tourCard.transform, "WHAT THIS DEMO SHOWS");
            var tour = CreateText(
                tourCard.transform,
                "LOBBY\n" +
                "• Live player presence, enabled matchmaking queues, and the ranked leaderboard — all refreshed automatically.\n\n" +
                "PLAY\n" +
                "• Create a match against the server AI and play the Astral Rift package turn by turn, or join ranked/casual matchmaking.\n\n" +
                "COLLECTION & DECKS\n" +
                "• Server-backed inventory, achievements, and saved deck records.\n\n" +
                "SOCIAL\n" +
                "• Friends and direct messages between player accounts.\n\n" +
                "GAME BROWSER & REALTIME\n" +
                "• Inspect active and recorded games by ID; SignalR streams live game state, chat, and announcements.",
                13, Foreground, FontStyle.Normal, TextAnchor.UpperLeft);
            SetLayout(tour.gameObject, -1, 340);
        }

        private void BuildLobbyPage(Transform page)
        {
            CreateTitle(page, "Arena lobby", "Live service data, matchmaking queues, rankings — everything refreshes automatically. Hit Play vs AI for an instant Astral Rift match.");
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
            CreateButton(identityButtons.transform, "Play vs AI (" + (settings == null ? "rules" : settings.DefaultRulePackId) + ")", CreateAiGame, Accent, Ink);
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
            CreateTitle(page, "ASTRAL RIFT  /  LIVE MATCH", "Deploy cards, control the field, and defeat the opposing player.");
            SetLayout(page.GetChild(0).gameObject, -1, 64);
            var controls = CreateButtonRow(page);
            gameIdInput = CreateInput(controls.transform, "Game ID", false, 360);
            CreateButton(controls.transform, "Watch game", WatchGame, Accent);
            CreateButton(controls.transform, "Back to lobby", () => ShowPage(lobbyPage), SurfaceRaised);
            if (TcgGameConnection.IsAvailable)
                CreateButton(controls.transform, "Connect live updates", ConnectRealtime, SurfaceRaised);
            CreateButton(controls.transform, "Match log (L)", ToggleMatchLog, SurfaceRaised);

            var battlefield = CreatePanel(page, "Battlefield", Background, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            SetLayout(battlefield, -1, 540);
            var battleLayout = battlefield.AddComponent<VerticalLayoutGroup>();
            battleLayout.padding = new RectOffset(22, 22, 12, 12);
            battleLayout.spacing = 8;
            battleLayout.childControlHeight = true;
            battleLayout.childForceExpandHeight = true;
            battleLayout.childControlWidth = true;
            battleLayout.childForceExpandWidth = true;
            AddBattlefieldBackdrop(battlefield.transform);

            var topRow = CreateHorizontalArea(battlefield.transform, 145);
            opponentAvatar = CreateAvatar(topRow.transform, "OPPONENT");
            opponentFieldContainer = CreateZone(topRow.transform, "OPPONENT BATTLEFIELD", 1f);
            opponentHandContainer = CreateZone(topRow.transform, "HAND (HIDDEN)", 0.36f);

            var centerRow = CreatePanel(battlefield.transform, "Turn Banner", SurfaceRaised, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            SetLayout(centerRow, -1, 36);
            var centerLayout = centerRow.AddComponent<HorizontalLayoutGroup>();
            centerLayout.padding = new RectOffset(20, 20, 4, 4);
            centerLayout.spacing = 12;
            centerLayout.childAlignment = TextAnchor.MiddleCenter;
            turnPhaseText = CreateText(centerRow.transform, "WAITING FOR GAME STATE...", 14, Accent, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetLayout(turnPhaseText.gameObject, -1, 28, 1);

            var bottomRow = CreateHorizontalArea(battlefield.transform, 145);
            playerAvatar = CreateAvatar(bottomRow.transform, "YOU");
            playerFieldContainer = CreateZone(bottomRow.transform, "YOUR BATTLEFIELD", 1f);
            playerHandContainer = CreateZone(bottomRow.transform, "YOUR HAND  /  CLICK TO PLAY", 0.58f);

            actionButtonRow = CreateButtonRow(battlefield.transform);
            realtimeSummary = CreateText(battlefield.transform, TcgGameConnection.IsAvailable ? "SignalR package detected." : "REST polling ready · SignalR optional package not enabled.", 12, Muted, FontStyle.Normal, TextAnchor.UpperLeft);
            SetLayout(realtimeSummary.gameObject, -1, 28);
            // One visible notification surface for transport errors, invalid plays and action results.
            gameStatus = realtimeSummary;
            actionStatus = realtimeSummary;
            eventBanner = CreateText(battlefield.transform, "Select a card to inspect it. Actions are validated by the server.", 12, Muted, FontStyle.Italic, TextAnchor.MiddleCenter);
            SetLayout(eventBanner.gameObject, -1, 24);
            if (TcgGameConnection.IsAvailable)
            {
                var chat = CreateButtonRow(battlefield.transform);
                chatInput = CreateInput(chat.transform, "Game chat", false, 420);
                CreateButton(chat.transform, "Send chat", SendChat, Accent);
            }

            // Floating inspector: does not consume battlefield layout space.
            cardInspector = CreatePanel(battlefield.transform, "Card Inspector", SurfaceRaised, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            var inspectorLayout = cardInspector.AddComponent<LayoutElement>();
            inspectorLayout.ignoreLayout = true;
            var inspectorRect = cardInspector.GetComponent<RectTransform>();
            inspectorRect.anchorMin = new Vector2(1f, .5f);
            inspectorRect.anchorMax = new Vector2(1f, .5f);
            inspectorRect.pivot = new Vector2(1f, .5f);
            inspectorRect.anchoredPosition = new Vector2(-18f, 0f);
            inspectorRect.sizeDelta = new Vector2(282f, 358f);
            var detailsLayout = cardInspector.AddComponent<VerticalLayoutGroup>();
            detailsLayout.padding = new RectOffset(12, 12, 12, 12);
            detailsLayout.spacing = 5;
            detailsLayout.childControlHeight = false;
            detailsLayout.childForceExpandHeight = false;
            detailsLayout.childControlWidth = true;
            detailsLayout.childForceExpandWidth = true;
            cardInspector.SetActive(false);

            resultPanel = CreatePanel(battlefield.transform, "Match Result", Background, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            resultPanel.AddComponent<LayoutElement>().ignoreLayout = true;
            var resultRect = resultPanel.GetComponent<RectTransform>();
            resultRect.anchorMin = new Vector2(.5f, .5f);
            resultRect.anchorMax = new Vector2(.5f, .5f);
            resultRect.pivot = new Vector2(.5f, .5f);
            resultRect.anchoredPosition = Vector2.zero;
            resultRect.sizeDelta = new Vector2(390f, 230f);
            var resultLayout = resultPanel.AddComponent<VerticalLayoutGroup>();
            resultLayout.padding = new RectOffset(22, 22, 20, 20);
            resultLayout.spacing = 9;
            resultLayout.childAlignment = TextAnchor.MiddleCenter;
            resultLayout.childControlHeight = false;
            resultLayout.childForceExpandHeight = false;
            resultLayout.childControlWidth = true;
            resultLayout.childForceExpandWidth = true;
            resultTitle = CreateText(resultPanel.transform, "VICTORY", 34, Accent, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetLayout(resultTitle.gameObject, -1, 55);
            var resultNote = CreateText(resultPanel.transform, "The server has finalized this match.", 13, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            SetLayout(resultNote.gameObject, -1, 27);
            CreateButton(resultPanel.transform, "Play another match", CreateAiGame, Accent, Ink);
            CreateButton(resultPanel.transform, "Return to lobby", () => ShowPage(lobbyPage), SurfaceRaised);
            resultPanel.SetActive(false);

            matchLogPanel = CreatePanel(battlefield.transform, "Match Event Log", SurfaceRaised,
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            matchLogPanel.AddComponent<LayoutElement>().ignoreLayout = true;
            var logRect = matchLogPanel.GetComponent<RectTransform>();
            logRect.anchorMin = new Vector2(1f, .5f);
            logRect.anchorMax = new Vector2(1f, .5f);
            logRect.pivot = new Vector2(1f, .5f);
            logRect.anchoredPosition = new Vector2(-18f, 0f);
            logRect.sizeDelta = new Vector2(345f, 400f);
            var logLayout = matchLogPanel.AddComponent<VerticalLayoutGroup>();
            logLayout.padding = new RectOffset(12, 12, 12, 12);
            logLayout.spacing = 8;
            logLayout.childControlHeight = false;
            logLayout.childForceExpandHeight = false;
            logLayout.childControlWidth = true;
            logLayout.childForceExpandWidth = true;
            var logHeading = CreateText(matchLogPanel.transform, "MATCH HISTORY", 16, Accent,
                FontStyle.Bold, TextAnchor.MiddleLeft);
            SetLayout(logHeading.gameObject, -1, 32);
            matchLogText = CreateText(matchLogPanel.transform, "Waiting for the first move...", 11,
                Foreground, FontStyle.Normal, TextAnchor.UpperLeft);
            SetLayout(matchLogText.gameObject, -1, 285);
            CreateButton(matchLogPanel.transform, "Close log (L)", ToggleMatchLog, SurfaceRaised);
            matchLogPanel.SetActive(false);
        }

        private static void AddBattlefieldBackdrop(Transform parent)
        {
            var backdrop = new GameObject("Astral Rift Board", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            backdrop.transform.SetParent(parent, false);
            backdrop.GetComponent<LayoutElement>().ignoreLayout = true;
            var rect = backdrop.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = backdrop.GetComponent<Image>();
            image.sprite = BattlefieldSprite;
            image.raycastTarget = false;
        }

        private static Sprite BattlefieldSprite
        {
            get
            {
                if (battlefieldSprite != null) return battlefieldSprite;
                const int width = 512;
                const int height = 512;
                var pixels = new Color32[width * height];
                var dark = Hex("081322");
                var nebula = Hex("203B55");
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var dx = (x - width * .5f) / width;
                        var dy = (y - height * .5f) / height;
                        var haze = Mathf.Exp(-(dx * dx * 5f + dy * dy * 16f)) * .56f;
                        pixels[y * width + x] = Color.Lerp(dark, nebula, haze);
                    }
                }
                var random = new System.Random(14817);
                for (var i = 0; i < 230; i++)
                {
                    var x = random.Next(width);
                    var y = random.Next(height);
                    var intensity = (byte)random.Next(70, 170);
                    pixels[y * width + x] = new Color32(intensity, (byte)Mathf.Min(255, intensity + 13),
                        (byte)Mathf.Min(255, intensity + 33), 255);
                }
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                texture.Apply(false, true);
                battlefieldSprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f));
                return battlefieldSprite;
            }
        }

        private GameObject CreateAvatar(Transform parent, string label)
        {
            var card = CreatePanel(parent, "Avatar", SurfaceRaised, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            SetLayout(card, 100, -1);
            var layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 4;
            layout.childAlignment = TextAnchor.MiddleCenter;
            CreateText(card.transform, label, 12, Muted, FontStyle.Bold, TextAnchor.MiddleCenter);
            CreateText(card.transform, "HULL", 10, Muted, FontStyle.Bold, TextAnchor.MiddleCenter);
            var life = CreateText(card.transform, "15", 26, Accent, FontStyle.Bold, TextAnchor.MiddleCenter);
            var energyText = CreateText(card.transform, "ENERGY  3", 11, AccentAlt, FontStyle.Bold, TextAnchor.MiddleCenter);
            var renownText = CreateText(card.transform, "RENOWN  0", 11, Foreground, FontStyle.Bold, TextAnchor.MiddleCenter);
            if (label == "YOU") { playerLifeText = life; playerEnergyText = energyText; playerRenownText = renownText; }
            else { opponentLifeText = life; opponentEnergyText = energyText; opponentRenownText = renownText; }
            var avatarButton = card.AddComponent<Button>();
            avatarButton.targetGraphic = card.GetComponent<Image>();
            avatarButton.onClick.AddListener(() => OnAvatarClicked(label == "YOU"));
            return card;
        }

        private GameObject CreateZone(Transform parent, string label, float flexibleWidth)
        {
            var zone = CreatePanel(parent, label, Surface, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            SetLayout(zone, -1, -1, flexibleWidth);
            var layout = zone.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 4, 4);
            layout.spacing = 4;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            CreateText(zone.transform, label, 10, Muted, FontStyle.Bold, TextAnchor.UpperLeft);
            var content = new GameObject("Cards", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            content.transform.SetParent(zone.transform, false);
            SetLayout(content, -1, -1, 1);
            var contentLayout = content.GetComponent<HorizontalLayoutGroup>();
            contentLayout.spacing = 6;
            contentLayout.childControlWidth = false;
            contentLayout.childForceExpandWidth = false;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandHeight = true;
            contentLayout.childAlignment = TextAnchor.MiddleLeft;
            if (label.IndexOf("HAND", StringComparison.OrdinalIgnoreCase) >= 0)
                content.AddComponent<TcgFanLayout>();
            return content;
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

        private Button CreateButton(Transform parent, string label, Action clicked, Color color, Color? textColor = null)
        {
            var buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            SetLayout(buttonObject, Mathf.Max(112, label.Length * 9 + 28), 42);
            buttonObject.GetComponent<Image>().color = color;
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.interactable = clicked != null;
            if (clicked != null) button.onClick.AddListener(() => clicked());
            var text = CreateText(buttonObject.transform, label, 13, textColor ?? Foreground, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 10);
            return button;
        }

        private void CreateGemRow(Transform parent)
        {
            var row = new GameObject("Mana Gems", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            SetLayout(row, -1, 22);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            foreach (var color in GemColors)
            {
                var gem = new GameObject("Gem", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                gem.transform.SetParent(row.transform, false);
                gem.GetComponent<Image>().sprite = CircleSprite;
                gem.GetComponent<Image>().color = color;
                SetLayout(gem, 18, 18);
            }
        }

        private static Sprite CircleSprite
        {
            get
            {
                if (circleSprite != null)
                    return circleSprite;

                const int size = 64;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color32[size * size];
                var center = (size - 1) / 2f;
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / center;
                        var alpha = Mathf.Clamp01((1f - distance) * size * 0.45f);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply();
                circleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                return circleSprite;
            }
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

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            // This project uses the new Input System only; StandaloneInputModule reads the legacy Input class and throws.
            new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
#else
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
        }

        private static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out var color);
            return color;
        }
    }
}
