using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CardForge.Editor;
using TcgServer.Client.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CardForge.Learn
{
    /// <summary>
    /// Owns the local sample-game loop: publish the current sample sources, start an isolated development
    /// server when necessary, import and activate the package, configure the client, and enter Play mode.
    /// </summary>
    [InitializeOnLoad]
    public static class CardForgePlayWorkflow
    {
        const int FirstPort = 5168;
        const int LastPort = 5198;
        const string EnabledKey = "CardForge.SamplePlay.Enabled";
        const string ManagementKeyKey = "CardForge.SamplePlay.ManagementKey";
        const string PortKey = "CardForge.SamplePlay.Port";
        const string PackageFolder = "Library/CardForgePlay/packages";
        static Process serverProcess;
        static bool running;
        static bool allowNextPlay;
        static string status = "Generate the sample to prepare one-click Play.";

        [Serializable]
        sealed class LocalManagementResponse
        {
            public string token;
        }

        static CardForgePlayWorkflow()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += StopOwnedServer;
        }

        public static event Action<string> StatusChanged;
        public static string Status => status;
        public static bool IsRunning => running;
        public static bool IsConfigured => EditorPrefs.GetBool(EnabledKey, false) && File.Exists(TcgTemplateProjectSetup.ScenePath);
        public static string BaseUrl => "http://127.0.0.1:" + EditorPrefs.GetInt(PortKey, FirstPort);

        [MenuItem("CardForge/Generate Sample Game")]
        public static void GenerateSampleFromMenu() => GenerateSample();

        [MenuItem("CardForge/Play Sample Game")]
        public static void PlaySampleFromMenu() => PlaySample();

        public static string GenerateSample()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before regenerating the sample game.");

            SetStatus("Generating Astral Rift cards, decks, rules, artwork, package, and client scene...");
            var releasePackage = CardForgeDemoGenerator.Generate();
            TcgTemplateProjectSetup.CreateTemplateScene();
            TcgTemplateProjectSetup.ConfigureConnection(BaseUrl, CardForgeDemoGenerator.GameId, CardForgeDemoGenerator.PackageId, CardForgeDemoGenerator.Version);
            EditorPrefs.SetBool(EnabledKey, true);
            SetStatus("Sample ready. Press Unity Play; CardForge will start the server and synchronize the package.");
            return releasePackage;
        }

        public static void PlaySample()
        {
            if (running) return;
            if (EditorApplication.isPlaying)
            {
                SetStatus("The CardForge client is already running.");
                return;
            }
            _ = PrepareAndEnterPlayModeAsync();
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                allowNextPlay = false;
                return;
            }
            if (change != PlayModeStateChange.ExitingEditMode || allowNextPlay || running || !ShouldPrepareForPlay()) return;

            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () =>
            {
                if (!running && !EditorApplication.isPlaying) _ = PrepareAndEnterPlayModeAsync();
            };
        }

        static bool ShouldPrepareForPlay()
        {
            if (!EditorPrefs.GetBool(EnabledKey, false)) return false;
            var activeScene = EditorSceneManager.GetActiveScene();
            return string.Equals(activeScene.path, TcgTemplateProjectSetup.ScenePath, StringComparison.OrdinalIgnoreCase);
        }

        static async Task PrepareAndEnterPlayModeAsync()
        {
            if (running) return;
            running = true;
            try
            {
                if (!SampleSourcesExist()) GenerateSample();
                if (!File.Exists(TcgTemplateProjectSetup.ScenePath)) TcgTemplateProjectSetup.CreateTemplateScene();

                EditorUtility.DisplayProgressBar("CardForge Play", "Validating and building the current sample package...", 0.12f);
                SetStatus("Validating and building the current sample package...");
                var package = PublishDevelopmentPackage(out var version);

                EditorUtility.DisplayProgressBar("CardForge Play", "Starting the local authoritative server if needed...", 0.38f);
                SetStatus("Starting the local authoritative server if needed...");
                var session = await EnsureServerAndSessionAsync();

                EditorUtility.DisplayProgressBar("CardForge Play", "Uploading and activating " + Path.GetFileName(package) + "...", 0.7f);
                SetStatus("Uploading and activating the current sample package...");
                await UploadAndActivateAsync(session, package);

                TcgTemplateProjectSetup.ConfigureConnection(BaseUrl, CardForgeDemoGenerator.GameId, CardForgeDemoGenerator.PackageId, version);
                if (!string.Equals(EditorSceneManager.GetActiveScene().path, TcgTemplateProjectSetup.ScenePath, StringComparison.OrdinalIgnoreCase))
                    EditorSceneManager.OpenScene(TcgTemplateProjectSetup.ScenePath, OpenSceneMode.Single);

                EditorUtility.DisplayProgressBar("CardForge Play", "Launching the configured Unity client...", 0.95f);
                SetStatus("Server ready, package active, launching the Astral Rift client...");
                await Task.Yield();
                allowNextPlay = true;
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
                allowNextPlay = false;
                SetStatus("Play preparation failed: " + exception.Message);
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("CardForge Play could not start", exception.Message, "OK");
            }
            finally
            {
                running = false;
                EditorUtility.ClearProgressBar();
            }
        }

        static bool SampleSourcesExist()
        {
            var block = Path.Combine(Application.dataPath, "CardForgeData", "blocks", CardForgeDemoGenerator.BlockId, "block.json");
            var definition = Path.Combine(Application.dataPath, "CardForgeData", "game-definitions", CardForgeDemoGenerator.BlockId + ".json");
            return File.Exists(block) && File.Exists(definition);
        }

        static string PublishDevelopmentPackage(out string version)
        {
            var store = new CardForgeStore();
            store.Reload();
            if (!store.Blocks.TryGetValue(CardForgeDemoGenerator.BlockId, out var block))
                throw new InvalidOperationException("The Astral Rift sample block is missing. Run Generate Sample first.");

            var game = CardForgeData.Copy(store.GetGameDefinition(block));
            version = "0.0.0-dev." + SourceFingerprint();
            game.packageVersion = version;
            var root = Directory.GetParent(Application.dataPath).FullName;
            var folder = Path.Combine(root, PackageFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, game.packageId + "-" + version + ".cardforge");
            return CardForgeGamePackageExporter.Export(store, block, game, path);
        }

        static string SourceFingerprint()
        {
            var root = Path.Combine(Application.dataPath, "CardForgeData");
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var relative = path.Substring(root.Length).Replace('\\', '/');
                    hash.AppendData(Encoding.UTF8.GetBytes(relative));
                    hash.AppendData(File.ReadAllBytes(path));
                }
                return BitConverter.ToString(hash.GetHashAndReset(), 0, 6).Replace("-", "").ToLowerInvariant();
            }
        }

        static async Task<string> EnsureServerAndSessionAsync()
        {
            var key = ManagementKey();
            var preferred = EditorPrefs.GetInt(PortKey, FirstPort);
            if (await IsHealthyAsync(preferred))
            {
                var existing = await TryCreateManagementSessionAsync(preferred, key);
                if (!string.IsNullOrWhiteSpace(existing)) return existing;
                preferred = FindAvailablePort();
                EditorPrefs.SetInt(PortKey, preferred);
            }

            StartServer(preferred, key);
            for (var attempt = 0; attempt < 120; attempt++)
            {
                if (serverProcess != null && serverProcess.HasExited)
                    throw new InvalidOperationException("The local server exited before it became ready. " + LastServerDiagnostic());
                if (await IsHealthyAsync(preferred))
                {
                    var token = await TryCreateManagementSessionAsync(preferred, key);
                    if (!string.IsNullOrWhiteSpace(token)) return token;
                }
                await Task.Delay(500);
            }
            throw new TimeoutException("The local CardForge server did not become ready within 60 seconds. " + LastServerDiagnostic());
        }

        static string ManagementKey()
        {
            var key = EditorPrefs.GetString(ManagementKeyKey, "");
            if (!string.IsNullOrWhiteSpace(key)) return key;
            key = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            EditorPrefs.SetString(ManagementKeyKey, key);
            return key;
        }

        static int FindAvailablePort()
        {
            for (var port = FirstPort; port <= LastPort; port++)
            {
                try
                {
                    var listener = new TcpListener(IPAddress.Loopback, port);
                    listener.Start();
                    listener.Stop();
                    return port;
                }
                catch (SocketException) { }
            }
            throw new InvalidOperationException("No free local CardForge development port was found between " + FirstPort + " and " + LastPort + ".");
        }

        static void StartServer(int port, string key)
        {
            if (serverProcess != null && !serverProcess.HasExited) return;
            var root = Directory.GetParent(Application.dataPath).FullName;
            var project = Path.Combine(root, "Server", "src", "TcgULtimateServer.Api", "TcgULtimateServer.Api.csproj");
            if (!File.Exists(project)) throw new FileNotFoundException("The integrated server project was not found.", project);

            var start = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.EnvironmentVariables["ASPNETCORE_URLS"] = "http://127.0.0.1:" + port;
            start.EnvironmentVariables["TCG_LOCAL_MANAGEMENT_KEY"] = key;
            start.EnvironmentVariables["TCG_CLIENT_MANAGED_SERVER"] = "true";
            start.EnvironmentVariables["TCG_LAUNCH_CLIENT"] = "false";
            start.EnvironmentVariables["CARDFORGE_GAME_PACKAGE_STORE"] = Path.Combine(root, "Library", "CardForgePlay", "server-packages");
            start.Arguments = "run --project \"" + project + "\" --configuration Debug --no-launch-profile";

            serverDiagnostics.Length = 0;
            serverProcess = new Process { StartInfo = start, EnableRaisingEvents = true };
            serverProcess.OutputDataReceived += (_, args) => CaptureServerDiagnostic(args.Data);
            serverProcess.ErrorDataReceived += (_, args) => CaptureServerDiagnostic(args.Data);
            if (!serverProcess.Start()) throw new InvalidOperationException("The local CardForge server process could not be started.");
            serverProcess.BeginOutputReadLine();
            serverProcess.BeginErrorReadLine();
            EditorPrefs.SetInt(PortKey, port);
        }

        static readonly StringBuilder serverDiagnostics = new StringBuilder();
        static readonly object diagnosticGate = new object();

        static void CaptureServerDiagnostic(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (diagnosticGate)
            {
                serverDiagnostics.AppendLine(line);
                if (serverDiagnostics.Length > 12000) serverDiagnostics.Remove(0, serverDiagnostics.Length - 12000);
            }
        }

        static string LastServerDiagnostic()
        {
            lock (diagnosticGate)
            {
                var lines = serverDiagnostics.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return lines.Length == 0 ? "Check the Unity Console for details." : lines[lines.Length - 1];
            }
        }

        static async Task<bool> IsHealthyAsync(int port)
        {
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) })
                using (var response = await client.GetAsync("http://127.0.0.1:" + port + "/api/health"))
                    return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException) { return false; }
            catch (TaskCanceledException) { return false; }
        }

        static async Task<string> TryCreateManagementSessionAsync(int port, string key)
        {
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) })
                using (var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + port + "/api/auth/local-management"))
                {
                    request.Headers.Add("X-Tcg-Management-Key", key);
                    using (var response = await client.SendAsync(request))
                    {
                        if (!response.IsSuccessStatusCode) return null;
                        var body = await response.Content.ReadAsStringAsync();
                        return JsonUtility.FromJson<LocalManagementResponse>(body)?.token;
                    }
                }
            }
            catch (HttpRequestException) { return null; }
            catch (TaskCanceledException) { return null; }
        }

        static async Task UploadAndActivateAsync(string token, string packagePath)
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
            using (var content = new MultipartFormDataContent())
            using (var file = new ByteArrayContent(File.ReadAllBytes(packagePath)))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.cardforge.game-package+zip");
                content.Add(file, "file", Path.GetFileName(packagePath));
                content.Add(new StringContent("true"), "activate");
                using (var response = await client.PostAsync(BaseUrl + "/api/admin/game-packages/import", content))
                {
                    var body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("The server rejected the generated game package (" + (int)response.StatusCode + "): " + body.Trim('"'));
                }
            }
        }

        static void StopOwnedServer()
        {
            try
            {
                if (serverProcess != null && !serverProcess.HasExited) serverProcess.Kill();
            }
            catch (InvalidOperationException) { }
            finally
            {
                serverProcess?.Dispose();
                serverProcess = null;
            }
        }

        static void SetStatus(string value)
        {
            status = value;
            StatusChanged?.Invoke(value);
            Debug.Log("CardForge Play: " + value);
        }
    }
}
