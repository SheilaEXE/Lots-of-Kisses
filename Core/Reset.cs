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
            playerWasTouchingPartner = false;
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
            continuousKissCyclesDone = 0;
            publicMultiKissDialogueTriggered = false;
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
