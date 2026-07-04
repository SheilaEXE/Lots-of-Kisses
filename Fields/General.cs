using Microsoft.Xna.Framework;
using StardewValley;
using System;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        // =====================================================================
        // FIELDS
        // =====================================================================
        // =====================================================================
        // GENERAL STATE
        // =====================================================================
        private int lastDayChecked = -1;
        private string lastLocation = "";
        private int cooldown = 0;
        private bool didReactThisTick = false;
        private int dialogueCooldown = 0;
        private int noticeEmoteCooldown = 0;
        private float lastNoticeDistance = -1f;
        private static bool allowForcedScheduleCheck = false;
        private int kissBlockAfterDialogueTimer = 0;
        private bool wasDialogueOrMenuOpenLastTick = false;
        internal static bool suppressDialogueFromAutoKissClick = false;
        internal static NPC suppressDialogueAutoKissNpc = null;
        internal static bool suppressedDialogueDuringAutoKissClick = false;
        private bool lastAutoKissClickWasBlockedDialogue = false;
        internal bool LotsOfKissesKissPatchActive = false;
        private bool passiveLookRestoreActive = false;
        private string passiveLookRestoreNpcName = "";
        private int passiveLookRestoreFacing = -1;
        private int passiveLookRestoreFrame = -1;
        private int passiveLookRestoreTimer = 0;
        private int passiveLookBlockAfterDialogueTimer = 0;
        private bool passiveLookDialogueWasOpenLastTick = false;
        private Point passiveLookRestoreTile = Point.Zero;
        private string passiveLookRestoreLocationName = "";
        private bool wasGameWindowActiveLastTick = true;
        private int multiKissClockAccumulatorTicks = 0;

        // =====================================================================
        // DAILY ROUTINE / DIALOGUES
        // =====================================================================
        private bool wasInNoticeZone = false;
        private bool talkedToSpouseToday = false;

        // =====================================================================
        // UTILITY HELPERS
        // =====================================================================
        private bool HasReadableDialogueWaiting(NPC npc)
        {
            if (npc == null || Game1.player == null)
                return false;

            if (kissBlockAfterDialogueTimer > 0)
                return true;

            if (Game1.dialogueUp || Game1.activeClickableMenu != null)
                return true;

            if (npc.CurrentDialogue != null && npc.CurrentDialogue.Count > 0)
                return true;

            return false;
        }

        // Reads the cross-mod flag written by the Outfit Reactions mod (NatrollEXE.OutfitReactions)
        // into the Farmer's modData. While present, an outfit reaction is in progress (noticing,
        // generating, or dialogue open) and kisses must hold off so the two mods don't collide.
        // Using modData means no hard dependency or load-order requirement between the mods.
        private const string OutfitReactionsActiveModDataKey = "NatrollEXE.OutfitReactions/ReactionActive";

        private bool IsOutfitReactionActive()
        {
            return Game1.player?.modData != null
                && Game1.player.modData.ContainsKey(OutfitReactionsActiveModDataKey);
        }
    }
}