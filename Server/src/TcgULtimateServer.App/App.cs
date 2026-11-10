using Microsoft.UI.Xaml;
namespace TcgULtimateServer.App;
public partial class App : Application
{
    private Window? window;
    private ServerProcess? server;
    private readonly CancellationTokenSource shutdown = new();
    private bool isClosing;
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            try
            {
                var logPath = Path.Combine(Path.GetTempPath(), "TcgULtimateServer-client-error.log");
                File.AppendAllText(logPath, $"{DateTimeOffset.Now:O}{Environment.NewLine}{args.Exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
            }
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        server = new ServerProcess();
        var mainWindow = new MainWindow(server.ManagementKey);
        window = mainWindow;
        mainWindow.SetServerStarting();
        window.Closed += (_, _) =>
        {
            isClosing = true;
            shutdown.Cancel();
            _ = StopServerAsync();
        };
        window.Activate();
        _ = StartServerAsync();
    }

    private async Task StartServerAsync()
    {
        try
        {
            var result = await server!.StartAsync(shutdown.Token);
            if (!isClosing && window is MainWindow mainWindow)
                mainWindow.SetServerReady(result.StartedLocally);
        }
        catch (OperationCanceledException) when (isClosing) { }
        catch (Exception exception)
        {
            if (!isClosing && window is MainWindow mainWindow)
                mainWindow.SetServerError(exception.Message);
        }
    }

    private async Task StopServerAsync()
    {
        if (server is not null)
        {
            await server.DisposeAsync();
            server = null;
        }
        shutdown.Dispose();
    }
}
