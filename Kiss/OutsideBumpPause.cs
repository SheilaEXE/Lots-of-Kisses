using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.GameData.Characters;
using System;
using System.Collections.Generic;
using System.Reflection;

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
            if (!outsideBumpPauseActive || outsideBumpPauseNpc == null)
                return;

            if (partner == null || partner != outsideBumpPauseNpc)
            {
                ResetOutsideBumpPause();
                return;
            }

            if (!Context.IsWorldReady || Game1.player == null)
            {
                ResetOutsideBumpPause();
                return;
            }

            if (partner.currentLocation != Game1.player.currentLocation)
            {
                ResetOutsideBumpPause();
                return;
            }

            if (IsHomeOrFarmLocation())
            {
                ResetOutsideBumpPause();
                return;
            }

            if (continuousKissActive || continuousKissPendingRestart)
            {
                ResetOutsideBumpPause();
                return;
            }

            if (outsideBumpPauseTimer <= 0)
            {
                ResetOutsideBumpPause();
                return;
            }

            float distance = DistanceToPlayer(partner);

            if (distance >= 600f) // Far enough to assume the player moved away or the NPC warped — cancel the pause to avoid locking the NPC unnecessarily.
            {
                this.Monitor.Log(
                    $"[OUTSIDE BUMP PAUSE EXIT] npc={partner.Name} dist={distance:F1} timer={outsideBumpPauseTimer} loc={partner.currentLocation?.NameOrUniqueName ?? "null"}",
                    LogLevel.Warn
                );

                ResetOutsideBumpPause();
                return;
            }

            partner.faceGeneralDirection(Game1.player.getStandingPosition(), 0, false, false);

            // Short pause — doesn't kill the controller.
            if (partner.movementPause < 6)
                partner.movementPause = 6;
        }

        // activates the outdoor pause after a bump kiss to prevent NPC teleport or walk-away before the kiss can escalate
        private void ResetOutsideBumpPause()
        {
            outsideBumpPauseActive = false;
            outsideBumpPauseNpc = null;
            outsideBumpPauseTimer = 0;
            outsideBumpPauseToken++;
        }
    }
}
