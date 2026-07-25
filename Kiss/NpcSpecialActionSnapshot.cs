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

        private void ClearNpcPreKissSpecialAction(NPC npc = null, string reason = "cleared")
        {
            if (preKissSpecialActionSnapshot == null)
                return;

            if (npc == null || preKissSpecialActionSnapshot.Npc == npc)
            {
                NpcPreKissSpecialActionSnapshot snapshot = preKissSpecialActionSnapshot;
                DebugLog("SNAPSHOT", () => $"Closed snapshot #{snapshot.DebugId} ({reason}): {DescribeNpcDebugState(snapshot.Npc)}.");
                preKissSpecialActionSnapshot = null;
                preKissSpecialActionRestoreDelayTicks = 0;
            }
        }

        private void LogNpcSnapshotWaitReason(NpcPreKissSpecialActionSnapshot snapshot, string reason)
        {
            if (snapshot == null || snapshot.LastDebugWaitReason == reason)
                return;

            snapshot.LastDebugWaitReason = reason;
            DebugLog("RESTORE", () => $"Snapshot #{snapshot.DebugId} waiting ({reason}): {DescribeNpcDebugState(snapshot.Npc)}.");
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

            if (hotkeyStoppedMultiKissAwaitingMoveAway && hotkeyStoppedMultiKissNpc == npc)
                return true;

            if (postMultiKissLookActive && postMultiKissLookNpc == npc)
                return true;

            return false;
        }

        private void CaptureNpcPreKissSpecialAction(NPC npc)
        {
            if (npc == null || npc.Sprite == null || npc.currentLocation == null)
            {
                DebugLog("SNAPSHOT", "Capture skipped: NPC, sprite, or location is unavailable.");
                return;
            }

            // Recruited followers are restored by The Stardew Squad itself. Capturing an idle
            // position here could later teleport the follower back to the kiss starting point,
            // while restoring a Squad task animation would compete with its own task manager.
            if (IsStardewSquadRecruited(npc))
            {
                DebugLog("SNAPSHOT", () => $"Capture skipped because Stardew Squad owns the NPC: {DescribeNpcDebugState(npc)}.");
                return;
            }

            if (preKissSpecialActionSnapshot != null && preKissSpecialActionSnapshot.Npc == npc)
                return;

            List<FarmerSprite.AnimationFrame> animation = null;
            if (npc.Sprite.CurrentAnimation != null && npc.Sprite.CurrentAnimation.Count > 0)
                animation = new List<FarmerSprite.AnimationFrame>(npc.Sprite.CurrentAnimation);

            // If the passive-look system already turned this NPC, preserve the pose from before
            // that turn instead of treating the temporary player-facing pose as the kiss origin.
            int originalFacing = npc.FacingDirection;
            int originalFrame = npc.Sprite.CurrentFrame;
            TryTransferPassiveLookOriginalPose(npc, out originalFacing, out originalFrame);

            // A schedule controller remains attached while another mod temporarily pauses the NPC.
            // Treat that as walking too, otherwise the paused partner is misclassified as plain idle
            // and the deferred restore teleports them back to the position where the kiss started.
            bool isWalking = npc.isMoving() || npc.controller != null;
            bool hasSpecialAnimation = animation != null && animation.Count > 0;
            bool hasSpecialStaticFrame = originalFrame >= 16;
            bool isPlainIdle = !isWalking && !hasSpecialAnimation && !hasSpecialStaticFrame;

            // If the NPC is walking with no special animation or frame, skip capture —
            // unless it's late night (22h+) where walking means going home and we still want the kiss to work.
            if (isWalking && !hasSpecialAnimation && !hasSpecialStaticFrame && Game1.timeOfDay < 2200)
            {
                DebugLog("SNAPSHOT", () => $"Capture skipped for ordinary walking NPC before 22:00: {DescribeNpcDebugState(npc)}.");
                return;
            }

            preKissSpecialActionSnapshot = new NpcPreKissSpecialActionSnapshot
            {
                DebugId = GetNextDebugSnapshotId(),
                Npc = npc,
                Location = npc.currentLocation,
                Position = npc.Position,
                RestorePositionWhenPlayerLeaves = isPlainIdle,
                WasMovingOrControlled = isWalking,
                FacingDirection = originalFacing,
                CurrentFrame = originalFrame,
                Flip = npc.flip,
                MovementPause = (int)npc.movementPause,
                AddedSpeed = (int)npc.addedSpeed,
                CurrentAnimation = animation
            };

            string classification = isPlainIdle
                ? "plain-idle"
                : hasSpecialAnimation
                    ? "special-animation"
                    : hasSpecialStaticFrame
                        ? "special-static-frame"
                        : "walking-late-night";
            DebugLog("SNAPSHOT", () =>
                $"Captured snapshot #{preKissSpecialActionSnapshot.DebugId} ({classification}, restorePosition={isPlainIdle}): " +
                $"{DescribeNpcDebugState(npc)}, savedPosition={preKissSpecialActionSnapshot.Position}, flip={preKissSpecialActionSnapshot.Flip}, addedSpeed={preKissSpecialActionSnapshot.AddedSpeed}."
            );

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
                ClearNpcPreKissSpecialAction(npc, "NPC or original location is no longer available");
                return false;
            }

            try
            {
                DebugLog("RESTORE", () =>
                    $"Restoring snapshot #{snapshot.DebugId} (restorePosition={snapshot.RestorePositionWhenPlayerLeaves}). " +
                    $"Current: {DescribeNpcDebugState(npc)}; savedPosition={snapshot.Position}, savedFacing={snapshot.FacingDirection}, savedFrame={snapshot.CurrentFrame}, savedAnimationFrames={snapshot.CurrentAnimation?.Count ?? 0}."
                );
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

                DebugLog("RESTORE", () => $"Snapshot #{snapshot.DebugId} restored successfully: {DescribeNpcDebugState(npc)}.");

                if (clearAfterRestore)
                    ClearNpcPreKissSpecialAction(npc, "restored successfully");

                return true;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"[SPECIAL ACTION RESTORE] Failed to restore special action for {npc?.Name ?? "null"}: {ex.Message}", LogLevel.Warn);
                ClearNpcPreKissSpecialAction(npc, "restore failed with exception");
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
                ClearNpcPreKissSpecialAction(npc, "deferred restore lost NPC, sprite, location, or player");
                return;
            }

            if (npc.currentLocation != Game1.player.currentLocation)
            {
                ClearNpcPreKissSpecialAction(npc, "player and NPC are no longer in the same location");
                return;
            }

            if (IsKissSystemHoldingNpc(npc))
            {
                LogNpcSnapshotWaitReason(snapshot, "kiss system still owns NPC");
                return;
            }

            if (preKissSpecialActionRestoreDelayTicks > 0)
            {
                LogNpcSnapshotWaitReason(snapshot, "restore delay is still active");
                preKissSpecialActionRestoreDelayTicks--;
                return;
            }

            if (Game1.activeClickableMenu != null || Game1.dialogueUp)
            {
                LogNpcSnapshotWaitReason(snapshot, "dialogue or menu is open");
                return;
            }

            float distance = DistanceToPlayer(npc);
            if (distance < NpcSpecialActionRestoreDistance)
            {
                LogNpcSnapshotWaitReason(snapshot, "player is still within restore distance");
                return;
            }

            snapshot.LastDebugWaitReason = null;
            DebugLog("RESTORE", () => $"Snapshot #{snapshot.DebugId} reached restore distance ({distance:0}/{NpcSpecialActionRestoreDistance:0}).");
            TryRestoreNpcPreKissSpecialAction(clearAfterRestore: true);
        }
    }
}
