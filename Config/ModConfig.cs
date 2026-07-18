using System.Collections.Generic;

namespace LotsOfKisses
{
    public enum BlushSmokeStyle
    {
        Style1 = 0,
        Style2 = 1
    }

    public enum KissClickPreference
    {
        Right = 0,
        Left = 1
    }

    public class ModConfig
    {
        // Master toggle for the mod.
        public bool ModEnabled { get; set; } = true;

        // Enable or disable multi-kiss sequences.
        public bool MultiKissEnabled { get; set; } = true;

        // When multi-kisses are disabled, replace a manually initiated vanilla kiss
        // with one equally weighted random tier, without starting a kiss chain.
        public bool RandomManualKissTier { get; set; } = false;

        // When multi-kisses are enabled, let a successful manual vanilla kiss start
        // the full chain immediately instead of waiting for the proximity hold.
        public bool ManualKissStartsMultiKiss { get; set; } = false;

        // Mouse button used by the two optional manual-kiss features.
        public KissClickPreference ManualKissButtonPreference { get; set; } = KissClickPreference.Right;

        // Enable or disable the bump kiss when running toward a partner.
        public bool BumpKissEnabled { get; set; } = true;

        // Treat all romantic NPCs as valid kiss partners, including those from other mods.
        // Exposed in GMCM and also configurable directly through config.json.
        public bool PolyamorySupport { get; set; } = true;

        // Which blush smoke animation style to use (row 0 = Style1, row 1 = Style2).
        public BlushSmokeStyle BlushSmokeStyle { get; set; } = BlushSmokeStyle.Style2;

        // Per-location tile coordinates that should not block NPC line of sight for kiss reactions.
        // Each axis accepts one coordinate or an inclusive range.
        // Example: { "SeedShop": [ "6,12", "1-8,15-18" ] }
        public Dictionary<string, List<string>> VisionIgnoredTiles { get; set; } = new();
    }
}
