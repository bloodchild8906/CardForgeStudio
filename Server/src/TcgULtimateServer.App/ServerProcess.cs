using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace TcgULtimateServer.App;

internal readonly record struct ServerStartResult(bool StartedLocally);

internal sealed class ServerProcess : IAsyncDisposable
{
    private static readonly Uri HealthEndpoint = new("https://localhost:5001/api/health");
    private readonly List<string> diagnostics = [];
    private Process? process;

    public ServerProcess()
    {
        ManagementKey = Environment.GetEnvironmentVariable("TCG_LOCAL_MANAGEMENT_KEY")
            ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Environment.SetEnvironmentVariable("TCG_LOCAL_MANAGEMENT_KEY", ManagementKey);
    }

    public string ManagementKey { get; }

    public async Task<ServerStartResult> StartAsync(CancellationToken cancellationToken = default)
    {
        if (await IsServerReadyAsync(cancellationToken))
            return new ServerStartResult(false);

        var projectPath = FindServerProject();
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["TCG_CLIENT_MANAGED_SERVER"] = "true";
        startInfo.Environment["TCG_LOCAL_MANAGEMENT_KEY"] = ManagementKey;
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--launch-profile");
        startInfo.ArgumentList.Add("TcgULtimateServer.Api");

        process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => CaptureDiagnostic(args.Data);
        process.ErrorDataReceived += (_, args) => CaptureDiagnostic(args.Data);

        if (!process.Start())
            throw new InvalidOperationException("The TcgULtimateServer API could not be started.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            for (var attempt = 0; attempt < 80; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await IsServerReadyAsync(cancellationToken))
                    return new ServerStartResult(true);

                if (process.HasExited)
                    throw new InvalidOperationException(BuildFailureMessage("The TcgULtimateServer API exited before it became ready."));

                await Task.Delay(375, cancellationToken);
            }

            throw new TimeoutException(BuildFailureMessage("The TcgULtimateServer API did not become healthy within 30 seconds."));
        }
        catch
        {
            await StopOwnedProcessAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync() => await StopOwnedProcessAsync();

    private async Task StopOwnedProcessAsync()
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
            process = null;
        }
    }

    private static async Task<bool> IsServerReadyAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            using var response = await client.GetAsync(HealthEndpoint, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return false;
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            return body.RootElement.TryGetProperty("service", out var service) &&
                   string.Equals(service.GetString(), "TcgULtimateServer", StringComparison.Ordinal);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void CaptureDiagnostic(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        lock (diagnostics)
        {
            diagnostics.Add(line);
            if (diagnostics.Count > 20)
                diagnostics.RemoveAt(0);
        }
    }

    private string BuildFailureMessage(string summary)
    {
        lock (diagnostics)
        {
            var details = string.Join(Environment.NewLine, diagnostics.TakeLast(6));
            return string.IsNullOrWhiteSpace(details) ? summary : $"{summary} {details}";
        }
    }

    private static string FindServerProject()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "TcgULtimateServer.Api", "TcgULtimateServer.Api.csproj");
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate the TcgULtimateServer API project. Launch the app from its project folder.");
    }
}
