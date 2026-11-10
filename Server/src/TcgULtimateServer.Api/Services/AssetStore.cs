using TcgULtimateServer.Api.Models;

namespace TcgULtimateServer.Api.Services;

public interface IAssetStore
{
    void Put(AssetRecord asset);
    bool TryGet(string id, out AssetRecord? asset);
    bool Remove(string id);
    IEnumerable<AssetRecord> List();
}

public sealed class InMemoryAssetStore(AppState state) : IAssetStore
{
    public void Put(AssetRecord asset) => state.Assets[asset.Id] = asset;

    public bool TryGet(string id, out AssetRecord? asset) => state.Assets.TryGetValue(id, out asset);

    public bool Remove(string id) => state.Assets.TryRemove(id, out _);

    public IEnumerable<AssetRecord> List() => state.Assets.Values;
}
