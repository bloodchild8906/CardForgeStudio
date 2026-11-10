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
    /// <summary>Imports open-licensed Google Fonts source binaries as usable Unity font assets.</summary>
    public sealed class CardForgeFontImporter : EditorWindow
    {
        [Serializable] sealed class RepositoryFile
        {
            public string name = "";
            public string download_url = "";
            public string type = "";
        }
        [Serializable] sealed class RepositoryList
        {
            public List<RepositoryFile> items = new List<RepositoryFile>();
        }
        [Serializable] sealed class FontManifest
        {
            public string family = "";
            public string source = "Google Fonts (github.com/google/fonts)";
            public string licenseAssetPath = "";
            public List<string> fontAssets = new List<string>();
        }

        const string FontRoot = "Assets/CardForgeData/fonts";
        const int MaxFontBytes = 20 * 1024 * 1024;
        string family = "Inter", status = "Choose a family and import its font files.", error = "";
        bool working;
        ScrollView installedList;
        Label state;

        [MenuItem("CardForge/Google Fonts Library")]
        public static void Open()
        {
            var window = GetWindow<CardForgeFontImporter>("Google Fonts");
            window.minSize = new Vector2(750, 550);
            window.Show(); window.Render();
        }

        public void CreateGUI() => Render();

        void Render()
        {
            rootVisualElement.Clear();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/CardForge/Editor/UI/USS/CardForge.uss");
            if (sheet != null) rootVisualElement.styleSheets.Add(sheet);
            var app = new VisualElement(); app.AddToClassList("cf-app"); app.AddToClassList("cf-tool-window");
            app.style.flexGrow = 1; rootVisualElement.Add(app); new ThemeController(app);
            var header = new VisualElement(); header.AddToClassList("cf-tool-header"); app.Add(header);
            var kicker = new Label("CARDFORGE / TYPE LIBRARY"); kicker.AddToClassList("cf-tool-kicker"); header.Add(kicker);
            var title = new Label("Google Fonts Importer"); title.AddToClassList("cf-tool-title"); header.Add(title);
            header.Add(new Label("Import licensed TTF/OTF fonts into Unity and use them in CardForge layout typography."));
            var bar = new VisualElement(); bar.AddToClassList("cf-tool-controls"); header.Add(bar);
            var entry = new TextField("Font family") { value = family, isDelayed = true }; entry.AddToClassList("cf-tool-asset");
            entry.RegisterValueChangedCallback(e => family = e.newValue); bar.Add(entry);
            var online = new Button(async () => await ImportFromGoogle()) { text = working ? "Importing…" : "Import Google font" };
            online.AddToClassList("cf-tool-primary"); online.SetEnabled(!working); bar.Add(online);
            bar.Add(new Button(ImportLocal) { text = "Import local TTF / OTF…" });
            bar.Add(new Button(() => Application.OpenURL("https://fonts.google.com/")) { text = "Browse fonts" });
            state = new Label(string.IsNullOrEmpty(error) ? status : error);
            state.AddToClassList("cf-tool-status"); header.Add(state);
            header.Add(new HelpBox("Google Fonts are open-licensed, but their terms vary. The importer downloads an available license alongside the font when importing from the official Google Fonts repository. Review the license before distribution. A network connection is required for online imports.", HelpBoxMessageType.Info));

            var main = new VisualElement(); main.AddToClassList("cf-font-main"); app.Add(main);
            var featured = new VisualElement(); featured.AddToClassList("cf-font-featured"); main.Add(featured);
            featured.Add(Heading("Suggested families"));
            var choices = new VisualElement(); choices.AddToClassList("cf-font-suggestions"); featured.Add(choices);
            foreach (var name in new[] { "Inter", "Cinzel", "Orbitron", "Rajdhani", "Alegreya", "Exo 2", "Uncial Antiqua", "Roboto" })
            {
                string candidate = name;
                choices.Add(new Button(() => { family = candidate; Render(); }) { text = candidate });
            }
            featured.Add(Heading("Preview"));
            var sample = new Label("CardForge  /  Legends & Lore\nAetherbound Guardian  •  8 / 8");
            sample.AddToClassList("cf-font-sample"); featured.Add(sample);
            if (AssetDatabase.FindAssets("t:Font", new[] { FontRoot }).Length > 0)
            {
                var fontAsset = AssetDatabase.FindAssets("t:Font", new[] { FontRoot })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<Font>)
                    .FirstOrDefault(f => f != null && f.name.IndexOf(family, StringComparison.OrdinalIgnoreCase) >= 0);
                if (fontAsset != null) sample.style.unityFont = fontAsset;
            }
            var installed = new VisualElement(); installed.AddToClassList("cf-font-installed"); main.Add(installed);
            installed.Add(Heading("Installed font families"));
            installedList = new ScrollView(); installed.Add(installedList);
            RenderInstalled();
        }

        void RenderInstalled()
        {
            if (installedList == null) return;
            installedList.Clear();
            if (!Directory.Exists(Path.GetFullPath(FontRoot)))
            {
                installedList.Add(new Label("No imported Google Fonts yet.")); return;
            }
            var folders = Directory.GetDirectories(Path.GetFullPath(FontRoot));
            if (folders.Length == 0) { installedList.Add(new Label("No imported fonts yet.")); return; }
            foreach (var folder in folders.OrderBy(x => x))
            {
                var fonts = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                    .Where(p => p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                                p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)).ToList();
                if (fonts.Count == 0) continue;
                var item = new VisualElement(); item.AddToClassList("cf-font-entry"); installedList.Add(item);
                var title = new Label(Path.GetFileName(folder) + "  ·  " + fonts.Count + " variant" +
                    (fonts.Count == 1 ? "" : "s"));
                title.AddToClassList("cf-font-entry-title"); item.Add(title);
                var font = AssetDatabase.LoadAssetAtPath<Font>(PathToAsset(fonts[0]));
                var sample = new Label("The quick brown fox jumps over the lazy dog. 0123456789");
                if (font != null) sample.style.unityFont = font;
                sample.AddToClassList("cf-font-entry-sample"); item.Add(sample);
                item.Add(new Button(() => EditorUtility.RevealInFinder(folder)) { text = "Open assets" });
            }
        }

        static string PathToAsset(string absolute)
        {
            string fullAssets = Path.GetFullPath("Assets").Replace('\\', '/');
            string normalized = Path.GetFullPath(absolute).Replace('\\', '/');
            return "Assets" + normalized.Substring(fullAssets.Length);
        }
        static string FamilySlug(string value) =>
            new string((value ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        async Task ImportFromGoogle()
        {
            if (working) return;
            string slug = FamilySlug(family);
            if (slug.Length < 2 || slug.Length > 80)
            {
                EditorUtility.DisplayDialog("Google Fonts", "Enter a valid font family name.", "OK"); return;
            }
            working = true; error = ""; status = "Searching the official Google Fonts repository…"; Render();
            try
            {
                RepositoryList available = null;
                foreach (var category in new[] { "ofl", "apache", "ufl" })
                {
                    string url = "https://api.github.com/repos/google/fonts/contents/" + category + "/" + slug;
                    string json;
                    try { json = await DownloadText(url); }
                    catch (InvalidOperationException e) when (e.Message.Contains("404")) { continue; }
                    available = JsonUtility.FromJson<RepositoryList>("{\"items\":" + json + "}");
                    if (available != null && available.items != null) break;
                }
                if (available?.items == null) throw new InvalidOperationException(
                    "Google Fonts family not found. Check the spelling, or import a downloaded TTF/OTF file.");
                var fonts = available.items.Where(x => x.type == "file" &&
                    (x.name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                     x.name.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))).ToList();
                if (fonts.Count == 0) throw new InvalidOperationException(
                    "No TTF/OTF source file was found for that family. Download a desktop version and import it locally.");
                var license = available.items.FirstOrDefault(x => x.type == "file" &&
                    (x.name.Equals("OFL.txt", StringComparison.OrdinalIgnoreCase) ||
                     x.name.Equals("LICENSE.txt", StringComparison.OrdinalIgnoreCase) ||
                     x.name.Equals("UFL.txt", StringComparison.OrdinalIgnoreCase)));
                var manifest = new FontManifest { family = family.Trim() };
                string root = FontRoot + "/" + CardForgeImageAssets.SafeName(family);
                foreach (var font in fonts)
                {
                    if (!SafeDownloadUrl(font.download_url)) throw new InvalidOperationException("Unexpected font download host.");
                    status = "Downloading " + font.name; if (state != null) state.text = status;
                    var bytes = await DownloadBytes(font.download_url);
                    string target = root + "/" + font.name;
                    WriteAsset(target, bytes); manifest.fontAssets.Add(target);
                }
                if (license != null && SafeDownloadUrl(license.download_url))
                {
                    var content = await DownloadBytes(license.download_url);
                    manifest.licenseAssetPath = root + "/" + license.name;
                    WriteAsset(manifest.licenseAssetPath, content);
                }
                WriteAsset(root + "/manifest.json",
                    System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(manifest, true)));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                status = "Imported " + fonts.Count + " font file(s) from Google Fonts.";
                Render();
                EditorUtility.DisplayDialog("Font imported", status + "\n" +
                    (license == null ? "No license file was found; review the source license before release." :
                    "The source license is saved with the font."), "OK");
            }
            catch (Exception e) { error = e.Message; Render(); }
            finally { working = false; Render(); }
        }

        void ImportLocal()
        {
            var source = EditorUtility.OpenFilePanelWithFilters("Import font", "",
                new[] { "Font files", "ttf,otf", "All files", "*" });
            if (string.IsNullOrEmpty(source)) return;
            try
            {
                var bytes = File.ReadAllBytes(source);
                if (bytes.Length < 100 || bytes.Length > MaxFontBytes) throw new FormatException("Invalid font size.");
                string root = FontRoot + "/" + CardForgeImageAssets.SafeName(Path.GetFileNameWithoutExtension(source));
                string dest = root + "/" + Path.GetFileName(source);
                WriteAsset(dest, bytes);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                status = "Imported local font: " + Path.GetFileName(source); Render();
                EditorUtility.DisplayDialog("Font imported", "Check redistribution rights for local fonts before exporting your game.", "OK");
            }
            catch (Exception e) { error = e.Message; Render(); }
        }

        /// <summary>Allows only official, HTTPS source hosts for the Google Fonts file pipeline.</summary>
        public static bool SafeDownloadUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                (uri.Host == "raw.githubusercontent.com" || uri.Host == "github.com" ||
                 uri.Host == "fonts.gstatic.com");
        }

        static void WriteAsset(string assetPath, byte[] bytes)
        {
            string dest = Path.GetFullPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.WriteAllBytes(dest, bytes);
        }

        static async Task<string> DownloadText(string url)
        {
            var bytes = await DownloadBytes(url);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        static async Task<byte[]> DownloadBytes(string url)
        {
            using (var web = UnityWebRequest.Get(url))
            {
                web.timeout = 35;
                web.SetRequestHeader("Accept", "application/vnd.github+json");
                web.SetRequestHeader("User-Agent", "CardForge-Unity-Editor");
                var operation = web.SendWebRequest();
                while (!operation.isDone) await Task.Delay(100);
                if (web.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException("Download failed (" + web.responseCode + "): " + url);
                if (web.downloadHandler.data.Length > MaxFontBytes)
                    throw new InvalidOperationException("Font download exceeded 20MB.");
                return web.downloadHandler.data;
            }
        }

        static Label Heading(string title)
        {
            var label = new Label(title); label.AddToClassList("cf-tool-heading"); return label;
        }
    }
}
