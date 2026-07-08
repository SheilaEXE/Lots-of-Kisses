namespace LotsOfKisses
{
    public enum BlushSmokeStyle
    {
        Style1 = 0,
        Style2 = 1
    }

    public class ModConfig
    {
        // Master toggle for the mod.
        public bool ModEnabled { get; set; } = true;

        // Enable or disable multi-kiss sequences.
        public bool MultiKissEnabled { get; set; } = true;

        // Enable or disable the bump kiss when running toward a partner.
        public bool BumpKissEnabled { get; set; } = true;

        // Treat all romantic NPCs as valid kiss partners, including those from other mods.
        // Always true — not exposed in GMCM, but kept configurable for advanced users via config.json.
        public bool PolyamorySupport { get; set; } = true;

        // Which blush smoke animation style to use (row 0 = Style1, row 1 = Style2).
        public BlushSmokeStyle BlushSmokeStyle { get; set; } = BlushSmokeStyle.Style2;
    }
}
