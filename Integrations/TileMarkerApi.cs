using System.Collections.Generic;
using StardewValley;

namespace LotsOfKisses
{
    /// <summary>
    /// Minimal consumer-side contract for Tile Marker. Keeping the interface here makes the
    /// integration optional: Lots of Kisses doesn't need a compile-time reference to its DLL.
    /// </summary>
    public interface ITileMarkerApi
    {
        void RegisterCategory(string ownerModId, string category, string displayName);

        IReadOnlyList<string> GetMarkedTileRanges(
            string ownerModId,
            string category,
            string locationName
        );

        bool IsTileMarked(
            string ownerModId,
            string category,
            GameLocation location,
            int x,
            int y
        );
    }

    public interface ISharedTileMarkerApi : ITileMarkerApi
    {
        void RegisterCategoryWithSharedGroup(
            string ownerModId,
            string category,
            string displayName,
            string sharedGroup
        );
    }
}
