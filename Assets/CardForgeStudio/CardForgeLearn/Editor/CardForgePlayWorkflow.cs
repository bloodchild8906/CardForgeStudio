using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CardForge.Editor;
using TcgServer.Client.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CardForge.Learn
{
    /// <summary>
    /// One-click native Unity Dedicated Server workflow. No ASP.NET process,
    /// legacy management token, or server-side package importer is involved.
    /// </summary>
#if UNITY_6000_5_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    [InitializeOnLoad]
    public static class CardForgePlayWorkflow
    {
        private const string Configured = "CardForge.NativeSample.Enabled";
        private const string NativeLauncher = "Demo/Start-CardForgeDedicatedServer.ps1";
        private static bool busy;
        private static string status = "Generate the sample to configure native play.";
        private static bool allowPlay;

        public static event Action<string> StatusChanged;
        public static string Status => status;
        public static bool IsRunning => busy;
        public static bool IsConfigured =>
            EditorPrefs.GetBool(Configured, false) &&
            File.Exists(TcgTemplateProjectSetup.ScenePath);
        public static string BaseUrl => "http://127.0.0.1:5168";

        static CardForgePlayWorkflow()
        {
            EditorApplication.playModeStateChanged += OnPlayState;
        }

        [MenuItem("CardForge/Generate Sample Game")]
        public static void GenerateSampleFromMenu() => GenerateSample();

        [MenuItem("CardForge/Play Sample Game")]
        public static void PlaySampleFromMenu() => PlaySample();

        public static string GenerateSample()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before preparing the sample.");
            SetStatus("Preparing Astral Rift 3.0: 240 cards, three unique starters and matched illustrations...");
            var rules = AstralRiftNativeContent.Prepare();
            EnsureEditableAuthoring();
            // Populates or upgrades a real editable UXML-backed Unity scene;
            // existing transforms and Inspector overrides are preserved.
            TcgTemplateProjectSetup.CreateTemplateScene();
            TcgTemplateProjectSetup.ConfigureConnection(BaseUrl, AstralRiftNativeContent.PackId,
                AstralRiftNativeContent.PackId, AstralRiftNativeContent.Version);
            EditorPrefs.SetBool(Configured, true);
            SetStatus("Astral Rift 3.0 staged. Build the native host if needed, then press Play.");
            return rules;
        }
        private static void EnsureEditableAuthoring()
        {
            var root = Directory.GetParent(Application.dataPath)?.FullName ??
                throw new InvalidOperationException("Unity project directory missing.");
            var game = Path.Combine(root, "Assets", "CardForgeData",
                "game-definitions", "astral-rift-core.json");
            bool requiresSync = true;
            if (File.Exists(game))
            {
                try
                {
                    var data = JObject.Parse(File.ReadAllText(game));
                    var blockPath = Path.Combine(root, "Assets", "CardForgeData",
                        "blocks", "astral-rift-core", "block.json");
                    var block = JObject.Parse(File.ReadAllText(blockPath));
                    requiresSync = (string)data["packageVersion"] != AstralRiftNativeContent.Version ||
                        (block["cardIds"] as JArray)?.Count != 240 ||
                        (data["sampleDecks"] as JArray)?.Count != 3;
                }
                catch (Exception exception) when (exception is IOException ||
                    exception is Newtonsoft.Json.JsonException ||
                    exception is NullReferenceException)
                {
                    requiresSync = true;
                }
            }

            if (requiresSync)
                RunAuthoringScript(root, "Sync-AstralRiftAuthoring.ps1");

            // Generate type-specific card fronts without clobbering edited
            // versions; the script only creates missing layouts and links cards.
            RunAuthoringScript(root, "Ensure-AstralRiftTypeLayouts.ps1");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void RunAuthoringScript(string root, string filename)
        {
            var script = Path.Combine(root, "Demo", filename);
            if (Application.platform != RuntimePlatform.WindowsEditor)
                throw new PlatformNotSupportedException(
                    "The Astral Rift authoring importer currently needs the Windows editor.");
            if (!File.Exists(script))
                throw new FileNotFoundException("Missing Astral Rift authoring importer.", script);

            using (var process = Process.Start(new ProcessStartInfo("powershell.exe")
            {
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"",
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }))
            {
                if (process == null)
                    throw new InvalidOperationException("Could not start authoring preparation.");
                var output = process.StandardOutput.ReadToEnd();
                var errors = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(180000) || process.ExitCode != 0)
                    throw new InvalidOperationException(
                        "Astral Rift authoring preparation failed: " + errors + "\n" + output);
                UnityEngine.Debug.Log(output.Trim());
            }
        }

        public static void PlaySample()
        {
            if (busy || EditorApplication.isPlayingOrWillChangePlaymode) return;
            _ = PrepareAndPlay();
        }

        private static void OnPlayState(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                allowPlay = false;
                return;
            }
            if (change != PlayModeStateChange.ExitingEditMode || allowPlay || busy || !IsConfigured)
                return;
            if (!string.Equals(EditorSceneManager.GetActiveScene().path,
                TcgTemplateProjectSetup.ScenePath, StringComparison.OrdinalIgnoreCase)) return;
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += PlaySample;
        }

        private static async Task PrepareAndPlay()
        {
            busy = true;
            try
            {
                var root = Directory.GetParent(Application.dataPath)?.FullName ??
                    throw new InvalidOperationException("Unity project root missing.");
                // The legacy 2.1 .cardforge archive must never overwrite
                // the authoritative Astral Rift 3 native rulepack or its art.
                AstralRiftNativeContent.Prepare();
                var scene = TcgTemplateProjectSetup.ScenePath;
                if (!File.Exists(scene)) TcgTemplateProjectSetup.CreateTemplateScene();

                var nativeExe = Path.Combine(root, "Builds", "DedicatedServer", "CardForgeServer.exe");
                if (!File.Exists(nativeExe))
                    throw new FileNotFoundException("Build the native Unity Dedicated Server first: " +
                        "CardForge > Server > Build Native Windows Dedicated Server.", nativeExe);

                SetStatus("Checking the native Unity Dedicated Server...");
                var baseUrl = BaseUrl;
                if (!await Healthy(baseUrl))
                {
                    SetStatus("Starting the native server. The client remains paused...");
                    var launcher = Path.Combine(root, NativeLauncher.Replace('/', Path.DirectorySeparatorChar));
                    using (var process = Process.Start(new ProcessStartInfo("powershell.exe")
                    {
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" +
                            launcher + "\" -NoClient",
                        WorkingDirectory = root,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }))
                    {
                        if (process == null) throw new InvalidOperationException("Could not launch Unity server.");
                        var output = await process.StandardOutput.ReadToEndAsync();
                        var errors = await process.StandardError.ReadToEndAsync();
                        if (!process.WaitForExit(1000) || process.ExitCode != 0)
                            throw new InvalidOperationException("Native server startup failed: " + errors + output);
                        var marker = "CARDFORGE_NATIVE_READY ";
                        var line = output.Split('\n').FirstOrDefault(value => value.Contains(marker));
                        if (line != null)
                            baseUrl = line.Substring(line.IndexOf(marker, StringComparison.Ordinal) +
                                marker.Length).Trim();
                    }
                }

                if (!await Healthy(baseUrl))
                    throw new InvalidOperationException("Native Unity server failed readiness verification.");

                TcgTemplateProjectSetup.ConfigureConnection(baseUrl, AstralRiftNativeContent.PackId,
                    AstralRiftNativeContent.PackId, AstralRiftNativeContent.Version);

                SetStatus("Checking Astral Rift 3 rules and publishing only changed illustrations...");
                await AstralRiftNativeContent.PublishAsync(baseUrl);
                if (!string.Equals(EditorSceneManager.GetActiveScene().path, scene,
                    StringComparison.OrdinalIgnoreCase))
                    EditorSceneManager.OpenScene(scene);

                SetStatus("Native server ready. Starting the UI Toolkit client.");
                allowPlay = true;
                EditorApplication.isPlaying = true;
            }
            catch (Exception error)
            {
                allowPlay = false;
                SetStatus("Native play preparation failed: " + error.Message);
                UnityEngine.Debug.LogException(error);
                EditorUtility.DisplayDialog("CardForge native play", error.Message, "OK");
            }
            finally { busy = false; }
        }

        private static async Task<bool> Healthy(string url)
        {
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
                {
                    var response = await client.GetAsync(url.TrimEnd('/') + "/api/health");
                    if (!response.IsSuccessStatusCode) return false;
                    var body = await response.Content.ReadAsStringAsync();
                    return body.Contains("\"simulationMode\":\"Unity Dedicated Server\"") &&
                           body.Contains("\"simulationReady\":true") &&
                           body.Contains("\"status\":\"healthy\"");
                }
            }
            catch (HttpRequestException) { return false; }
            catch (TaskCanceledException) { return false; }
        }

        private static void SetStatus(string message)
        {
            status = message;
            StatusChanged?.Invoke(status);
        }
    }
}
