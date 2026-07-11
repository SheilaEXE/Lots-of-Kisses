using StardewValley;

namespace LotsOfKisses
{
    /// <summary>Owns the per-tick coordination of kiss, bystander, and timer systems.</summary>
    public partial class ModEntry
    {
        private void ReinforceHoldAfterFocusRegained()
        {
            if (continuousKissActive && continuousKissNpc != null)
                continuousKissNpc.movementPause = System.Math.Max(continuousKissNpc.movementPause, 60);

            NPC partner = GetPartner();
            if (partner != null && (kissSequenceActive || kissPostSequenceActive))
                partner.movementPause = System.Math.Max(partner.movementPause, 60);

            foreach (var snapshot in activeBystanderSnapshots)
            {
                if (snapshot?.Npc != null)
                    snapshot.Npc.movementPause = System.Math.Max(snapshot.Npc.movementPause, 60);
            }
        }

        private void UpdateKissSystems()
        {
            UpdateContinuousKissPlayerLeanEffect();
            UpdateDeferredNpcSpecialActionRestore();
            UpdateBystanderRestore();
            UpdatePipeTextQueues();

            NPC partner = GetPartner();
            if (partner == null)
                return;

            UpdateOutsideBumpPause(partner);
            UpdatePendingNpcKissReset(partner);

            if (IsOutfitReactionActive() && !continuousKissActive && !kissSequenceActive && !kissPostSequenceActive)
            {
                UpdatePostKissSystem(partner);
                UpdateDailyPartnerSystems(partner);
                return;
            }

            UpdateKissSystem(partner);
            UpdateContinuousKissSystem(partner);
            UpdatePostKissSystem(partner);
            UpdatePendingPublicMultiKissShyEmote();
            UpdateDailyPartnerSystems(partner);
        }
    }
}
