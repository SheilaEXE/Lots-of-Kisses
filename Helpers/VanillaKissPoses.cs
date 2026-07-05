using StardewValley;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        // Vanilla datable NPCs have a dedicated "kissing" pose baked into their tilesheet, but the
        // base game only ever plays it for the player's actual spouse (via NPC.checkAction). For a
        // dating partner (not yet married), that branch never fires, so there's no built-in way to
        // know "which frame is the kiss pose" for them — the game only ever needs to look it up for
        // spouses. This table fills that gap: frame index (0-based) + which way the NPC needs to be
        // flipped so the kiss pose faces the right direction, per datable NPC.
        //
        // These are plain positional facts about the vanilla tilesheets (which frame index holds
        // which pose), not creative/copyrighted content — the same way "frame 4 is Abigail facing
        // north" is a fact about the game's spritesheet layout, not a piece of writing.
        private static int GetVanillaKissFrame(string npcName)
        {
            switch (npcName)
            {
                case "Sam": return 36;
                case "Penny": return 35;
                case "Sebastian": return 40;
                case "Alex": return 42;
                case "Krobus": return 16;
                case "Maru": return 28;
                case "Emily": return 33;
                case "Harvey": return 31;
                case "Shane": return 34;
                case "Elliott": return 35;
                case "Leah": return 25;
                case "Abigail": return 33;
                // Generic fallback for datable NPCs added by other mods (SVE, custom NPC mods,
                // etc.) that follow the same tilesheet convention as vanilla but aren't in the
                // table above by name.
                default: return 28;
            }
        }

        // True if the NPC's kiss pose on the tilesheet naturally faces right (so it needs to be
        // flipped when the NPC is currently facing left, and vice-versa).
        private static bool GetVanillaKissFacingRight(string npcName)
        {
            switch (npcName)
            {
                case "Sam": return true;
                case "Penny": return true;
                case "Sebastian": return false;
                case "Alex": return true;
                case "Krobus": return true;
                case "Maru": return false;
                case "Emily": return false;
                case "Harvey": return false;
                case "Shane": return false;
                case "Elliott": return false;
                case "Leah": return true;
                case "Abigail": return false;
                default: return true;
            }
        }
    }
}
