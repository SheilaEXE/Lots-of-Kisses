using Microsoft.Xna.Framework;
using StardewValley;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        internal sealed class OutsideBumpPauseState
        {
            public bool IsActive;
            public NPC Npc;
            public int Timer;
            public int Token;
        }

        // =====================================================================
        // FIELDS
        // =====================================================================
        // =====================================================================
        // BEIJO SIMPLES / LEGADO
        // =====================================================================
        private int kissDialogueTimer = 0;
        private int kissDelayTimer = 0;
        private NPC pendingKissNpc = null;
        private bool kissSequenceActive = false;
        private int kissCycleTimer = 0;
        private int kissRepeatTimer = 0;
        private bool holdingKissPose = false;
        private bool playerWasTouchingPartner = false;
        private bool approachKissTriggered = false;
        private bool approachKissRearmPending = false;
        internal bool approachKissHoldActive = false;
        internal NPC approachKissHoldNpc = null;
        private int approachKissHoldToken = 0;
        private System.Collections.Generic.Dictionary<string, int> approachKissBlockTimerByNpc = new();
        private int approachKissDialogueLastTimeOfDay = -1;
        private string pendingKissCycleLine = null;
        internal readonly OutsideBumpPauseState OutsideBumpPause = new();

        // =====================================================================
        // POST-KISS / PROXIMITY
        // =====================================================================
        private bool kissPostSequenceActive = false;
        private NPC kissPostSequenceNpc = null;
        private float lastKissPostDistance = -1f;
        private bool kissPostLineTriggered = false;
        private string kissPostLine = null;
        private int kissProximityTimer = 0;

        // =====================================================================
        // CONTINUOUS KISS / MULTI-KISSES
        // =====================================================================
        internal bool continuousKissActive = false;
        private NPC continuousKissNpc = null;
        private int continuousKissTimer = 0;
        private bool continuousKissHeartTriggered = false;
        private bool continuousKissSmokeTriggered = false;
        private int continuousKissTier = 0;
        private int continuousKissGapTimer = 0;
        internal bool continuousKissPendingRestart = false;
        private int continuousKissCyclesDone = 0;
        private int continuousKissTouchHoldTimer = 0;
        private const int ContinuousKissHoldTicks = 60;
        private bool continuousKissWasTouchingPartner = false;

        // =====================================================================
        // EFEITO VISUAL DO TIER 3: FARMER SE APROXIMA EM PIXELS E VOLTA
        // =====================================================================
        private bool continuousKissPlayerLeanInTriggered = false;
        private bool continuousKissPlayerLeanOutTriggered = false;
        private bool continuousKissPlayerLeanAnimationActive = false;
        private Vector2 continuousKissPlayerLeanDirection = Vector2.Zero;
        private Vector2 continuousKissPlayerLeanStartOffset = Vector2.Zero;
        private Vector2 continuousKissPlayerLeanTargetOffset = Vector2.Zero;
        private Vector2 continuousKissPlayerLeanAppliedOffset = Vector2.Zero;
        private int continuousKissPlayerLeanTimer = 0;
        private int continuousKissPlayerLeanDuration = 0;

        private const float ContinuousKissTier3LeanPixels = 8f;
        private const int ContinuousKissTier3LeanInDelayMs = 2500;
        private const int ContinuousKissTier3LeanAnimationTicks = 22;

        private bool publicMultiKissDialogueTriggered = false;
        private bool pendingPublicMultiKissShyEmote = false;
        private NPC pendingPublicMultiKissShyNpc = null;
        private int pendingPublicMultiKissShyEmoteTimer = 0;

        // =====================================================================
        // NPC VISUAL RESET AFTER KISS
        // =====================================================================
        internal NPC pendingNpcKissResetNpc = null;
        internal int pendingNpcKissResetTimer = 0;
        internal int pendingNpcKissResetDirection = -1;
        internal bool pendingNpcKissResetQueued = false;
        internal int pendingNpcKissResetFrame = -1;
        internal int activeKissVisualDelayMs = bumpKissVisualDelayMs;
        private const int bumpKissVisualDelayMs = 1000;
    }
}
