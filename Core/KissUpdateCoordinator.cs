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

            if (hotkeyStoppedMultiKissAwaitingMoveAway && hotkeyStoppedMultiKissNpc != null)
                hotkeyStoppedMultiKissNpc.movementPause = System.Math.Max(hotkeyStoppedMultiKissNpc.movementPause, 60);

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
            UpdateHotkeyStoppedMultiKiss();

            // Farmer-to-Farmer kisses own their own isolated state. Don't let the NPC systems
            // start a second kiss against a nearby romantic NPC while that sequence is active.
            if (IsPlayerSpouseKissActiveForLocalPlayer())
                return;

            NPC partner = GetPartner();
            if (partner == null)
                return;

            UpdateOutsideBumpPause(partner);
            UpdatePendingNpcKissReset(partner);
            UpdatePendingPublicMultiKissShyEmote();

            // Only hold off on kiss systems while an Outfit Reactions dialogue is genuinely on
            // screen (blocking player input anyway). The broader IsOutfitReactionActive() flag also
            // covers "noticed, waiting for a manual click" and the post-dialogue linger — states
            // where the partner is just standing there and kissing should work normally.
            if (IsAutoKissBlockedByOpenDialogueOrMenu() && !continuousKissActive && !kissSequenceActive && !kissPostSequenceActive)
            {
                UpdatePostKissSystem(partner);
                UpdateDailyPartnerSystems(partner);
                return;
            }

            UpdateKissSystem(partner);
            UpdateContinuousKissSystem(partner);
            UpdatePostKissSystem(partner);
            UpdateDailyPartnerSystems(partner);
        }
    }
}
