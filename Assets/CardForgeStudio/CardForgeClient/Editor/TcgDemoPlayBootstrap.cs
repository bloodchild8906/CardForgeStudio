using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TcgServer.Client.Editor
{
    /// <summary>
    /// Gated native-only demo startup. Unity Play is cancelled until the standalone
    /// Unity Dedicated Server reports readiness. No ASP.NET fallback exists.
    /// </summary>
    [InitializeOnLoad]
    public static class TcgDemoPlayBootstrap
    {
        private const string ScenePath = "Assets/CardForgeStudio/CardForgeClient/Scenes/TcgClient.unity";
        private static Process launcher;
        private static bool starting;
        private static bool launchAllowed;
        private static DateTime lastCheck;

        static TcgDemoPlayBootstrap()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += CheckServer;
        }

        [MenuItem("CardForge/Demo/Play Astral Rift")]
        private static void PlayDemo()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(ScenePath)) TcgTemplateProjectSetup.CreateTemplateScene();
            else EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode ||
                !string.Equals(SceneManager.GetActiveScene().path, ScenePath,
                    StringComparison.OrdinalIgnoreCase))
                return;
            if (launchAllowed) { launchAllowed = false; return; }

            var settings = AssetDatabase.LoadAssetAtPath<TcgConnectionSettings>(
                TcgTemplateProjectSetup.SettingsPath);
            if (settings != null && IsNativeReady(settings.BaseUrl)) return;
            // Do not permit the client scene to run before the native host is healthy.
            EditorApplication.isPlaying = false;
            if (!starting) StartNativeServer();
        }

        private static void StartNativeServer()
        {
            var root = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(root)) return;
            var executable = Path.Combine(root, "Builds", "DedicatedServer",
                "CardForgeServer.exe");
            if (!File.Exists(executable))
            {
                UnityEngine.Debug.LogError("Native CardForge server build is required. Select " +
                    "CardForge > Server > Build Native Windows Dedicated Server. " +
                    "The old server is no longer supported.");
                return;
            }
            var script = Path.Combine(root, "Demo", "Start-CardForgeDedicatedServer.ps1");
            if (!File.Exists(script))
            {
                UnityEngine.Debug.LogError("Native startup script is missing: " + script);
                return;
            }
            try
            {
                launcher = Process.Start(new ProcessStartInfo("powershell.exe")
                {
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script +
                        "\" -NoClient",
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                starting = true;
                lastCheck = DateTime.UtcNow;
                UnityEngine.Debug.Log("CardForge: starting native Unity Dedicated Server.");
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError("Cannot launch the native server: " + exception.Message);
                starting = false;
            }
        }

        private static void CheckServer()
        {
            if (!starting || EditorApplication.isPlayingOrWillChangePlaymode ||
                DateTime.UtcNow - lastCheck < TimeSpan.FromSeconds(.5)) return;
            lastCheck = DateTime.UtcNow;

            var settings = AssetDatabase.LoadAssetAtPath<TcgConnectionSettings>(
                TcgTemplateProjectSetup.SettingsPath);
            if (settings != null && IsNativeReady(settings.BaseUrl))
            {
                StartPlayAfterReadiness();
                return;
            }

            if (launcher == null || !launcher.HasExited) return;
            // The startup script may have switched from an occupied port. Refresh the
            // connection asset before deciding readiness has failed.
            AssetDatabase.ImportAsset(TcgTemplateProjectSetup.SettingsPath,
                ImportAssetOptions.ForceUpdate);
            settings = AssetDatabase.LoadAssetAtPath<TcgConnectionSettings>(
                TcgTemplateProjectSetup.SettingsPath);
            if (settings != null && IsNativeReady(settings.BaseUrl))
            {
                StartPlayAfterReadiness();
                return;
            }
            UnityEngine.Debug.LogError("Native server startup failed. Run " +
                "Demo/Start-CardForgeDedicatedServer.ps1 for diagnostics.");
            starting = false;
            launcher.Dispose();
            launcher = null;
        }

        private static void StartPlayAfterReadiness()
        {
            starting = false;
            launcher?.Dispose();
            launcher = null;
            launchAllowed = true;
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    EditorApplication.isPlaying = true;
            };
        }

        private static bool IsNativeReady(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return false;
            try
            {
#pragma warning disable SYSLIB0014
                var request = WebRequest.CreateHttp(baseUrl.TrimEnd('/') + "/api/health");
#pragma warning restore SYSLIB0014
                request.Timeout = 800;
                using (var response = request.GetResponse() as HttpWebResponse)
                {
                    if (response?.StatusCode != HttpStatusCode.OK) return false;
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        var payload = reader.ReadToEnd();
                        return payload.Contains("Unity Dedicated Server") &&
                            payload.Contains("\"healthy\"") &&
                            payload.Contains("\"simulationReady\":true");
                    }
                }
            }
            catch (WebException) { return false; }
            catch (UriFormatException) { return false; }
        }
    }
}
