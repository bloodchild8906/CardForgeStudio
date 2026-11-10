using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace TcgULtimateServer.App;

public sealed class MainWindow : Window
{
    private readonly string managementKey;
    private readonly HttpClient http = new()
    {
        BaseAddress = new Uri("https://localhost:5001"),
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly TextBlock serverState = new() { Text = "STARTING", FontSize = 10.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = 65 };
    private readonly TextBlock serverDetail = new() { Text = "Starting the local API...", FontSize = 13.5, Foreground = Muted };
    private readonly TextBlock operatorDetail = new() { Text = "CONNECTING", FontSize = 10.5, Foreground = Muted, CharacterSpacing = 85 };
    private readonly TextBlock livePlayerCount = new() { Text = "0 PLAYERS ONLINE", FontSize = 10.5, Foreground = Muted, CharacterSpacing = 70 };
    private readonly TextBlock notification = new() { FontSize = 13.5, Foreground = Muted, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ProgressRing serverProgress = new() { IsActive = true, Width = 18, Height = 18 };
    private readonly ContentControl content = new();
    private readonly NavigationView navigation = new()
    {
        IsPaneOpen = true,
        IsPaneToggleButtonVisible = true,
        IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
        IsSettingsVisible = false,
        OpenPaneLength = 286,
        CompactPaneLength = 58,
        PaneTitle = "OPERATIONS"
    };
    private readonly DispatcherTimer liveRefreshTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private Func<Task>? activeLiveRefresh;

    private readonly List<AdminUserRow> adminUsers = [];
    private readonly DateTimeOffset dashboardStartedAt = DateTimeOffset.Now;
    private bool isServerReady;
    private int onlinePlayerTotal;
    private string? token;
    private string? userId;
    private string? role;
    private string? signedInHandle;

    public MainWindow(string managementKey)
    {
        this.managementKey = managementKey;
        Title = "TcgULtimateServer";
        serverState.FontFamily = DisplayFont;
        serverDetail.FontFamily = AppFont;
        operatorDetail.FontFamily = DisplayFont;
        livePlayerCount.FontFamily = DisplayFont;
        notification.FontFamily = AppFont;
        navigation.FontFamily = AppFont;
        liveRefreshTimer.Tick += async (_, _) =>
        {
            await RefreshLivePresenceAsync();
            if (activeLiveRefresh is not null) await activeLiveRefresh();
        };
        Closed += (_, _) => liveRefreshTimer.Stop();
        var root = new Grid { Background = Background, RequestedTheme = ElementTheme.Dark };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(BuildTopBar());

        navigation.SelectionChanged += Navigation_SelectionChanged;
        var contentHost = new Grid { Background = Background };
        contentHost.Children.Add(content);
        navigation.Content = contentHost;
        Grid.SetRow(navigation, 1);
        root.Children.Add(navigation);
        Content = root;

        BuildNavigation();
        ShowOverview();
    }

    internal void SetServerStarting()
    {
        isServerReady = false;
        serverState.Text = "STARTING";
        serverState.Foreground = Warning;
        serverDetail.Text = "Starting the local API and checking HTTPS...";
        serverProgress.IsActive = true;
        serverProgress.Visibility = Visibility.Visible;
        operatorDetail.Text = "CONNECTING";
    }

    internal void SetServerReady(bool startedLocally)
    {
        ApplyServerReadyState(startedLocally
            ? "Local service ready at https://localhost:5001"
            : "Connected to the service at https://localhost:5001");
        notification.Text = "Opening the local management session...";
        _ = ConnectManagementSessionAsync();
    }

    private void ApplyServerReadyState(string detail)
    {
        isServerReady = true;
        serverState.Text = "ONLINE";
        serverState.Foreground = Accent;
        serverDetail.Text = detail;
        serverProgress.IsActive = false;
        serverProgress.Visibility = Visibility.Collapsed;
    }

    internal void SetServerError(string message)
    {
        isServerReady = false;
        serverState.Text = "OFFLINE";
        serverState.Foreground = Danger;
        serverDetail.Text = "The local service could not be started.";
        serverProgress.IsActive = false;
        serverProgress.Visibility = Visibility.Collapsed;
        notification.Text = message;
        notification.Foreground = Danger;
        operatorDetail.Text = "UNAVAILABLE";
    }

    private UIElement BuildTopBar()
    {
        var bar = new Grid
        {
            Padding = new Thickness(22, 11, 22, 11),
            Background = Surface,
            BorderBrush = Divider,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        var logoFrame = new Border
        {
            Width = 48,
            Height = 48,
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(13),
            Background = SurfaceRaised,
            BorderBrush = Accent,
            BorderThickness = new Thickness(1)
        };
        logoFrame.Child = new Image
        {
            Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "BrandMark-256.png"))),
            Width = 36,
            Height = 36,
            Stretch = Stretch.Uniform
        };
        identity.Children.Add(logoFrame);
        var identityCopy = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        identityCopy.Children.Add(new TextBlock { Text = "TCG ULTIMATE SERVER", FontSize = 10.5, FontFamily = DisplayFont, CharacterSpacing = 120, Foreground = Accent, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        identityCopy.Children.Add(new TextBlock { Text = "Live Operations Console", FontSize = 19, FontFamily = DisplayFont, Foreground = Foreground, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = -8 });
        identity.Children.Add(identityCopy);
        bar.Children.Add(identity);

        notification.HorizontalAlignment = HorizontalAlignment.Center;
        notification.VerticalAlignment = VerticalAlignment.Center;
        notification.Margin = new Thickness(24, 0, 24, 0);
        Grid.SetColumn(notification, 1);
        bar.Children.Add(notification);

        var health = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        health.Children.Add(serverProgress);
        var healthCopy = new StackPanel { Spacing = 1 };
        healthCopy.Children.Add(serverState);
        healthCopy.Children.Add(serverDetail);
        health.Children.Add(healthCopy);
        var liveCopy = new StackPanel { Spacing = 2, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        liveCopy.Children.Add(livePlayerCount);
        liveCopy.Children.Add(new TextBlock { Text = "LIVE PLAYER PRESENCE", FontSize = 10.5, FontFamily = AppFont, Foreground = Foreground, CharacterSpacing = 20 });
        health.Children.Add(liveCopy);
        var operatorCopy = new StackPanel { Spacing = 2, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        operatorCopy.Children.Add(operatorDetail);
        operatorCopy.Children.Add(new TextBlock { Text = "SECURE DESKTOP SESSION", FontSize = 10.5, FontFamily = AppFont, Foreground = Foreground, CharacterSpacing = 20 });
        health.Children.Add(operatorCopy);
        var healthFrame = new Border
        {
            Background = SurfaceRaised,
            BorderBrush = Divider,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 9, 14, 9),
            Child = health
        };
        Grid.SetColumn(healthFrame, 2);
        bar.Children.Add(healthFrame);
        return bar;
    }

    private void BuildNavigation()
    {
        navigation.MenuItems.Clear();
        navigation.FooterMenuItems.Clear();

        navigation.MenuItems.Add(NavHeader("MONITOR"));
        navigation.MenuItems.Add(NavItem("Overview", "overview", Symbol.View));
        navigation.MenuItems.Add(NavItem("Management map", "management", Symbol.Document));
        navigation.MenuItems.Add(NavItem("Service health", "operations", Symbol.Setting));
        navigation.MenuItems.Add(NavItem("Rankings", "rankings", Symbol.Favorite));
        navigation.MenuItems.Add(NavItem("API explorer", "explorer", Symbol.Document));

        navigation.MenuItems.Add(NavHeader("CONTENT & RUNTIME"));
        navigation.MenuItems.Add(NavItem("Content catalog", "catalog", Symbol.Library));
        navigation.MenuItems.Add(NavItem("Games", "games", Symbol.Play, token is not null));
        navigation.MenuItems.Add(NavItem("Game packages", "game_packages", Symbol.Save, IsAdmin));
        navigation.MenuItems.Add(NavItem("Content packs", "content_packs", Symbol.Folder, IsAdmin));
        navigation.MenuItems.Add(NavItem("Visual content studio", "rules", Symbol.Setting, IsAdmin));
        navigation.MenuItems.Add(NavItem("Workflow runtime", "workflow_runtime", Symbol.Sync, IsAdmin));
        navigation.MenuItems.Add(NavItem("Upload center", "uploads", Symbol.Add, IsAdmin));

        navigation.MenuItems.Add(NavHeader("PLAYER OPERATIONS"));
        navigation.MenuItems.Add(NavItem("Accounts & access", "accounts", Symbol.People, CanModerate));
        navigation.MenuItems.Add(NavItem("Player intelligence", "players", Symbol.Contact, IsAdmin));
        navigation.MenuItems.Add(NavItem("Live chat center", "chat", Symbol.Message, CanModerate));

        navigation.MenuItems.Add(NavHeader("LIVE OPERATIONS"));
        navigation.MenuItems.Add(NavItem("Matchmaking control", "matchmaking", Symbol.People, IsAdmin));
        navigation.MenuItems.Add(NavItem("AI bot manager", "bots", Symbol.Play, IsAdmin));
        navigation.MenuItems.Add(NavItem("Events, seasons & rewards", "liveops", Symbol.Calendar, IsAdmin));
        navigation.MenuItems.Add(NavItem("Store & economy", "commerce", Symbol.Shop, IsAdmin));
        navigation.MenuItems.Add(NavItem("Early warning", "warnings", Symbol.Important, IsAdmin));

        navigation.MenuItems.Add(NavHeader("GOVERNANCE"));
        navigation.MenuItems.Add(NavItem("Audit logs", "logs", Symbol.Document, IsAdmin));
        navigation.MenuItems.Add(NavItem("Reports & archives", "reports", Symbol.Save, IsAdmin));

    }

    private static NavigationViewItemHeader NavHeader(string label) => new()
    {
        Content = new TextBlock
        {
            Text = label,
            FontFamily = DisplayFont,
            FontSize = 10.5,
            Foreground = Muted,
            CharacterSpacing = 110,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        }
    };

    private static NavigationViewItem NavItem(string label, string tag, Symbol symbol, bool enabled = true) => new()
    {
        Content = label,
        Tag = tag,
        Icon = new SymbolIcon(symbol),
        IsEnabled = enabled,
        Opacity = enabled ? 1 : 0.55
    };

    private async void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item)
            return;
        activeLiveRefresh = null;

        switch (item.Tag?.ToString())
        {
            case "management":
                ShowManagementMap();
                break;
            case "operations":
                await ShowOperationsAsync();
                break;
            case "catalog":
                await ShowCatalogAsync();
                break;
            case "rankings":
                await ShowRankingsAsync();
                break;
            case "explorer":
                await ShowApiExplorerAsync();
                break;
            case "games":
                await ShowGamesAsync();
                break;
            case "rules":
                await ShowRuleEngineAsync();
                break;
            case "content_packs":
                await ShowContentPacksAsync();
                break;
            case "game_packages":
                await ShowGamePackagesAsync();
                break;
            case "uploads":
                await ShowUploadCenterAsync();
                break;
            case "bots":
                await ShowBotsAsync();
                break;
            case "matchmaking":
                await ShowMatchmakingAsync();
                break;
            case "liveops":
                await ShowLiveOpsAsync();
                break;
            case "commerce":
                await ShowCommerceAsync();
                break;
            case "warnings":
                await ShowEarlyWarningsAsync();
                break;
            case "workflow_runtime":
                await ShowWorkflowRuntimeAsync();
                break;
            case "accounts":
                await ShowAccountsAsync();
                break;
            case "players":
                await ShowPlayerOperationsAsync();
                break;
            case "chat":
                await ShowChatCenterAsync();
                break;
            case "logs":
                await ShowLogsAsync();
                break;
            case "reports":
                await ShowReportsAsync();
                break;
            default:
                ShowOverview();
                break;
        }
    }

    private void ShowOverview()
    {
        content.Content = null;
        var page = PagePanel();
        page.Children.Add(BuildDashboardBanner(
            token is null ? "Starting operations command center" : "Operations command center",
            token is null ? "Connecting the native console to the local server." : "Every live service, player, content, AI, commerce, moderation, and reporting workspace in one organized dashboard."));

        if (token is null)
        {
            page.Children.Add(BuildSignedOutOverview());
        }
        else
        {
            page.Children.Add(BuildSessionOverview());
            if (IsAdmin)
                _ = LoadSummaryAsync(page);
            page.Children.Add(PageHeader("All management workspaces", "Every management feature remains visible here, grouped by operational responsibility and delivery status."));
            page.Children.Add(BuildManagementDirectory());
        }

        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private void ShowManagementMap()
    {
        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Management map", "Every management capability is visible here, including the page that owns it, its access level, and roadmap modules that are not yet active."));

        var access = Card(accented: true);
        var accessCopy = new StackPanel { Spacing = 8 };
        accessCopy.Children.Add(Eyebrow("ACCESS & DELIVERY STATUS"));
        accessCopy.Children.Add(Label(token is null ? "Connecting to local management" : $"Current access: {role?.ToUpperInvariant()}", 20, Foreground, true));
        accessCopy.Children.Add(Body("READY features are implemented in the native console. Disabled navigation items require the indicated role. ROADMAP entries are displayed for complete product visibility and are not represented as live controls."));
        access.Child = accessCopy;
        page.Children.Add(access);
        page.Children.Add(BuildManagementDirectory());

        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private static UIElement BuildManagementDirectory()
    {
        var grid = new Grid { ColumnSpacing = 14, RowSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < 4; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var cards = new[]
        {
            FeatureDirectoryCard("GAME & CONTENT", "Runtime and content control", new[]
            {
                ("Game sessions and termination", "READY · ADMIN"),
                ("Rule packs, cards, and sets", "READY · ADMIN"),
                ("Content packs and assets", "READY · ADMIN"),
                ("Deck review and suspension", "READY · ADMIN"),
                ("Game workflows and configuration", "READY · ADMIN"),
                ("Visual content studio", "READY · ADMIN")
            }, Symbol.Library),
            FeatureDirectoryCard("PLAYER OPERATIONS", "Accounts and player intelligence", new[]
            {
                ("Live player presence", "READY - ADMIN"),
                ("Unified chat monitoring", "READY - MODERATOR"),
                ("Server announcements", "READY - ADMIN"),
                ("Users, roles, and permissions", "READY · MODERATOR"),
                ("Collections and achievements", "READY · ADMIN"),
                ("Currency wallets and balances", "READY · ADMIN"),
                ("Social graph and friends", "READY · ADMIN"),
                ("Message moderation", "READY · ADMIN"),
                ("Match history and recordings", "READY · ADMIN"),
                ("Rankings and deck statistics", "READY · ADMIN"),
                ("Investigations and appeals", "ROADMAP")
            }, Symbol.People),
            FeatureDirectoryCard("AI MANAGEMENT", "Bot fleet and simulation", new[]
            {
                ("Bot profiles and personalities", "READY · ADMIN"),
                ("Difficulty configuration", "READY · ADMIN"),
                ("Model version assignment", "READY · ADMIN"),
                ("Simulation laboratory", "READY · ADMIN"),
                ("Model rollback and inference budgets", "ROADMAP")
            }, Symbol.Play),
            FeatureDirectoryCard("LIVE OPERATIONS", "Campaign and progression controls", new[]
            {
                ("Matchmaking queues and rating bands", "READY · ADMIN"),
                ("Queue monitoring, bot backfill, and flush", "READY · ADMIN"),
                ("Events and tournaments", "READY · ADMIN"),
                ("Seasons and quests", "READY · ADMIN"),
                ("Rewards and distribution limits", "READY · ADMIN"),
                ("Achievements and points", "READY · ADMIN"),
                ("Scheduled campaign windows", "READY · ADMIN")
            }, Symbol.Calendar),
            FeatureDirectoryCard("COMMERCE & ECONOMY", "Store and currency guardrails", new[]
            {
                ("In-game products and bundles", "READY · ADMIN"),
                ("Multi-product bulk catalog controls", "READY · ADMIN"),
                ("Soft and premium currencies", "READY · ADMIN"),
                ("Daily caps and store tax", "READY · ADMIN"),
                ("Player wallet adjustments and transaction ledger", "READY · ADMIN"),
                ("Economy analytics and simulator", "ROADMAP")
            }, Symbol.Shop),
            FeatureDirectoryCard("MODERATION & SECURITY", "Safety and early warning", new[]
            {
                ("Account moderation", "READY · MODERATOR"),
                ("Message and deck enforcement", "READY · ADMIN"),
                ("AI early-warning signals", "READY · ADMIN"),
                ("Security and request audit trail", "READY · ADMIN"),
                ("Reports and appeal workflow", "ROADMAP")
            }, Symbol.Important),
            FeatureDirectoryCard("REPORTING & ARCHIVES", "Exports and historical records", new[]
            {
                ("Player, game, content, and audit reports", "READY · ADMIN"),
                ("JSON and CSV exports", "READY · ADMIN"),
                ("Archive manager and explorer", "READY · ADMIN"),
                ("SHA-256 integrity verification", "READY · ADMIN"),
                ("Scheduled reports and retention", "ROADMAP"),
                ("Backup restore", "ROADMAP")
            }, Symbol.Save),
            FeatureDirectoryCard("SYSTEM", "Platform administration", new[]
            {
                ("Service lifecycle and health", "READY"),
                ("API explorer", "READY"),
                ("Audit logs", "READY · ADMIN"),
                ("Users and permissions", "READY · MODERATOR"),
                ("Plugin management", "ROADMAP"),
                ("System settings", "ROADMAP")
            }, Symbol.Setting)
        };

        for (var index = 0; index < cards.Length; index++)
        {
            Grid.SetColumn(cards[index], index % 2);
            Grid.SetRow(cards[index], index / 2);
            grid.Children.Add(cards[index]);
        }
        return grid;
    }

    private static Border FeatureDirectoryCard(string eyebrow, string title, IEnumerable<(string Feature, string Status)> features, Symbol symbol)
    {
        var panel = new StackPanel { Spacing = 10 };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        heading.Children.Add(new Border
        {
            Width = 34,
            Height = 34,
            Background = new SolidColorBrush(Color.FromArgb(255, 31, 45, 62)),
            BorderBrush = AccentDim,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new SymbolIcon(symbol) { Foreground = Accent, Width = 16, Height = 16 }
        });
        var headingCopy = new StackPanel { Spacing = 2 };
        headingCopy.Children.Add(Eyebrow(eyebrow));
        headingCopy.Children.Add(Label(title, 18, Foreground, true));
        heading.Children.Add(headingCopy);
        panel.Children.Add(heading);

        foreach (var feature in features)
        {
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = feature.Feature, FontFamily = AppFont, FontSize = 13, Foreground = Foreground, TextWrapping = TextWrapping.Wrap });
            var status = new TextBlock
            {
                Text = feature.Status,
                FontFamily = AppFont,
                FontSize = 10,
                CharacterSpacing = 40,
                Foreground = feature.Status.StartsWith("READY", StringComparison.Ordinal) ? Accent : Warning,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(status, 1);
            row.Children.Add(status);
            panel.Children.Add(row);
        }

        var card = Card();
        card.Child = panel;
        return card;
    }

    private UIElement BuildSignedOutOverview()
    {
        var columns = new Grid { ColumnSpacing = 18 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        var overview = new StackPanel { Spacing = 16 };
        var serviceCard = Card(accented: true);
        var serviceContent = new StackPanel { Spacing = 13 };
        serviceContent.Children.Add(Eyebrow("LOCAL CONTROL PLANE"));
        serviceContent.Children.Add(Label("Everything needed to run the arena.", 28, Foreground, true));
        serviceContent.Children.Add(Body("The native console owns the local service lifecycle, verifies HTTPS health, and brings operational tools together without opening a browser."));
        serviceContent.Children.Add(StatusRow("API endpoint", "https://localhost:5001"));
        serviceContent.Children.Add(StatusRow("Secure transport", "HTTPS + WSS"));
        serviceContent.Children.Add(StatusRow("Runtime", ".NET 8 / WinUI 3"));
        serviceContent.Children.Add(StatusRow("Current state", isServerReady ? "Healthy" : serverState.Text.ToLowerInvariant()));
        serviceCard.Child = serviceContent;
        overview.Children.Add(serviceCard);

        var featureGrid = new Grid { ColumnSpacing = 14 };
        featureGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        featureGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        featureGrid.Children.Add(InfoCard("SECURE BY DEFAULT", "Protected operations", "Bearer sessions, signed real-time packets, audit trails, and role-aware management.", Symbol.Setting));
        var lifecycle = InfoCard("NATIVE LIFECYCLE", "Managed automatically", "The API starts with this dashboard and is stopped cleanly when the console closes.", Symbol.Sync);
        Grid.SetColumn(lifecycle, 1);
        featureGrid.Children.Add(lifecycle);
        overview.Children.Add(featureGrid);
        columns.Children.Add(overview);

        var sessionCard = Card(accented: true);
        var session = new StackPanel { Spacing = 14 };
        session.Children.Add(Eyebrow("LOCAL MANAGEMENT"));
        session.Children.Add(Label("No operator login required", 24, Foreground, true));
        session.Children.Add(Body("This desktop console receives its management session directly from the local server. Player and game-client sign-ins remain enforced by the API."));
        session.Children.Add(StatusRow("Console access", token is null ? "Connecting" : "Active"));
        session.Children.Add(StatusRow("Network boundary", "Loopback only"));
        session.Children.Add(StatusRow("Authorization", "Server-issued admin session"));
        session.Children.Add(StatusRow("Player sign-in", "Managed by server"));
        sessionCard.Child = session;
        Grid.SetColumn(sessionCard, 1);
        columns.Children.Add(sessionCard);
        return columns;
    }

    private UIElement BuildSessionOverview()
    {
        var stack = new StackPanel { Spacing = 18 };
        var metrics = new Grid { ColumnSpacing = 14 };
        for (var index = 0; index < 4; index++)
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metrics.Children.Add(MetricCard("SERVICE", "Online", "HTTPS health check passed"));
        var presence = MetricCard("PLAYERS ONLINE", onlinePlayerTotal.ToString("N0"), "Active in the last two minutes");
        Grid.SetColumn(presence, 1);
        metrics.Children.Add(presence);
        var access = MetricCard("ACCESS", IsAdmin ? "Full" : "Standard", IsAdmin ? "Operations and moderation" : "Games and session tools");
        Grid.SetColumn(access, 2);
        metrics.Children.Add(access);
        var uptime = MetricCard("DASHBOARD", FormatDuration(DateTimeOffset.Now - dashboardStartedAt), "Current control session");
        Grid.SetColumn(uptime, 3);
        metrics.Children.Add(uptime);
        stack.Children.Add(metrics);

        var workspace = new Grid { ColumnSpacing = 14 };
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        var quickActions = Card(accented: true);
        var actions = new StackPanel { Spacing = 12 };
        actions.Children.Add(Eyebrow("QUICK ACTIONS"));
        actions.Children.Add(Label("Jump into operations", 20, Foreground, true));
        actions.Children.Add(Body("Use the most common management tools without hunting through menus."));
        var buttons = new Grid { ColumnSpacing = 10, RowSpacing = 10, Margin = new Thickness(0, 4, 0, 0) };
        for (var column = 0; column < 3; column++)
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        buttons.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        buttons.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var gamesButton = IconButton("Manage games", Symbol.Play);
        gamesButton.Click += (_, _) => SelectNavigation("games");
        buttons.Children.Add(gamesButton);
        var playersButton = IconButton("Player profiles", Symbol.Contact);
        playersButton.Click += (_, _) => SelectNavigation("players");
        Grid.SetColumn(playersButton, 1);
        buttons.Children.Add(playersButton);
        var contentButton = IconButton("Content & assets", Symbol.Library);
        contentButton.Click += (_, _) => SelectNavigation("rules");
        Grid.SetColumn(contentButton, 2);
        buttons.Children.Add(contentButton);
        var botsButton = IconButton("AI bot manager", Symbol.Play);
        botsButton.Click += (_, _) => SelectNavigation("bots");
        Grid.SetRow(botsButton, 1);
        buttons.Children.Add(botsButton);
        var liveOpsButton = IconButton("Live operations", Symbol.Calendar);
        liveOpsButton.Click += (_, _) => SelectNavigation("liveops");
        Grid.SetColumn(liveOpsButton, 1);
        Grid.SetRow(liveOpsButton, 1);
        buttons.Children.Add(liveOpsButton);
        var reportsButton = IconButton("Reports & archives", Symbol.Save);
        reportsButton.Click += (_, _) => SelectNavigation("reports");
        Grid.SetColumn(reportsButton, 2);
        Grid.SetRow(reportsButton, 1);
        buttons.Children.Add(reportsButton);
        var matchmakingButton = IconButton("Matchmaking", Symbol.People);
        matchmakingButton.Click += (_, _) => SelectNavigation("matchmaking");
        Grid.SetRow(matchmakingButton, 2);
        buttons.Children.Add(matchmakingButton);
        var storeButton = IconButton("Store & economy", Symbol.Shop);
        storeButton.Click += (_, _) => SelectNavigation("commerce");
        Grid.SetColumn(storeButton, 1);
        Grid.SetRow(storeButton, 2);
        buttons.Children.Add(storeButton);
        var warningButton = IconButton("Early warning", Symbol.Important);
        warningButton.Click += (_, _) => SelectNavigation("warnings");
        Grid.SetColumn(warningButton, 2);
        Grid.SetRow(warningButton, 2);
        buttons.Children.Add(warningButton);
        actions.Children.Add(buttons);
        quickActions.Child = actions;
        workspace.Children.Add(quickActions);

        var posture = Card();
        var postureContent = new StackPanel { Spacing = 10 };
        postureContent.Children.Add(Eyebrow("SECURITY POSTURE"));
        postureContent.Children.Add(Label("Protected", 20, Accent, true));
        postureContent.Children.Add(StatusRow("Session", "Bearer token"));
        postureContent.Children.Add(StatusRow("Transport", "TLS enabled"));
        postureContent.Children.Add(StatusRow("Real-time", "Signed packets"));
        postureContent.Children.Add(StatusRow("Audit", IsAdmin ? "Available" : "Admin only"));
        posture.Child = postureContent;
        Grid.SetColumn(posture, 1);
        workspace.Children.Add(posture);
        stack.Children.Add(workspace);
        return stack;
    }

    private async Task LoadSummaryAsync(StackPanel page)
    {
        var section = new StackPanel { Spacing = 12 };
        section.Children.Add(Label("Live service totals", 20, Foreground, true));
        var loading = Body("Loading the latest totals...");
        section.Children.Add(loading);
        page.Children.Add(section);

        try
        {
            using var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/summary");
            var summary = await response.Content.ReadFromJsonAsync<AdminSummaryRow>()
                ?? throw new InvalidOperationException("The service returned an empty summary.");
            section.Children.Remove(loading);
            section.Children.Add(BuildSummaryGrid(summary));
        }
        catch (Exception exception)
        {
            loading.Text = FriendlyError(exception);
            loading.Foreground = Danger;
        }
    }

    private static UIElement BuildSummaryGrid(AdminSummaryRow summary)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        for (var index = 0; index < 3; index++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var values = new (string Label, int Value, string Detail)[]
        {
            ("USERS", summary.Users, $"{summary.DisabledUsers} disabled"),
            ("GAMES", summary.Games, "active in memory"),
            ("WORKFLOWS", summary.StateMachines, "state-machine instances"),
            ("ASSETS", summary.Assets, "addressable resources"),
            ("AUDIT", summary.RecentLogs, "recent entries"),
            ("MATCHMAKING", summary.WaitingPlayers, $"across {summary.MatchmakingQueues} queues"),
            ("STORE", summary.StoreProducts, "catalog products"),
            ("TRANSACTIONS", summary.CurrencyTransactions, "currency ledger entries"),
            ("SERVICE", 1, "healthy instance")
        };

        for (var index = 0; index < values.Length; index++)
        {
            var card = MetricCard(values[index].Label, values[index].Value.ToString(), values[index].Detail);
            Grid.SetColumn(card, index % 3);
            Grid.SetRow(card, index / 3);
            grid.Children.Add(card);
        }
        return grid;
    }

    private async Task ShowOperationsAsync()
    {
        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Service operations", "Inspect the local control plane, verify connectivity, and review exposed service channels."));

        var metrics = new Grid { ColumnSpacing = 14 };
        for (var index = 0; index < 4; index++)
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var serviceMetric = MutableMetricCard("SERVICE", isServerReady ? "Online" : "Unavailable", isServerReady ? "Health gate passed" : "Waiting for service", out var serviceMetricValue, out var serviceMetricDetail);
        metrics.Children.Add(serviceMetric);
        var transport = MetricCard("TRANSPORT", "TLS", "HTTPS + secure WebSockets");
        Grid.SetColumn(transport, 1);
        metrics.Children.Add(transport);
        var runtime = MetricCard("RUNTIME", ".NET 8", Environment.Is64BitProcess ? "64-bit process" : "32-bit process");
        Grid.SetColumn(runtime, 2);
        metrics.Children.Add(runtime);
        var uptime = MetricCard("CONSOLE UPTIME", FormatDuration(DateTimeOffset.Now - dashboardStartedAt), "Since dashboard launch");
        Grid.SetColumn(uptime, 3);
        metrics.Children.Add(uptime);
        page.Children.Add(metrics);

        var columns = new Grid { ColumnSpacing = 14 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        var probeCard = Card(accented: true);
        var probeContent = new StackPanel { Spacing = 12 };
        probeContent.Children.Add(Eyebrow("LIVE HEALTH PROBE"));
        probeContent.Children.Add(Label("API availability", 22, Foreground, true));
        var probeStatus = Body("Preparing a live request...");
        var probeDetails = new TextBlock { FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap };
        probeContent.Children.Add(probeStatus);
        probeContent.Children.Add(probeDetails);
        var probeButton = IconButton("Run health check", Symbol.Refresh);
        probeButton.HorizontalAlignment = HorizontalAlignment.Left;
        probeContent.Children.Add(probeButton);
        probeCard.Child = probeContent;
        columns.Children.Add(probeCard);

        var channelsCard = Card();
        var channels = new StackPanel { Spacing = 10 };
        channels.Children.Add(Eyebrow("SERVICE CHANNELS"));
        channels.Children.Add(StatusRow("REST API", "/api"));
        channels.Children.Add(StatusRow("Game events", "/hubs/game"));
        channels.Children.Add(StatusRow("Workflows", "/hubs/state-machine"));
        channels.Children.Add(StatusRow("Compression", "Brotli / GZIP"));
        channels.Children.Add(StatusRow("Host", "localhost:5001"));
        channelsCard.Child = channels;
        Grid.SetColumn(channelsCard, 1);
        columns.Children.Add(channelsCard);
        page.Children.Add(columns);

        var endpointCard = Card();
        var endpoints = new StackPanel { Spacing = 10 };
        endpoints.Children.Add(Eyebrow("OPERATIONAL ENDPOINTS"));
        endpoints.Children.Add(StatusRow("Health", "GET /api/health"));
        endpoints.Children.Add(StatusRow("Rules", "GET /api/rules"));
        endpoints.Children.Add(StatusRow("Games", "GET /api/games"));
        endpoints.Children.Add(StatusRow("Rankings", "GET /api/rankings"));
        endpoints.Children.Add(StatusRow("Asset manifest", "GET /api/assets/manifest"));
        endpointCard.Child = endpoints;
        page.Children.Add(endpointCard);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        async Task ProbeAsync()
        {
            probeButton.IsEnabled = false;
            probeStatus.Text = "Contacting the local API...";
            probeStatus.Foreground = Muted;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var response = await http.GetAsync("/api/health");
                await EnsureSuccessAsync(response);
                var health = await response.Content.ReadFromJsonAsync<JsonElement>();
                watch.Stop();
                probeStatus.Text = "Healthy and accepting requests";
                probeStatus.Foreground = Accent;
                probeDetails.Text = $"HTTP {(int)response.StatusCode} · {watch.ElapsedMilliseconds} ms · {health.GetProperty("service").GetString()}";
                if (!isServerReady)
                    ApplyServerReadyState("Connected to the service at https://localhost:5001");
                serviceMetricValue.Text = "Online";
                serviceMetricValue.Foreground = Accent;
                serviceMetricDetail.Text = "Live health probe passed";
                SetNotification($"Health check passed · {watch.ElapsedMilliseconds} ms", Accent);
            }
            catch (Exception exception)
            {
                watch.Stop();
                probeStatus.Text = "Health check failed";
                probeStatus.Foreground = Danger;
                probeDetails.Text = FriendlyError(exception);
                serviceMetricValue.Text = "Unavailable";
                serviceMetricValue.Foreground = Danger;
                serviceMetricDetail.Text = "Health probe failed";
                SetNotification(FriendlyError(exception), Danger);
            }
            finally
            {
                probeButton.IsEnabled = true;
            }
        }

        probeButton.Click += async (_, _) => await ProbeAsync();
        await ProbeAsync();
    }

    private async Task ShowCatalogAsync()
    {
        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Content catalog", "A live visual inventory of rules, workflows, assets, releases, products, events, rewards, and achievements."));

        var refresh = IconButton("Refresh catalog", Symbol.Refresh);
        refresh.HorizontalAlignment = HorizontalAlignment.Left;
        refresh.Click += async (_, _) => await ShowCatalogAsync();
        page.Children.Add(refresh);

        var loading = Body("Loading catalog from the local service...");
        page.Children.Add(loading);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        try
        {
            var rulesTask = http.GetFromJsonAsync<JsonElement[]>("/api/rules");
            var workflowsTask = http.GetFromJsonAsync<JsonElement[]>("/api/state-machines/configurations");
            var assetsTask = http.GetFromJsonAsync<JsonElement[]>("/api/assets/manifest");
            await Task.WhenAll(rulesTask, workflowsTask, assetsTask);
            var rules = await rulesTask ?? [];
            var workflows = await workflowsTask ?? [];
            var assets = await assetsTask ?? [];
            JsonElement[] packs = [];
            JsonElement[] products = [];
            JsonElement[] campaigns = [];
            JsonElement[] rewards = [];
            JsonElement[] achievements = [];
            if (IsAdmin)
            {
                using var packsResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/content-packs");
                packs = await packsResponse.Content.ReadFromJsonAsync<JsonElement[]>() ?? [];
                using var productsResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/store/products");
                products = await productsResponse.Content.ReadFromJsonAsync<JsonElement[]>() ?? [];
                using var campaignsResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/liveops/campaigns");
                campaigns = await campaignsResponse.Content.ReadFromJsonAsync<JsonElement[]>() ?? [];
                using var rewardsResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/liveops/rewards");
                rewards = await rewardsResponse.Content.ReadFromJsonAsync<JsonElement[]>() ?? [];
                using var achievementsResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/liveops/achievements");
                achievements = await achievementsResponse.Content.ReadFromJsonAsync<JsonElement[]>() ?? [];
            }

            page.Children.Remove(loading);
            var metrics = new Grid { ColumnSpacing = 14, RowSpacing = 14 };
            for (var index = 0; index < 4; index++)
                metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var metricData = new (string Label, int Count, string Detail)[]
            {
                ("RULE PACKS", rules.Length, "Playable rule definitions"),
                ("WORKFLOWS", workflows.Length, "State-machine configurations"),
                ("ASSETS", assets.Length, "Addressable content entries"),
                ("CONTENT PACKS", packs.Length, "Versioned release packages"),
                ("STORE PRODUCTS", products.Length, "Products and bundles"),
                ("EVENTS", campaigns.Length, "Live campaigns and seasons"),
                ("REWARDS", rewards.Length, "Reward programs"),
                ("ACHIEVEMENTS", achievements.Length, "Player goals and milestones")
            };
            for (var index = 0; index < metricData.Length; index++)
            {
                var metric = MetricCard(metricData[index].Label, metricData[index].Count.ToString(), metricData[index].Detail);
                Grid.SetColumn(metric, index % 4);
                Grid.SetRow(metric, index / 4);
                metrics.Children.Add(metric);
            }
            page.Children.Add(metrics);

            var inventory = new StackPanel { Spacing = 8 };
            inventory.Children.Add(Eyebrow("LIVE INVENTORY"));
            AddCatalogEntries(inventory, "RULE", rules);
            AddCatalogEntries(inventory, "WORKFLOW", workflows);
            AddCatalogEntries(inventory, "ASSET", assets);
            AddCatalogEntries(inventory, "PACK", packs);
            AddCatalogEntries(inventory, "PRODUCT", products);
            AddCatalogEntries(inventory, "EVENT", campaigns);
            AddCatalogEntries(inventory, "REWARD", rewards);
            AddCatalogEntries(inventory, "ACHIEVEMENT", achievements);
            var total = rules.Length + workflows.Length + assets.Length + packs.Length + products.Length + campaigns.Length + rewards.Length + achievements.Length;
            if (total == 0)
                inventory.Children.Add(Body("The catalog is currently empty."));
            var inventoryCard = Card();
            inventoryCard.Child = inventory;
            page.Children.Add(inventoryCard);
            SetNotification($"Catalog refreshed · {rules.Length + workflows.Length + assets.Length} items", Accent);
        }
        catch (Exception exception)
        {
            loading.Text = FriendlyError(exception);
            loading.Foreground = Danger;
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private async Task ShowRankingsAsync()
    {
        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Rankings", "Player standings ordered by live server rating."));
        var refresh = IconButton("Refresh leaderboard", Symbol.Refresh);
        refresh.HorizontalAlignment = HorizontalAlignment.Left;
        refresh.Click += async (_, _) => await ShowRankingsAsync();
        page.Children.Add(refresh);

        var list = new ListView { MinHeight = 430, SelectionMode = ListViewSelectionMode.None };
        var card = Card();
        card.Child = list;
        page.Children.Add(card);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        try
        {
            var rankings = await http.GetFromJsonAsync<JsonElement[]>("/api/rankings") ?? [];
            foreach (var entry in rankings)
            {
                var user = entry.GetProperty("user");
                list.Items.Add(RankingRow(
                    entry.GetProperty("rank").GetInt32(),
                    user.GetProperty("handle").GetString() ?? "Unknown player",
                    user.GetProperty("rating").GetInt32()));
            }
            if (rankings.Length == 0)
                list.Items.Add(Body("No ranked players are available yet."));
            SetNotification($"Leaderboard refreshed · {rankings.Length} players", Accent);
        }
        catch (Exception exception)
        {
            list.Items.Add(Body(FriendlyError(exception)));
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private async Task ShowApiExplorerAsync()
    {
        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("API explorer", "Run safe read-only requests against the local service and inspect formatted responses."));

        var endpointPicker = new ComboBox
        {
            Header = "Endpoint",
            ItemsSource = new[]
            {
                "/api/health",
                "/api/rules",
                "/api/games",
                "/api/rankings",
                "/api/state-machines/configurations",
                "/api/assets/manifest"
            },
            SelectedIndex = 0,
            MinWidth = 340
        };
        var execute = IconButton("Send GET request", Symbol.Go);
        execute.VerticalAlignment = VerticalAlignment.Bottom;
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        toolbar.Children.Add(endpointPicker);
        toolbar.Children.Add(execute);
        page.Children.Add(toolbar);

        var responseStatus = Body("Ready to send a request.");
        page.Children.Add(responseStatus);
        var responseBody = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 390,
            FontFamily = MonoFont,
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromArgb(255, 13, 18, 27)),
            BorderBrush = Divider
        };
        var responseCard = Card();
        responseCard.Padding = new Thickness(12);
        responseCard.Child = responseBody;
        page.Children.Add(responseCard);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        async Task ExecuteAsync()
        {
            var endpoint = endpointPicker.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(endpoint))
                return;

            execute.IsEnabled = false;
            responseStatus.Text = $"GET {endpoint} · sending...";
            responseStatus.Foreground = Muted;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var response = await http.GetAsync(endpoint);
                var raw = await response.Content.ReadAsStringAsync();
                watch.Stop();
                await EnsureSuccessAsync(response);
                try
                {
                    using var json = JsonDocument.Parse(raw);
                    responseBody.Text = JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true });
                }
                catch (JsonException)
                {
                    responseBody.Text = raw;
                }
                responseStatus.Text = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} · {watch.ElapsedMilliseconds} ms · {raw.Length:N0} bytes";
                responseStatus.Foreground = Accent;
                SetNotification($"API request completed · {watch.ElapsedMilliseconds} ms", Accent);
            }
            catch (Exception exception)
            {
                watch.Stop();
                responseBody.Text = FriendlyError(exception);
                responseStatus.Text = $"Request failed after {watch.ElapsedMilliseconds} ms";
                responseStatus.Foreground = Danger;
                SetNotification(FriendlyError(exception), Danger);
            }
            finally
            {
                execute.IsEnabled = true;
            }
        }

        execute.Click += async (_, _) => await ExecuteAsync();
        await ExecuteAsync();
    }

