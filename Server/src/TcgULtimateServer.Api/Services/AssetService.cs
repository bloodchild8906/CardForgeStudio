using System.Security.Cryptography;
using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

public sealed class AssetService(IAssetStore store)
{
    public const long MaxUploadBytes = 50 * 1024 * 1024;

    public AssetDescriptor Add(string id, string name, string contentType, byte[] content, string baseUrl)
    {
        ValidateMetadata(id, name, contentType);
        if (content.LongLength > MaxUploadBytes) throw new InvalidOperationException("Asset is too large.");
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var asset = new AssetRecord(id, name, contentType, content, hash);
        store.Put(asset);
        return Describe(asset, baseUrl);
    }

    public AssetDescriptor Upload(string id, string name, string contentType, Stream input, string baseUrl)
    {
        using var content = new MemoryStream();
        var buffer = new byte[64 * 1024];
        var total = 0L;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxUploadBytes) throw new InvalidOperationException("Asset is too large.");
            content.Write(buffer, 0, read);
        }
        return Add(id, name, contentType, content.ToArray(), baseUrl);
    }

    public IEnumerable<AssetDescriptor> Manifest(string baseUrl) => store.List().Select(asset => Describe(asset, baseUrl));
    public AssetRecord Get(string id) => store.TryGet(id, out var asset) && asset is not null ? asset : throw new KeyNotFoundException("Asset not found.");
    public bool Remove(string id) => store.Remove(id);
    private static AssetDescriptor Describe(AssetRecord asset, string baseUrl) => new(asset.Id, asset.Name, asset.ContentType, asset.Content.LongLength, asset.Sha256, $"{baseUrl.TrimEnd('/')}/api/assets/{Uri.EscapeDataString(asset.Id)}");
    private static void ValidateMetadata(string id, string name, string contentType)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 120 || id.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'))) throw new InvalidOperationException("Invalid asset id.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 240) throw new InvalidOperationException("Invalid asset name.");
        if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > 200 || contentType.Any(char.IsControl)) throw new InvalidOperationException("Invalid asset content type.");
    }
}
