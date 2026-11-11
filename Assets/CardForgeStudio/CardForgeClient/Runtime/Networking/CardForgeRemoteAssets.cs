using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;
#if CARDFORGE_ADDRESSABLES
using UnityEngine.AddressableAssets;
#endif

namespace TcgServer.Client
{
    /// <summary>
    /// Lazy remote art provider: manifests contain metadata only. Individual files are
    /// downloaded on first use, verified by SHA-256, cached and reused thereafter.
    /// The same logical IDs can be resolved by a remote Addressables catalog.
    /// </summary>
    public sealed class CardForgeRemoteAssets : MonoBehaviour
    {
        private TcgRestClient api;
        private TcgClientSession session;
        private readonly Dictionary<string, AssetDescriptorDto> manifest = new();
        private readonly Dictionary<string, Texture2D> textures = new();
        private readonly HashSet<string> inflight = new();
        private readonly Dictionary<string, List<Action<Texture2D>>> callbacks = new();
        private bool manifestLoaded;

        public void Configure(TcgRestClient client, TcgClientSession identity)
        { api = client; session = identity; }

        public IEnumerator RefreshManifest()
        {
            if (api == null) yield break;
            yield return api.AssetManifest(assets =>
            {
                manifest.Clear();
                foreach (var asset in assets)
                    if (!string.IsNullOrWhiteSpace(asset.id))
                        manifest[asset.id] = asset;
                manifestLoaded = true;
            }, error => Debug.LogWarning("Asset manifest unavailable: " + error));
        }

        public void RequestTexture(string id, Action<Texture2D> completed)
        {
            if (textures.TryGetValue(id, out var cached))
            {
                completed(cached);
                return;
            }

            if (!callbacks.TryGetValue(id, out var listeners))
                callbacks[id] = listeners = new List<Action<Texture2D>>();
            listeners.Add(completed);

            if (inflight.Add(id))
                StartCoroutine(Fetch(id));
        }

        private IEnumerator Fetch(string id)
        {
            if (!manifestLoaded) yield return RefreshManifest();

#if CARDFORGE_ADDRESSABLES
            // An Addressables remote catalog may provide this key. Missing entries
            // fall back to the server's hash-verified per-asset endpoint.
            var handle = Addressables.LoadAssetAsync<Texture2D>(id);
            yield return handle;
            if (handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded &&
                handle.Result != null)
            {
                Complete(id, handle.Result);
                yield break;
            }
            Addressables.Release(handle);
#endif
            if (!manifest.TryGetValue(id, out var asset) || string.IsNullOrWhiteSpace(asset.sha256))
            {
                Complete(id, null);
                yield break;
            }

            var directory = Path.Combine(Application.persistentDataPath, "CardForge", "remote-assets");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, id + "." + asset.sha256 + ".bin");
            byte[] bytes = null;
            if (File.Exists(path))
            {
                var candidate = File.ReadAllBytes(path);
                if (Verify(candidate, asset.sha256)) bytes = candidate;
                else File.Delete(path);
            }
            if (bytes == null)
            {
                using (var request = UnityWebRequest.Get(asset.url))
                {
                    request.timeout = 20;
                    if (session != null && session.IsAuthenticated)
                        request.SetRequestHeader("Authorization", "Bearer " + session.Token);
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var candidate = request.downloadHandler.data;
                        if (Verify(candidate, asset.sha256))
                        {
                            bytes = candidate;
                            File.WriteAllBytes(path, bytes);
                        }
                        else Debug.LogWarning("Remote asset failed integrity verification: " + id);
                    }
                }
            }

            Texture2D texture = null;
            if (bytes != null)
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes, true))
                {
                    Destroy(texture);
                    texture = null;
                }
            }
            Complete(id, texture);
        }

        private void Complete(string id, Texture2D texture)
        {
            if (texture != null) textures[id] = texture;
            if (callbacks.TryGetValue(id, out var listeners))
            {
                foreach (var listener in listeners)
                    try { listener?.Invoke(texture); } catch (Exception exception) { Debug.LogException(exception); }
                callbacks.Remove(id);
            }
            inflight.Remove(id);
        }

        private static bool Verify(byte[] bytes, string expected)
        {
            if (bytes == null || bytes.Length == 0) return false;
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "")
                    .Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        private void OnDestroy()
        {
            foreach (var texture in textures.Values) if (texture != null) Destroy(texture);
            textures.Clear();
        }
    }
}
