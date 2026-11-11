using System.Text.Json;
using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

/// <summary>
/// Persistent asset repository used by remote cards, bundles and catalogs.
/// Files are stored by validated logical ID, with an atomic metadata write.
/// </summary>
public sealed class FileAssetStore : IAssetStore
{
    private readonly AppState state;
    private readonly string directory;
    private readonly object gate = new();

    private sealed record Metadata(string Id, string Name, string ContentType, string Sha256);

    public FileAssetStore(AppState state)
    {
        this.state = state;
        directory = Path.GetFullPath(Environment.GetEnvironmentVariable("CARDFORGE_ASSET_STORE") ??
            Path.Combine("artifacts", "cardforge-assets"));
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<Metadata>(File.ReadAllText(file));
                if (meta is null || !ValidId(meta.Id)) continue;
                var blob = BlobPath(meta.Id);
                if (!File.Exists(blob)) continue;
                var bytes = File.ReadAllBytes(blob);
                if (!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))
                    .Equals(meta.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                state.Assets[meta.Id] = new AssetRecord(meta.Id, meta.Name, meta.ContentType, bytes, meta.Sha256);
            }
            catch (Exception) { /* Corrupt individual assets never prevent server startup. */ }
        }
    }

    public void Put(AssetRecord asset)
    {
        if (!ValidId(asset.Id)) throw new InvalidOperationException("Invalid asset ID.");
        lock (gate)
        {
            var temp = BlobPath(asset.Id) + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temp, asset.Content);
                File.Move(temp, BlobPath(asset.Id), true);
                var meta = JsonSerializer.Serialize(new Metadata(asset.Id, asset.Name, asset.ContentType, asset.Sha256));
                var metaTemp = MetadataPath(asset.Id) + ".tmp-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(metaTemp, meta);
                File.Move(metaTemp, MetadataPath(asset.Id), true);
                state.Assets[asset.Id] = asset;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public bool TryGet(string id, out AssetRecord? asset) => state.Assets.TryGetValue(id, out asset);

    public bool Remove(string id)
    {
        lock (gate)
        {
            var removed = state.Assets.TryRemove(id, out _);
            if (File.Exists(BlobPath(id))) File.Delete(BlobPath(id));
            if (File.Exists(MetadataPath(id))) File.Delete(MetadataPath(id));
            return removed;
        }
    }

    public IEnumerable<AssetRecord> List() => state.Assets.Values;
    private static bool ValidId(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 120 &&
        id.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');
    private string BlobPath(string id) => Path.Combine(directory, id + ".bin");
    private string MetadataPath(string id) => Path.Combine(directory, id + ".json");
}
