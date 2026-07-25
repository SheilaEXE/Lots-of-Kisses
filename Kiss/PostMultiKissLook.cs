using StardewModdingAPI;
using StardewValley;

namespace LotsOfKisses
{
    /// <summary>
    /// Keeps a partner looking at the player after a Multi-Kiss until the player moves far enough
    /// away. Stationary NPCs restore their saved pose/animation; walking NPCs resume the existing
    /// route naturally. Controllers are never replaced or cleared.
    /// </summary>
    public partial class ModEntry
    {
        private const float PostMultiKissLookRestoreDistance = 600f;
        private bool postMultiKissLookActive;
        private NPC postMultiKissLookNpc;
        private string postMultiKissLookReason;
        private bool postMultiKissLookResumeRouteNaturally;

        private bool HasStationaryPostMultiKissSnapshot(NPC npc)
        {
            return npc != null
                && preKissSpecialActionSnapshot != null
                && preKissSpecialActionSnapshot.Npc == npc
                && preKissSpecialActionSnapshot.Location == npc.currentLocation
                && !preKissSpecialActionSnapshot.WasMovingOrControlled;
        }

        private bool CanResumeExistingRouteAfterMultiKiss(NPC npc)
        {
            return npc != null
                && continuousKissNpc == npc
                && continuousKissNpcHadControllerAtSequenceStart
                && npc.controller != null
                // A stationary special-animation snapshot is the source of truth even if a
                // controller happened to remain attached at capture time. Restore that pose
                // after 600px instead of treating it as an active walking route.
                && !HasStationaryPostMultiKissSnapshot(npc);
        }

        private bool CanUsePostMultiKissLookWait(NPC npc)
        {
            return HasStationaryPostMultiKissSnapshot(npc)
                || CanResumeExistingRouteAfterMultiKiss(npc);
        }

        private bool BeginPostMultiKissLookWait(NPC npc, string reason)
        {
            if (!CanUsePostMultiKissLookWait(npc))
            {
                DebugLog("POST-LOOK", () =>
                    $"Post-Multi-Kiss look wait not used ({reason}): snapshot={(preKissSpecialActionSnapshot != null)}, " +
                    $"snapshotMatches={(preKissSpecialActionSnapshot?.Npc == npc)}, originallyMoving={preKissSpecialActionSnapshot?.WasMovingOrControlled}, " +
                    $"{DescribeNpcDebugState(npc)}."
                );
                return false;
            }

            bool resumeRouteNaturally = CanResumeExistingRouteAfterMultiKiss(npc);

            postMultiKissLookActive = true;
            postMultiKissLookNpc = npc;
            postMultiKissLookReason = reason;
            postMultiKissLookResumeRouteNaturally = resumeRouteNaturally;
            npc.movementPause = System.Math.Max(npc.movementPause, 6);
            string restoreMode = resumeRouteNaturally
                ? "resume existing controller naturally"
                : $"restore snapshot #{preKissSpecialActionSnapshot.DebugId}";
            DebugLog("POST-LOOK", $"Started post-Multi-Kiss look wait ({reason}): npc={npc.Name}, restoreDistance={PostMultiKissLookRestoreDistance:0}, mode={restoreMode}.");
            return true;
        }

        private void UpdatePostMultiKissLookWait()
        {
            if (!postMultiKissLookActive)
                return;

            NPC npc = postMultiKissLookNpc;
            if (!Context.IsWorldReady || Game1.player == null || npc?.currentLocation == null || npc.Sprite == null)
            {
                ClearPostMultiKissLookWait(releaseNpc: true, "world, player, NPC, or location became unavailable");
                return;
            }

            if (npc.currentLocation != Game1.player.currentLocation)
            {
                ClearPostMultiKissLookWait(releaseNpc: true, "player and NPC changed locations");
                return;
            }

            // A new kiss or the public interruption still owns the pose. Preserve the wait, but
            // don't turn the NPC over an active animation, dialogue, or blush emote.
            if (continuousKissActive || continuousKissPendingRestart
                || Game1.activeClickableMenu != null || Game1.dialogueUp
                || pendingPublicMultiKissShyEmote
                || IsNpcShowingKissVisual(npc))
            {
                npc.movementPause = System.Math.Max(npc.movementPause, 6);
                return;
            }

            float distance = DistanceToPlayer(npc);
            if (distance < PostMultiKissLookRestoreDistance)
            {
                ApplyPostMultiKissLookAtPlayer(npc, 6);
                return;
            }

            string reason = postMultiKissLookReason;
            bool resumeRouteNaturally = postMultiKissLookResumeRouteNaturally;
            ClearPostMultiKissLookWait(releaseNpc: false, $"player reached restore distance ({distance:0}/{PostMultiKissLookRestoreDistance:0})");
            if (resumeRouteNaturally)
            {
                ReleaseExistingRouteAfterMultiKiss(npc, maximumOwnedPause: 6, reason);
                return;
            }

            DebugLog("POST-LOOK", $"Restoring original pose after post-Multi-Kiss wait ({reason}): npc={npc.Name}, distance={distance:0}.");
            TryRestoreNpcPreKissSpecialAction(clearAfterRestore: true);
        }

        private void ReleaseExistingRouteAfterMultiKiss(NPC npc, int maximumOwnedPause, string reason)
        {
            if (npc == null)
                return;

            // A late-night walking snapshot may exist, but the unchanged controller is the source
            // of truth for the route. Don't restore a stale walking frame over it.
            if (preKissSpecialActionSnapshot?.Npc == npc && preKissSpecialActionSnapshot.WasMovingOrControlled)
                ClearNpcPreKissSpecialAction(npc, $"existing route released after {reason}");

            if (npc.movementPause <= maximumOwnedPause)
                npc.movementPause = 0;

            DebugLog("POST-LOOK", () =>
                $"Released post-Multi-Kiss route hold ({reason}) without changing the controller: {DescribeNpcDebugState(npc)}."
            );
        }

        /// <summary>
        /// Removes only the kiss visual and keeps the NPC looking at the player. This never
        /// changes the controller or queued schedule paths.
        /// </summary>
        private void ApplyPostMultiKissLookAtPlayer(NPC npc, int minimumPause)
        {
            if (npc?.Sprite == null || Game1.player == null)
                return;

            try
            {
                npc.faceGeneralDirection(Game1.player.getStandingPosition(), 0, false, false);
            }
            catch
            {
                npc.FacingDirection = GetDirectionTowardPlayer(npc);
            }

            npc.Sprite.StopAnimation();
            npc.Sprite.ClearAnimation();
            npc.Sprite.CurrentAnimation = null;
            npc.flip = false;
            npc.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(npc.FacingDirection);
            npc.Sprite.UpdateSourceRect();
            npc.movementPause = System.Math.Max(npc.movementPause, minimumPause);
        }

        private void ClearPostMultiKissLookWait(bool releaseNpc, string reason = "cleared")
        {
            NPC npc = postMultiKissLookNpc;
            if (postMultiKissLookActive)
                DebugLog("POST-LOOK", $"Cleared post-Multi-Kiss look wait ({reason}): npc={npc?.Name ?? "null"}, releaseNpc={releaseNpc}.");

            if (releaseNpc && npc != null)
                npc.movementPause = 0;

            postMultiKissLookActive = false;
            postMultiKissLookNpc = null;
            postMultiKissLookReason = null;
            postMultiKissLookResumeRouteNaturally = false;
        }
    }
}
