using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.GameData.Characters;
using System;
using System.Collections.Generic;

namespace LotsOfKisses
{
    // Saves and restores an NPC's special/scripted pose (e.g. fishing) around a kiss, so it isn't lost.
    public partial class ModEntry
    {
        private bool HasNpcPreKissSpecialAction(NPC npc)
        {
            return npc != null &&
                   preKissSpecialActionSnapshot != null &&
                   preKissSpecialActionSnapshot.Npc == npc;
        }

        private void ClearNpcPreKissSpecialAction(NPC npc = null)
        {
            if (preKissSpecialActionSnapshot == null)
                return;

            if (npc == null || preKissSpecialActionSnapshot.Npc == npc)
            {
                preKissSpecialActionSnapshot = null;
                preKissSpecialActionRestoreDelayTicks = 0;
            }
        }

        private bool IsKissSystemHoldingNpc(NPC npc)
        {
            if (npc == null)
                return false;

            if (continuousKissActive && continuousKissNpc == npc)
                return true;

            if (continuousKissPendingRestart && continuousKissNpc == npc)
                return true;

            if (kissSequenceActive && pendingKissNpc == npc)
                return true;

            if (kissPostSequenceActive && kissPostSequenceNpc == npc)
                return true;

            if (OutsideBumpPause.IsActive && OutsideBumpPause.Npc == npc)
                return true;

            return false;
        }

        private void CaptureNpcPreKissSpecialAction(NPC npc)
        {
            if (npc == null || npc.Sprite == null || npc.currentLocation == null)
                return;

            // Recruited followers are restored by The Stardew Squad itself. Capturing an idle
            // position here could later teleport the follower back to the kiss starting point,
            // while restoring a Squad task animation would compete with its own task manager.
            if (IsStardewSquadRecruited(npc))
                return;

            if (preKissSpecialActionSnapshot != null && preKissSpecialActionSnapshot.Npc == npc)
                return;

            List<FarmerSprite.AnimationFrame> animation = null;
            if (npc.Sprite.CurrentAnimation != null && npc.Sprite.CurrentAnimation.Count > 0)
                animation = new List<FarmerSprite.AnimationFrame>(npc.Sprite.CurrentAnimation);

            // A schedule controller remains attached while another mod temporarily pauses the NPC.
            // Treat that as walking too, otherwise the paused partner is misclassified as plain idle
            // and the deferred restore teleports them back to the position where the kiss started.
            bool isWalking = npc.isMoving() || npc.controller != null;
            bool hasSpecialAnimation = animation != null && animation.Count > 0;
            bool hasSpecialStaticFrame = npc.Sprite.CurrentFrame >= 16;
            bool isPlainIdle = !isWalking && !hasSpecialAnimation && !hasSpecialStaticFrame;

            // If the NPC is walking with no special animation or frame, skip capture —
            // unless it's late night (22h+) where walking means going home and we still want the kiss to work.
            if (isWalking && !hasSpecialAnimation && !hasSpecialStaticFrame && Game1.timeOfDay < 2200)
                return;

            preKissSpecialActionSnapshot = new NpcPreKissSpecialActionSnapshot
            {
                Npc = npc,
                Location = npc.currentLocation,
                Position = npc.Position,
                RestorePositionWhenPlayerLeaves = isPlainIdle,
                FacingDirection = npc.FacingDirection,
                CurrentFrame = npc.Sprite.CurrentFrame,
                Flip = npc.flip,
                MovementPause = (int)npc.movementPause,
                AddedSpeed = (int)npc.addedSpeed,
                CurrentAnimation = animation
            };

            // Pause movement temporarily so the vanilla kiss animation can play.
            // The controller is never touched — it resumes when movementPause reaches 0.
            if (isWalking)
                npc.movementPause = 60;

            // Only clears the special visual action so the vanilla kiss animation can play.
            // Does not touch the controller or queuedSchedulePaths, to avoid breaking the walk/routine.
            npc.Sprite.StopAnimation();
            npc.Sprite.ClearAnimation();
            npc.Sprite.CurrentAnimation = null;
            npc.flip = false;
            npc.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(npc.FacingDirection);
            npc.Sprite.UpdateSourceRect();
        }

        private bool TryRestoreNpcPreKissSpecialAction(bool clearAfterRestore)
        {
            NpcPreKissSpecialActionSnapshot snapshot = preKissSpecialActionSnapshot;
            if (snapshot == null || snapshot.Npc == null)
                return false;

            NPC npc = snapshot.Npc;

            if (npc.Sprite == null || npc.currentLocation == null || npc.currentLocation != snapshot.Location)
            {
                ClearNpcPreKissSpecialAction(npc);
                return false;
            }

            try
            {
                // A plain idle NPC has no schedule/special action capable of returning it to
                // the exact pre-kiss spot. Restore that position only through this deferred
                // path, after the player has moved away. Walking and special-action NPCs keep
                // their existing restoration behavior and are never repositioned here.
                if (snapshot.RestorePositionWhenPlayerLeaves)
                    npc.Position = snapshot.Position;

                npc.FacingDirection = snapshot.FacingDirection;
                npc.flip = snapshot.Flip;
                npc.movementPause = snapshot.MovementPause;
                npc.addedSpeed = snapshot.AddedSpeed;

                if (snapshot.CurrentAnimation != null && snapshot.CurrentAnimation.Count > 0)
                {
                    npc.Sprite.CurrentAnimation = new List<FarmerSprite.AnimationFrame>(snapshot.CurrentAnimation);
                    TrySetPrivateField(npc.Sprite, "currentAnimationIndex", 0);
                    TrySetPrivateField(npc.Sprite, "timer", 0);
                }
                else
                {
                    npc.Sprite.StopAnimation();
                    npc.Sprite.ClearAnimation();
                    npc.Sprite.CurrentAnimation = null;
                }

                npc.Sprite.CurrentFrame = snapshot.CurrentFrame;
                npc.Sprite.UpdateSourceRect();

                if (clearAfterRestore)
                    ClearNpcPreKissSpecialAction(npc);

                return true;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"[SPECIAL ACTION RESTORE] Failed to restore special action for {npc?.Name ?? "null"}: {ex.Message}", LogLevel.Warn);
                ClearNpcPreKissSpecialAction(npc);
                return false;
            }
        }

        private void UpdateDeferredNpcSpecialActionRestore()
        {
            NpcPreKissSpecialActionSnapshot snapshot = preKissSpecialActionSnapshot;
            if (snapshot == null)
                return;

            NPC npc = snapshot.Npc;
            if (npc == null || npc.Sprite == null || npc.currentLocation == null || Game1.player == null)
            {
                ClearNpcPreKissSpecialAction(npc);
                return;
            }

            if (npc.currentLocation != Game1.player.currentLocation)
            {
                ClearNpcPreKissSpecialAction(npc);
                return;
            }

            if (IsKissSystemHoldingNpc(npc))
                return;

            if (preKissSpecialActionRestoreDelayTicks > 0)
            {
                preKissSpecialActionRestoreDelayTicks--;
                return;
            }

            if (Game1.activeClickableMenu != null || Game1.dialogueUp)
                return;

            if (DistanceToPlayer(npc) < NpcSpecialActionRestoreDistance)
                return;

            TryRestoreNpcPreKissSpecialAction(clearAfterRestore: true);
        }
    }
}
