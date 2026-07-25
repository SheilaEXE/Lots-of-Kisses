using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.GameData.Characters;
using System;
using System.Collections.Generic;

namespace LotsOfKisses
{
    // Briefly pauses the partner NPC outdoors right after a bump kiss, so they don't wander off before the kiss can escalate.
    public partial class ModEntry
    {
        // ========================================================================================================================================
        // Outdoor bump-kiss pause: briefly pauses the NPC after a bump kiss to prevent them from teleporting or walking away before the kiss can escalate into a continuous kiss.
        // ========================================================================================================================================
        private void UpdateOutsideBumpPause(NPC partner)
        {
            if (!OutsideBumpPause.IsActive || OutsideBumpPause.Npc == null)
                return;

            if (partner == null || partner != OutsideBumpPause.Npc)
            {
                ResetOutsideBumpPause("active romantic partner changed");
                return;
            }

            if (!Context.IsWorldReady || Game1.player == null)
            {
                ResetOutsideBumpPause("world or player became unavailable");
                return;
            }

            if (partner.currentLocation != Game1.player.currentLocation)
            {
                ResetOutsideBumpPause("partner changed location");
                return;
            }

            if (IsHomeOrFarmLocation())
            {
                ResetOutsideBumpPause("entered home or farm location");
                return;
            }

            if (continuousKissActive || continuousKissPendingRestart)
            {
                ResetOutsideBumpPause("escalated into Multi-Kiss");
                return;
            }

            if (OutsideBumpPause.Timer <= 0)
            {
                ResetOutsideBumpPause("pause timer expired");
                return;
            }

            float distance = DistanceToPlayer(partner);

            if (distance >= 600f) // Far enough to assume the player moved away or the NPC warped — cancel the pause to avoid locking the NPC unnecessarily.
            {
                ResetOutsideBumpPause($"player moved away ({distance:0}/600)");
                return;
            }

            partner.faceGeneralDirection(Game1.player.getStandingPosition(), 0, false, false);

            // Short pause — doesn't kill the controller.
            if (partner.movementPause < 6)
                partner.movementPause = 6;
        }

        // activates the outdoor pause after a bump kiss to prevent NPC teleport or walk-away before the kiss can escalate
        private void ResetOutsideBumpPause(string reason = "cleared")
        {
            if (OutsideBumpPause.IsActive)
                DebugLog("BUMP", () => $"Released outside bump pause ({reason}): token={OutsideBumpPause.Token}, {DescribeNpcDebugState(OutsideBumpPause.Npc)}.");

            OutsideBumpPause.IsActive = false;
            OutsideBumpPause.Npc = null;
            OutsideBumpPause.Timer = 0;
            OutsideBumpPause.Token++;
        }
    }
}
