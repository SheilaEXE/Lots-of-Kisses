namespace LotsOfKisses
{
    public partial class ModEntry
    {
        private void ResetKissState()
        {
            kissSequenceActive = false;
            pendingKissNpc = null;
            kissDelayTimer = 0;
            kissDialogueTimer = 0;
            kissCycleTimer = 0;
            kissRepeatTimer = 0;
            holdingKissPose = false;
            playerWasTouchingSpouse = false;
            pendingKissCycleLine = null;
        }

        private void ResetContinuousKissState()
        {
            ResetContinuousKissPlayerLeanEffect(true);

            continuousKissActive = false;
            continuousKissNpc = null;
            continuousKissTimer = 0;
            continuousKissHeartTriggered = false;
            continuousKissSmokeTriggered = false;
            continuousKissTier = 0;
            continuousKissGapTimer = 0;
            continuousKissPendingRestart = false;
            continuousKissVanillaTriggered = false;
            continuousKissCyclesDone = 0;
            publicMultiKissDialogueTriggered = false;

            // GetActiveRomanticNpcFromState() (used by GetSpouse(), which drives every automatic
            // kiss system) checks these NPC-reference fields to figure out "who is the player
            // currently kissing". If any were left pointing at the previous partner after a kiss
            // ended, GetSpouse() would keep returning that stale NPC even after the player walked
            // away and approached someone else — silently blocking every kiss system for the new
            // NPC until these happened to clear some other way. This runs on every path that ends
            // a continuous kiss (not just ForceEndContinuousKiss), since some cycles reset state
            // directly here without going through that helper.
            outsideBumpPauseNpc = null;
            outsideBumpPauseActive = false;
            approachKissHoldNpc = null;
            approachKissHoldActive = false;
            pendingNpcKissResetNpc = null;
            pendingPublicMultiKissShyNpc = null;
            pendingPublicMultiKissShyEmote = false;
        }

        private void ResetPostKissState()
        {
            kissPostSequenceActive = false;
            kissPostSequenceNpc = null;
            lastKissPostDistance = -1f;
            kissPostLineTriggered = false;
            kissPostLine = null;
            kissProximityTimer = 0;
        }
}
}