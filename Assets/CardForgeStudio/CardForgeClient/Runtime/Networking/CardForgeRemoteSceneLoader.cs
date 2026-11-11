using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
#if CARDFORGE_ADDRESSABLES
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
#endif

namespace TcgServer.Client
{
    [Serializable]
    public sealed class RemoteSceneEntry
    {
        public string address;
        public string title;
        public string category;
    }

    [Serializable]
    public sealed class RemoteSceneIndex
    {
        public string version;
        public RemoteSceneEntry[] scenes = Array.Empty<RemoteSceneEntry>();
    }

    /// <summary>
    /// Loads compatible remote Addressables catalogs and scenes without client updates.
    /// The authoritative server supplies metadata; bundles are downloaded by Addressables
    /// on demand, authenticated, versioned and cached by Unity.
    /// </summary>
    public sealed class CardForgeRemoteSceneLoader : MonoBehaviour
    {
        public const string CatalogId = "cardforge.scene-catalog";
        public const string IndexId = "cardforge.scene-index";

        private TcgRestClient rest;
        private TcgClientSession session;
        private TcgConnectionSettings settings;
        private bool catalogReady;
        private RemoteSceneIndex index = new RemoteSceneIndex();
#if CARDFORGE_ADDRESSABLES
        private AsyncOperationHandle<SceneInstance>? loadedScene;
#endif

        public RemoteSceneIndex Index => index;
        public bool CatalogReady => catalogReady;

        public void Configure(TcgRestClient restClient, TcgClientSession identity, TcgConnectionSettings server)
        {
            rest = restClient;
            session = identity;
            settings = server;
        }

        public IEnumerator Refresh(Action<bool> finished)
        {
            if (rest == null || session == null || !session.IsAuthenticated)
            {
                finished?.Invoke(false);
                yield break;
            }
            AssetDescriptorDto[] manifest = null;
            yield return rest.AssetManifest(value => manifest = value,
                error => Debug.LogWarning("Remote scene manifest unavailable: " + error));
            if (manifest == null)
            {
                finished?.Invoke(false);
                yield break;
            }

            var indexAsset = manifest.FirstOrDefault(asset => asset.id == IndexId);
            if (indexAsset != null)
            {
                byte[] bytes = null;
                yield return ReadVerified(indexAsset, result => bytes = result);
                if (bytes != null)
                {
                    var json = System.Text.Encoding.UTF8.GetString(bytes);
                    var parsed = JsonUtility.FromJson<RemoteSceneIndex>(json);
                    if (parsed != null && parsed.scenes != null) index = parsed;
                }
            }
            var catalog = manifest.FirstOrDefault(asset => asset.id == CatalogId);
            if (catalog == null)
            {
                finished?.Invoke(false);
                yield break;
            }

            byte[] catalogBytes = null;
            yield return ReadVerified(catalog, result => catalogBytes = result);
            if (catalogBytes == null)
            {
                finished?.Invoke(false);
                yield break;
            }
#if CARDFORGE_ADDRESSABLES
            var directory = Path.Combine(Application.persistentDataPath, "CardForge", "scene-catalogs");
            Directory.CreateDirectory(directory);
            var filename = Path.Combine(directory, catalog.sha256 + ".json");
            if (!File.Exists(filename)) File.WriteAllBytes(filename, catalogBytes);
            // Remote bundles require the same bearer token as the catalog.
            var serverRoot = settings.BaseUrl.TrimEnd('/') + "/api/assets/";
            Addressables.WebRequestOverride = request =>
            {
                if (session?.IsAuthenticated == true &&
                    request.url.StartsWith(serverRoot, StringComparison.OrdinalIgnoreCase))
                    request.SetRequestHeader("Authorization", "Bearer " + session.Token);
            };
            var handle = Addressables.LoadContentCatalogAsync(new Uri(filename).AbsoluteUri, true);
            yield return handle;
            catalogReady = handle.Status == AsyncOperationStatus.Succeeded;
#else
            Debug.LogWarning("Addressables is not installed or enabled for remote scene loading.");
            catalogReady = false;
#endif
            finished?.Invoke(catalogReady);
        }

        public IEnumerator Load(string address, Action<bool> finished)
        {
            if (!catalogReady || !index.scenes.Any(scene => scene.address == address))
            {
                finished?.Invoke(false);
                yield break;
            }
#if CARDFORGE_ADDRESSABLES
            if (loadedScene.HasValue)
            {
                var unload = Addressables.UnloadSceneAsync(loadedScene.Value, true);
                yield return unload;
                loadedScene = null;
            }
            var operation = Addressables.LoadSceneAsync(address, LoadSceneMode.Additive, true);
            yield return operation;
            if (operation.Status == AsyncOperationStatus.Succeeded)
            {
                loadedScene = operation;
                finished?.Invoke(true);
            }
            else finished?.Invoke(false);
#else
            finished?.Invoke(false);
            yield break;
#endif
        }

        private IEnumerator ReadVerified(AssetDescriptorDto descriptor, Action<byte[]> complete)
        {
            var cacheDir = Path.Combine(Application.persistentDataPath, "CardForge", "catalog-cache");
            Directory.CreateDirectory(cacheDir);
            var filename = Path.Combine(cacheDir, descriptor.sha256 + ".bin");
            if (File.Exists(filename))
            {
                var cached = File.ReadAllBytes(filename);
                if (Valid(cached, descriptor.sha256))
                {
                    complete(cached);
                    yield break;
                }
                File.Delete(filename);
            }
            using (var request = UnityWebRequest.Get(descriptor.url))
            {
                request.timeout = 30;
                request.SetRequestHeader("Authorization", "Bearer " + session.Token);
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success &&
                    Valid(request.downloadHandler.data, descriptor.sha256))
                {
                    var bytes = request.downloadHandler.data;
                    File.WriteAllBytes(filename, bytes);
                    complete(bytes);
                }
                else complete(null);
            }
        }

        private static bool Valid(byte[] bytes, string hash)
        {
            if (bytes == null || bytes.Length == 0 || string.IsNullOrWhiteSpace(hash)) return false;
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "")
                    .Equals(hash, StringComparison.OrdinalIgnoreCase);
        }
    }
}