    private async Task ConnectManagementSessionAsync()
    {
        SetNotification("Opening the local management session...", Muted);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/local-management");
            request.Headers.Add("X-Tcg-Management-Key", managementKey);
            using var response = await http.SendAsync(request);
            await EnsureSuccessAsync(response);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            token = json.RootElement.GetProperty("token").GetString();
            var user = json.RootElement.GetProperty("user");
            userId = user.GetProperty("id").GetString();
            signedInHandle = user.GetProperty("handle").GetString();
            role = user.TryGetProperty("role", out var roleValue) ? roleValue.GetString() : "admin";
            operatorDetail.Text = $"{signedInHandle?.ToUpperInvariant()} · {role?.ToUpperInvariant()}";
            SetNotification("Local management session active. Player sign-in remains server-managed.", Accent);
            BuildNavigation();
            await RefreshLivePresenceAsync();
            liveRefreshTimer.Start();
            ShowOverview();
        }
        catch (Exception exception)
        {
            token = null;
            operatorDetail.Text = "SESSION FAILED";
            SetNotification(FriendlyError(exception), Danger);
            BuildNavigation();
            ShowOverview();
        }
    }

    private async Task RefreshLivePresenceAsync()
    {
        if (token is null)
            return;

        try
        {
            using var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/presence");
            var presence = await response.Content.ReadFromJsonAsync<PresenceSnapshotRow>();
            if (presence is null)
                return;

            onlinePlayerTotal = presence.OnlinePlayers;
            var noun = onlinePlayerTotal == 1 ? "PLAYER" : "PLAYERS";
            livePlayerCount.Text = $"{onlinePlayerTotal:N0} {noun} ONLINE";
            livePlayerCount.Foreground = onlinePlayerTotal > 0 ? Accent : Muted;
        }
        catch
        {
            livePlayerCount.Text = "PRESENCE UNAVAILABLE";
            livePlayerCount.Foreground = Warning;
        }
    }

    private async Task ShowRuleEngineAsync()
    {
        if (!IsAdmin)
            return;

        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Visual content studio", "Browse, validate, edit, import, export, upload, and publish system-neutral TCG rules and every supporting content type."));

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var refresh = IconButton("Refresh inventory", Symbol.Refresh);
        var upload = IconButton("Open upload center", Symbol.Add);
        refresh.Click += async (_, _) => await ShowRuleEngineAsync();
        upload.Click += (_, _) => SelectNavigation("uploads");
        toolbar.Children.Add(refresh);
        toolbar.Children.Add(upload);
        page.Children.Add(toolbar);
        AddContentWorkspaceBrowser(page);

        var loading = Body("Loading engine inventory...");
        page.Children.Add(loading);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        try
        {
            var rulesTask = http.GetFromJsonAsync<JsonElement[]>("/api/rules");
            var capabilitiesTask = http.GetFromJsonAsync<JsonElement>("/api/rules/capabilities");
            var workflowsTask = http.GetFromJsonAsync<JsonElement[]>("/api/state-machines/configurations");
            var assetsTask = http.GetFromJsonAsync<JsonElement[]>("/api/assets/manifest");
            await Task.WhenAll(rulesTask, capabilitiesTask, workflowsTask, assetsTask);
            var rules = await rulesTask ?? [];
            var capabilities = await capabilitiesTask;
            var workflows = await workflowsTask ?? [];
            var assets = await assetsTask ?? [];
            page.Children.Remove(loading);

            AddVisualRuleStudio(page, rules, capabilities);
            AddVisualWorkflowStudio(page, workflows);

            var definitionGrid = new Grid { ColumnSpacing = 14 };
            definitionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            definitionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var ruleCard = ManagementSection("RULE PACKS", "Playable card and rules definitions", out var ruleList, out var removeRule);
            foreach (var rule in rules)
            {
                var cards = rule.TryGetProperty("cards", out var cardArray) && cardArray.ValueKind == JsonValueKind.Array ? cardArray.GetArrayLength() : 0;
                ruleList.Items.Add($"{JsonText(rule, "name", "id")}  ·  v{JsonText(rule, "version")}  ·  {cards} cards");
            }
            if (rules.Length == 0) ruleList.Items.Add("No rule packs are installed.");
            removeRule.Click += async (_, _) => await DeleteManagedItemAsync(ruleList, rules, "/api/rules/", "rule pack", ShowRuleEngineAsync);
            definitionGrid.Children.Add(ruleCard);

            var workflowCard = ManagementSection("WORKFLOWS", "Declarative state-machine definitions", out var workflowList, out var removeWorkflow);
            foreach (var workflow in workflows)
            {
                var states = workflow.TryGetProperty("states", out var stateArray) && stateArray.ValueKind == JsonValueKind.Array ? stateArray.GetArrayLength() : 0;
                workflowList.Items.Add($"{JsonText(workflow, "name", "id")}  ·  v{JsonText(workflow, "version")}  ·  {states} states");
            }
            if (workflows.Length == 0) workflowList.Items.Add("No workflows are installed.");
            removeWorkflow.Click += async (_, _) => await DeleteManagedItemAsync(workflowList, workflows, "/api/state-machines/configurations/", "workflow", ShowRuleEngineAsync);
            Grid.SetColumn(workflowCard, 1);
            definitionGrid.Children.Add(workflowCard);
            page.Children.Add(definitionGrid);

            var assetCard = ManagementSection("ADDRESSABLE ASSETS", "Server-owned files available to authenticated clients", out var assetList, out var removeAsset);
            foreach (var asset in assets)
            {
                var length = asset.TryGetProperty("length", out var size) && size.TryGetInt64(out var bytes) ? FormatBytes(bytes) : "Unknown size";
                assetList.Items.Add($"{JsonText(asset, "name", "id")}  ·  {JsonText(asset, "contentType")}  ·  {length}");
            }
            if (assets.Length == 0) assetList.Items.Add("No addressable assets are installed.");
            removeAsset.Click += async (_, _) => await DeleteManagedItemAsync(assetList, assets, "/api/assets/", "asset", ShowRuleEngineAsync);
            page.Children.Add(assetCard);
            SetNotification($"Rule engine inventory loaded · {rules.Length} packs · {workflows.Length} workflows · {assets.Length} assets", Accent);
        }
        catch (Exception exception)
        {
            loading.Text = FriendlyError(exception);
            loading.Foreground = Danger;
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private void AddContentWorkspaceBrowser(StackPanel page)
    {
        var section = new StackPanel { Spacing = 12 };
        section.Children.Add(Eyebrow("ALL CONTENT WORKSPACES"));
        section.Children.Add(Body("Open every authoring, upload, and operations surface from one visual browser."));
        var workspaces = new (string Title, string Detail, string Tag, Symbol Icon)[]
        {
            ("Rules and cards", "System-neutral rules, zones, resources, phases, actions, cards, and abilities.", "rules", Symbol.Setting),
            ("Workflow runtime", "Test declarative workflows and inspect live instances.", "workflow_runtime", Symbol.Sync),
            ("Assets and uploads", "Upload artwork, audio, bundles, JSON definitions, and addressable files.", "uploads", Symbol.Add),
            ("Game packages", "Validate, import, activate, download, and roll back complete CardForge releases.", "game_packages", Symbol.Save),
            ("Content packs", "Version, activate, download, and retire packaged releases.", "content_packs", Symbol.Folder),
            ("Store and economy", "Products, bundles, currencies, pricing, limits, and economy controls.", "commerce", Symbol.Shop),
            ("LiveOps content", "Events, seasons, rewards, quests, and achievements.", "liveops", Symbol.Calendar)
        };
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        for (var column = 0; column < 3; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var row = 0; row < (workspaces.Length + 2) / 3; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < workspaces.Length; index++)
        {
            var workspace = workspaces[index];
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(Eyebrow(workspace.Title.ToUpperInvariant()));
            panel.Children.Add(Body(workspace.Detail));
            var open = IconButton("Open workspace", workspace.Icon);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Click += (_, _) => SelectNavigation(workspace.Tag);
            panel.Children.Add(open);
            var card = Card(index == 0);
            card.Child = panel;
            Grid.SetColumn(card, index % 3);
            Grid.SetRow(card, index / 3);
            grid.Children.Add(card);
        }
        section.Children.Add(grid);
        page.Children.Add(section);
    }

    private void AddVisualRuleStudio(StackPanel page, JsonElement[] rules, JsonElement capabilities)
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(Eyebrow("SYSTEM-NEUTRAL RULE DESIGNER"));
        panel.Children.Add(Label("Build any trading card game model", 22, Foreground, true));
        panel.Children.Add(Body("Define the zones, resources, phases, actions, conditions, effects, cards, and abilities that your game needs. Legacy rule packs are upgraded automatically before validation."));

        var capabilityText = capabilities.ValueKind == JsonValueKind.Object
            ? $"Schema {JsonText(capabilities, "schemaVersion")}  ·  {capabilities.GetProperty("effectKinds").GetArrayLength()} effects  ·  {capabilities.GetProperty("conditionKinds").GetArrayLength()} conditions"
            : "System-neutral schema";
        panel.Children.Add(StatusRow("ENGINE", capabilityText));

        var picker = new ComboBox
        {
            Header = "Installed rule pack",
            ItemsSource = rules.Select(rule => $"{JsonText(rule, "name", "id")}  ·  {JsonText(rule, "gameSystem")}  ·  v{JsonText(rule, "version")}").ToList(),
            SelectedIndex = rules.Length > 0 ? 0 : -1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontFamily = AppFont
        };
        panel.Children.Add(picker);

        var metadata = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        for (var column = 0; column < 3; column++)
            metadata.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metadata.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        metadata.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var id = new TextBox { Header = "Rule pack ID", PlaceholderText = "my-tcg", FontFamily = AppFont };
        var name = new TextBox { Header = "Display name", PlaceholderText = "My TCG", FontFamily = AppFont };
        var version = new TextBox { Header = "Version", PlaceholderText = "1.0.0", FontFamily = AppFont };
        var system = new TextBox { Header = "Game system", PlaceholderText = "custom", FontFamily = AppFont };
        var minimumPlayers = new TextBox { Header = "Minimum players", PlaceholderText = "2", FontFamily = AppFont };
        var maximumPlayers = new TextBox { Header = "Maximum players", PlaceholderText = "2", FontFamily = AppFont };
        var metadataFields = new[] { id, name, version, system, minimumPlayers, maximumPlayers };
        for (var index = 0; index < metadataFields.Length; index++)
        {
            Grid.SetColumn(metadataFields[index], index % 3);
            Grid.SetRow(metadataFields[index], index / 3);
            metadata.Children.Add(metadataFields[index]);
        }
        panel.Children.Add(metadata);

        var editorGrid = new Grid { ColumnSpacing = 14 };
        editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
        editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        var browserPanel = new StackPanel { Spacing = 8 };
        browserPanel.Children.Add(Eyebrow("VISUAL COMPONENT BROWSER"));
        browserPanel.Children.Add(Body("A structured inventory of the current definition."));
        var browser = new ListView { MinHeight = 360, MaxHeight = 520, SelectionMode = ListViewSelectionMode.None };
        browserPanel.Children.Add(browser);
        var browserCard = Card();
        browserCard.Child = browserPanel;
        editorGrid.Children.Add(browserCard);

        var editorPanel = new StackPanel { Spacing = 8 };
        editorPanel.Children.Add(Eyebrow("ADVANCED DEFINITION"));
        editorPanel.Children.Add(Body("Edit every property directly when the guided fields do not expose a custom extension."));
        var editor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 360,
            MaxHeight = 520,
            FontFamily = MonoFont,
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromArgb(255, 13, 18, 27)),
            BorderBrush = Divider
        };
        editorPanel.Children.Add(editor);
        var editorCard = Card();
        editorCard.Padding = new Thickness(12);
        editorCard.Child = editorPanel;
        Grid.SetColumn(editorCard, 1);
        editorGrid.Children.Add(editorCard);
        panel.Children.Add(editorGrid);

        var status = Body("Choose an installed pack or create a new template.");
        panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var create = IconButton("New template", Symbol.Add);
        var import = IconButton("Import JSON", Symbol.Folder);
        var apply = IconButton("Apply guided fields", Symbol.Sync);
        var validate = IconButton("Validate", Symbol.Accept);
        var save = IconButton("Publish", Symbol.Save);
        var export = IconButton("Export JSON", Symbol.Save);
        buttons.Children.Add(create);
        buttons.Children.Add(import);
        buttons.Children.Add(apply);
        buttons.Children.Add(validate);
        buttons.Children.Add(save);
        buttons.Children.Add(export);
        panel.Children.Add(buttons);

        JsonObject ReadDefinition()
        {
            try
            {
                return JsonNode.Parse(editor.Text) as JsonObject ?? throw new InvalidOperationException("The rule definition must be a JSON object.");
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("Enter valid rule pack JSON.", exception);
            }
        }

        void RefreshBrowser(JsonObject definition)
        {
            browser.Items.Clear();
            AddBrowserGroup(browser, "ZONE", definition["zones"] as JsonArray, "name", "id");
            AddBrowserGroup(browser, "RESOURCE", definition["resources"] as JsonArray, "name", "id");
            AddBrowserGroup(browser, "PHASE", definition["phases"] as JsonArray, "name", "id");
            AddBrowserGroup(browser, "ACTION", definition["actions"] as JsonArray, "name", "id");
            AddBrowserGroup(browser, "CARD", definition["cards"] as JsonArray, "name", "id");
            if (browser.Items.Count == 0)
                browser.Items.Add("No rule components are defined yet.");
        }

        void LoadDefinition(string json)
        {
            var definition = JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("The rule definition must be a JSON object.");
            editor.Text = PrettyJson(definition);
            id.Text = NodeText(definition, "id");
            name.Text = NodeText(definition, "name");
            version.Text = NodeText(definition, "version");
            system.Text = NodeText(definition, "gameSystem");
            minimumPlayers.Text = NodeText(definition, "minimumPlayers", "2");
            maximumPlayers.Text = NodeText(definition, "maximumPlayers", "2");
            RefreshBrowser(definition);
        }

        JsonObject ApplyFields()
        {
            var definition = ReadDefinition();
            definition["schemaVersion"] ??= "2";
            definition["id"] = id.Text.Trim();
            definition["name"] = name.Text.Trim();
            definition["version"] = version.Text.Trim();
            definition["gameSystem"] = string.IsNullOrWhiteSpace(system.Text) ? "custom" : system.Text.Trim();
            definition["minimumPlayers"] = int.TryParse(minimumPlayers.Text, out var minimum) ? Math.Max(1, minimum) : 2;
            definition["maximumPlayers"] = int.TryParse(maximumPlayers.Text, out var maximum) ? Math.Max(1, maximum) : 2;
            editor.Text = PrettyJson(definition);
            RefreshBrowser(definition);
            return definition;
        }

        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedIndex < 0 || picker.SelectedIndex >= rules.Length) return;
            LoadDefinition(JsonSerializer.Serialize(rules[picker.SelectedIndex]));
            status.Text = $"Loaded {JsonText(rules[picker.SelectedIndex], "name", "id")}.";
            status.Foreground = Accent;
        };
        create.Click += (_, _) =>
        {
            LoadDefinition(NewRulePackTemplate());
            picker.SelectedIndex = -1;
            status.Text = "New system-neutral template ready.";
            status.Foreground = Accent;
        };
        import.Click += async (_, _) =>
        {
            var file = await PickFileAsync(".json");
            if (file is null) return;
            try
            {
                LoadDefinition(await File.ReadAllTextAsync(file.Path));
                picker.SelectedIndex = -1;
                status.Text = $"Imported {file.Name}. Validate before publishing.";
                status.Foreground = Accent;
            }
            catch (Exception exception)
            {
                status.Text = FriendlyError(exception);
                status.Foreground = Danger;
            }
        };
        apply.Click += (_, _) =>
        {
            try
            {
                ApplyFields();
                status.Text = "Guided fields applied to the definition.";
                status.Foreground = Accent;
            }
            catch (Exception exception)
            {
                status.Text = FriendlyError(exception);
                status.Foreground = Danger;
            }
        };
        validate.Click += async (_, _) =>
        {
            try
            {
                var definition = ApplyFields();
                using var request = AuthorizedRequest(HttpMethod.Post, "/api/rules/validate");
                request.Content = JsonContent.Create(definition);
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                var valid = result.GetProperty("valid").GetBoolean();
                status.Text = ValidationSummary(result, valid ? "Rule pack is valid and ready to publish." : "Rule pack validation failed.");
                status.Foreground = valid ? Accent : Danger;
                if (valid && result.TryGetProperty("normalized", out var normalized) && normalized.ValueKind == JsonValueKind.Object)
                    LoadDefinition(JsonSerializer.Serialize(normalized));
            }
            catch (Exception exception)
            {
                status.Text = FriendlyError(exception);
                status.Foreground = Danger;
            }
        };
        save.Click += async (_, _) =>
        {
            try
            {
                var definition = ApplyFields();
                var ruleId = NodeText(definition, "id");
                if (string.IsNullOrWhiteSpace(ruleId)) throw new InvalidOperationException("Enter a rule pack ID first.");
                using var request = AuthorizedRequest(HttpMethod.Put, "/api/rules/" + Uri.EscapeDataString(ruleId));
                request.Content = JsonContent.Create(definition);
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                var saved = await response.Content.ReadAsStringAsync();
                LoadDefinition(saved);
                status.Text = $"Published {ruleId} to the live server.";
                status.Foreground = Accent;
                SetNotification($"Rule pack published · {ruleId}", Accent);
            }
            catch (Exception exception)
            {
                status.Text = FriendlyError(exception);
                status.Foreground = Danger;
                SetNotification(FriendlyError(exception), Danger);
            }
        };
        export.Click += async (_, _) =>
        {
            try
            {
                var definition = ApplyFields();
                await SaveTextFileAsync(PrettyJson(definition), NodeText(definition, "id", "rule-pack"), ".json");
            }
            catch (Exception exception)
            {
                status.Text = FriendlyError(exception);
                status.Foreground = Danger;
            }
        };

        if (rules.Length > 0)
            LoadDefinition(JsonSerializer.Serialize(rules[0]));
        else
            LoadDefinition(NewRulePackTemplate());

        var card = Card(accented: true);
        card.Child = panel;
        page.Children.Add(card);
    }

    private void AddVisualWorkflowStudio(StackPanel page, JsonElement[] workflows)
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(Eyebrow("VISUAL WORKFLOW DESIGNER"));
        panel.Children.Add(Label("Author reusable server workflows", 22, Foreground, true));
        panel.Children.Add(Body("Create state-based flows for tournaments, quests, moderation cases, publishing approvals, rewards, and custom game operations."));
        var picker = new ComboBox
        {
            Header = "Installed workflow",
            ItemsSource = workflows.Select(item => $"{JsonText(item, "name", "id")}  ·  v{JsonText(item, "version")}").ToList(),
            SelectedIndex = workflows.Length > 0 ? 0 : -1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontFamily = AppFont
        };
        panel.Children.Add(picker);

        var fields = new Grid { ColumnSpacing = 12 };
        for (var column = 0; column < 4; column++)
            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var id = new TextBox { Header = "Workflow ID", PlaceholderText = "reward-approval", FontFamily = AppFont };
        var name = new TextBox { Header = "Display name", PlaceholderText = "Reward approval", FontFamily = AppFont };
        var version = new TextBox { Header = "Version", PlaceholderText = "1.0.0", FontFamily = AppFont };
        var initialState = new TextBox { Header = "Initial state", PlaceholderText = "draft", FontFamily = AppFont };
        var fieldControls = new[] { id, name, version, initialState };
        for (var index = 0; index < fieldControls.Length; index++)
        {
            Grid.SetColumn(fieldControls[index], index);
            fields.Children.Add(fieldControls[index]);
        }
        panel.Children.Add(fields);

        var split = new Grid { ColumnSpacing = 14 };
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        var browserPanel = new StackPanel { Spacing = 8 };
        browserPanel.Children.Add(Eyebrow("STATES AND TRANSITIONS"));
        var browser = new ListView { MinHeight = 280, MaxHeight = 430, SelectionMode = ListViewSelectionMode.None };
        browserPanel.Children.Add(browser);
        var browserCard = Card();
        browserCard.Child = browserPanel;
        split.Children.Add(browserCard);
        var editor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 280,
            MaxHeight = 430,
            FontFamily = MonoFont,
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromArgb(255, 13, 18, 27)),
            BorderBrush = Divider
        };
        var editorCard = Card();
        editorCard.Padding = new Thickness(12);
        editorCard.Child = editor;
        Grid.SetColumn(editorCard, 1);
        split.Children.Add(editorCard);
        panel.Children.Add(split);

        var status = Body("Choose a workflow or create a template.");
        panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var create = IconButton("New template", Symbol.Add);
        var import = IconButton("Import JSON", Symbol.Folder);
        var apply = IconButton("Apply guided fields", Symbol.Sync);
        var validate = IconButton("Validate", Symbol.Accept);
        var save = IconButton("Publish", Symbol.Save);
        var export = IconButton("Export JSON", Symbol.Save);
        foreach (var button in new[] { create, import, apply, validate, save, export }) buttons.Children.Add(button);
        panel.Children.Add(buttons);

        JsonObject ReadDefinition()
        {
            try
            {
                return JsonNode.Parse(editor.Text) as JsonObject ?? throw new InvalidOperationException("The workflow must be a JSON object.");
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("Enter valid workflow JSON.", exception);
            }
        }

        void RefreshBrowser(JsonObject definition)
        {
            browser.Items.Clear();
            if (definition["states"] is JsonArray states)
                foreach (var state in states)
                    browser.Items.Add($"STATE  ·  {state?.ToString()}");
            if (definition["transitions"] is JsonArray transitions)
                foreach (var transition in transitions.OfType<JsonObject>())
                    browser.Items.Add($"EVENT  ·  {NodeText(transition, "event")}  ·  {NodeText(transition, "from")} → {NodeText(transition, "to")}");
            if (browser.Items.Count == 0) browser.Items.Add("No states or transitions are defined yet.");
        }

        void LoadDefinition(string json)
        {
            var definition = JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("The workflow must be a JSON object.");
            editor.Text = PrettyJson(definition);
            id.Text = NodeText(definition, "id");
            name.Text = NodeText(definition, "name");
            version.Text = NodeText(definition, "version");
            initialState.Text = NodeText(definition, "initialState");
            RefreshBrowser(definition);
        }

        JsonObject ApplyFields()
        {
            var definition = ReadDefinition();
            definition["schemaVersion"] ??= "1";
            definition["id"] = id.Text.Trim();
            definition["name"] = name.Text.Trim();
            definition["version"] = version.Text.Trim();
            definition["initialState"] = initialState.Text.Trim();
            editor.Text = PrettyJson(definition);
            RefreshBrowser(definition);
            return definition;
        }

        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedIndex < 0 || picker.SelectedIndex >= workflows.Length) return;
            LoadDefinition(JsonSerializer.Serialize(workflows[picker.SelectedIndex]));
            status.Text = $"Loaded {JsonText(workflows[picker.SelectedIndex], "name", "id")}.";
            status.Foreground = Accent;
        };
        create.Click += (_, _) =>
        {
            LoadDefinition(NewWorkflowTemplate());
            picker.SelectedIndex = -1;
            status.Text = "New workflow template ready.";
            status.Foreground = Accent;
        };
        import.Click += async (_, _) =>
        {
            var file = await PickFileAsync(".json");
            if (file is null) return;
            try
            {
                LoadDefinition(await File.ReadAllTextAsync(file.Path));
                picker.SelectedIndex = -1;
                status.Text = $"Imported {file.Name}. Validate before publishing.";
                status.Foreground = Accent;
            }
            catch (Exception exception)
            {
                status.Text = FriendlyError(exception);
                status.Foreground = Danger;
            }
        };
        apply.Click += (_, _) =>
        {
            try
            {
                ApplyFields();
                status.Text = "Guided fields applied to the workflow.";
                status.Foreground = Accent;
            }
            catch (Exception exception)
            {
                status.Text = FriendlyError(exception);
                status.Foreground = Danger;
            }
        };
        validate.Click += async (_, _) =>
        {
            try
            {
                var definition = ApplyFields();
                using var request = AuthorizedRequest(HttpMethod.Post, "/api/state-machines/configurations/validate");
                request.Content = JsonContent.Create(definition);
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                var valid = result.GetProperty("valid").GetBoolean();
                status.Text = ValidationSummary(result, valid ? "Workflow is valid and ready to publish." : "Workflow validation failed.");
                status.Foreground = valid ? Accent : Danger;
                if (valid && result.TryGetProperty("normalized", out var normalized) && normalized.ValueKind == JsonValueKind.Object)
                    LoadDefinition(JsonSerializer.Serialize(normalized));
            }
            catch (Exception exception)
            {
                status.Text = FriendlyError(exception);
                status.Foreground = Danger;
            }
        };
        save.Click += async (_, _) =>
        {
            try
            {
                var definition = ApplyFields();
                var workflowId = NodeText(definition, "id");
                if (string.IsNullOrWhiteSpace(workflowId)) throw new InvalidOperationException("Enter a workflow ID first.");
                using var request = AuthorizedRequest(HttpMethod.Put, "/api/state-machines/configurations/" + Uri.EscapeDataString(workflowId));
                request.Content = JsonContent.Create(definition);
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                LoadDefinition(await response.Content.ReadAsStringAsync());
                status.Text = $"Published {workflowId} to the live server.";
                status.Foreground = Accent;
                SetNotification($"Workflow published · {workflowId}", Accent);
            }
            catch (Exception exception)
            {
                status.Text = FriendlyError(exception);
                status.Foreground = Danger;
                SetNotification(FriendlyError(exception), Danger);
            }
        };
        export.Click += async (_, _) =>
        {
            try
            {
                var definition = ApplyFields();
                await SaveTextFileAsync(PrettyJson(definition), NodeText(definition, "id", "workflow"), ".json");
            }
            catch (Exception exception)
            {
                status.Text = FriendlyError(exception);
                status.Foreground = Danger;
            }
        };

        if (workflows.Length > 0)
            LoadDefinition(JsonSerializer.Serialize(workflows[0]));
        else
            LoadDefinition(NewWorkflowTemplate());

        var card = Card();
        card.Child = panel;
        page.Children.Add(card);
    }

    private async Task ShowUploadCenterAsync()
    {
        if (!IsAdmin)
            return;

        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Upload center", "Validate and install server content directly from the native management console."));

        var grid = new Grid { ColumnSpacing = 14 };
        for (var index = 0; index < 3; index++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var ruleStack = UploadPanel("RULE PACK", "Install or replace a JSON rule pack. The server validates its ID and card definitions.");
        var ruleStatus = Body("JSON files only");
        var ruleUpload = IconButton("Choose rule pack", Symbol.Add);
        ruleStack.Children.Add(ruleStatus);
        ruleStack.Children.Add(ruleUpload);
        var ruleCard = Card(accented: true);
        ruleCard.Child = ruleStack;
        grid.Children.Add(ruleCard);

        var workflowStack = UploadPanel("WORKFLOW", "Install a declarative state-machine configuration with validated states and transitions.");
        var workflowStatus = Body("JSON files only");
        var workflowUpload = IconButton("Choose workflow", Symbol.Add);
        workflowStack.Children.Add(workflowStatus);
        workflowStack.Children.Add(workflowUpload);
        var workflowCard = Card();
        workflowCard.Child = workflowStack;
        Grid.SetColumn(workflowCard, 1);
        grid.Children.Add(workflowCard);

        var assetStack = UploadPanel("ADDRESSABLE ASSET", "Upload a binary asset up to 50 MB with a stable server identifier and display name.");
        var assetId = new TextBox { Header = "Asset ID", PlaceholderText = "card-art-001", MaxLength = 120, FontFamily = AppFont };
        var assetName = new TextBox { Header = "Display name", PlaceholderText = "Card artwork", MaxLength = 240, FontFamily = AppFont };
        var assetStatus = Body("Any file type · 50 MB maximum");
        var assetUpload = IconButton("Choose asset", Symbol.Add);
        assetStack.Children.Add(assetId);
        assetStack.Children.Add(assetName);
        assetStack.Children.Add(assetStatus);
        assetStack.Children.Add(assetUpload);
        var assetCard = Card();
        assetCard.Child = assetStack;
        Grid.SetColumn(assetCard, 2);
        grid.Children.Add(assetCard);
        page.Children.Add(grid);

        var guidance = Card();
        var guidanceCopy = new StackPanel { Spacing = 8 };
        guidanceCopy.Children.Add(Eyebrow("SAFE CONTENT OPERATIONS"));
        guidanceCopy.Children.Add(Label("Uploads are validated before activation", 20, Foreground, true));
        guidanceCopy.Children.Add(Body("Uploading an existing ID replaces that definition atomically. Every upload is administrator-only and written to the audit trail. Use the Rule engine page to review or remove installed content."));
        guidance.Child = guidanceCopy;
        page.Children.Add(guidance);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        ruleUpload.Click += async (_, _) => await RunJsonUploadAsync("/api/rules/upload", "rule pack", ruleUpload, ruleStatus);
        workflowUpload.Click += async (_, _) => await RunJsonUploadAsync("/api/state-machines/configurations/upload", "workflow", workflowUpload, workflowStatus);
        assetUpload.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(assetId.Text) || string.IsNullOrWhiteSpace(assetName.Text))
            {
                assetStatus.Text = "Enter an asset ID and display name first.";
                assetStatus.Foreground = Warning;
                return;
            }
            var file = await PickFileAsync("*");
            if (file is null) return;
            var length = new FileInfo(file.Path).Length;
            if (length > 50L * 1024 * 1024)
            {
                assetStatus.Text = "The selected file exceeds the 50 MB limit.";
                assetStatus.Foreground = Danger;
                return;
            }
            assetUpload.IsEnabled = false;
            assetStatus.Text = $"Uploading {file.Name}...";
            assetStatus.Foreground = Muted;
            try
            {
                await UploadFileAsync("/api/assets/upload", file, new Dictionary<string, string>
                {
                    ["id"] = assetId.Text.Trim(),
                    ["name"] = assetName.Text.Trim()
                });
                assetStatus.Text = $"Installed {file.Name} · {FormatBytes(length)}";
                assetStatus.Foreground = Accent;
                SetNotification($"Asset uploaded · {assetId.Text.Trim()}", Accent);
            }
            catch (Exception exception)
            {
                assetStatus.Text = FriendlyError(exception);
                assetStatus.Foreground = Danger;
                SetNotification(FriendlyError(exception), Danger);
            }
            finally
            {
                assetUpload.IsEnabled = true;
            }
        };
    }

    private async Task ShowWorkflowRuntimeAsync()
    {
        if (!IsAdmin)
            return;

        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Workflow runtime", "Create state-machine instances, apply validated transitions, and terminate obsolete runtime state."));
        var loading = Body("Loading workflow runtime...");
        page.Children.Add(loading);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        try
        {
            var configurations = await http.GetFromJsonAsync<JsonElement[]>("/api/state-machines/configurations") ?? [];
            using var instancesResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/state-machines");
            var instances = await instancesResponse.Content.ReadFromJsonAsync<JsonElement[]>() ?? [];
            page.Children.Remove(loading);

            var forms = new Grid { ColumnSpacing = 14 };
            forms.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            forms.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var createPanel = new StackPanel { Spacing = 10 };
            createPanel.Children.Add(Eyebrow("CREATE INSTANCE"));
            createPanel.Children.Add(Body("Start a workflow using one of the installed configurations and an optional JSON context object."));
            var configurationPicker = new ComboBox
            {
                Header = "Configuration",
                ItemsSource = configurations.Select(item => $"{JsonText(item, "name", "id")} · {JsonText(item, "id")}").ToList(),
                SelectedIndex = configurations.Length > 0 ? 0 : -1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                FontFamily = AppFont
            };
            var initialContext = new TextBox
            {
                Header = "Initial context (JSON)",
                Text = "{}",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 90,
                FontFamily = MonoFont
            };
            var create = IconButton("Create workflow instance", Symbol.Add);
            create.HorizontalAlignment = HorizontalAlignment.Left;
            createPanel.Children.Add(configurationPicker);
            createPanel.Children.Add(initialContext);
            createPanel.Children.Add(create);
            var createCard = Card(accented: true);
            createCard.Child = createPanel;
            forms.Children.Add(createCard);

            var transitionPanel = new StackPanel { Spacing = 10 };
            transitionPanel.Children.Add(Eyebrow("APPLY TRANSITION"));
            transitionPanel.Children.Add(Body("Select a runtime instance below, then submit an event and optional JSON payload."));
            var eventName = new TextBox { Header = "Event", PlaceholderText = "approve", FontFamily = AppFont };
            var eventData = new TextBox
            {
                Header = "Event data (JSON)",
                Text = "{}",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 90,
                FontFamily = MonoFont
            };
            var apply = IconButton("Apply transition", Symbol.Go);
            apply.HorizontalAlignment = HorizontalAlignment.Left;
            transitionPanel.Children.Add(eventName);
            transitionPanel.Children.Add(eventData);
            transitionPanel.Children.Add(apply);
            var transitionCard = Card();
            transitionCard.Child = transitionPanel;
            Grid.SetColumn(transitionCard, 1);
            forms.Children.Add(transitionCard);
            page.Children.Add(forms);

            var runtimePanel = new StackPanel { Spacing = 10 };
            runtimePanel.Children.Add(Eyebrow("ACTIVE INSTANCES"));
            var instanceList = new ListView { MinHeight = 250, SelectionMode = ListViewSelectionMode.Single };
            foreach (var instance in instances)
            {
                var id = JsonText(instance, "id");
                var configuration = JsonText(instance, "configurationId");
                var currentState = JsonText(instance, "currentState");
                var finished = instance.TryGetProperty("finished", out var isFinished) && isFinished.GetBoolean();
                instanceList.Items.Add(new ListViewItem
                {
                    Tag = id,
                    Content = $"{id[..Math.Min(10, id.Length)]}  ·  {configuration}  ·  {currentState}  ·  {(finished ? "FINISHED" : "ACTIVE")}"
                });
            }
            if (instances.Length == 0)
                instanceList.Items.Add("No workflow instances are running.");
            runtimePanel.Children.Add(instanceList);
            var runtimeButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var refresh = IconButton("Refresh", Symbol.Refresh);
            var terminate = IconButton("Terminate selected", Symbol.Delete);
            runtimeButtons.Children.Add(refresh);
            runtimeButtons.Children.Add(terminate);
            runtimePanel.Children.Add(runtimeButtons);
            var runtimeCard = Card();
            runtimeCard.Child = runtimePanel;
            page.Children.Add(runtimeCard);

            create.Click += async (_, _) =>
            {
                if (configurationPicker.SelectedIndex < 0 || configurationPicker.SelectedIndex >= configurations.Length)
                {
                    SetNotification("Install a workflow configuration first.", Warning);
                    return;
                }
                try
                {
                    var data = ParseJsonObject(initialContext.Text);
                    var configurationId = JsonText(configurations[configurationPicker.SelectedIndex], "id");
                    using var request = AuthorizedRequest(HttpMethod.Post, "/api/state-machines");
                    request.Content = JsonContent.Create(new { configurationId, data });
                    using var response = await http.SendAsync(request);
                    await EnsureSuccessAsync(response);
                    SetNotification($"Workflow instance created · {configurationId}", Accent);
                    await ShowWorkflowRuntimeAsync();
                }
                catch (Exception exception)
                {
                    SetNotification(FriendlyError(exception), Danger);
                }
            };

            apply.Click += async (_, _) =>
            {
                if (instanceList.SelectedItem is not ListViewItem { Tag: string instanceId })
                {
                    SetNotification("Select a workflow instance first.", Warning);
                    return;
                }
                if (string.IsNullOrWhiteSpace(eventName.Text))
                {
                    SetNotification("Enter a transition event.", Warning);
                    return;
                }
                try
                {
                    var data = ParseJsonObject(eventData.Text);
                    using var request = AuthorizedRequest(HttpMethod.Post, $"/api/state-machines/{Uri.EscapeDataString(instanceId)}/transitions");
                    request.Content = JsonContent.Create(new { @event = eventName.Text.Trim(), data });
                    using var response = await http.SendAsync(request);
                    await EnsureSuccessAsync(response);
                    SetNotification($"Transition applied · {eventName.Text.Trim()}", Accent);
                    await ShowWorkflowRuntimeAsync();
                }
                catch (Exception exception)
                {
                    SetNotification(FriendlyError(exception), Danger);
                }
            };

            refresh.Click += async (_, _) => await ShowWorkflowRuntimeAsync();
            terminate.Click += async (_, _) =>
            {
                if (instanceList.SelectedItem is not ListViewItem { Tag: string instanceId })
                {
                    SetNotification("Select a workflow instance first.", Warning);
                    return;
                }
                if (!await ConfirmAsync("Terminate workflow?", $"Remove workflow instance '{instanceId}' from runtime memory?"))
                    return;
                try
                {
                    using var response = await SendAuthorizedAsync(HttpMethod.Delete, "/api/state-machines/" + Uri.EscapeDataString(instanceId));
                    SetNotification("Workflow instance terminated.", Accent);
                    await ShowWorkflowRuntimeAsync();
                }
                catch (Exception exception)
                {
                    SetNotification(FriendlyError(exception), Danger);
                }
            };
        }
        catch (Exception exception)
        {
            loading.Text = FriendlyError(exception);
            loading.Foreground = Danger;
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private static JsonElement ParseJsonObject(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("JSON data must be an object.");
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Enter valid JSON data.", exception);
        }
    }

    private async Task RunJsonUploadAsync(string endpoint, string kind, Button button, TextBlock status)
    {
        var file = await PickFileAsync(".json");
        if (file is null) return;
        var length = new FileInfo(file.Path).Length;
        if (length > 5L * 1024 * 1024)
        {
            status.Text = "The selected JSON file exceeds the 5 MB management limit.";
            status.Foreground = Danger;
            return;
        }
        button.IsEnabled = false;
        status.Text = $"Validating and uploading {file.Name}...";
        status.Foreground = Muted;
        try
        {
            await UploadFileAsync(endpoint, file);
            status.Text = $"Installed {file.Name} · {FormatBytes(length)}";
            status.Foreground = Accent;
            SetNotification($"{char.ToUpperInvariant(kind[0]) + kind[1..]} uploaded successfully.", Accent);
        }
        catch (Exception exception)
        {
            status.Text = FriendlyError(exception);
            status.Foreground = Danger;
            SetNotification(FriendlyError(exception), Danger);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async Task<StorageFile?> PickFileAsync(string extension)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(extension);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        return await picker.PickSingleFileAsync();
    }

    private async Task<bool> SaveAuthorizedResponseAsync(string endpoint, string suggestedName, string extension)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Get, endpoint);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var contentName = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName;
        var fileName = string.IsNullOrWhiteSpace(contentName) ? suggestedName : contentName.Trim('"');
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(fileName)
        };
        picker.FileTypeChoices.Add(extension.TrimStart('.').ToUpperInvariant() + " file", new List<string> { extension });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return false;
        await File.WriteAllBytesAsync(file.Path, bytes);
        return true;
    }

    private async Task<bool> SaveTextFileAsync(string text, string suggestedName, string extension)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName)
        };
        picker.FileTypeChoices.Add(extension.TrimStart('.').ToUpperInvariant() + " file", new List<string> { extension });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return false;
        await File.WriteAllTextAsync(file.Path, text, Encoding.UTF8);
        SetNotification($"Exported {file.Name}", Accent);
        return true;
    }

    private async Task UploadFileAsync(string endpoint, StorageFile file, IReadOnlyDictionary<string, string>? fields = null)
    {
        using var form = new MultipartFormDataContent();
        if (fields is not null)
            foreach (var field in fields)
                form.Add(new StringContent(field.Value), field.Key);
        var bytes = await File.ReadAllBytesAsync(file.Path);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        form.Add(fileContent, "file", file.Name);
        using var request = AuthorizedRequest(HttpMethod.Post, endpoint);
        request.Content = form;
        using var response = await http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    private async Task DeleteManagedItemAsync(ListView list, JsonElement[] items, string endpointPrefix, string kind, Func<Task> refresh)
    {
        if (list.SelectedIndex < 0 || list.SelectedIndex >= items.Length)
        {
            SetNotification($"Select a {kind} first.", Warning);
            return;
        }
        var id = JsonText(items[list.SelectedIndex], "id");
        if (!await ConfirmAsync($"Remove {kind}?", $"Remove '{id}' from the live server? This cannot be undone."))
            return;
        try
        {
            using var response = await SendAuthorizedAsync(HttpMethod.Delete, endpointPrefix + Uri.EscapeDataString(id));
            SetNotification($"Removed {kind} · {id}", Accent);
            await refresh();
        }
        catch (Exception exception)
        {
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private async Task RemoveLiveOpsItemAsync(ListView list, IReadOnlyList<string> ids, string endpointPrefix, string kind, Func<Task> refresh)
    {
        if (list.SelectedIndex < 0 || list.SelectedIndex >= ids.Count)
        {
            SetNotification($"Select a {kind} first.", Warning);
            return;
        }
        var id = ids[list.SelectedIndex];
        if (!await ConfirmAsync($"Remove {kind}?", $"Remove '{id}' from live operations? This cannot be undone."))
            return;
        try
        {
            using var response = await SendAuthorizedAsync(HttpMethod.Delete, endpointPrefix + Uri.EscapeDataString(id));
            SetNotification($"Removed {kind} · {id}", Accent);
            await refresh();
        }
        catch (Exception exception)
        {
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = content.XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowGamesAsync()
    {
        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Games", "Create a bot match and inspect current in-memory sessions."));

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var create = IconButton("Create secure bot game", Symbol.Add);
        var refresh = IconButton("Refresh", Symbol.Refresh);
        toolbar.Children.Add(create);
        toolbar.Children.Add(refresh);
        Button? terminate = null;
        if (IsAdmin)
        {
            terminate = IconButton("Terminate selected", Symbol.Delete);
            toolbar.Children.Add(terminate);
        }
        page.Children.Add(toolbar);

        var list = new ListView { MinHeight = 340, SelectionMode = IsAdmin ? ListViewSelectionMode.Single : ListViewSelectionMode.None };
        var card = Card();
        card.Child = list;
        page.Children.Add(card);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        create.Click += async (_, _) => await CreateGameAsync(list);
        refresh.Click += async (_, _) => await RefreshGamesAsync(list);
        if (terminate is not null)
            terminate.Click += async (_, _) => await TerminateGameAsync(list);
        await RefreshGamesAsync(list);
    }

    private async Task CreateGameAsync(ListView list)
    {
        try
        {
            using var request = AuthorizedRequest(HttpMethod.Post, "/api/games");
            request.Content = JsonContent.Create(new { rulePackId = "mtg-like", playerIds = new[] { userId }, addAi = true });
            using var response = await http.SendAsync(request);
            await EnsureSuccessAsync(response);
            SetNotification("Secure bot game created.", Accent);
            await RefreshGamesAsync(list);
        }
        catch (Exception exception)
        {
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private async Task RefreshGamesAsync(ListView list)
    {
        try
        {
            using var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/games");
            var results = await response.Content.ReadFromJsonAsync<JsonElement[]>() ?? [];
            list.Items.Clear();
            foreach (var game in results)
            {
                var id = game.GetProperty("id").GetString() ?? "unknown";
                var players = game.GetProperty("players").GetArrayLength();
                var phase = game.TryGetProperty("phase", out var phaseValue) ? phaseValue.GetString() ?? "active" : "active";
                list.Items.Add(new ListViewItem
                {
                    Tag = id,
                    Content = $"Game {id[..Math.Min(8, id.Length)]}    ·    {players} players    ·    {phase}"
                });
            }
            if (results.Length == 0)
                list.Items.Add("No games yet. Create a secure bot game to get started.");
            SetNotification($"Games refreshed · {results.Length} found", Accent);
        }
        catch (Exception exception)
        {
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private async Task TerminateGameAsync(ListView list)
    {
        if (list.SelectedItem is not ListViewItem { Tag: string id })
        {
            SetNotification("Select a game first.", Warning);
            return;
        }
        if (!await ConfirmAsync("Terminate game?", $"Terminate game '{id}' and remove its in-memory state?"))
            return;
        try
        {
            using var response = await SendAuthorizedAsync(HttpMethod.Delete, "/api/games/" + Uri.EscapeDataString(id));
            SetNotification($"Game terminated · {id[..Math.Min(8, id.Length)]}", Accent);
            await RefreshGamesAsync(list);
        }
        catch (Exception exception)
        {
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private async Task ShowAccountsAsync()
    {
        if (!CanModerate)
            return;

        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Accounts", "Review roles and disable access without leaving the desktop console."));

        var summaryText = Body("Loading account summary...");
        page.Children.Add(summaryText);
        var refresh = IconButton("Refresh accounts", Symbol.Refresh);
        refresh.HorizontalAlignment = HorizontalAlignment.Left;
        refresh.Click += async (_, _) => await ShowAccountsAsync();
        page.Children.Add(refresh);

        var users = new ListView { Height = 300, SelectionMode = ListViewSelectionMode.Single };
        var listCard = Card();
        listCard.Child = users;
        page.Children.Add(listCard);

        var rolePicker = new ComboBox { Header = "Role", ItemsSource = new[] { "user", "moderator", "admin" }, Width = 200 };
        rolePicker.IsEnabled = IsAdmin;
        var disable = new CheckBox { Content = "Account disabled", VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) };
        var save = IconButton("Save account", Symbol.Save);
        save.VerticalAlignment = VerticalAlignment.Bottom;
        var editor = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        editor.Children.Add(rolePicker);
        editor.Children.Add(disable);
        editor.Children.Add(save);
        page.Children.Add(editor);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        users.SelectionChanged += (_, _) =>
        {
            if (users.SelectedIndex < 0 || users.SelectedIndex >= adminUsers.Count)
                return;
            var selected = adminUsers[users.SelectedIndex];
            rolePicker.SelectedItem = selected.Role;
            disable.IsChecked = selected.Disabled;
        };

        save.Click += async (_, _) =>
        {
            if (users.SelectedIndex < 0)
            {
                SetNotification("Select an account first.", Warning);
                return;
            }

            var selected = adminUsers[users.SelectedIndex];
            try
            {
                using var request = AuthorizedRequest(HttpMethod.Patch, $"/api/admin/users/{selected.Id}");
                request.Content = JsonContent.Create(new { role = IsAdmin ? rolePicker.SelectedItem?.ToString() : null, disabled = disable.IsChecked == true });
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                SetNotification($"Updated {selected.Handle}.", Accent);
                await ShowAccountsAsync();
            }
            catch (Exception exception)
            {
                SetNotification(FriendlyError(exception), Danger);
            }
        };

        try
        {
            using var summaryResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/summary");
            var summary = await summaryResponse.Content.ReadFromJsonAsync<AdminSummaryRow>();
            summaryText.Text = summary is null
                ? "Account summary unavailable."
                : $"{summary.Users} total accounts · {summary.DisabledUsers} disabled";

            using var usersResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/users");
            adminUsers.Clear();
            adminUsers.AddRange(await usersResponse.Content.ReadFromJsonAsync<List<AdminUserRow>>() ?? []);
            users.ItemsSource = adminUsers.Select(user => $"{user.Handle,-24}  {user.Role,-10}  {(user.Disabled ? "DISABLED" : "ACTIVE")}").ToList();
        }
        catch (Exception exception)
        {
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private async Task ShowLogsAsync()
    {
        if (!IsAdmin)
            return;

        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Audit logs", "The 500 most recent request, error, and moderation records."));
        var refresh = IconButton("Refresh logs", Symbol.Refresh);
        refresh.HorizontalAlignment = HorizontalAlignment.Left;
        refresh.Click += async (_, _) => await ShowLogsAsync();
        page.Children.Add(refresh);
        var logs = new ListView { MinHeight = 480, SelectionMode = ListViewSelectionMode.None };
        var card = Card();
        card.Child = logs;
        page.Children.Add(card);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        try
        {
            using var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/logs?limit=500");
            var entries = await response.Content.ReadFromJsonAsync<List<AuditLogRow>>() ?? [];
            logs.ItemsSource = entries.Select(entry =>
                $"{entry.At.ToLocalTime():yyyy-MM-dd HH:mm:ss}  [{entry.Level}]  {entry.Category}  {entry.Message}  {entry.RequestPath}  {entry.StatusCode}").ToList();
            SetNotification($"Audit log refreshed · {entries.Count} entries", Accent);
        }
        catch (Exception exception)
        {
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private async Task ShowMatchmakingAsync()
    {
        if (!IsAdmin)
            return;

        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Matchmaking control", "Configure queues, monitor waiting players, expand rating bands, backfill with bots, create matches, and clear stalled queues."));

        var queueRows = new List<MatchmakingQueueStatusRow>();
        var queueList = new ListView { MinHeight = 220, SelectionMode = ListViewSelectionMode.Single };
        var entryList = new ListView { MinHeight = 150, MaxHeight = 260, SelectionMode = ListViewSelectionMode.None };
        var queueStatus = Body("Loading matchmaking queues...");

        var layout = new Grid { ColumnSpacing = 14, RowSpacing = 14 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        var editor = new StackPanel { Spacing = 10 };
        editor.Children.Add(Eyebrow("QUEUE CONFIGURATION"));
        var queueId = new TextBox { Header = "Queue ID", PlaceholderText = "ranked", FontFamily = AppFont };
        var queueName = new TextBox { Header = "Display name", PlaceholderText = "Ranked Duel", FontFamily = AppFont };
        var mode = new TextBox { Header = "Mode", PlaceholderText = "ranked", FontFamily = AppFont };
        var minPlayers = new TextBox { Header = "Minimum players", Text = "2", FontFamily = AppFont };
        var maxPlayers = new TextBox { Header = "Maximum players", Text = "2", FontFamily = AppFont };
        var initialRange = new TextBox { Header = "Initial rating range", Text = "100", FontFamily = AppFont };
        var expansion = new TextBox { Header = "Rating expansion / second", Text = "10", FontFamily = AppFont };
        var maxWait = new TextBox { Header = "Bot backfill wait (seconds)", Text = "90", FontFamily = AppFont };
        var botBackfill = new CheckBox { Content = "Allow AI backfill after maximum wait", IsChecked = true };
        var enabled = new CheckBox { Content = "Queue accepts players", IsChecked = true };
        foreach (var control in new Control[] { queueId, queueName, mode, minPlayers, maxPlayers, initialRange, expansion, maxWait, botBackfill, enabled }) editor.Children.Add(control);
        var editorActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var newQueue = IconButton("New queue", Symbol.Add);
        var saveQueue = IconButton("Save queue", Symbol.Save);
        editorActions.Children.Add(newQueue);
        editorActions.Children.Add(saveQueue);
        editor.Children.Add(editorActions);
        var editorCard = Card(accented: true);
        editorCard.Child = editor;
        layout.Children.Add(editorCard);

        var inventory = new StackPanel { Spacing = 10 };
        inventory.Children.Add(Eyebrow("LIVE QUEUES"));
        inventory.Children.Add(Body("Select a queue to edit it or inspect its waiting parties."));
        inventory.Children.Add(queueList);
        var queueActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var runQueue = IconButton("Run now", Symbol.Play);
        var clearQueue = IconButton("Flush", Symbol.Delete);
        var removeQueue = IconButton("Remove", Symbol.Delete);
        var refresh = IconButton("Refresh", Symbol.Refresh);
        queueActions.Children.Add(runQueue);
        queueActions.Children.Add(clearQueue);
        queueActions.Children.Add(removeQueue);
        queueActions.Children.Add(refresh);
        inventory.Children.Add(queueActions);
        inventory.Children.Add(queueStatus);
        var inventoryCard = Card();
        inventoryCard.Child = inventory;
        Grid.SetColumn(inventoryCard, 1);
        layout.Children.Add(inventoryCard);
        page.Children.Add(layout);

        var entriesPanel = new StackPanel { Spacing = 10 };
        entriesPanel.Children.Add(Eyebrow("WAITING PLAYERS & PARTIES"));
        entriesPanel.Children.Add(Body("Shows live player, rating, party size, region, and queue wait information."));
        entriesPanel.Children.Add(entryList);
        var entriesCard = Card();
        entriesCard.Child = entriesPanel;
        page.Children.Add(entriesCard);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        async Task LoadEntriesAsync(string id)
        {
            try
            {
                using var response = await SendAuthorizedAsync(HttpMethod.Get, $"/api/admin/matchmaking/queues/{Uri.EscapeDataString(id)}/entries");
                var entries = await response.Content.ReadFromJsonAsync<List<MatchmakingQueueEntryRow>>() ?? [];
                entryList.ItemsSource = entries.Select(entry => $"{entry.UserId,-32}  rating {entry.Rating,-5}  party {entry.PartySize}  {entry.Region,-10}  waiting {(int)(DateTimeOffset.Now - entry.JoinedAt).TotalSeconds}s").ToList();
                if (entries.Count == 0) entryList.Items.Add("No players are currently waiting in this queue.");
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        }

        queueList.SelectionChanged += async (_, _) =>
        {
            if (queueList.SelectedIndex < 0 || queueList.SelectedIndex >= queueRows.Count) return;
            var selected = queueRows[queueList.SelectedIndex].Queue;
            queueId.Text = selected.Id;
            queueName.Text = selected.Name;
            mode.Text = selected.Mode;
            minPlayers.Text = selected.MinPlayers.ToString();
            maxPlayers.Text = selected.MaxPlayers.ToString();
            initialRange.Text = selected.InitialRatingRange.ToString();
            expansion.Text = selected.RatingExpansionPerSecond.ToString();
            maxWait.Text = selected.MaxWaitSeconds.ToString();
            botBackfill.IsChecked = selected.BotBackfill;
            enabled.IsChecked = selected.Enabled;
            await LoadEntriesAsync(selected.Id);
        };
        newQueue.Click += (_, _) =>
        {
            queueList.SelectedIndex = -1;
            queueId.Text = string.Empty;
            queueName.Text = string.Empty;
            mode.Text = string.Empty;
            minPlayers.Text = "2";
            maxPlayers.Text = "2";
            initialRange.Text = "100";
            expansion.Text = "10";
            maxWait.Text = "90";
            botBackfill.IsChecked = true;
            enabled.IsChecked = true;
        };
        saveQueue.Click += async (_, _) =>
        {
            if (!int.TryParse(minPlayers.Text, out var minimum) || !int.TryParse(maxPlayers.Text, out var maximum) ||
                !int.TryParse(initialRange.Text, out var ratingRange) || !int.TryParse(expansion.Text, out var ratingExpansion) ||
                !int.TryParse(maxWait.Text, out var waitSeconds))
            {
                SetNotification("Enter valid matchmaking limits and rating values.", Warning);
                return;
            }
            try
            {
                var id = queueId.Text.Trim();
                using var request = AuthorizedRequest(HttpMethod.Put, "/api/admin/matchmaking/queues/" + Uri.EscapeDataString(id));
                request.Content = JsonContent.Create(new { id, name = queueName.Text.Trim(), mode = mode.Text.Trim(), minPlayers = minimum, maxPlayers = maximum, initialRatingRange = ratingRange, ratingExpansionPerSecond = ratingExpansion, maxWaitSeconds = waitSeconds, botBackfill = botBackfill.IsChecked == true, enabled = enabled.IsChecked == true });
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                SetNotification($"Matchmaking queue saved · {id}", Accent);
                await ShowMatchmakingAsync();
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        runQueue.Click += async (_, _) =>
        {
            if (queueList.SelectedIndex < 0 || queueList.SelectedIndex >= queueRows.Count) { SetNotification("Select a matchmaking queue first.", Warning); return; }
            var selected = queueRows[queueList.SelectedIndex];
            try
            {
                using var response = await SendAuthorizedAsync(HttpMethod.Post, $"/api/admin/matchmaking/queues/{Uri.EscapeDataString(selected.Queue.Id)}/run");
                var result = await response.Content.ReadFromJsonAsync<MatchmakingRunResultRow>();
                SetNotification(result is null ? "Matchmaking pass completed." : $"Created {result.MatchesCreated} matches · {result.PlayersMatched} players · {result.BotsAdded} bots", Accent);
                await ShowMatchmakingAsync();
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        clearQueue.Click += async (_, _) =>
        {
            if (queueList.SelectedIndex < 0 || queueList.SelectedIndex >= queueRows.Count) { SetNotification("Select a matchmaking queue first.", Warning); return; }
            var selected = queueRows[queueList.SelectedIndex];
            if (!await ConfirmAsync("Flush matchmaking queue?", $"Remove every waiting party from '{selected.Queue.Name}'?")) return;
            try
            {
                using var response = await SendAuthorizedAsync(HttpMethod.Delete, $"/api/admin/matchmaking/queues/{Uri.EscapeDataString(selected.Queue.Id)}/entries");
                SetNotification($"Queue flushed · {selected.Queue.Name}", Accent);
                await ShowMatchmakingAsync();
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        removeQueue.Click += async (_, _) =>
        {
            if (queueList.SelectedIndex < 0 || queueList.SelectedIndex >= queueRows.Count) { SetNotification("Select a matchmaking queue first.", Warning); return; }
            var selected = queueRows[queueList.SelectedIndex];
            if (!await ConfirmAsync("Remove matchmaking queue?", $"Remove '{selected.Queue.Name}' and clear its waiting players?")) return;
            try
            {
                using var response = await SendAuthorizedAsync(HttpMethod.Delete, $"/api/admin/matchmaking/queues/{Uri.EscapeDataString(selected.Queue.Id)}");
                SetNotification($"Queue removed · {selected.Queue.Name}", Accent);
                await ShowMatchmakingAsync();
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        refresh.Click += async (_, _) => await ShowMatchmakingAsync();

        try
        {
            using var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/matchmaking/queues");
            queueRows.AddRange(await response.Content.ReadFromJsonAsync<List<MatchmakingQueueStatusRow>>() ?? []);
            queueList.ItemsSource = queueRows.Select(status => $"{status.Queue.Name,-22}  {status.WaitingPlayers,3} players  {status.WaitingParties,3} parties  {status.LongestWaitSeconds,4}s  {(status.Queue.Enabled ? "OPEN" : "PAUSED")}").ToList();
            if (queueRows.Count > 0) queueList.SelectedIndex = 0;
            else queueList.Items.Add("No matchmaking queues configured.");
            queueStatus.Text = $"{queueRows.Count} queues · {queueRows.Count(row => row.Queue.Enabled)} open · {queueRows.Sum(row => row.WaitingPlayers)} waiting players";
            queueStatus.Foreground = Accent;
            SetNotification($"Matchmaking loaded · {queueRows.Count} queues", Accent);
        }
        catch (Exception exception)
        {
            queueStatus.Text = FriendlyError(exception);
            queueStatus.Foreground = Danger;
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private async Task ShowBotsAsync()
    {
        if (!IsAdmin)
            return;

        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("AI bot manager", "Configure matchmaking bots, control their availability, and run deterministic readiness simulations."));

        var form = new StackPanel { Spacing = 10 };
        form.Children.Add(Eyebrow("BOT PROFILE"));
        var id = new TextBox { Header = "Bot ID", PlaceholderText = "arcane-guide", FontFamily = AppFont };
        var name = new TextBox { Header = "Display name", PlaceholderText = "Arcane Guide", FontFamily = AppFont };
        var personality = new TextBox { Header = "Personality", PlaceholderText = "Patient control strategist", FontFamily = AppFont };
        var difficulty = new ComboBox { Header = "Difficulty", ItemsSource = new[] { "beginner", "intermediate", "advanced", "expert" }, SelectedIndex = 1, FontFamily = AppFont };
        var modelVersion = new TextBox { Header = "Model version", Text = "rules-v1", FontFamily = AppFont };
        var pool = new TextBox { Header = "Matchmaking pool", Text = "ranked", FontFamily = AppFont };
        var limit = new TextBox { Header = "Daily match limit", Text = "1000", FontFamily = AppFont };
        var enabled = new CheckBox { Content = "Bot available for matchmaking", IsChecked = true };
        foreach (var control in new Control[] { id, name, personality, difficulty, modelVersion, pool, limit, enabled }) form.Children.Add(control);
        var save = IconButton("Save bot profile", Symbol.Save);
        form.Children.Add(save);
        var formCard = Card(accented: true);
        formCard.Child = form;
        page.Children.Add(formCard);

        var profiles = new List<BotProfileRow>();
        var list = new ListView { MinHeight = 250, SelectionMode = ListViewSelectionMode.Single };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var simulate = IconButton("Run 500-match simulation", Symbol.Play);
        var remove = IconButton("Remove selected", Symbol.Delete);
        var refresh = IconButton("Refresh", Symbol.Refresh);
        actions.Children.Add(simulate);
        actions.Children.Add(remove);
        actions.Children.Add(refresh);
        var inventory = new StackPanel { Spacing = 10 };
        inventory.Children.Add(Eyebrow("BOT FLEET"));
        inventory.Children.Add(Body("Select a profile to edit it, test its difficulty baseline, or remove it from the live bot fleet."));
        inventory.Children.Add(list);
        inventory.Children.Add(actions);
        var inventoryCard = Card();
        inventoryCard.Child = inventory;
        page.Children.Add(inventoryCard);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= profiles.Count) return;
            var selected = profiles[list.SelectedIndex];
            id.Text = selected.Id;
            name.Text = selected.Name;
            personality.Text = selected.Personality;
            difficulty.SelectedItem = selected.Difficulty;
            modelVersion.Text = selected.ModelVersion;
            pool.Text = selected.MatchmakingPool;
            limit.Text = selected.DailyMatchLimit.ToString();
            enabled.IsChecked = selected.Enabled;
        };
        save.Click += async (_, _) =>
        {
            if (!int.TryParse(limit.Text, out var dailyLimit))
            {
                SetNotification("Enter a valid daily match limit.", Warning);
                return;
            }
            try
            {
                var botId = id.Text.Trim();
                using var request = AuthorizedRequest(HttpMethod.Put, "/api/admin/bots/" + Uri.EscapeDataString(botId));
                request.Content = JsonContent.Create(new
                {
                    id = botId,
                    name = name.Text.Trim(),
                    personality = personality.Text.Trim(),
                    difficulty = difficulty.SelectedItem?.ToString() ?? "intermediate",
                    modelVersion = modelVersion.Text.Trim(),
                    matchmakingPool = pool.Text.Trim(),
                    enabled = enabled.IsChecked == true,
                    dailyMatchLimit = dailyLimit
                });
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                SetNotification($"Bot profile saved · {botId}", Accent);
                await ShowBotsAsync();
            }
            catch (Exception exception)
            {
                SetNotification(FriendlyError(exception), Danger);
            }
        };
        simulate.Click += async (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= profiles.Count)
            {
                SetNotification("Select a bot profile first.", Warning);
                return;
            }
            try
            {
                var selected = profiles[list.SelectedIndex];
                using var request = AuthorizedRequest(HttpMethod.Post, "/api/admin/bots/" + Uri.EscapeDataString(selected.Id) + "/simulations");
                request.Content = JsonContent.Create(new { matches = 500 });
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                var run = await response.Content.ReadFromJsonAsync<BotSimulationRunRow>();
                SetNotification(run is null
                    ? "Bot simulation completed."
                    : $"Simulation complete · {run.EstimatedWinRate:P0} expected win rate · {run.AverageTurns} average turns", Accent);
            }
            catch (Exception exception)
            {
                SetNotification(FriendlyError(exception), Danger);
            }
        };
        remove.Click += async (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= profiles.Count)
            {
                SetNotification("Select a bot profile first.", Warning);
                return;
            }
            var selected = profiles[list.SelectedIndex];
            if (!await ConfirmAsync("Remove bot profile?", $"Remove '{selected.Name}' from the bot fleet?")) return;
            try
            {
                using var response = await SendAuthorizedAsync(HttpMethod.Delete, "/api/admin/bots/" + Uri.EscapeDataString(selected.Id));
                SetNotification($"Removed bot profile · {selected.Name}", Accent);
                await ShowBotsAsync();
            }
            catch (Exception exception)
            {
                SetNotification(FriendlyError(exception), Danger);
            }
        };
        refresh.Click += async (_, _) => await ShowBotsAsync();

        try
        {
            using var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/bots");
            profiles.AddRange(await response.Content.ReadFromJsonAsync<List<BotProfileRow>>() ?? []);
            list.ItemsSource = profiles.Select(bot => $"{bot.Name,-24}  {bot.Difficulty,-14}  {bot.MatchmakingPool,-12}  {(bot.Enabled ? "ENABLED" : "PAUSED")}").ToList();
            if (profiles.Count == 0) list.Items.Add("No bot profiles configured. Create the first profile above.");
        }
        catch (Exception exception)
        {
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private async Task ShowEarlyWarningsAsync()
    {
        if (!IsAdmin)
            return;

        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Early warning", "Automated operational signals highlight configuration gaps and potential LiveOps, economy, moderation, and bot-fleet risks."));
        var refresh = IconButton("Refresh signals", Symbol.Refresh);
        refresh.HorizontalAlignment = HorizontalAlignment.Left;
        page.Children.Add(refresh);
        var summary = Body("Evaluating operational signals...");
        page.Children.Add(summary);
        var alerts = new ListView { MinHeight = 360, SelectionMode = ListViewSelectionMode.None };
        var alertCard = Card();
        alertCard.Child = alerts;
        page.Children.Add(alertCard);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        refresh.Click += async (_, _) => await ShowEarlyWarningsAsync();
        try
        {
            using var summaryResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/operations/summary");
            var operations = await summaryResponse.Content.ReadFromJsonAsync<OperationsSummaryRow>();
            summary.Text = operations is null
                ? "Operational summary is unavailable."
                : $"{operations.ActiveBots} active bots · {operations.ActiveCampaigns} active campaigns · {operations.ActiveProducts} active store products · {operations.EarlyWarnings} current signals";
            using var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/early-warnings");
            var results = await response.Content.ReadFromJsonAsync<List<EarlyWarningAlertRow>>() ?? [];
            alerts.ItemsSource = results.Select(alert => $"[{alert.Severity.ToUpperInvariant()}]  {alert.Category}  ·  {alert.Title}{Environment.NewLine}{alert.Detail}").ToList();
            if (results.Count == 0) alerts.Items.Add("No active warnings. Current operational safeguards are satisfied.");
            SetNotification($"Early warning scan complete · {results.Count} signals", results.Count == 0 ? Accent : Warning);
        }
        catch (Exception exception)
        {
            summary.Text = FriendlyError(exception);
            summary.Foreground = Danger;
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private async Task ShowLiveOpsAsync()
    {
        if (!IsAdmin)
            return;

        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Events, rewards & achievements", "Plan time-boxed game operations, define controlled reward campaigns, and manage player milestones from one workspace."));

        var campaignRows = new List<LiveOpsCampaignRow>();
        var rewardRows = new List<RewardCampaignRow>();
        var achievementRows = new List<ManagedAchievementRow>();
        var grid = new Grid { ColumnSpacing = 14, RowSpacing = 14 };
        for (var index = 0; index < 3; index++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var campaignPanel = new StackPanel { Spacing = 9 };
        campaignPanel.Children.Add(Eyebrow("EVENTS, SEASONS & QUESTS"));
        var campaignId = new TextBox { Header = "Campaign ID", PlaceholderText = "winter-cup", FontFamily = AppFont };
        var campaignName = new TextBox { Header = "Name", PlaceholderText = "Winter Cup", FontFamily = AppFont };
        var campaignKind = new ComboBox { Header = "Type", ItemsSource = new[] { "event", "season", "quest", "tournament" }, SelectedIndex = 0, FontFamily = AppFont };
        var campaignDescription = new TextBox { Header = "Description", PlaceholderText = "Limited-time competitive event", TextWrapping = TextWrapping.Wrap, FontFamily = AppFont };
        var startsAt = new TextBox { Header = "Starts at (ISO 8601)", Text = DateTimeOffset.UtcNow.ToString("O"), FontFamily = MonoFont };
        var endsAt = new TextBox { Header = "Ends at (ISO 8601)", Text = DateTimeOffset.UtcNow.AddDays(7).ToString("O"), FontFamily = MonoFont };
        var campaignActive = new CheckBox { Content = "Campaign enabled", IsChecked = true };
        foreach (var control in new Control[] { campaignId, campaignName, campaignKind, campaignDescription, startsAt, endsAt, campaignActive }) campaignPanel.Children.Add(control);
        var saveCampaign = IconButton("Save campaign", Symbol.Save);
        campaignPanel.Children.Add(saveCampaign);
        var campaignList = new ListView { MinHeight = 130, MaxHeight = 200, SelectionMode = ListViewSelectionMode.Single };
        campaignPanel.Children.Add(campaignList);
        var removeCampaign = IconButton("Remove selected", Symbol.Delete);
        campaignPanel.Children.Add(removeCampaign);
        var campaignCard = Card(accented: true);
        campaignCard.Child = campaignPanel;
        grid.Children.Add(campaignCard);

        var rewardPanel = new StackPanel { Spacing = 9 };
        rewardPanel.Children.Add(Eyebrow("REWARD CAMPAIGNS"));
        var rewardId = new TextBox { Header = "Reward ID", PlaceholderText = "daily-win", FontFamily = AppFont };
        var rewardName = new TextBox { Header = "Name", PlaceholderText = "Daily victory", FontFamily = AppFont };
        var trigger = new TextBox { Header = "Trigger", Text = "match_win", FontFamily = AppFont };
        var rewardType = new TextBox { Header = "Reward type", Text = "gold", FontFamily = AppFont };
        var rewardAmount = new TextBox { Header = "Amount", Text = "100", FontFamily = AppFont };
        var distributionLimit = new TextBox { Header = "Distribution limit (optional)", PlaceholderText = "1", FontFamily = AppFont };
        var rewardActive = new CheckBox { Content = "Reward enabled", IsChecked = true };
        foreach (var control in new Control[] { rewardId, rewardName, trigger, rewardType, rewardAmount, distributionLimit, rewardActive }) rewardPanel.Children.Add(control);
        var saveReward = IconButton("Save reward", Symbol.Save);
        rewardPanel.Children.Add(saveReward);
        var rewardList = new ListView { MinHeight = 130, MaxHeight = 200, SelectionMode = ListViewSelectionMode.Single };
        rewardPanel.Children.Add(rewardList);
        var removeReward = IconButton("Remove selected", Symbol.Delete);
        rewardPanel.Children.Add(removeReward);
        var rewardCard = Card();
        rewardCard.Child = rewardPanel;
        Grid.SetColumn(rewardCard, 1);
        grid.Children.Add(rewardCard);

        var achievementPanel = new StackPanel { Spacing = 9 };
        achievementPanel.Children.Add(Eyebrow("ACHIEVEMENTS"));
        var achievementId = new TextBox { Header = "Achievement ID", PlaceholderText = "first-victory", FontFamily = AppFont };
        var achievementName = new TextBox { Header = "Name", PlaceholderText = "First victory", FontFamily = AppFont };
        var achievementDescription = new TextBox { Header = "Description", PlaceholderText = "Win your first match", TextWrapping = TextWrapping.Wrap, FontFamily = AppFont };
        var points = new TextBox { Header = "Points", Text = "10", FontFamily = AppFont };
        var achievementActive = new CheckBox { Content = "Achievement enabled", IsChecked = true };
        foreach (var control in new Control[] { achievementId, achievementName, achievementDescription, points, achievementActive }) achievementPanel.Children.Add(control);
        var saveAchievement = IconButton("Save achievement", Symbol.Save);
        achievementPanel.Children.Add(saveAchievement);
        var achievementList = new ListView { MinHeight = 130, MaxHeight = 200, SelectionMode = ListViewSelectionMode.Single };
        achievementPanel.Children.Add(achievementList);
        var removeAchievement = IconButton("Remove selected", Symbol.Delete);
        achievementPanel.Children.Add(removeAchievement);
        var achievementCard = Card();
        achievementCard.Child = achievementPanel;
        Grid.SetColumn(achievementCard, 2);
        grid.Children.Add(achievementCard);
        page.Children.Add(grid);
        var refresh = IconButton("Refresh LiveOps", Symbol.Refresh);
        refresh.HorizontalAlignment = HorizontalAlignment.Left;
        page.Children.Add(refresh);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        campaignList.SelectionChanged += (_, _) =>
        {
            if (campaignList.SelectedIndex < 0 || campaignList.SelectedIndex >= campaignRows.Count) return;
            var selected = campaignRows[campaignList.SelectedIndex];
            campaignId.Text = selected.Id;
            campaignName.Text = selected.Name;
            campaignKind.SelectedItem = selected.Kind;
            campaignDescription.Text = selected.Description;
            startsAt.Text = selected.StartsAt.ToString("O");
            endsAt.Text = selected.EndsAt.ToString("O");
            campaignActive.IsChecked = selected.Active;
        };
        rewardList.SelectionChanged += (_, _) =>
        {
            if (rewardList.SelectedIndex < 0 || rewardList.SelectedIndex >= rewardRows.Count) return;
            var selected = rewardRows[rewardList.SelectedIndex];
            rewardId.Text = selected.Id;
            rewardName.Text = selected.Name;
            trigger.Text = selected.Trigger;
            rewardType.Text = selected.RewardType;
            rewardAmount.Text = selected.Amount.ToString();
            distributionLimit.Text = selected.DistributionLimit?.ToString() ?? string.Empty;
            rewardActive.IsChecked = selected.Active;
        };
        achievementList.SelectionChanged += (_, _) =>
        {
            if (achievementList.SelectedIndex < 0 || achievementList.SelectedIndex >= achievementRows.Count) return;
            var selected = achievementRows[achievementList.SelectedIndex];
            achievementId.Text = selected.Id;
            achievementName.Text = selected.Name;
            achievementDescription.Text = selected.Description;
            points.Text = selected.Points.ToString();
            achievementActive.IsChecked = selected.Active;
        };
        saveCampaign.Click += async (_, _) =>
        {
            if (!DateTimeOffset.TryParse(startsAt.Text, out var start) || !DateTimeOffset.TryParse(endsAt.Text, out var end))
            {
                SetNotification("Use ISO 8601 dates for campaign start and end.", Warning);
                return;
            }
            try
            {
                var id = campaignId.Text.Trim();
                using var request = AuthorizedRequest(HttpMethod.Put, "/api/admin/liveops/campaigns/" + Uri.EscapeDataString(id));
                request.Content = JsonContent.Create(new { id, name = campaignName.Text.Trim(), kind = campaignKind.SelectedItem?.ToString() ?? "event", description = campaignDescription.Text.Trim(), startsAt = start, endsAt = end, active = campaignActive.IsChecked == true });
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                SetNotification($"Campaign saved · {id}", Accent);
                await ShowLiveOpsAsync();
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        saveReward.Click += async (_, _) =>
        {
            if (!int.TryParse(rewardAmount.Text, out var amount) || (!string.IsNullOrWhiteSpace(distributionLimit.Text) && !int.TryParse(distributionLimit.Text, out _)))
            {
                SetNotification("Enter valid reward amount and distribution limit values.", Warning);
                return;
            }
            try
            {
                int? limit = string.IsNullOrWhiteSpace(distributionLimit.Text) ? null : int.Parse(distributionLimit.Text);
                var id = rewardId.Text.Trim();
                using var request = AuthorizedRequest(HttpMethod.Put, "/api/admin/liveops/rewards/" + Uri.EscapeDataString(id));
                request.Content = JsonContent.Create(new { id, name = rewardName.Text.Trim(), trigger = trigger.Text.Trim(), rewardType = rewardType.Text.Trim(), amount, distributionLimit = limit, active = rewardActive.IsChecked == true });
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                SetNotification($"Reward saved · {id}", Accent);
                await ShowLiveOpsAsync();
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        saveAchievement.Click += async (_, _) =>
        {
            if (!int.TryParse(points.Text, out var achievementPoints))
            {
                SetNotification("Enter a valid achievement point value.", Warning);
                return;
            }
            try
            {
                var id = achievementId.Text.Trim();
                using var request = AuthorizedRequest(HttpMethod.Put, "/api/admin/liveops/achievements/" + Uri.EscapeDataString(id));
                request.Content = JsonContent.Create(new { id, name = achievementName.Text.Trim(), description = achievementDescription.Text.Trim(), points = achievementPoints, active = achievementActive.IsChecked == true });
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                SetNotification($"Achievement saved · {id}", Accent);
                await ShowLiveOpsAsync();
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        removeCampaign.Click += async (_, _) => await RemoveLiveOpsItemAsync(campaignList, campaignRows.Select(row => row.Id).ToList(), "/api/admin/liveops/campaigns/", "campaign", ShowLiveOpsAsync);
        removeReward.Click += async (_, _) => await RemoveLiveOpsItemAsync(rewardList, rewardRows.Select(row => row.Id).ToList(), "/api/admin/liveops/rewards/", "reward", ShowLiveOpsAsync);
        removeAchievement.Click += async (_, _) => await RemoveLiveOpsItemAsync(achievementList, achievementRows.Select(row => row.Id).ToList(), "/api/admin/liveops/achievements/", "achievement", ShowLiveOpsAsync);
        refresh.Click += async (_, _) => await ShowLiveOpsAsync();

        try
        {
            using var campaignResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/liveops/campaigns");
            campaignRows.AddRange(await campaignResponse.Content.ReadFromJsonAsync<List<LiveOpsCampaignRow>>() ?? []);
            campaignList.ItemsSource = campaignRows.Select(row => $"{row.Name} · {row.Kind} · {(row.Active ? "ENABLED" : "PAUSED")}").ToList();
            if (campaignRows.Count == 0) campaignList.Items.Add("No campaigns configured.");
            using var rewardResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/liveops/rewards");
            rewardRows.AddRange(await rewardResponse.Content.ReadFromJsonAsync<List<RewardCampaignRow>>() ?? []);
            rewardList.ItemsSource = rewardRows.Select(row => $"{row.Name} · {row.Amount:N0} {row.RewardType} · {(row.Active ? "ENABLED" : "PAUSED")}").ToList();
            if (rewardRows.Count == 0) rewardList.Items.Add("No reward campaigns configured.");
            using var achievementResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/liveops/achievements");
            achievementRows.AddRange(await achievementResponse.Content.ReadFromJsonAsync<List<ManagedAchievementRow>>() ?? []);
            achievementList.ItemsSource = achievementRows.Select(row => $"{row.Name} · {row.Points:N0} points · {(row.Active ? "ENABLED" : "PAUSED")}").ToList();
            if (achievementRows.Count == 0) achievementList.Items.Add("No achievements configured.");
            SetNotification($"LiveOps loaded · {campaignRows.Count} campaigns · {rewardRows.Count} rewards · {achievementRows.Count} achievements", Accent);
        }
        catch (Exception exception)
        {
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private async Task ShowCommerceAsync()
    {
        if (!IsAdmin)
            return;

        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Store & economy", "Maintain in-game products and the operational currency guardrails that underpin pricing and reward distribution."));
        var layout = new Grid { ColumnSpacing = 14, RowSpacing = 14 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        var productRows = new List<StoreProductRow>();
        var productPanel = new StackPanel { Spacing = 10 };
        productPanel.Children.Add(Eyebrow("MULTI-PRODUCT CATALOG"));
        productPanel.Children.Add(Body("Create any number of products, duplicate existing offers, and apply visibility or removal actions to multiple selected records."));
        var productId = new TextBox { Header = "Product ID", PlaceholderText = "starter-booster", FontFamily = AppFont };
        var productName = new TextBox { Header = "Name", PlaceholderText = "Starter booster", FontFamily = AppFont };
        var productDescription = new TextBox { Header = "Description", PlaceholderText = "A bundle for new players", TextWrapping = TextWrapping.Wrap, FontFamily = AppFont };
        var price = new TextBox { Header = "Price", Text = "100", FontFamily = AppFont };
        var currency = new TextBox { Header = "Currency", Text = "gold", FontFamily = AppFont };
        var assetId = new TextBox { Header = "Store artwork asset ID", PlaceholderText = "Optional", FontFamily = AppFont };
        var contentPackId = new TextBox { Header = "Delivered content pack ID", PlaceholderText = "Optional", FontFamily = AppFont };
        var productActive = new CheckBox { Content = "Product available in store", IsChecked = true };
        foreach (var control in new Control[] { productId, productName, productDescription, price, currency, assetId, contentPackId, productActive }) productPanel.Children.Add(control);
        var editorActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var newProduct = IconButton("New product", Symbol.Add);
        var duplicateProduct = IconButton("Duplicate selected", Symbol.Copy);
        var saveProduct = IconButton("Save product", Symbol.Save);
        editorActions.Children.Add(newProduct);
        editorActions.Children.Add(duplicateProduct);
        editorActions.Children.Add(saveProduct);
        productPanel.Children.Add(editorActions);
        productPanel.Children.Add(Eyebrow("PRODUCT INVENTORY"));
        var products = new ListView { MinHeight = 280, SelectionMode = ListViewSelectionMode.Multiple };
        productPanel.Children.Add(products);
        var productActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var toggleProducts = IconButton("Show / hide selected", Symbol.Sync);
        var removeProduct = IconButton("Remove selected products", Symbol.Delete);
        var refresh = IconButton("Refresh", Symbol.Refresh);
        productActions.Children.Add(toggleProducts);
        productActions.Children.Add(removeProduct);
        productActions.Children.Add(refresh);
        productPanel.Children.Add(productActions);
        var productCard = Card(accented: true);
        productCard.Child = productPanel;
        layout.Children.Add(productCard);

        var economyPanel = new StackPanel { Spacing = 10 };
        economyPanel.Children.Add(Eyebrow("ECONOMY GUARDRAILS"));
        economyPanel.Children.Add(Body("These values are surfaced to operations tools and archive exports. They do not retroactively alter existing player balances."));
        var softCurrency = new TextBox { Header = "Soft currency", FontFamily = AppFont };
        var premiumCurrency = new TextBox { Header = "Premium currency", FontFamily = AppFont };
        var dailyCap = new TextBox { Header = "Daily soft-currency cap", FontFamily = AppFont };
        var taxPercent = new TextBox { Header = "Store tax percent", FontFamily = AppFont };
        foreach (var control in new Control[] { softCurrency, premiumCurrency, dailyCap, taxPercent }) economyPanel.Children.Add(control);
        var saveEconomy = IconButton("Save economy", Symbol.Save);
        economyPanel.Children.Add(saveEconomy);
        var economyStatus = Body("Loading economy configuration...");
        economyPanel.Children.Add(economyStatus);
        var economyCard = Card();
        economyCard.Child = economyPanel;
        Grid.SetColumn(economyCard, 1);
        layout.Children.Add(economyCard);
        page.Children.Add(layout);
        var transactionList = new ListView { MinHeight = 180, MaxHeight = 320, SelectionMode = ListViewSelectionMode.None };
        var ledgerPanel = new StackPanel { Spacing = 10 };
        ledgerPanel.Children.Add(Eyebrow("CURRENCY TRANSACTION LEDGER"));
        ledgerPanel.Children.Add(Body("Auditable grants, refunds, corrections, and deductions applied from player management."));
        ledgerPanel.Children.Add(transactionList);
        var ledgerCard = Card();
        ledgerCard.Child = ledgerPanel;
        page.Children.Add(ledgerCard);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        products.SelectionChanged += (_, _) =>
        {
            if (products.SelectedIndex < 0 || products.SelectedIndex >= productRows.Count) return;
            var selected = productRows[products.SelectedIndex];
            productId.Text = selected.Id;
            productName.Text = selected.Name;
            productDescription.Text = selected.Description;
            price.Text = selected.Price.ToString();
            currency.Text = selected.Currency;
            assetId.Text = selected.AssetId ?? string.Empty;
            contentPackId.Text = selected.ContentPackId ?? string.Empty;
            productActive.IsChecked = selected.Active;
        };
        newProduct.Click += (_, _) =>
        {
            products.SelectedItems.Clear();
            productId.Text = string.Empty;
            productName.Text = string.Empty;
            productDescription.Text = string.Empty;
            price.Text = "100";
            currency.Text = softCurrency.Text.Length > 0 ? softCurrency.Text : "gold";
            assetId.Text = string.Empty;
            contentPackId.Text = string.Empty;
            productActive.IsChecked = true;
        };
        duplicateProduct.Click += (_, _) =>
        {
            if (products.SelectedIndex < 0 || products.SelectedIndex >= productRows.Count)
            {
                SetNotification("Select a product to duplicate.", Warning);
                return;
            }
            var selected = productRows[products.SelectedIndex];
            products.SelectedItems.Clear();
            productId.Text = selected.Id + "-copy";
            productName.Text = selected.Name + " Copy";
            productDescription.Text = selected.Description;
            price.Text = selected.Price.ToString();
            currency.Text = selected.Currency;
            assetId.Text = selected.AssetId ?? string.Empty;
            contentPackId.Text = selected.ContentPackId ?? string.Empty;
            productActive.IsChecked = false;
        };
        saveProduct.Click += async (_, _) =>
        {
            if (!decimal.TryParse(price.Text, out var productPrice))
            {
                SetNotification("Enter a valid store price.", Warning);
                return;
            }
            try
            {
                var id = productId.Text.Trim();
                using var request = AuthorizedRequest(HttpMethod.Put, "/api/admin/store/products/" + Uri.EscapeDataString(id));
                request.Content = JsonContent.Create(new { id, name = productName.Text.Trim(), description = productDescription.Text.Trim(), price = productPrice, currency = currency.Text.Trim(), assetId = assetId.Text.Trim(), contentPackId = contentPackId.Text.Trim(), active = productActive.IsChecked == true });
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                SetNotification($"Store product saved · {id}", Accent);
                await ShowCommerceAsync();
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        removeProduct.Click += async (_, _) =>
        {
            var selectedProducts = products.SelectedItems.Cast<object>()
                .Select(item => products.Items.IndexOf(item))
                .Where(index => index >= 0 && index < productRows.Count)
                .Select(index => productRows[index])
                .DistinctBy(product => product.Id)
                .ToList();
            if (selectedProducts.Count == 0)
            {
                SetNotification("Select one or more store products first.", Warning);
                return;
            }
            if (!await ConfirmAsync("Remove store products?", $"Remove {selectedProducts.Count} selected products from the in-game store?")) return;
            try
            {
                foreach (var selected in selectedProducts)
                    using (await SendAuthorizedAsync(HttpMethod.Delete, "/api/admin/store/products/" + Uri.EscapeDataString(selected.Id))) { }
                SetNotification($"Removed {selectedProducts.Count} store products", Accent);
                await ShowCommerceAsync();
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        toggleProducts.Click += async (_, _) =>
        {
            var selectedProducts = products.SelectedItems.Cast<object>()
                .Select(item => products.Items.IndexOf(item))
                .Where(index => index >= 0 && index < productRows.Count)
                .Select(index => productRows[index])
                .DistinctBy(product => product.Id)
                .ToList();
            if (selectedProducts.Count == 0)
            {
                SetNotification("Select one or more store products first.", Warning);
                return;
            }
            var makeActive = selectedProducts.Any(product => !product.Active);
            try
            {
                foreach (var selected in selectedProducts)
                {
                    using var request = AuthorizedRequest(HttpMethod.Put, "/api/admin/store/products/" + Uri.EscapeDataString(selected.Id));
                    request.Content = JsonContent.Create(new { id = selected.Id, name = selected.Name, description = selected.Description, price = selected.Price, currency = selected.Currency, assetId = selected.AssetId, contentPackId = selected.ContentPackId, active = makeActive });
                    using var response = await http.SendAsync(request);
                    await EnsureSuccessAsync(response);
                }
                SetNotification($"{selectedProducts.Count} products are now {(makeActive ? "visible" : "hidden")}", Accent);
                await ShowCommerceAsync();
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        saveEconomy.Click += async (_, _) =>
        {
            if (!int.TryParse(dailyCap.Text, out var cap) || !decimal.TryParse(taxPercent.Text, out var tax))
            {
                SetNotification("Enter valid economy limit and tax values.", Warning);
                return;
            }
            try
            {
                using var request = AuthorizedRequest(HttpMethod.Put, "/api/admin/economy");
                request.Content = JsonContent.Create(new { softCurrency = softCurrency.Text.Trim(), premiumCurrency = premiumCurrency.Text.Trim(), dailySoftCurrencyCap = cap, storeTaxPercent = tax });
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                SetNotification("Economy configuration saved.", Accent);
                await ShowCommerceAsync();
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        refresh.Click += async (_, _) => await ShowCommerceAsync();

        try
        {
            using var productsResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/store/products");
            productRows.AddRange(await productsResponse.Content.ReadFromJsonAsync<List<StoreProductRow>>() ?? []);
            products.ItemsSource = productRows.Select(product => $"{product.Name,-24}  {product.Price:N2} {product.Currency,-8}  {(product.Active ? "ACTIVE" : "HIDDEN")}").ToList();
            if (productRows.Count == 0) products.Items.Add("No store products configured.");
            using var economyResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/economy");
            var economy = await economyResponse.Content.ReadFromJsonAsync<EconomyConfigurationRow>();
            if (economy is not null)
            {
                softCurrency.Text = economy.SoftCurrency;
                premiumCurrency.Text = economy.PremiumCurrency;
                dailyCap.Text = economy.DailySoftCurrencyCap.ToString();
                taxPercent.Text = economy.StoreTaxPercent.ToString();
                economyStatus.Text = $"Last updated {economy.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}.";
            }
            using var transactionsResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/economy/transactions");
            var transactions = await transactionsResponse.Content.ReadFromJsonAsync<List<CurrencyTransactionRow>>() ?? [];
            transactionList.ItemsSource = transactions.Select(transaction => $"{transaction.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}  ·  {transaction.UserId[..Math.Min(10, transaction.UserId.Length)]}  ·  {transaction.Amount.ToString("+0;-0;0"),10} {transaction.Currency,-8}  ·  balance {transaction.BalanceAfter,-10}  ·  {transaction.Reason}").ToList();
            if (transactions.Count == 0) transactionList.Items.Add("No currency transactions recorded.");
            SetNotification($"Commerce loaded · {productRows.Count} store products", Accent);
        }
        catch (Exception exception)
        {
            economyStatus.Text = FriendlyError(exception);
            economyStatus.Foreground = Danger;
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private async Task ShowReportsAsync()
    {
        if (!IsAdmin)
            return;

        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Reports & archives", "Generate portable operational reports, package archival snapshots, and verify the integrity of the archives created during this service session."));

        var controls = new Grid { ColumnSpacing = 14 };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var reportType = new ComboBox
        {
            Header = "Report data",
            ItemsSource = new[] { "players", "games", "content", "bots", "matchmaking", "liveops", "economy", "audit", "full" },
            SelectedItem = "players",
            FontFamily = AppFont
        };
        var format = new ComboBox
        {
            Header = "Export format",
            ItemsSource = new[] { "json", "csv" },
            SelectedItem = "json",
            FontFamily = AppFont
        };
        controls.Children.Add(reportType);
        Grid.SetColumn(format, 1);
        controls.Children.Add(format);
        page.Children.Add(controls);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var export = IconButton("Save report", Symbol.Save);
        var archive = IconButton("Create ZIP archive", Symbol.Folder);
        var refresh = IconButton("Refresh archive list", Symbol.Refresh);
        actions.Children.Add(export);
        actions.Children.Add(archive);
        actions.Children.Add(refresh);
        page.Children.Add(actions);
        var archiveStatus = Body("Reports contain administrator-visible operational data. Store exports in an approved location.");
        page.Children.Add(archiveStatus);

        var archiveRows = new List<ArchiveDescriptorRow>();
        var archives = new ListView { MinHeight = 280, SelectionMode = ListViewSelectionMode.Single };
        var archiveCardContent = new StackPanel { Spacing = 10 };
        archiveCardContent.Children.Add(Eyebrow("ARCHIVE EXPLORER"));
        archiveCardContent.Children.Add(Body("Archives are retained in the server's current memory session until it stops. Save the ZIP file to preserve it beyond the session."));
        archiveCardContent.Children.Add(archives);
        var archiveActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var saveArchive = IconButton("Save selected ZIP", Symbol.Save);
        var verify = IconButton("Verify integrity", Symbol.Accept);
        archiveActions.Children.Add(saveArchive);
        archiveActions.Children.Add(verify);
        archiveCardContent.Children.Add(archiveActions);
        var archiveCard = Card();
        archiveCard.Child = archiveCardContent;
        page.Children.Add(archiveCard);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        export.Click += async (_, _) =>
        {
            var selectedType = reportType.SelectedItem?.ToString() ?? "players";
            var selectedFormat = format.SelectedItem?.ToString() ?? "json";
            try
            {
                var saved = await SaveAuthorizedResponseAsync(
                    $"/api/admin/reports/{Uri.EscapeDataString(selectedType)}?format={Uri.EscapeDataString(selectedFormat)}",
                    $"tcg-{selectedType}-report.{selectedFormat}",
                    "." + selectedFormat);
                if (saved) SetNotification($"Saved {selectedFormat.ToUpperInvariant()} report · {selectedType}", Accent);
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        archive.Click += async (_, _) =>
        {
            var scope = reportType.SelectedItem?.ToString() ?? "full";
            try
            {
                using var request = AuthorizedRequest(HttpMethod.Post, "/api/admin/archives");
                request.Content = JsonContent.Create(new { scope });
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                var created = await response.Content.ReadFromJsonAsync<ArchiveDescriptorRow>() ?? throw new InvalidOperationException("The service returned an empty archive descriptor.");
                archiveStatus.Text = $"Created {created.Name} · {FormatBytes(created.Length)} · SHA-256 {created.Sha256[..Math.Min(16, created.Sha256.Length)]}…";
                archiveStatus.Foreground = Accent;
                await ShowReportsAsync();
            }
            catch (Exception exception)
            {
                archiveStatus.Text = FriendlyError(exception);
                archiveStatus.Foreground = Danger;
                SetNotification(FriendlyError(exception), Danger);
            }
        };
        refresh.Click += async (_, _) => await ShowReportsAsync();
        saveArchive.Click += async (_, _) =>
        {
            if (archives.SelectedIndex < 0 || archives.SelectedIndex >= archiveRows.Count)
            {
                SetNotification("Select an archive first.", Warning);
                return;
            }
            try
            {
                var selected = archiveRows[archives.SelectedIndex];
                var saved = await SaveAuthorizedResponseAsync("/api/admin/archives/" + Uri.EscapeDataString(selected.Id), selected.Name, ".zip");
                if (saved) SetNotification($"Saved archive · {selected.Name}", Accent);
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        verify.Click += async (_, _) =>
        {
            if (archives.SelectedIndex < 0 || archives.SelectedIndex >= archiveRows.Count)
            {
                SetNotification("Select an archive first.", Warning);
                return;
            }
            try
            {
                var selected = archiveRows[archives.SelectedIndex];
                using var response = await SendAuthorizedAsync(HttpMethod.Post, "/api/admin/archives/" + Uri.EscapeDataString(selected.Id) + "/verify");
                var result = await response.Content.ReadFromJsonAsync<ArchiveVerificationRow>();
                archiveStatus.Text = result is not null && result.IsValid
                    ? $"Integrity verified · {result.Sha256[..Math.Min(16, result.Sha256.Length)]}… · {FormatBytes(result.Length)}"
                    : "Archive integrity verification failed.";
                archiveStatus.Foreground = result?.IsValid == true ? Accent : Danger;
                SetNotification(result?.IsValid == true ? "Archive integrity verified." : "Archive integrity check failed.", result?.IsValid == true ? Accent : Danger);
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };

        try
        {
            using var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/archives");
            archiveRows.AddRange(await response.Content.ReadFromJsonAsync<List<ArchiveDescriptorRow>>() ?? []);
            archives.ItemsSource = archiveRows.Select(item => $"{item.Name}  ·  {item.Scope}  ·  {FormatBytes(item.Length)}  ·  {item.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}").ToList();
            if (archiveRows.Count == 0) archives.Items.Add("No archives have been generated in this service session.");
            SetNotification($"Archive explorer loaded · {archiveRows.Count} archives", Accent);
        }
        catch (Exception exception)
        {
            archiveStatus.Text = FriendlyError(exception);
            archiveStatus.Foreground = Danger;
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private async Task ShowGamePackagesAsync()
    {
        if (!IsAdmin)
            return;

        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Game packages", "Import complete .cardforge releases from the editor, review immutable versions, activate a validated ruleset, or roll back by activating an earlier version."));
        var rows = new List<GamePackageVersionRow>();

        var importPanel = new StackPanel { Spacing = 10 };
        importPanel.Children.Add(Eyebrow("IMPORT AUTHORITATIVE RELEASE"));
        importPanel.Children.Add(Body("The server checks archive limits, safe paths, manifest declarations, every SHA-256, rule references, turn flow, and victory definitions before recording a version."));
        var activateAfterImport = new CheckBox { Content = "Activate after successful import", IsChecked = false };
        var importStatus = Body("CardForge package · .cardforge · 100 MB maximum");
        var import = IconButton("Choose .cardforge file", Symbol.Add);
        importPanel.Children.Add(activateAfterImport);
        importPanel.Children.Add(importStatus);
        importPanel.Children.Add(import);
        var importCard = Card(accented: true); importCard.Child = importPanel; page.Children.Add(importCard);

        var list = new ListView { MinHeight = 280, SelectionMode = ListViewSelectionMode.Single };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var activate = IconButton("Activate selected", Symbol.Play);
        var download = IconButton("Download selected", Symbol.Save);
        var refresh = IconButton("Refresh", Symbol.Refresh);
        actions.Children.Add(activate); actions.Children.Add(download); actions.Children.Add(refresh);
        var inventory = new StackPanel { Spacing = 10 };
        inventory.Children.Add(Eyebrow("IMPORTED VERSIONS"));
        inventory.Children.Add(Body("Versions are immutable. Activation changes the rules used by new games and is blocked while unfinished matches use that game definition."));
        inventory.Children.Add(list); inventory.Children.Add(actions);
        var inventoryCard = Card(); inventoryCard.Child = inventory; page.Children.Add(inventoryCard);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        import.Click += async (_, _) =>
        {
            var file = await PickFileAsync(".cardforge");
            if (file is null) return;
            var length = new FileInfo(file.Path).Length;
            if (length > 100L * 1024 * 1024)
            {
                importStatus.Text = "The selected package exceeds the 100 MB limit.";
                importStatus.Foreground = Danger;
                return;
            }
            import.IsEnabled = false; importStatus.Text = $"Validating {file.Name}..."; importStatus.Foreground = Muted;
            try
            {
                await UploadFileAsync("/api/admin/game-packages/import", file, new Dictionary<string, string>
                {
                    ["activate"] = (activateAfterImport.IsChecked == true).ToString().ToLowerInvariant()
                });
                importStatus.Text = $"Imported {file.Name} · {FormatBytes(length)}"; importStatus.Foreground = Accent;
                SetNotification("Game package imported successfully.", Accent);
                await ShowGamePackagesAsync();
            }
            catch (Exception exception)
            {
                importStatus.Text = FriendlyError(exception); importStatus.Foreground = Danger; SetNotification(FriendlyError(exception), Danger);
            }
            finally { import.IsEnabled = true; }
        };
        activate.Click += async (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= rows.Count) { SetNotification("Select a game package version first.", Warning); return; }
            var selected = rows[list.SelectedIndex];
            try
            {
                var endpoint = "/api/admin/game-packages/" + Uri.EscapeDataString(selected.PackageId) + "/versions/" + Uri.EscapeDataString(selected.Version) + "/activate";
                using var response = await SendAuthorizedAsync(HttpMethod.Post, endpoint);
                SetNotification($"Activated {selected.Name} v{selected.Version}.", Accent);
                await ShowGamePackagesAsync();
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        download.Click += async (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= rows.Count) { SetNotification("Select a game package version first.", Warning); return; }
            var selected = rows[list.SelectedIndex];
            try
            {
                var endpoint = "/api/admin/game-packages/" + Uri.EscapeDataString(selected.PackageId) + "/versions/" + Uri.EscapeDataString(selected.Version) + "/download";
                var saved = await SaveAuthorizedResponseAsync(endpoint, selected.FileName, ".cardforge");
                if (saved) SetNotification($"Saved {selected.Name} v{selected.Version}.", Accent);
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        refresh.Click += async (_, _) => await ShowGamePackagesAsync();

        try
        {
            using var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/game-packages");
            rows.AddRange(await response.Content.ReadFromJsonAsync<List<GamePackageVersionRow>>() ?? []);
            list.ItemsSource = rows.Select(package => $"{(package.Active ? "ACTIVE" : "READY"),-7}  {package.Name,-22}  v{package.Version,-10}  game {package.GameId,-16}  {package.Content.Cards} cards · {package.Content.Actions} actions").ToList();
            if (rows.Count == 0) list.Items.Add("No game packages imported. Publish one from CardForgeStudio Editor.");
            SetNotification($"Game package versions loaded · {rows.Count}", Accent);
        }
        catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
    }

    private async Task ShowContentPacksAsync()
    {
        if (!IsAdmin)
            return;

        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Content packs", "Upload, activate, download, and remove bundled content packs with validated metadata and integrity hashes."));

        var packRows = new List<ContentPackRow>();

        var form = new StackPanel { Spacing = 10 };
        form.Children.Add(Eyebrow("UPLOAD CONTENT PACK"));
        var packId = new TextBox { Header = "Pack ID", PlaceholderText = "booster-set-1", FontFamily = AppFont };
        var packName = new TextBox { Header = "Display name", PlaceholderText = "Booster Set 1", FontFamily = AppFont };
        var packVersion = new TextBox { Header = "Version", PlaceholderText = "1.0.0", FontFamily = AppFont };
        var packDescription = new TextBox { Header = "Description", PlaceholderText = "A bundle of card art and rules", TextWrapping = TextWrapping.Wrap, FontFamily = AppFont };
        var packActive = new CheckBox { Content = "Activate immediately", IsChecked = true };
        foreach (var control in new Control[] { packId, packName, packVersion, packDescription, packActive }) form.Children.Add(control);
        var uploadStatus = Body("Any file type · 50 MB maximum");
        var upload = IconButton("Choose file", Symbol.Add);
        form.Children.Add(uploadStatus);
        form.Children.Add(upload);
        var formCard = Card(accented: true);
        formCard.Child = form;
        page.Children.Add(formCard);

        var list = new ListView { MinHeight = 250, SelectionMode = ListViewSelectionMode.Single };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var download = IconButton("Download selected", Symbol.Save);
        var toggleActive = IconButton("Toggle active", Symbol.Sync);
        var remove = IconButton("Remove selected", Symbol.Delete);
        var refresh = IconButton("Refresh", Symbol.Refresh);
        actions.Children.Add(download);
        actions.Children.Add(toggleActive);
        actions.Children.Add(remove);
        actions.Children.Add(refresh);
        var inventoryPanel = new StackPanel { Spacing = 10 };
        inventoryPanel.Children.Add(Eyebrow("INSTALLED PACKS"));
        inventoryPanel.Children.Add(Body("Select a pack to download it, toggle its active state, or remove it from the server."));
        inventoryPanel.Children.Add(list);
        inventoryPanel.Children.Add(actions);
        var inventoryCard = Card();
        inventoryCard.Child = inventoryPanel;
        page.Children.Add(inventoryCard);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        upload.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(packId.Text) || string.IsNullOrWhiteSpace(packName.Text) || string.IsNullOrWhiteSpace(packVersion.Text) || string.IsNullOrWhiteSpace(packDescription.Text))
            {
                uploadStatus.Text = "Enter a pack ID, name, version, and description first.";
                uploadStatus.Foreground = Warning;
                return;
            }
            var file = await PickFileAsync("*");
            if (file is null) return;
            var length = new FileInfo(file.Path).Length;
            if (length > 50L * 1024 * 1024)
            {
                uploadStatus.Text = "The selected file exceeds the 50 MB limit.";
                uploadStatus.Foreground = Danger;
                return;
            }
            upload.IsEnabled = false;
            uploadStatus.Text = $"Uploading {file.Name}...";
            uploadStatus.Foreground = Muted;
            try
            {
                await UploadFileAsync("/api/admin/content-packs/upload", file, new Dictionary<string, string>
                {
                    ["id"] = packId.Text.Trim(),
                    ["name"] = packName.Text.Trim(),
                    ["version"] = packVersion.Text.Trim(),
                    ["description"] = packDescription.Text.Trim(),
                    ["active"] = (packActive.IsChecked == true).ToString().ToLowerInvariant()
                });
                uploadStatus.Text = $"Installed {file.Name} · {FormatBytes(length)}";
                uploadStatus.Foreground = Accent;
                SetNotification($"Content pack uploaded · {packId.Text.Trim()}", Accent);
                await ShowContentPacksAsync();
            }
            catch (Exception exception)
            {
                uploadStatus.Text = FriendlyError(exception);
                uploadStatus.Foreground = Danger;
                SetNotification(FriendlyError(exception), Danger);
            }
            finally
            {
                upload.IsEnabled = true;
            }
        };
        download.Click += async (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= packRows.Count)
            {
                SetNotification("Select a content pack first.", Warning);
                return;
            }
            var selected = packRows[list.SelectedIndex];
            try
            {
                var ext = Path.GetExtension(selected.FileName);
                if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";
                var saved = await SaveAuthorizedResponseAsync("/api/admin/content-packs/" + Uri.EscapeDataString(selected.Id) + "/download", selected.FileName, ext);
                if (saved) SetNotification($"Saved content pack · {selected.Name}", Accent);
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        toggleActive.Click += async (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= packRows.Count)
            {
                SetNotification("Select a content pack first.", Warning);
                return;
            }
            var selected = packRows[list.SelectedIndex];
            try
            {
                using var request = AuthorizedRequest(HttpMethod.Patch, "/api/admin/content-packs/" + Uri.EscapeDataString(selected.Id));
                request.Content = JsonContent.Create(new { active = !selected.Active });
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                SetNotification($"{selected.Name} is now {(!selected.Active ? "active" : "inactive")}.", Accent);
                await ShowContentPacksAsync();
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        remove.Click += async (_, _) =>
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= packRows.Count)
            {
                SetNotification("Select a content pack first.", Warning);
                return;
            }
            var selected = packRows[list.SelectedIndex];
            if (!await ConfirmAsync("Remove content pack?", $"Remove '{selected.Name}' from the server? This cannot be undone.")) return;
            try
            {
                using var response = await SendAuthorizedAsync(HttpMethod.Delete, "/api/admin/content-packs/" + Uri.EscapeDataString(selected.Id));
                SetNotification($"Removed content pack · {selected.Name}", Accent);
                await ShowContentPacksAsync();
            }
            catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
        };
        refresh.Click += async (_, _) => await ShowContentPacksAsync();

        try
        {
            using var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/content-packs");
            packRows.AddRange(await response.Content.ReadFromJsonAsync<List<ContentPackRow>>() ?? []);
            list.ItemsSource = packRows.Select(pack => $"{pack.Name,-24}  v{pack.Version,-10}  {FormatBytes(pack.Length),-10}  {(pack.Active ? "ACTIVE" : "INACTIVE")}").ToList();
            if (packRows.Count == 0) list.Items.Add("No content packs installed. Upload one above.");
            SetNotification($"Content packs loaded · {packRows.Count}", Accent);
        }
        catch (Exception exception)
        {
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private async Task ShowChatCenterAsync()
    {
        if (!CanModerate)
            return;

        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Live chat center", "Monitor active players, moderate direct messages, inspect game channels, and broadcast server announcements from one workspace."));

        var metrics = new Grid { ColumnSpacing = 14 };
        for (var index = 0; index < 3; index++)
            metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        page.Children.Add(metrics);

        var announcement = new TextBox
        {
            Header = "Server announcement",
            PlaceholderText = "Write a message for every connected game client...",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxLength = 500,
            MinHeight = 82,
            FontFamily = AppFont
        };
        var broadcast = IconButton("Broadcast announcement", Symbol.Send);
        broadcast.IsEnabled = IsAdmin;
        var announcementPanel = new StackPanel { Spacing = 10 };
        announcementPanel.Children.Add(Eyebrow("SERVER BROADCAST"));
        announcementPanel.Children.Add(Body("Announcements appear immediately in active game channels and connected clients. Every broadcast is written to the audit log."));
        announcementPanel.Children.Add(announcement);
        announcementPanel.Children.Add(broadcast);
        var announcementCard = Card(accented: true);
        announcementCard.Child = announcementPanel;
        page.Children.Add(announcementCard);

        var directMessages = new ListView { MinHeight = 280, MaxHeight = 420, SelectionMode = ListViewSelectionMode.Single };
        var flagMessage = IconButton("Flag / unflag", Symbol.Flag);
        var removeMessage = IconButton("Remove / restore", Symbol.Delete);
        var directActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        directActions.Children.Add(flagMessage);
        directActions.Children.Add(removeMessage);
        var directPanel = new StackPanel { Spacing = 10 };
        directPanel.Children.Add(Eyebrow("DIRECT MESSAGES"));
        directPanel.Children.Add(Body("Select a message to flag it for review or hide it from player inboxes."));
        directPanel.Children.Add(directMessages);
        directPanel.Children.Add(directActions);
        var directCard = Card();
        directCard.Child = directPanel;

        var gameChannels = new ListView { MinHeight = 150, MaxHeight = 210, SelectionMode = ListViewSelectionMode.Single };
        var transcript = new TextBlock
        {
            Text = "Select a game channel to inspect its live transcript.",
            FontFamily = MonoFont,
            FontSize = 12,
            Foreground = Muted,
            TextWrapping = TextWrapping.Wrap
        };
        var transcriptScroll = new ScrollViewer
        {
            Content = transcript,
            MinHeight = 150,
            MaxHeight = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var gamePanel = new StackPanel { Spacing = 10 };
        gamePanel.Children.Add(Eyebrow("GAME CHANNELS"));
        gamePanel.Children.Add(Body("Open any active or completed game channel to review its conversation."));
        gamePanel.Children.Add(gameChannels);
        gamePanel.Children.Add(new Border
        {
            Background = SurfaceRaised,
            BorderBrush = Divider,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = transcriptScroll
        });
        var gameCard = Card();
        gameCard.Child = gamePanel;

        var workspace = new Grid { ColumnSpacing = 14 };
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        workspace.Children.Add(directCard);
        Grid.SetColumn(gameCard, 1);
        workspace.Children.Add(gameCard);
        page.Children.Add(workspace);

        var onlinePlayers = new ListView { MinHeight = 110, MaxHeight = 240, SelectionMode = ListViewSelectionMode.None };
        var onlinePanel = new StackPanel { Spacing = 10 };
        onlinePanel.Children.Add(Eyebrow("ONLINE PLAYERS"));
        onlinePanel.Children.Add(Body("Player sessions remain online while they are actively using the server."));
        onlinePanel.Children.Add(onlinePlayers);
        var onlineCard = Card();
        onlineCard.Child = onlinePanel;
        page.Children.Add(onlineCard);

        var refresh = IconButton("Refresh live data", Symbol.Refresh);
        page.Children.Add(refresh);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        AdminChatSnapshotRow? chatSnapshot = null;
        PresenceSnapshotRow? presenceSnapshot = null;

        void ShowSelectedTranscript()
        {
            if (chatSnapshot is null || gameChannels.SelectedIndex < 0 || gameChannels.SelectedIndex >= chatSnapshot.GameChannels.Count)
            {
                transcript.Text = "Select a game channel to inspect its live transcript.";
                transcript.Foreground = Muted;
                return;
            }

            var channel = chatSnapshot.GameChannels[gameChannels.SelectedIndex];
            var lines = new List<string>
            {
                $"GAME {channel.GameId}",
                $"Players: {(channel.Players.Count == 0 ? "None" : string.Join(", ", channel.Players))}",
                $"Status: {(channel.Finished ? "FINISHED" : "ACTIVE")}",
                string.Empty
            };
            lines.AddRange(channel.Messages.Count == 0 ? ["No chat messages recorded."] : channel.Messages);
            transcript.Text = string.Join(Environment.NewLine, lines);
            transcript.Foreground = Foreground;
        }

        async Task LoadChatAsync()
        {
            try
            {
                using var chatResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/chat");
                chatSnapshot = await chatResponse.Content.ReadFromJsonAsync<AdminChatSnapshotRow>();
                if (IsAdmin)
                {
                    using var presenceResponse = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/presence");
                    presenceSnapshot = await presenceResponse.Content.ReadFromJsonAsync<PresenceSnapshotRow>();
                }

                if (chatSnapshot is null)
                    return;

                onlinePlayerTotal = chatSnapshot.OnlinePlayers;
                var noun = onlinePlayerTotal == 1 ? "PLAYER" : "PLAYERS";
                livePlayerCount.Text = $"{onlinePlayerTotal:N0} {noun} ONLINE";
                livePlayerCount.Foreground = onlinePlayerTotal > 0 ? Accent : Muted;

                metrics.Children.Clear();
                metrics.Children.Add(MetricCard("PLAYERS ONLINE", chatSnapshot.OnlinePlayers.ToString("N0"), "Active server sessions"));
                var messageMetric = MetricCard("DIRECT MESSAGES", chatSnapshot.DirectMessages.Count.ToString("N0"), "Available for moderation");
                Grid.SetColumn(messageMetric, 1);
                metrics.Children.Add(messageMetric);
                var channelMetric = MetricCard("GAME CHANNELS", chatSnapshot.GameChannels.Count.ToString("N0"), "Active and recorded games");
                Grid.SetColumn(channelMetric, 2);
                metrics.Children.Add(channelMetric);

                var selectedMessageId = directMessages.SelectedItem is ListViewItem selectedMessage ? selectedMessage.Tag?.ToString() : null;
                directMessages.Items.Clear();
                foreach (var message in chatSnapshot.DirectMessages)
                {
                    var state = message.Removed ? "REMOVED" : message.Flagged ? "FLAGGED" : "OK";
                    directMessages.Items.Add(new ListViewItem
                    {
                        Tag = message.Id,
                        Content = $"{state,-9} {message.SenderId[..Math.Min(8, message.SenderId.Length)]} → {message.RecipientId[..Math.Min(8, message.RecipientId.Length)]}  ·  {message.SentAt.LocalDateTime:g}  ·  {message.Body}"
                    });
                }
                if (chatSnapshot.DirectMessages.Count == 0)
                    directMessages.Items.Add("No direct messages recorded.");
                else if (selectedMessageId is not null)
                {
                    var selectedIndex = chatSnapshot.DirectMessages.FindIndex(message => message.Id == selectedMessageId);
                    if (selectedIndex >= 0) directMessages.SelectedIndex = selectedIndex;
                }

                var selectedGameId = gameChannels.SelectedItem is ListViewItem selectedGame ? selectedGame.Tag?.ToString() : null;
                gameChannels.Items.Clear();
                foreach (var channel in chatSnapshot.GameChannels)
                {
                    gameChannels.Items.Add(new ListViewItem
                    {
                        Tag = channel.GameId,
                        Content = $"{(channel.Finished ? "FINISHED" : "ACTIVE"),-8} {channel.GameId[..Math.Min(12, channel.GameId.Length)]}  ·  {channel.Players.Count} players  ·  {channel.Messages.Count} messages"
                    });
                }
                if (chatSnapshot.GameChannels.Count == 0)
                    gameChannels.Items.Add("No game channels recorded.");
                else
                {
                    var selectedIndex = selectedGameId is null ? 0 : chatSnapshot.GameChannels.FindIndex(channel => channel.GameId == selectedGameId);
                    gameChannels.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
                }

                onlinePlayers.Items.Clear();
                if (presenceSnapshot is null || presenceSnapshot.Players.Count == 0)
                {
                    onlinePlayers.Items.Add("No players are currently active.");
                }
                else
                {
                    foreach (var player in presenceSnapshot.Players)
                        onlinePlayers.Items.Add($"{player.Handle}  ·  rating {player.Rating:N0}  ·  last active {player.LastSeenAt.LocalDateTime:T}");
                }

                ShowSelectedTranscript();
            }
            catch (Exception exception)
            {
                SetNotification(FriendlyError(exception), Danger);
            }
        }

        async Task ModerateSelectedMessageAsync(bool toggleFlag)
        {
            if (chatSnapshot is null || directMessages.SelectedIndex < 0 || directMessages.SelectedIndex >= chatSnapshot.DirectMessages.Count)
            {
                SetNotification("Select a direct message first.", Warning);
                return;
            }

            var selected = chatSnapshot.DirectMessages[directMessages.SelectedIndex];
            try
            {
                using var request = AuthorizedRequest(HttpMethod.Patch, "/api/admin/messages/" + Uri.EscapeDataString(selected.Id));
                request.Content = toggleFlag
                    ? JsonContent.Create(new { flagged = !selected.Flagged })
                    : JsonContent.Create(new { removed = !selected.Removed });
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                SetNotification(toggleFlag ? "Message moderation flag updated." : "Message visibility updated.", Accent);
                await LoadChatAsync();
            }
            catch (Exception exception)
            {
                SetNotification(FriendlyError(exception), Danger);
            }
        }

        broadcast.Click += async (_, _) =>
        {
            var body = announcement.Text.Trim();
            if (body.Length is < 1 or > 500)
            {
                SetNotification("Enter an announcement between 1 and 500 characters.", Warning);
                return;
            }

            try
            {
                using var request = AuthorizedRequest(HttpMethod.Post, "/api/admin/chat/announcements");
                request.Content = JsonContent.Create(new { body });
                using var response = await http.SendAsync(request);
                await EnsureSuccessAsync(response);
                announcement.Text = string.Empty;
                SetNotification("Server announcement broadcast successfully.", Accent);
                await LoadChatAsync();
            }
            catch (Exception exception)
            {
                SetNotification(FriendlyError(exception), Danger);
            }
        };
        flagMessage.Click += async (_, _) => await ModerateSelectedMessageAsync(true);
        removeMessage.Click += async (_, _) => await ModerateSelectedMessageAsync(false);
        gameChannels.SelectionChanged += (_, _) => ShowSelectedTranscript();
        refresh.Click += async (_, _) => await LoadChatAsync();

        activeLiveRefresh = LoadChatAsync;
        await LoadChatAsync();
    }

    private async Task ShowPlayerOperationsAsync()
    {
        if (!IsAdmin)
            return;

        content.Content = null;
        var page = PagePanel();
        page.Children.Add(PageHeader("Player operations", "Open any registered player's complete server record: account, ranking, currency, inventory, decks, messages, friends, and replay history."));

        var playerRows = new List<AdminUserRow>();
        var playerPicker = new ComboBox { Header = "Account", HorizontalAlignment = HorizontalAlignment.Stretch, FontFamily = AppFont, MinWidth = 360 };
        var load = IconButton("Load profile", Symbol.Find);
        var refresh = IconButton("Refresh accounts", Symbol.Refresh);
        var selectorButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        selectorButtons.Children.Add(load);
        selectorButtons.Children.Add(refresh);
        var selectorPanel = new StackPanel { Spacing = 10 };
        selectorPanel.Children.Add(Eyebrow("SELECT PLAYER"));
        selectorPanel.Children.Add(playerPicker);
        selectorPanel.Children.Add(selectorButtons);
        var selectorCard = Card(accented: true);
        selectorCard.Child = selectorPanel;
        page.Children.Add(selectorCard);

        var profileHost = new StackPanel { Spacing = 14 };
        page.Children.Add(profileHost);
        content.Content = new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        async Task LoadProfileAsync()
        {
            if (playerPicker.SelectedIndex < 0 || playerPicker.SelectedIndex >= playerRows.Count)
            {
                SetNotification("Select an account first.", Warning);
                return;
            }
            var selected = playerRows[playerPicker.SelectedIndex];
            profileHost.Children.Clear();
            var loading = Body("Loading player profile...");
            profileHost.Children.Add(loading);
            try
            {
                using var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/players/" + Uri.EscapeDataString(selected.Id) + "/profile");
                var profile = await response.Content.ReadFromJsonAsync<JsonElement>();
                profileHost.Children.Remove(loading);

                var user = profile.GetProperty("user");
                var summary = new StackPanel { Spacing = 8 };
                summary.Children.Add(Eyebrow("PLAYER SUMMARY"));
                summary.Children.Add(StatusRow("Handle", JsonText(user, "handle")));
                summary.Children.Add(StatusRow("Role", JsonText(user, "role")));
                summary.Children.Add(StatusRow("Rating", JsonText(user, "rating")));
                summary.Children.Add(StatusRow("Account", user.TryGetProperty("disabled", out var disabled) && disabled.GetBoolean() ? "DISABLED" : "ACTIVE"));
                if (profile.TryGetProperty("ranking", out var ranking))
                {
                    summary.Children.Add(StatusRow("Global rank", $"#{JsonText(ranking, "rank")} of {JsonText(ranking, "totalPlayers")}"));
                    summary.Children.Add(StatusRow("Percentile", $"Top {JsonText(ranking, "percentile")}%"));
                }
                var summaryCard = Card(accented: true);
                summaryCard.Child = summary;
                profileHost.Children.Add(summaryCard);

                var inventory = profile.GetProperty("inventory");
                var cardCount = inventory.TryGetProperty("cards", out var cards) && cards.ValueKind == JsonValueKind.Array ? cards.GetArrayLength() : 0;
                var achievementCount = inventory.TryGetProperty("achievements", out var achievements) && achievements.ValueKind == JsonValueKind.Array ? achievements.GetArrayLength() : 0;
                var inventoryPanel = new StackPanel { Spacing = 8 };
                inventoryPanel.Children.Add(Eyebrow("INVENTORY"));
                inventoryPanel.Children.Add(StatusRow("Cards", cardCount.ToString()));
                inventoryPanel.Children.Add(StatusRow("Achievements", achievementCount.ToString()));
                if (cards.ValueKind == JsonValueKind.Array && cards.GetArrayLength() > 0)
                {
                    var cardList = new ListView { MinHeight = 90, MaxHeight = 220, SelectionMode = ListViewSelectionMode.None };
                    foreach (var group in cards.EnumerateArray().GroupBy(card => JsonText(card, "cardId"), StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key))
                        cardList.Items.Add($"{group.Key}  ·  {group.Count()} owned");
                    inventoryPanel.Children.Add(cardList);
                }
                if (achievements.ValueKind == JsonValueKind.Array && achievements.GetArrayLength() > 0)
                {
                    inventoryPanel.Children.Add(Body("Achievements: " + string.Join(", ", achievements.EnumerateArray().Select(item => item.GetString()))));
                }
                var inventoryCard = Card();
                inventoryCard.Child = inventoryPanel;
                profileHost.Children.Add(inventoryCard);

                var walletPanel = new StackPanel { Spacing = 8 };
                walletPanel.Children.Add(Eyebrow("CURRENCY WALLET"));
                if (profile.TryGetProperty("wallet", out var wallet) && wallet.TryGetProperty("balances", out var balances) && balances.ValueKind == JsonValueKind.Object)
                {
                    foreach (var balance in balances.EnumerateObject().OrderBy(item => item.Name))
                        walletPanel.Children.Add(StatusRow(balance.Name.ToUpperInvariant(), balance.Value.ToString()));
                }
                else
                {
                    walletPanel.Children.Add(Body("No currency balances recorded."));
                }
                var adjustmentGrid = new Grid { ColumnSpacing = 10 };
                adjustmentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                adjustmentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var adjustmentCurrency = new TextBox { Header = "Currency", PlaceholderText = "gold", FontFamily = AppFont };
                var adjustmentAmount = new TextBox { Header = "Adjustment", PlaceholderText = "500 or -100", FontFamily = AppFont };
                adjustmentGrid.Children.Add(adjustmentCurrency);
                Grid.SetColumn(adjustmentAmount, 1);
                adjustmentGrid.Children.Add(adjustmentAmount);
                walletPanel.Children.Add(adjustmentGrid);
                var adjustmentReason = new TextBox { Header = "Audit reason", PlaceholderText = "Support grant, refund, correction...", FontFamily = AppFont };
                walletPanel.Children.Add(adjustmentReason);
                var adjustWallet = IconButton("Apply currency adjustment", Symbol.Accept);
                walletPanel.Children.Add(adjustWallet);
                walletPanel.Children.Add(Eyebrow("TRANSACTION LEDGER"));
                var transactionList = new ListView { MinHeight = 100, MaxHeight = 220, SelectionMode = ListViewSelectionMode.None };
                var transactions = profile.TryGetProperty("transactions", out var transactionArray) && transactionArray.ValueKind == JsonValueKind.Array ? transactionArray.EnumerateArray().ToArray() : [];
                foreach (var transaction in transactions)
                {
                    var amountText = JsonText(transaction, "amount");
                    transactionList.Items.Add($"{JsonText(transaction, "createdAt")}  ·  {amountText} {JsonText(transaction, "currency")}  ·  balance {JsonText(transaction, "balanceAfter")}  ·  {JsonText(transaction, "reason")}");
                }
                if (transactions.Length == 0) transactionList.Items.Add("No currency transactions recorded.");
                walletPanel.Children.Add(transactionList);
                var walletCard = Card();
                walletCard.Child = walletPanel;
                profileHost.Children.Add(walletCard);

                adjustWallet.Click += async (_, _) =>
                {
                    if (!long.TryParse(adjustmentAmount.Text, out var amount) || amount == 0)
                    {
                        SetNotification("Enter a non-zero whole-number currency adjustment.", Warning);
                        return;
                    }
                    try
                    {
                        using var request = AuthorizedRequest(HttpMethod.Post, $"/api/admin/players/{Uri.EscapeDataString(selected.Id)}/wallet-adjustments");
                        request.Content = JsonContent.Create(new { currency = adjustmentCurrency.Text.Trim(), amount, reason = adjustmentReason.Text.Trim() });
                        using var response = await http.SendAsync(request);
                        await EnsureSuccessAsync(response);
                        SetNotification($"Currency adjusted · {amount} {adjustmentCurrency.Text.Trim()}", Accent);
                        await LoadProfileAsync();
                    }
                    catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
                };

                var messages = profile.TryGetProperty("messages", out var msgArray) && msgArray.ValueKind == JsonValueKind.Array ? msgArray.EnumerateArray().ToArray() : [];
                var messageInfo = new List<(string Id, bool Flagged, bool Removed)>();
                var messageList = new ListView { MinHeight = 160, MaxHeight = 280, SelectionMode = ListViewSelectionMode.Single };
                foreach (var message in messages)
                {
                    var id = JsonText(message, "id");
                    var senderId = JsonText(message, "senderId");
                    var sender = senderId[..Math.Min(8, senderId.Length)];
                    var recipientId = JsonText(message, "recipientId");
                    var sentAt = JsonText(message, "sentAt");
                    var body = JsonText(message, "body");
                    var flagged = message.TryGetProperty("flagged", out var f) && f.GetBoolean();
                    var removed = message.TryGetProperty("removed", out var r) && r.GetBoolean();
                    messageInfo.Add((id, flagged, removed));
                    var state = removed ? "REMOVED" : flagged ? "FLAGGED" : "OK";
                    messageList.Items.Add(new ListViewItem { Tag = id, Content = $"{state,-9} {sender} → {recipientId[..Math.Min(8, recipientId.Length)]}  ·  {sentAt}  ·  {body}" });
                }
                if (messages.Length == 0) messageList.Items.Add("No direct messages.");
                var flagMessage = IconButton("Flag / unflag", Symbol.Flag);
                var removeMessage = IconButton("Remove / restore", Symbol.Delete);
                var messageActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                messageActions.Children.Add(flagMessage);
                messageActions.Children.Add(removeMessage);
                var messagePanel = new StackPanel { Spacing = 10 };
                messagePanel.Children.Add(Eyebrow("DIRECT MESSAGES"));
                messagePanel.Children.Add(Body($"{messages.Length} messages involving this player. Select one to flag, remove, or restore it."));
                messagePanel.Children.Add(messageList);
                messagePanel.Children.Add(messageActions);
                var messageCard = Card();
                messageCard.Child = messagePanel;
                profileHost.Children.Add(messageCard);

                var decks = profile.TryGetProperty("decks", out var deckArray) && deckArray.ValueKind == JsonValueKind.Array ? deckArray.EnumerateArray().ToArray() : [];
                var deckStatistics = profile.TryGetProperty("deckStatistics", out var deckStatisticsArray) && deckStatisticsArray.ValueKind == JsonValueKind.Array ? deckStatisticsArray.EnumerateArray().ToArray() : [];
                var deckInfo = new List<(string Id, bool Suspended)>();
                var deckList = new ListView { MinHeight = 160, MaxHeight = 280, SelectionMode = ListViewSelectionMode.Single };
                foreach (var deck in decks)
                {
                    var id = JsonText(deck, "id");
                    var name = JsonText(deck, "name");
                    var rulePack = JsonText(deck, "rulePackId");
                    var deckCardCount = deck.TryGetProperty("cardIds", out var deckCards) && deckCards.ValueKind == JsonValueKind.Array ? deckCards.GetArrayLength() : 0;
                    var suspended = deck.TryGetProperty("suspended", out var s) && s.GetBoolean();
                    var statistics = deckStatistics.FirstOrDefault(item => string.Equals(JsonText(item, "deckId"), id, StringComparison.Ordinal));
                    var uniqueCards = statistics.ValueKind == JsonValueKind.Object ? JsonText(statistics, "uniqueCards") : "0";
                    var averageCost = statistics.ValueKind == JsonValueKind.Object ? JsonText(statistics, "averageCost") : "0";
                    var typeSummary = statistics.ValueKind == JsonValueKind.Object && statistics.TryGetProperty("cardTypes", out var cardTypes) && cardTypes.ValueKind == JsonValueKind.Object
                        ? string.Join(", ", cardTypes.EnumerateObject().Select(type => $"{type.Name} {type.Value}"))
                        : "No composition data";
                    deckInfo.Add((id, suspended));
                    deckList.Items.Add(new ListViewItem { Tag = id, Content = $"{(suspended ? "SUSPENDED" : "OK"),-10} {name}  ·  {rulePack}  ·  {deckCardCount} cards / {uniqueCards} unique  ·  avg cost {averageCost}  ·  {typeSummary}" });
                }
                if (decks.Length == 0) deckList.Items.Add("No decks.");
                var suspendDeck = IconButton("Suspend / unsuspend", Symbol.Pause);
                var deckActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                deckActions.Children.Add(suspendDeck);
                var deckPanel = new StackPanel { Spacing = 10 };
                deckPanel.Children.Add(Eyebrow("DECKS"));
                deckPanel.Children.Add(Body($"{decks.Length} decks. Composition statistics include card count, unique cards, average cost, and type distribution."));
                deckPanel.Children.Add(deckList);
                deckPanel.Children.Add(deckActions);
                var deckCard = Card();
                deckCard.Child = deckPanel;
                profileHost.Children.Add(deckCard);

                var friends = profile.TryGetProperty("friends", out var friendArray) && friendArray.ValueKind == JsonValueKind.Array ? friendArray.EnumerateArray().ToArray() : [];
                var friendList = new ListView { MinHeight = 80, MaxHeight = 200, SelectionMode = ListViewSelectionMode.None };
                foreach (var friend in friends)
                {
                    var friendId = JsonText(friend, "friendUserId");
                    var status = JsonText(friend, "status");
                    friendList.Items.Add($"{friendId}  ·  {status}");
                }
                if (friends.Length == 0) friendList.Items.Add("No friend connections.");
                var friendPanel = new StackPanel { Spacing = 10 };
                friendPanel.Children.Add(Eyebrow("FRIENDS"));
                friendPanel.Children.Add(friendList);
                var friendCard = Card();
                friendCard.Child = friendPanel;
                profileHost.Children.Add(friendCard);

                var matches = profile.TryGetProperty("matches", out var matchArray) && matchArray.ValueKind == JsonValueKind.Array ? matchArray.EnumerateArray().ToArray() : [];
                var matchList = new ListView { MinHeight = 100, MaxHeight = 240, SelectionMode = ListViewSelectionMode.Single };
                foreach (var match in matches)
                {
                    var matchId = JsonText(match, "id");
                    var matchRulePack = JsonText(match, "rulePackId");
                    var finished = match.TryGetProperty("finished", out var fin) && fin.GetBoolean();
                    var turn = match.TryGetProperty("turn", out var t) ? t.GetInt32() : 0;
                    matchList.Items.Add($"{matchId[..Math.Min(10, matchId.Length)]}  ·  {matchRulePack}  ·  turn {turn}  ·  {(finished ? "FINISHED" : "ACTIVE")}");
                }
                if (matches.Length == 0) matchList.Items.Add("No match history.");
                var viewReplay = IconButton("Open selected replay", Symbol.Play);
                var replayDetails = new TextBlock
                {
                    Text = "Select a match to inspect its recorded events and chat.",
                    FontFamily = AppFont,
                    FontSize = 12,
                    Foreground = Muted,
                    TextWrapping = TextWrapping.Wrap
                };
                var matchPanel = new StackPanel { Spacing = 10 };
                matchPanel.Children.Add(Eyebrow("MATCH HISTORY & REPLAYS"));
                matchPanel.Children.Add(Body($"{matches.Length} server recordings. Open a replay to inspect turn events, actors, outcomes, and game chat."));
                matchPanel.Children.Add(matchList);
                matchPanel.Children.Add(viewReplay);
                matchPanel.Children.Add(new Border
                {
                    Background = SurfaceRaised,
                    BorderBrush = Divider,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14),
                    Child = replayDetails
                });
                var matchCard = Card();
                matchCard.Child = matchPanel;
                profileHost.Children.Add(matchCard);

                viewReplay.Click += (_, _) =>
                {
                    if (matchList.SelectedIndex < 0 || matchList.SelectedIndex >= matches.Length)
                    {
                        SetNotification("Select a match recording first.", Warning);
                        return;
                    }

                    var replay = matches[matchList.SelectedIndex];
                    var lines = new List<string>
                    {
                        $"Replay {JsonText(replay, "id")}",
                        $"Rule pack: {JsonText(replay, "rulePackId")}  |  Turn: {JsonText(replay, "turn")}  |  Finished: {JsonText(replay, "finished")}",
                        $"Players: {(replay.TryGetProperty("players", out var replayPlayers) ? string.Join(", ", replayPlayers.EnumerateArray().Select(player => player.GetString())) : "None")}",
                        "",
                        "EVENTS"
                    };
                    if (replay.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
                    {
                        lines.AddRange(events.EnumerateArray().Select(gameEvent =>
                            $"{JsonText(gameEvent, "at")}  {JsonText(gameEvent, "type")}  {JsonText(gameEvent, "actorId")}  {JsonText(gameEvent, "message")}"));
                    }
                    if (replay.TryGetProperty("chat", out var replayChat) && replayChat.ValueKind == JsonValueKind.Array)
                    {
                        lines.Add("");
                        lines.Add("CHAT");
                        lines.AddRange(replayChat.EnumerateArray().Select(entry => entry.GetString() ?? string.Empty));
                    }
                    replayDetails.Text = string.Join(Environment.NewLine, lines);
                    replayDetails.Foreground = Foreground;
                    SetNotification("Replay opened.", Accent);
                };

                SetNotification($"Profile loaded · {JsonText(user, "handle")}", Accent);

                flagMessage.Click += async (_, _) =>
                {
                    if (messageList.SelectedIndex < 0 || messageList.SelectedIndex >= messageInfo.Count)
                    {
                        SetNotification("Select a message first.", Warning);
                        return;
                    }
                    var info = messageInfo[messageList.SelectedIndex];
                    try
                    {
                        using var request = AuthorizedRequest(HttpMethod.Patch, "/api/admin/messages/" + Uri.EscapeDataString(info.Id));
                        request.Content = JsonContent.Create(new { flagged = !info.Flagged });
                        using var resp = await http.SendAsync(request);
                        await EnsureSuccessAsync(resp);
                        SetNotification($"Message {(!info.Flagged ? "flagged" : "unflagged")}.", Accent);
                        await LoadProfileAsync();
                    }
                    catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
                };
                removeMessage.Click += async (_, _) =>
                {
                    if (messageList.SelectedIndex < 0 || messageList.SelectedIndex >= messageInfo.Count)
                    {
                        SetNotification("Select a message first.", Warning);
                        return;
                    }
                    var info = messageInfo[messageList.SelectedIndex];
                    try
                    {
                        using var request = AuthorizedRequest(HttpMethod.Patch, "/api/admin/messages/" + Uri.EscapeDataString(info.Id));
                        request.Content = JsonContent.Create(new { removed = !info.Removed });
                        using var resp = await http.SendAsync(request);
                        await EnsureSuccessAsync(resp);
                        SetNotification($"Message {(!info.Removed ? "removed" : "restored")}.", Accent);
                        await LoadProfileAsync();
                    }
                    catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
                };
                suspendDeck.Click += async (_, _) =>
                {
                    if (deckList.SelectedIndex < 0 || deckList.SelectedIndex >= deckInfo.Count)
                    {
                        SetNotification("Select a deck first.", Warning);
                        return;
                    }
                    var info = deckInfo[deckList.SelectedIndex];
                    try
                    {
                        using var request = AuthorizedRequest(HttpMethod.Patch, "/api/admin/decks/" + Uri.EscapeDataString(info.Id));
                        request.Content = JsonContent.Create(new { suspended = !info.Suspended });
                        using var resp = await http.SendAsync(request);
                        await EnsureSuccessAsync(resp);
                        SetNotification($"Deck {(!info.Suspended ? "suspended" : "reinstated")}.", Accent);
                        await LoadProfileAsync();
                    }
                    catch (Exception exception) { SetNotification(FriendlyError(exception), Danger); }
                };
            }
            catch (Exception exception)
            {
                loading.Text = FriendlyError(exception);
                loading.Foreground = Danger;
                SetNotification(FriendlyError(exception), Danger);
            }
        }

        load.Click += async (_, _) => await LoadProfileAsync();
        refresh.Click += async (_, _) => await ShowPlayerOperationsAsync();

        try
        {
            using var response = await SendAuthorizedAsync(HttpMethod.Get, "/api/admin/users");
            playerRows.Clear();
            playerRows.AddRange(await response.Content.ReadFromJsonAsync<List<AdminUserRow>>() ?? []);
            playerPicker.ItemsSource = playerRows.Select(user => $"{user.Handle,-24}  {user.Role,-10}  {(user.Disabled ? "DISABLED" : "ACTIVE")}").ToList();
            if (playerRows.Count > 0)
            {
                playerPicker.SelectedIndex = 0;
                await LoadProfileAsync();
            }
            SetNotification($"Accounts loaded · {playerRows.Count}", Accent);
        }
        catch (Exception exception)
        {
            SetNotification(FriendlyError(exception), Danger);
        }
    }

    private void SelectNavigation(string tag)
    {
        var item = navigation.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => string.Equals(candidate.Tag?.ToString(), tag, StringComparison.Ordinal));
        if (item is not null)
            navigation.SelectedItem = item;
    }

    private HttpRequestMessage AuthorizedRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", token);
        return request;
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string path)
    {
        using var request = AuthorizedRequest(method, path);
        var response = await http.SendAsync(request);
        await EnsureSuccessAsync(response);
        return response;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;
        var details = (await response.Content.ReadAsStringAsync()).Trim().Trim('"');
        throw new HttpRequestException(string.IsNullOrWhiteSpace(details)
            ? $"The service returned {(int)response.StatusCode} {response.ReasonPhrase}."
            : details);
    }

    private void SetNotification(string message, Brush color)
    {
        notification.Text = message;
        notification.Foreground = color;
    }

    private static string FriendlyError(Exception exception) => exception switch
    {
        TaskCanceledException => "The service did not respond in time.",
        HttpRequestException { Message.Length: > 0 } => exception.Message,
        _ => exception.Message
    };

    private bool IsAdmin => string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
    private bool CanModerate => IsAdmin || string.Equals(role, "moderator", StringComparison.OrdinalIgnoreCase);

    private static StackPanel PagePanel() => new()
    {
        Spacing = 18,
        Padding = new Thickness(28, 24, 28, 40),
        MaxWidth = 1440,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static UIElement PageHeader(string title, string subtitle)
    {
        var layout = new Grid { ColumnSpacing = 16 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(new Border
        {
            Background = Accent,
            CornerRadius = new CornerRadius(2)
        });
        var copy = new StackPanel { Spacing = 4 };
        copy.Children.Add(Label(title, 29, Foreground, true));
        copy.Children.Add(Body(subtitle));
        Grid.SetColumn(copy, 1);
        layout.Children.Add(copy);
        return new Border
        {
            Background = Surface,
            BorderBrush = Divider,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20, 17, 20, 17),
            Child = layout
        };
    }

    private UIElement BuildDashboardBanner(string title, string subtitle)
    {
        var canvas = new Grid();
        canvas.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "DashboardBanner.png"))),
            Stretch = Stretch.UniformToFill,
            Opacity = 0.92
        });

        var shade = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0.5),
            EndPoint = new Windows.Foundation.Point(1, 0.5)
        };
        shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(248, 13, 18, 27), Offset = 0 });
        shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(232, 13, 18, 27), Offset = 0.48 });
        shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(100, 13, 18, 27), Offset = 0.82 });
        shade.GradientStops.Add(new GradientStop { Color = Color.FromArgb(45, 13, 18, 27), Offset = 1 });
        canvas.Children.Add(new Border { Background = shade });

        var copy = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(30, 26, 30, 26),
            MaxWidth = 620,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        copy.Children.Add(Eyebrow(token is null ? "NATIVE MANAGEMENT WORKSPACE" : "AUTHENTICATED OPERATIONS"));
        copy.Children.Add(Label(title, 34, Foreground, true));
        copy.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 15.5,
            FontFamily = AppFont,
            Foreground = Muted,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 23,
            MaxWidth = 590
        });
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 5, 0, 0) };
        badges.Children.Add(Badge(isServerReady ? "SERVICE ONLINE" : serverState.Text, Symbol.Setting, isServerReady));
        badges.Children.Add(Badge("TLS + WSS", Symbol.Setting));
        badges.Children.Add(Badge("LOCAL CONTROL", Symbol.Home));
        copy.Children.Add(badges);
        canvas.Children.Add(copy);

        return new Border
        {
            Height = 210,
            CornerRadius = new CornerRadius(16),
            BorderBrush = AccentDim,
            BorderThickness = new Thickness(1),
            Child = canvas
        };
    }

    private static Border Badge(string text, Symbol symbol, bool highlighted = false)
    {
        var badgeForeground = highlighted ? Ink : Foreground;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new SymbolIcon(symbol) { Width = 13, Height = 13, Foreground = badgeForeground });
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            FontFamily = DisplayFont,
            Foreground = badgeForeground,
            CharacterSpacing = 70,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        return new Border
        {
            Background = highlighted ? Accent : new SolidColorBrush(Color.FromArgb(225, 30, 39, 53)),
            BorderBrush = highlighted ? Accent : Divider,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 5, 9, 5),
            Child = content
        };
    }

    private static Border Card(bool accented = false) => new()
    {
        Background = accented ? SurfaceRaised : Surface,
        BorderBrush = accented ? AccentDim : Divider,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(18)
    };

    private static Border InfoCard(string eyebrow, string title, string detail, Symbol symbol)
    {
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new Border
        {
            Width = 38,
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(255, 31, 45, 62)),
            BorderBrush = AccentDim,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Child = new SymbolIcon(symbol) { Foreground = Accent, Width = 17, Height = 17 }
        });
        content.Children.Add(Eyebrow(eyebrow));
        content.Children.Add(Label(title, 18, Foreground, true));
        content.Children.Add(Body(detail));
        var card = Card();
        card.Child = content;
        return card;
    }

    private static Border ManagementSection(string eyebrow, string description, out ListView list, out Button removeButton)
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(Eyebrow(eyebrow));
        content.Children.Add(Body(description));
        list = new ListView { MinHeight = 180, MaxHeight = 260, SelectionMode = ListViewSelectionMode.Single };
        content.Children.Add(list);
        removeButton = IconButton("Remove selected", Symbol.Delete);
        removeButton.HorizontalAlignment = HorizontalAlignment.Left;
        content.Children.Add(removeButton);
        var card = Card();
        card.Child = content;
        return card;
    }

    private static StackPanel UploadPanel(string eyebrow, string description)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Eyebrow(eyebrow));
        panel.Children.Add(Body(description));
        return panel;
    }

    private static Border MetricCard(string eyebrow, string value, string detail)
    {
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(Eyebrow(eyebrow));
        content.Children.Add(Label(value, 26, Foreground, true));
        content.Children.Add(new TextBlock { Text = detail, FontSize = 12.5, FontFamily = AppFont, Foreground = Muted, TextWrapping = TextWrapping.Wrap, LineHeight = 18 });
        var card = Card();
        card.Child = content;
        return card;
    }

    private static Border MutableMetricCard(string eyebrow, string value, string detail, out TextBlock valueBlock, out TextBlock detailBlock)
    {
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(Eyebrow(eyebrow));
        valueBlock = Label(value, 26, Foreground, true);
        detailBlock = new TextBlock { Text = detail, FontSize = 12.5, FontFamily = AppFont, Foreground = Muted, TextWrapping = TextWrapping.Wrap, LineHeight = 18 };
        content.Children.Add(valueBlock);
        content.Children.Add(detailBlock);
        var card = Card();
        card.Child = content;
        return card;
    }

    private static Button IconButton(string text, Symbol symbol)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new SymbolIcon(symbol));
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 13.5,
            FontFamily = AppFont,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        return new Button
        {
            Content = content,
            Background = SurfaceRaised,
            BorderBrush = Divider,
            Foreground = Foreground,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(14, 8, 14, 8)
        };
    }

    private static void AddCatalogEntries(StackPanel target, string kind, JsonElement[] entries)
    {
        foreach (var entry in entries.Take(8))
        {
            var name = JsonText(entry, "name", "id");
            var detail = JsonText(entry, "version", "contentType", "id");
            target.Children.Add(StatusRow(kind, string.IsNullOrWhiteSpace(detail) || detail == name ? name : $"{name} · {detail}"));
        }
        if (entries.Length > 8)
            target.Children.Add(StatusRow(kind, $"+{entries.Length - 8} more"));
    }

    private static void AddBrowserGroup(ListView target, string kind, JsonArray? entries, string nameProperty, string idProperty)
    {
        if (entries is null) return;
        foreach (var entry in entries.OfType<JsonObject>())
        {
            var name = NodeText(entry, nameProperty, NodeText(entry, idProperty));
            var id = NodeText(entry, idProperty);
            target.Items.Add($"{kind}  ·  {name}{(string.IsNullOrWhiteSpace(id) || id == name ? "" : $"  ·  {id}")}");
        }
    }

    private static string NodeText(JsonObject node, string property, string fallback = "")
    {
        var value = node[property];
        return value is null ? fallback : value.ToString();
    }

    private static string PrettyJson(JsonNode node) => node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static string ValidationSummary(JsonElement result, string lead)
    {
        var messages = new List<string> { lead };
        foreach (var property in new[] { "errors", "warnings" })
        {
            if (!result.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array) continue;
            messages.AddRange(values.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))!);
        }
        return string.Join(" ", messages);
    }

    private static string NewRulePackTemplate() => """
    {
      "schemaVersion": "2",
      "id": "new-tcg",
      "name": "New Trading Card Game",
      "version": "1.0.0",
      "description": "A system-neutral trading card game ruleset.",
      "gameSystem": "custom",
      "minimumPlayers": 2,
      "maximumPlayers": 2,
      "startingRules": {
        "startingHand": 1
      },
      "zones": [
        { "id": "deck", "name": "Deck", "ownerScope": "player", "visibility": "hidden", "ordered": true },
        { "id": "hand", "name": "Hand", "ownerScope": "player", "visibility": "owner" },
        { "id": "table", "name": "Table", "ownerScope": "player", "visibility": "public" },
        { "id": "discard", "name": "Discard", "ownerScope": "player", "visibility": "public", "ordered": true }
      ],
      "resources": [
        { "id": "energy", "name": "Energy", "scope": "player", "initialValue": 1, "minimumValue": 0, "maximumValue": 20 }
      ],
      "phases": [
        { "id": "main", "name": "Main", "allowedActions": [ "play_card", "end_turn" ] }
      ],
      "actions": [
        {
          "id": "play_card",
          "name": "Play card",
          "sourceZone": "hand",
          "destinationZone": "table",
          "requiresActivePlayer": true,
          "costs": { "energy": 1 },
          "conditions": [ { "kind": "source_in_zone", "subject": "actor" } ],
          "effects": [ { "kind": "emit_event", "eventType": "card_played", "message": "A card was played." } ]
        },
        {
          "id": "end_turn",
          "name": "End turn",
          "requiresActivePlayer": true,
          "effects": [ { "kind": "end_turn" } ]
        }
      ],
      "cards": [
        {
          "id": "starter-card",
          "name": "Starter Card",
          "type": "card",
          "cost": 1,
          "power": 0,
          "toughness": 0,
          "text": "A starter card for this game system.",
          "tags": [ "starter" ],
          "properties": {},
          "abilities": []
        }
      ],
      "metadata": {}
    }
    """;

    private static string NewWorkflowTemplate() => """
    {
      "schemaVersion": "1",
      "id": "new-workflow",
      "name": "New Workflow",
      "version": "1.0.0",
      "initialState": "draft",
      "states": [ "draft", "review", "published" ],
      "terminalStates": [ "published" ],
      "transitions": [
        { "event": "submit", "from": "draft", "to": "review" },
        { "event": "approve", "from": "review", "to": "published" },
        { "event": "return", "from": "review", "to": "draft" }
      ],
      "metadata": {},
      "stateMetadata": {}
    }
    """;

    private static string JsonText(JsonElement element, params string[] properties)
    {
        foreach (var property in properties)
        {
            if (!element.TryGetProperty(property, out var value))
                continue;
            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }
        return "Unnamed";
    }

    private static UIElement RankingRow(int rank, string player, int rating)
    {
        var row = new Grid { Padding = new Thickness(8, 10, 8, 10), ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.Children.Add(new TextBlock { Text = $"#{rank}", FontSize = 16, FontFamily = DisplayFont, Foreground = rank <= 3 ? Accent : Muted, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var name = new TextBlock { Text = player, FontSize = 15, FontFamily = AppFont, Foreground = Foreground, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        var score = new TextBlock { Text = $"{rating:N0} rating", FontSize = 13, FontFamily = AppFont, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(score, 2);
        row.Children.Add(score);
        return row;
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
        : $"{Math.Max(0, duration.Minutes)}m {duration.Seconds}s";

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.##} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.##} MB",
        >= 1024L => $"{bytes / 1024d:0.##} KB",
        _ => $"{bytes:N0} bytes"
    };

    private static UIElement StatusRow(string label, string value)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = label, FontSize = 13.5, FontFamily = AppFont, Foreground = Muted });
        var valueBlock = new TextBlock { Text = value, FontSize = 13.5, FontFamily = AppFont, Foreground = Foreground, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(valueBlock);
        return row;
    }

    private static TextBlock Eyebrow(string text) => new()
    {
        Text = text,
        FontSize = 10.5,
        FontFamily = DisplayFont,
        Foreground = Accent,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        CharacterSpacing = 110
    };

    private static TextBlock Label(string text, double size, Brush color, bool strong = false) => new()
    {
        Text = text,
        FontSize = size,
        FontFamily = size >= 18 ? DisplayFont : AppFont,
        Foreground = color,
        FontWeight = strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        CharacterSpacing = size >= 24 ? -10 : 0,
        LineHeight = size >= 24 ? size * 1.16 : 0,
        TextWrapping = TextWrapping.Wrap
    };

    private static TextBlock Body(string text) => new()
    {
        Text = text,
        FontSize = 14.5,
        FontFamily = AppFont,
        Foreground = Muted,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 22
    };

    private static SolidColorBrush Background => new(Color.FromArgb(255, 13, 18, 27));
    private static SolidColorBrush Surface => new(Color.FromArgb(255, 21, 28, 39));
    private static SolidColorBrush SurfaceRaised => new(Color.FromArgb(255, 30, 39, 53));
    private static SolidColorBrush Divider => new(Color.FromArgb(255, 55, 67, 84));
    private static SolidColorBrush AccentDim => new(Color.FromArgb(255, 50, 82, 118));
    private static SolidColorBrush Accent => new(Color.FromArgb(255, 82, 148, 214));
    private static SolidColorBrush Foreground => new(Color.FromArgb(255, 235, 240, 246));
    private static SolidColorBrush Muted => new(Color.FromArgb(255, 160, 171, 185));
    private static SolidColorBrush Ink => new(Color.FromArgb(255, 247, 250, 253));
    private static SolidColorBrush Warning => new(Color.FromArgb(255, 224, 169, 80));
    private static SolidColorBrush Danger => new(Color.FromArgb(255, 225, 101, 112));
    // Use fixed Windows font families that resolve reliably in unpackaged WinUI.
    private static FontFamily AppFont => new("Segoe UI");
    private static FontFamily DisplayFont => new("Bahnschrift");
    private static FontFamily MonoFont => new("Consolas");

    private sealed record AdminSummaryRow(int Users, int DisabledUsers, int Games, int StateMachines, int Assets, int RecentLogs, int FriendConnections, int Messages, int Decks, int StoreProducts, int ContentPacks, int MatchmakingQueues, int WaitingPlayers, int CurrencyTransactions);
    private sealed record AdminUserRow(string Id, string Handle, int Rating, string Role, bool Disabled);
    private sealed record PlayerPresenceRow(string Id, string Handle, int Rating, DateTimeOffset LastSeenAt);
    private sealed record PresenceSnapshotRow(int OnlinePlayers, List<PlayerPresenceRow> Players, DateTimeOffset GeneratedAt);
    private sealed record DirectMessageRow(string Id, string SenderId, string RecipientId, string Body, DateTimeOffset SentAt, bool Flagged, bool Removed);
    private sealed record AdminGameChatChannelRow(string GameId, List<string> Players, List<string> Messages, bool Finished);
    private sealed record AdminChatSnapshotRow(int OnlinePlayers, List<DirectMessageRow> DirectMessages, List<AdminGameChatChannelRow> GameChannels, DateTimeOffset GeneratedAt);
    private sealed record AuditLogRow(DateTimeOffset At, string Level, string Category, string Message, string? ActorId, string? RequestPath, int? StatusCode);
    private sealed record BotProfileRow(string Id, string Name, string Personality, string Difficulty, string ModelVersion, string MatchmakingPool, bool Enabled, int DailyMatchLimit, DateTimeOffset UpdatedAt);
    private sealed record BotSimulationRunRow(string Id, string BotId, int Matches, decimal EstimatedWinRate, int AverageTurns, DateTimeOffset CompletedAt);
    private sealed record MatchmakingQueueRow(string Id, string Name, string Mode, int MinPlayers, int MaxPlayers, int InitialRatingRange, int RatingExpansionPerSecond, int MaxWaitSeconds, bool BotBackfill, bool Enabled, DateTimeOffset UpdatedAt);
    private sealed record MatchmakingQueueStatusRow(MatchmakingQueueRow Queue, int WaitingPlayers, int WaitingParties, int LongestWaitSeconds);
    private sealed record MatchmakingQueueEntryRow(string Id, string QueueId, string UserId, int Rating, int PartySize, string Region, DateTimeOffset JoinedAt);
    private sealed record MatchmakingRunResultRow(string QueueId, int MatchesCreated, int PlayersMatched, int BotsAdded, int PlayersRemaining, List<string> GameIds);
    private sealed record OperationsSummaryRow(int Bots, int ActiveBots, int Campaigns, int ActiveCampaigns, int Rewards, int ActiveProducts, int EarlyWarnings);
    private sealed record EarlyWarningAlertRow(string Id, string Severity, string Category, string Title, string Detail, DateTimeOffset DetectedAt);
    private sealed record LiveOpsCampaignRow(string Id, string Name, string Kind, string Description, DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool Active, DateTimeOffset UpdatedAt);
    private sealed record RewardCampaignRow(string Id, string Name, string Trigger, string RewardType, int Amount, int? DistributionLimit, bool Active, DateTimeOffset UpdatedAt);
    private sealed record ManagedAchievementRow(string Id, string Name, string Description, int Points, bool Active, DateTimeOffset UpdatedAt);
    private sealed record StoreProductRow(string Id, string Name, string Description, decimal Price, string Currency, string? AssetId, string? ContentPackId, bool Active, DateTimeOffset UpdatedAt);
    private sealed record EconomyConfigurationRow(string SoftCurrency, string PremiumCurrency, int DailySoftCurrencyCap, decimal StoreTaxPercent, DateTimeOffset UpdatedAt);
    private sealed record CurrencyTransactionRow(string Id, string UserId, string Currency, long Amount, long BalanceAfter, string Reason, string ActorId, DateTimeOffset CreatedAt);
    private sealed record ArchiveDescriptorRow(string Id, string Name, string Scope, long Length, string Sha256, DateTimeOffset CreatedAt);
    private sealed record ArchiveVerificationRow(string Id, bool IsValid, string Sha256, long Length, DateTimeOffset VerifiedAt);
    private sealed record ContentPackRow(string Id, string Name, string Version, string Description, string FileName, string ContentType, long Length, string Sha256, DateTimeOffset UploadedAt, bool Active);
    private sealed record GamePackageContentRow(int Cards, int Zones, int Frames, int Assets, int Actions, int Abilities, int Keywords, int TurnFlows, int VictoryRules);
    private sealed record GamePackageVersionRow(string PackageId, string GameId, string Name, string Version, string Description, string FileName, long Length, string Sha256, DateTimeOffset ImportedAt, bool Active, GamePackageContentRow Content);
}
