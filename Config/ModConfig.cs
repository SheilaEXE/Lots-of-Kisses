using System.Collections.Generic;
using StardewModdingAPI.Utilities;

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

        // Optional start/stop control for multi-kisses. When bound, the hotkey becomes
        // the exclusive way to start the chain, leaving proximity and mouse clicks free.
        public KeybindList MultiKissToggleKey { get; set; } = KeybindList.Parse("None");

        // Replace a manually initiated vanilla kiss with one random tier, without starting
        // a chain. Also available while multi-kisses are controlled exclusively by hotkey.
        public bool RandomManualKissTier { get; set; } = false;

        // When multi-kisses are enabled, let a successful manual vanilla kiss start
        // the full chain immediately instead of waiting for the proximity hold.
        public bool ManualKissStartsMultiKiss { get; set; } = false;

        // Keyboard, controller, or mouse button used by the two optional manual-kiss features.
        // GMCM exposes this as one configurable binding; MouseRight remains the default.
        public KeybindList ManualKissButton { get; set; } = KeybindList.Parse("MouseRight");

        // Let a spouse controlled by another player initiate a synchronized kiss.
        public bool AcceptPlayerSpouseKisses { get; set; } = true;

        // Enable or disable the bump kiss when running toward a partner.
        public bool BumpKissEnabled { get; set; } = true;

        // Let Lots of Kisses temporarily pause an active Stardew Squad task.
        // When disabled, mod-triggered kisses wait until the recruited partner is idle/following.
        public bool AllowKissesDuringStardewSquadTasks { get; set; } = true;

        // Allow simultaneous romantic relationships. When disabled, kisses still work with
        // exactly one boyfriend/girlfriend, fiance(e), or spouse, but are blocked if the save
        // contains two or more romantic partners.
        public bool PolyamorySupport { get; set; } = true;

        // Which blush smoke animation style to use (row 0 = Style1, row 1 = Style2).
        public BlushSmokeStyle BlushSmokeStyle { get; set; } = BlushSmokeStyle.Style2;

        // Opt-in diagnostic logging for troubleshooting state capture/restoration.
        public bool EnableDebugLogging { get; set; } = false;

        // Per-location tile coordinates that should not block NPC line of sight for kiss reactions.
        // Each axis accepts one coordinate or an inclusive range.
        // Example: { "SeedShop": [ "6,12", "1-8,15-18" ] }
        public Dictionary<string, List<string>> VisionIgnoredTiles { get; set; } = new();
    }
}
