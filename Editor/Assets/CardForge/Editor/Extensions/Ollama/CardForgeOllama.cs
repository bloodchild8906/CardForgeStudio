using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace CardForge.EditorUI
{
    /// <summary>Optional AI vision analysis for frame composition. Credentials never enter project assets.</summary>
    public static class CardForgeOllama
    {
        public const string CloudEndpoint = "https://ollama.com/api/chat";
        public const string LocalEndpoint = "http://localhost:11434/api/chat";
        public const string DefaultCloudModel = "gemma4:31b";
        const string Prompt = @"Analyze this DIGITAL TRADING CARD FRAME or card screenshot.
Identify rectangular reusable content slots on the card and their functions.
Especially title/name, artwork, cost/mana, type, rules text, attack, defense/health, faction icon.
Reply with ONLY a JSON object in this exact shape:
{""zones"":[{""name"":""Title"",""kind"":""Text"",""field"":""title"",""x"":40,""y"":40,""w"":750,""h"":70,""confidence"":0.9}]}
All x,y,w,h are INTEGERS in a 1000x1000 normalized image coordinate system.
Coordinates start at upper-left and all regions must fit entirely inside 1000x1000.
Kinds must be one of Text, Number, Image, Icon, StaticText.
Never include the entire background as a zone; identify only replaceable content.
Never invent regions that cannot be localized visually. Return 1-16 zones.";

        [Serializable] public sealed class ZoneProposal
        {
            public string name, kind, field;
            public float x, y, w, h, confidence;
        }
        [Serializable] public sealed class ProposalDocument
        {
            public List<ZoneProposal> zones = new List<ZoneProposal>();
        }
        [Serializable] sealed class Request
        {
            public string model;
            public List<ChatMessage> messages;
            public bool stream = false;
            public string format = "json";
        }
        [Serializable] sealed class ChatMessage
        {
            public string role;
            public string content;
            public List<string> images;
        }
        [Serializable] sealed class Response
        {
            public ChatMessage message = null;
            public string error = "";
        }

        // Editor SessionState persists across domain reloads but is not written into assets or EditorPrefs.
        // Prefer an OLLAMA_API_KEY OS environment variable for reliable unattended configuration.
        const string SessionKey = "CardForge.AI.Ollama.SessionApiKey";
        public static string SessionApiKey { set => SessionState.SetString(SessionKey, (value ?? "").Trim()); }
        public static bool CloudMode
        {
            get => EditorPrefs.GetBool("CardForge.AI.Ollama.Cloud", true);
            set => EditorPrefs.SetBool("CardForge.AI.Ollama.Cloud", value);
        }
        public static string Model
        {
            get => EditorPrefs.GetString("CardForge.AI.Ollama.Model", DefaultCloudModel);
            set => EditorPrefs.SetString("CardForge.AI.Ollama.Model", string.IsNullOrWhiteSpace(value) ? DefaultCloudModel : value.Trim());
        }
        public static string Endpoint => CloudMode ? CloudEndpoint : LocalEndpoint;
        public static bool HasCloudKey => !string.IsNullOrWhiteSpace(ApiKey);
        static string ApiKey
        {
            get
            {
                var environment = Environment.GetEnvironmentVariable("OLLAMA_API_KEY");
                var transient = SessionState.GetString(SessionKey, "");
                return !string.IsNullOrWhiteSpace(transient) ? transient : (environment ?? "").Trim();
            }
        }

        public static async Task<ProposalDocument> AnalyzeAsync(Texture2D image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (CloudMode && !HasCloudKey)
                throw new InvalidOperationException("Ollama Cloud requires an API key. Set OLLAMA_API_KEY or paste a temporary key into AI settings.");
            string model = Model;
            if (string.IsNullOrWhiteSpace(model)) throw new InvalidOperationException("Choose a vision-capable Ollama model.");

            // JPEG keeps multimodal requests affordable even for very large reference images.
            byte[] imageBytes = CardForgeImageAssets.EncodeForVision(image, 1280);
            var request = new Request
            {
                model = model,
                messages = new List<ChatMessage>
                {
                    new ChatMessage { role = "user", content = Prompt,
                        images = new List<string> { Convert.ToBase64String(imageBytes) } }
                }
            };
            var body = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(request));
            using (var web = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                web.uploadHandler = new UploadHandlerRaw(body);
                web.downloadHandler = new DownloadHandlerBuffer();
                web.timeout = 120;
                web.SetRequestHeader("Content-Type", "application/json");
                if (CloudMode) web.SetRequestHeader("Authorization", "Bearer " + ApiKey);
                var operation = web.SendWebRequest();
                while (!operation.isDone) await Task.Delay(100);
                if (web.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException("Ollama request failed (" + web.responseCode +
                        "): " + (web.downloadHandler.text ?? web.error));
                var response = JsonUtility.FromJson<Response>(web.downloadHandler.text);
                if (response == null || !string.IsNullOrEmpty(response.error))
                    throw new FormatException("Ollama returned an error: " + (response?.error ?? "empty response"));
                if (string.IsNullOrWhiteSpace(response.message?.content))
                    throw new FormatException("Ollama did not return a zone description.");
                return ParseProposals(response.message.content);
            }
        }

        /// <summary>
        /// Validates a model's JSON response independently of HTTP; test this with fixture responses.
        /// Coordinates remain in normalized 1000x1000 space until FrameGeometry converts them.
        /// </summary>
        public static ProposalDocument ParseProposals(string content)
        {
                if (string.IsNullOrWhiteSpace(content)) throw new FormatException("Empty AI response.");
                string json = content.Trim();
                if (json.StartsWith("```", StringComparison.Ordinal))
                {
                    int start = json.IndexOf('\n'), end = json.LastIndexOf("```", StringComparison.Ordinal);
                    if (start >= 0 && end > start) json = json.Substring(start + 1, end - start - 1).Trim();
                }
                var result = JsonUtility.FromJson<ProposalDocument>(json);
                if (result == null || result.zones == null || result.zones.Count == 0)
                    throw new FormatException("Ollama response contains no usable zones. Try a clearer card-frame image or a different vision model.");
                result.zones = result.zones.Where(z =>
                    z != null && z.w > 5 && z.h > 5 && !float.IsNaN(z.x) && !float.IsNaN(z.y))
                    .Take(24).ToList();
                foreach (var z in result.zones)
                {
                    z.name = string.IsNullOrWhiteSpace(z.name) ? "Content region" : z.name.Trim();
                    z.field = string.IsNullOrWhiteSpace(z.field) ? "field" : z.field.Trim();
                    z.x = Mathf.Clamp(z.x, 0, 990);
                    z.y = Mathf.Clamp(z.y, 0, 990);
                    z.w = Mathf.Clamp(z.w, 10, 1000 - z.x);
                    z.h = Mathf.Clamp(z.h, 10, 1000 - z.y);
                    z.confidence = Mathf.Clamp01(z.confidence);
                }
                return result;
        }
    }

    /// <summary>Simple API settings panel reusable by all CardForge AI features.</summary>
    public sealed class CardForgeAiSettings : EditorWindow
    {
        [MenuItem("CardForge/AI Provider Settings")]
        public static void Open()
        {
            var window = GetWindow<CardForgeAiSettings>("CardForge AI");
            window.minSize = new Vector2(460, 250);
            window.Show();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = 18; root.style.paddingTop = 16;
            root.style.paddingRight = 18;
            var heading = new UnityEngine.UIElements.Label("Ollama vision provider");
            heading.style.fontSize = 20;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(heading);
            root.Add(new UnityEngine.UIElements.HelpBox(
                "Ollama Cloud is the default. Images are transmitted only when you choose Analyze. " +
                "The model can propose regions but you must review them in Frame Composer.",
                UnityEngine.UIElements.HelpBoxMessageType.Info));
            var cloud = new UnityEngine.UIElements.Toggle("Use Ollama Cloud (default)") { value = CardForgeOllama.CloudMode };
            cloud.RegisterValueChangedCallback(e => { CardForgeOllama.CloudMode = e.newValue; CreateGUI(); });
            root.Add(cloud);
            var endpoint = new UnityEngine.UIElements.Label("Endpoint: " + CardForgeOllama.Endpoint);
            root.Add(endpoint);
            var model = new UnityEngine.UIElements.TextField("Vision model")
            { value = CardForgeOllama.Model, isDelayed = true };
            model.RegisterValueChangedCallback(e => CardForgeOllama.Model = e.newValue);
            root.Add(model);
            if (CardForgeOllama.CloudMode)
            {
                var key = new UnityEngine.UIElements.TextField("Session API key") { isPasswordField = true };
                key.RegisterValueChangedCallback(e => CardForgeOllama.SessionApiKey = e.newValue);
                root.Add(key);
                root.Add(new UnityEngine.UIElements.HelpBox(
                    "Best: configure OLLAMA_API_KEY in your OS environment, then restart Unity. " +
                    "Alternatively paste a key for this editor session; it is NOT saved in project files or EditorPrefs.",
                    UnityEngine.UIElements.HelpBoxMessageType.Warning));
                root.Add(new UnityEngine.UIElements.Button(() =>
                    Application.OpenURL("https://ollama.com/settings/keys")) { text = "Manage Ollama API keys" });
            }
        }
    }
}
