using BruTile;

namespace TheEye.Core.Maps;

/// <summary>
/// Answers for tile columns past either edge of the world with the real tile at
/// that place. Mapsui fetches and caches tiles for one viewport at a time, so the
/// tile layer cannot be asked to draw the map once per copy of the world like the
/// other layers; instead its schema (<see cref="MapBasemap.WrappedSchema"/>) simply
/// carries on past the edges. The tile keeps the column it was asked for in the
/// layer, which is what places it, and the request and the cache on disk only see
/// the real one, so moving to another copy of the world finds the tile already
/// stored. The coarsest levels are narrower than the view, so two columns on screen
/// can be one tile and both may ask for it once before the first answer is stored.
/// </summary>
public sealed class WrappedTileSource(IHttpTileSource source, ITileSchema schema) : IHttpTileSource
{
    public ITileSchema Schema => schema;

    public string Name => source.Name;

    public Attribution Attribution => source.Attribution;

    public Task<byte[]> GetTileAsync(HttpClient httpClient, TileInfo tileInfo, CancellationToken? cancellationToken = null) =>
        source.GetTileAsync(
            httpClient,
            new TileInfo { Index = RealIndex(tileInfo.Index), Extent = tileInfo.Extent },
            cancellationToken);

    /// <summary>A level holds 2^level columns, so the columns repeat with that period.</summary>
    public static TileIndex RealIndex(TileIndex index)
    {
        var columns = 1 << index.Level;
        return new TileIndex(((index.Col % columns) + columns) % columns, index.Row, index.Level);
    }
}
