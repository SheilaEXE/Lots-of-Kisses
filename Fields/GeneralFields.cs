using Microsoft.Xna.Framework;
using StardewValley;

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
        internal static bool allowForcedScheduleCheck = false;
        private int kissBlockAfterDialogueTimer = 0;
        private bool wasDialogueOrMenuOpenLastTick = false;
        internal static bool suppressDialogueFromAutoKissClick = false;
        internal static NPC suppressDialogueAutoKissNpc = null;
        internal static bool suppressedDialogueDuringAutoKissClick = false;

        // Suppresses NPC.HasLocationOverrideDialogue / NPC.hasTemporaryMessageAvailable during a
        // simulated auto-kiss click. Some NPCs have a fixed "location override" line tied to their
        // current pose/location (e.g. Sebastian playing video games, Abigail sitting on the couch)
        // that never clears on its own — unlike a regular queued dialogue, it's recalculated fresh
        // every time checkAction runs, so stashing CurrentDialogue alone doesn't help here. Without
        // this, checkAction takes the "show dialogue" branch instead of the kiss branch, causing a
        // "ghost kiss" (mod effects fire, pose never actually changes).
        internal static bool suppressLocationOverrideDialogueDuringAutoKissClick = false;
        private bool lastAutoKissClickWasBlockedDialogue = false;
        internal bool LotsOfKissesKissPatchActive = false;
        // Set only when our scoped Farmer.PerformKiss patch actually starts the player's kiss
        // animation. This is more reliable than inferring success from CanMove, whose timing can
        // differ between the desktop game loop and launchers such as Cinderbox.
        internal bool autoKissPlayerAnimationStarted = false;
        // Tracks only the NPC pose created by our direct visual fallback. Vanilla/other-mod
        // animations must never be cleared through this path.
        private NPC directAutoKissVisualNpc = null;
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
        // Disabling the mod needs one cleanup pass, not a cleanup on every update tick. Repeating
        // completelyStopAnimatingOrDoingAction each tick freezes the player's walking sprite.
        private bool modDisabledCleanupApplied = false;
        // Invalidates callbacks scheduled by this mod when the save/context changes.
        private int delayedActionContextToken = 0;

        // =====================================================================
        // DAILY ROUTINE / DIALOGUES
        // =====================================================================
        private bool wasInNoticeZone = false;
        private bool talkedToPartnerToday = false;
    }
}
