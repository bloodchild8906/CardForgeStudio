using System.Security.Cryptography;
using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

public sealed class ContentPackService(AppState state, AuditLogService audit)
{
    public IReadOnlyList<ContentPackDescriptor> Packs() => state.ContentPacks.Values
        .OrderByDescending(pack => pack.UploadedAt)
        .Select(Describe)
        .ToList();

    public ContentPackDescriptor Upload(string id, string name, string version, string description, bool active, string fileName, string contentType, Stream content, string actorId)
    {
        Validate(id, name, version, description);
        using var output = new MemoryStream();
        content.CopyTo(output);
        var bytes = output.ToArray();
        if (bytes.Length == 0) throw new InvalidOperationException("Content pack file cannot be empty.");
        var record = new ContentPackRecord(
            id.Trim(),
            name.Trim(),
            version.Trim(),
            description.Trim(),
            Path.GetFileName(fileName),
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            bytes,
            Convert.ToHexString(SHA256.HashData(bytes)),
            DateTimeOffset.UtcNow,
            active);
        state.ContentPacks[record.Id] = record;
        audit.Write("Information", "content-packs", $"Uploaded content pack {record.Id} version {record.Version}.", actorId);
        return Describe(record);
    }

    public ContentPackDescriptor SetActive(string id, bool active, string actorId)
    {
        if (!state.ContentPacks.TryGetValue(id, out var current)) throw new KeyNotFoundException("Content pack not found.");
        var updated = current with { Active = active };
        state.ContentPacks[id] = updated;
        audit.Write("Information", "content-packs", $"Set content pack {id} active={active}.", actorId);
        return Describe(updated);
    }

    public bool Remove(string id, string actorId)
    {
        if (state.StoreProducts.Values.Any(product => string.Equals(product.ContentPackId, id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The content pack is referenced by a store product.");
        if (!state.ContentPacks.TryRemove(id, out _)) return false;
        audit.Write("Information", "content-packs", $"Removed content pack {id}.", actorId);
        return true;
    }

    public ContentPackRecord Get(string id) => state.ContentPacks.TryGetValue(id, out var pack)
        ? pack
        : throw new KeyNotFoundException("Content pack not found.");

    private static ContentPackDescriptor Describe(ContentPackRecord pack) => new(
        pack.Id,
        pack.Name,
        pack.Version,
        pack.Description,
        pack.FileName,
        pack.ContentType,
        pack.Content.LongLength,
        pack.Sha256,
        pack.UploadedAt,
        pack.Active);

    private static void Validate(string id, string name, string version, string description)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Trim().Length > 80 || id.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_')))
            throw new InvalidOperationException("Content pack ID must be up to 80 letters, numbers, hyphens, or underscores.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 160 || string.IsNullOrWhiteSpace(version) || version.Trim().Length > 40)
            throw new InvalidOperationException("Content pack name and version are required.");
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 1_000)
            throw new InvalidOperationException("Content pack description is required and must be at most 1,000 characters.");
    }
}
