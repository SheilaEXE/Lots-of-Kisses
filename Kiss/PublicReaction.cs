using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using System;
using System.Collections.Generic;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        // ── Constants ────────────────────────────────────────────────────────

        /// <summary>Chance (0–1) per bystander of noticing the kiss, per tier.</summary>
        private static readonly double[] BystanderNoticeChance = { 0.30, 0.60, 0.90 };

        /// <summary>Chance (0–1) that a noticing bystander plays an embarrassed emote.</summary>
        private const double BystanderEmoteChance = 0.30;

        /// <summary>Chance (0–1) that a noticing bystander says a crowd reaction line above their head.</summary>
        private const double CrowdReactionLineChance = 0.10;

        /// <summary>Ticks to wait after the public multi-kiss dialogue closes / kiss scene ends before releasing bystanders.</summary>
        private const int BystanderRestoreDelayTicks = 120; // ~2s

        /// <summary>Safety timeout so bystanders never stay paused forever if the kiss/dialogue state changes unexpectedly.</summary>
        private const int BystanderRestoreSafetyTicks = 600; // ~10s

        /// <summary>
        /// Tiny pause refreshed while a bystander is intentionally watching.
        /// This mirrors the outdoor bump-kiss hold: don't kill controller, just keep a short pause alive.
        /// </summary>
        private const int BystanderHoldPauseTicks = 6;

        /// <summary>
        /// Ticks to wait after a bystander starts turning (faceGeneralDirection/faceDirection) before
        /// playing their reaction emote. Both turn calls are gradual, not instant, so firing the emote
        /// in the same tick makes it look like the NPC reacts before actually looking at the player.
        /// </summary>
        private const int BystanderEmoteTurnDelayTicks = 35; // ~0.6s, comfortable margin for the turn to visibly complete

        // Cross-mod flag written into the bystander NPC's own modData while they are turned to
        // watch a public multi-kiss. Other mods (e.g. Outfit Reactions) can check this key on the
        // NPC to skip starting their own reactions on that NPC during the audience moment, without
        // any hard dependency or load-order requirement between the mods.
        private const string BystanderWatchingModDataKey = "NatrollEXE.LotsOfKisses/BystanderWatching";

        // ── State ────────────────────────────────────────────────────────────

        private readonly List<BystanderSnapshot> activeBystanderSnapshots = new();
        private bool bystanderRestorePending = false;
        private bool bystanderRestoreCountdownStarted = false;
        private int bystanderRestoreTimer = 0;
        private int bystanderRestoreSafetyTimer = 0;
        private NPC bystanderRestorePartner = null;

        // ── Public entry points ───────────────────────────────────────────────

        /// <summary>
        /// Called when a public multi-kiss cycle completes. Scans visible NPCs,
        /// saves their state, turns them toward the player, and maybe plays an emote.
        /// </summary>
        internal void TriggerBystanderReactions(int kissTier, NPC spouse)
        {
            if (Game1.currentLocation == null || Game1.player == null)
                return;

            string locName = Game1.currentLocation.Name;
            if (locName == "Farm" || locName == "FarmHouse")
                return;

            if (IsPrivateKissMoment(spouse))
                return;

            int tierIndex = Math.Clamp(kissTier - 1, 0, BystanderNoticeChance.Length - 1);
            double noticeChance = BystanderNoticeChance[tierIndex];

            foreach (NPC npc in Game1.currentLocation.characters)
            {
                if (npc == null)
                    continue;

                // Skip the romantic partner and any NPC already reacting.
                if (IsCurrentSpouse(npc.Name) || IsDatingPartner(npc.Name))
                    continue;

                // Skip NPCs already in our snapshot list.
                if (activeBystanderSnapshots.Exists(s => s.Npc == npc))
                    continue;

                float distance = DistanceToPlayer(npc);
                bool wasMoving = npc.isMoving();
                bool hadController = npc.controller != null;
                bool isWalkingToward = IsNpcWalkingTowardPlayer(npc);
                bool wasRouteNpc = wasMoving || hadController || isWalkingToward;

                // NPCs walking toward the player within 600f stop to watch — no chance roll needed.
                // Other NPCs on screen need to pass the notice chance roll.
                bool forceReact = isWalkingToward && distance <= 600f;
                if (!forceReact)
                {
                    if (!IsNpcOnScreen(npc))
                        continue;

                    if (random.NextDouble() >= noticeChance)
                        continue;
                }

                // Save state. We don't touch controller/queued paths; bystanders use the same gentle
                // hold idea as the spouse's outside bump-kiss pause.
                var snapshot = new BystanderSnapshot
                {
                    Npc              = npc,
                    Location         = npc.currentLocation,
                    Position         = npc.Position,
                    FacingDirection  = npc.FacingDirection,
                    CurrentFrame     = npc.Sprite.CurrentFrame,
                    CurrentAnimation = npc.Sprite.CurrentAnimation != null
                                       ? new List<FarmerSprite.AnimationFrame>(npc.Sprite.CurrentAnimation)
                                       : null,
                    Flip             = npc.flip,
                    MovementPause    = (int)npc.movementPause,
                    HadController    = hadController,
                    WasMoving        = wasMoving,
                    WasWalkingTowardPlayer = isWalkingToward,
                    WasPausedByMod   = wasRouteNpc,
                };

                activeBystanderSnapshots.Add(snapshot);

                // Mark this NPC as watching so other mods (e.g. Outfit Reactions) can skip
                // starting their own reactions on them until they're released below.
                npc.modData[BystanderWatchingModDataKey] = "1";

                HoldBystanderWatching(snapshot);

                // Roll the emote now, but don't play it yet — faceGeneralDirection/faceDirection take
                // a few ticks to visually finish the turn. Stash it on the snapshot and let
                // UpdateBystanderRestore's tick loop fire it once the NPC has actually turned.
                if (random.NextDouble() < BystanderEmoteChance)
                {
                    int emote = random.Next(2) == 0 ? 28 : 60;
                    snapshot.PendingEmote = emote;
                    snapshot.PendingEmoteDelayTicks = BystanderEmoteTurnDelayTicks;
                }

                this.Monitor.Log(
                    $"[BYSTANDER] {npc.Name} noticed (tier {kissTier}, forced={forceReact}, moving={wasMoving}, controller={hadController}, dist={distance:F0})",
                    LogLevel.Trace);
            }

            // Every bystander already watching — whether they just noticed above or noticed on a
            // previous kiss cycle — gets an independent 10% roll for a crowd reaction line, every cycle.
            foreach (var snapshot in activeBystanderSnapshots)
            {
                TryShowCrowdReactionLine(snapshot.Npc);
            }
        }

        /// <summary>
        /// Clears the active bystander list AND releases the cross-mod watching flag from every
        /// NPC still in it. Use this instead of calling activeBystanderSnapshots.Clear() directly
        /// anywhere state is force-reset (day start, save load, warp, etc.) — otherwise an NPC could
        /// be left with the watching flag stuck on them forever, since RestoreAllBystanders() would
        /// never get a chance to run for them.
        /// </summary>
        private void ClearActiveBystanderSnapshots()
        {
            foreach (var snapshot in activeBystanderSnapshots)
                snapshot?.Npc?.modData?.Remove(BystanderWatchingModDataKey);

            activeBystanderSnapshots.Clear();
        }

        /// <summary>
        /// Schedules bystander restoration after the kiss/dialogue state clears, then waits two seconds.
        /// </summary>
        internal void ScheduleBystanderRestore(NPC partner = null)
        {
            if (activeBystanderSnapshots.Count == 0)
                return;

            bystanderRestorePending = true;
            bystanderRestoreCountdownStarted = false;
            bystanderRestoreTimer = BystanderRestoreDelayTicks;
            bystanderRestoreSafetyTimer = BystanderRestoreSafetyTicks;
            bystanderRestorePartner = partner;
        }

        /// <summary>
        /// Ticked every update. Keeps bystanders looking at the player while the scene is active,
        /// then releases them two seconds after the publicMultiKiss dialogue closes / kiss ends.
        /// Controller is intentionally never cleared here.
        /// </summary>
        internal void UpdateBystanderRestore()
        {
            if (activeBystanderSnapshots.Count == 0)
                return;

            // If something added snapshots but forgot to schedule the restore, schedule it anyway.
            if (!bystanderRestorePending)
            {
                bystanderRestorePending = true;
                bystanderRestoreCountdownStarted = false;
                bystanderRestoreTimer = BystanderRestoreDelayTicks;
                bystanderRestoreSafetyTimer = BystanderRestoreSafetyTicks;
            }

            if (bystanderRestoreSafetyTimer > 0)
                bystanderRestoreSafetyTimer--;
            else
            {
                RestoreAllBystanders();
                return;
            }

            KeepBystandersWatchingPlayer();

            // If the kiss is still going, keep bystanders watching and reset the countdown.
            if (continuousKissActive || continuousKissPendingRestart)
            {
                bystanderRestoreCountdownStarted = false;
                bystanderRestoreTimer = BystanderRestoreDelayTicks;
                return;
            }

            // If publicMultiKiss is on screen, wait until it closes. Once it closes, the two-second
            // timer starts immediately; it doesn't wait for the partner's shy emote timer.
            if (pendingPublicMultiKissShyEmote && (Game1.activeClickableMenu != null || Game1.dialogueUp))
            {
                bystanderRestoreCountdownStarted = false;
                bystanderRestoreTimer = BystanderRestoreDelayTicks;
                return;
            }

            bool partnerReleased = bystanderRestoreForceStart
                                || bystanderRestorePartner == null
                                || bystanderRestorePartner.currentLocation != Game1.currentLocation
                                || bystanderRestorePartner.isMoving()
                                || !IsKissSystemHoldingNpc(bystanderRestorePartner);

            if (!partnerReleased)
            {
                bystanderRestoreCountdownStarted = false;
                bystanderRestoreTimer = BystanderRestoreDelayTicks;
                return;
            }

            if (!bystanderRestoreCountdownStarted)
            {
                bystanderRestoreCountdownStarted = true;
                bystanderRestoreTimer = BystanderRestoreDelayTicks;
                return;
            }

            if (bystanderRestoreTimer > 0)
            {
                bystanderRestoreTimer--;
                return;
            }

            RestoreAllBystanders();
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        private void KeepBystandersWatchingPlayer()
        {
            foreach (var snapshot in activeBystanderSnapshots)
            {
                HoldBystanderWatching(snapshot);
                TickPendingBystanderEmote(snapshot);
            }
        }

        /// <summary>
        /// Counts down the bystander's turn-to-face delay and plays the stashed emote once it
        /// finishes, so the NPC visibly looks at the player before reacting instead of emoting mid-turn.
        /// </summary>
        private void TickPendingBystanderEmote(BystanderSnapshot snapshot)
        {
            if (snapshot == null || snapshot.PendingEmote == null)
                return;

            NPC npc = snapshot.Npc;
            if (npc == null || npc.currentLocation != Game1.currentLocation)
            {
                snapshot.PendingEmote = null;
                return;
            }

            if (snapshot.PendingEmoteDelayTicks > 0)
            {
                snapshot.PendingEmoteDelayTicks--;
                return;
            }

            npc.doEmote(snapshot.PendingEmote.Value);
            snapshot.PendingEmote = null;
        }

        private void HoldBystanderWatching(BystanderSnapshot snapshot)
        {
            if (snapshot == null || Game1.player == null)
                return;

            NPC npc = snapshot.Npc;
            if (npc == null || npc.currentLocation != Game1.currentLocation)
                return;

            bool wasRouteNpc = snapshot.WasPausedByMod
                            || snapshot.WasMoving
                            || snapshot.WasWalkingTowardPlayer
                            || snapshot.HadController;

            // Route NPCs use the same gentle idea as UpdateOutsideBumpPause: turn visually toward
            // the player and keep only a tiny refreshed pause. No Halt(), no controller clear, no
            // schedule refresh. This is what lets walking NPCs resume normally afterward.
            if (wasRouteNpc)
            {
                try
                {
                    npc.faceGeneralDirection(Game1.player.getStandingPosition(), 0, false, false);
                }
                catch
                {
                    npc.FacingDirection = GetDirectionTowardPlayer(npc);
                }

                // Snap the sprite to the idle frame for whichever direction the NPC ended up
                // facing, so they don't freeze mid-stride with a leg in the air.
                npc.Sprite.StopAnimation();
                npc.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(npc.FacingDirection);
                npc.Sprite.UpdateSourceRect();
            }
            else
            {
                // Static/special-action NPCs, like someone sitting in the Saloon, don't have a
                // route controller to protect. Temporarily clear the animation so they actually
                // stop what they're doing and look at the farmer, then RestoreAllBystanders puts
                // the original animation/frame back two seconds later.
                int lookDirection = GetDirectionTowardPlayer(npc);

                npc.Sprite.StopAnimation();
                npc.Sprite.ClearAnimation();
                npc.Sprite.CurrentAnimation = null;
                npc.flip = false;
                npc.FacingDirection = lookDirection;
                npc.faceDirection(lookDirection);
                npc.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(lookDirection);
                npc.Sprite.UpdateSourceRect();
            }

            if (npc.movementPause < BystanderHoldPauseTicks)
                npc.movementPause = BystanderHoldPauseTicks;
        }

        private void RestoreAllBystanders()
        {
            var routeSnapshots = new List<BystanderSnapshot>();

            foreach (var snapshot in activeBystanderSnapshots)
            {
                NPC npc = snapshot.Npc;

                // Cancel any emote that hadn't fired yet — the NPC is going back to normal now,
                // so it shouldn't pop later, disconnected from the reaction it belonged to.
                snapshot.PendingEmote = null;

                if (npc == null || npc.currentLocation == null)
                    continue;

                // Release the cross-mod watching flag now that this bystander is going back to normal.
                npc.modData.Remove(BystanderWatchingModDataKey);

                bool wasRouteNpc = snapshot.WasPausedByMod || snapshot.WasMoving || snapshot.WasWalkingTowardPlayer || snapshot.HadController;

                if (wasRouteNpc)
                {
                    ReleaseRouteBystanderPauseOnly(snapshot);
                    routeSnapshots.Add(snapshot);
                    this.Monitor.Log($"[BYSTANDER] {npc.Name} route released gently (wasMoving={snapshot.WasMoving}, wasWalkingToward={snapshot.WasWalkingTowardPlayer}, controller={snapshot.HadController}).", LogLevel.Trace);
                    continue;
                }

                npc.movementPause = snapshot.MovementPause;
                npc.FacingDirection = snapshot.FacingDirection;
                npc.flip = snapshot.Flip;

                if (snapshot.CurrentAnimation != null && snapshot.CurrentAnimation.Count > 0)
                {
                    npc.Sprite.CurrentAnimation =
                        new List<FarmerSprite.AnimationFrame>(snapshot.CurrentAnimation);
                    TrySetSpritePrivateField(npc.Sprite, "currentAnimationIndex", 0);
                    TrySetSpritePrivateField(npc.Sprite, "timer", 0);
                }
                else
                {
                    npc.Sprite.StopAnimation();
                    npc.Sprite.ClearAnimation();
                    npc.Sprite.CurrentAnimation = null;
                }

                npc.Sprite.CurrentFrame = snapshot.CurrentFrame;
                npc.Sprite.UpdateSourceRect();

                this.Monitor.Log($"[BYSTANDER] {npc.Name} state restored (idle/static).", LogLevel.Trace);
            }

            // Tiny safety pass: only removes any refreshed pause that might remain from an emote/animation tick.
            // It still doesn't touch controller, Halt, or schedule.
            if (routeSnapshots.Count > 0)
            {
                DelayedAction.functionAfterDelay(() =>
                {
                    foreach (var snapshot in routeSnapshots)
                        ReleaseRouteBystanderPauseOnly(snapshot);
                }, 250);
            }

            ClearActiveBystanderSnapshots();
            bystanderRestorePending = false;
            bystanderRestoreCountdownStarted = false;
            bystanderRestoreTimer = 0;
            bystanderRestoreSafetyTimer = 0;
            bystanderRestorePartner = null;
            bystanderRestoreForceStart = false;
        }

        private void ReleaseRouteBystanderPauseOnly(BystanderSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            NPC npc = snapshot.Npc;
            if (npc == null || npc.currentLocation == null)
                return;

            if (snapshot.Location != null && npc.currentLocation != snapshot.Location)
                return;

            // Route NPCs work like the spouse outside bump-kiss pause: stop refreshing movementPause
            // and let vanilla continue the route by itself.
            npc.movementPause = 0;
        }

        /// <summary>
        /// Returns true if any NPC other than the romantic partner is currently visible on screen.
        /// Used to suppress the post-kiss dialogue balloon when there are bystanders watching.
        /// </summary>
        /// <summary>
        /// 10% independent chance for a noticing bystander to say a short reaction line above
        /// their head. Uses the NPC's own "Reaction&lt;Name&gt;" lines when available (e.g. ReactionAbigail),
        /// falls back to the generic "CrowdReaction" pool for NPCs without personalized lines
        /// (custom/SVE NPCs), and uses "CrowdReaction.Child" for children instead.
        /// </summary>
        private void TryShowCrowdReactionLine(NPC npc)
        {
            if (npc == null || random.NextDouble() >= CrowdReactionLineChance)
                return;

            string line;

            if (IsChildNpc(npc))
            {
                line = GetSimpleDialogueLine("CrowdReaction.Child", 1, 10);
            }
            else
            {
                // "Reaction<Name>" has no separator between the prefix and the NPC name
                // (e.g. "ReactionAbigail.1"), unlike the mod's usual "{prefix}.{name}.{n}" pattern.
                line = GetSimpleDialogueLine($"Reaction{npc.Name}", 1, 10);

                if (string.IsNullOrEmpty(line))
                    line = GetSimpleDialogueLine("CrowdReaction", 1, 30);
            }

            if (!string.IsNullOrEmpty(line))
                npc.showTextAboveHead(line);
        }

        /// <summary>
        /// Looks up "{prefix}.{n}" translation keys directly (content pack first, then i18n),
        /// picking a random number in [min, max]. Used for crowd reaction pools that don't
        /// follow the mod's usual per-NPC dialogue key pattern.
        /// </summary>
        private string GetSimpleDialogueLine(string prefix, int min, int max)
        {
            if (string.IsNullOrEmpty(prefix))
                return null;

            for (int i = 0; i < 10; i++)
            {
                int number = random.Next(min, max + 1);
                string value = GetSimpleDialogueKey(prefix, number);

                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            for (int number = min; number <= max; number++)
            {
                string value = GetSimpleDialogueKey(prefix, number);

                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            return null;
        }

        private string GetSimpleDialogueKey(string prefix, int number)
        {
            string key = $"{prefix}.{number}";

            if (contentPackLoader.TryGetEntry(key, out string packValue) && !string.IsNullOrEmpty(packValue))
                return packValue.Replace("@", Game1.player.Name);

            var translation = this.Helper.Translation.Get(key);
            if (translation.HasValue())
                return translation.ToString().Replace("@", Game1.player.Name);

            return null;
        }

        internal bool HasBystandersOnScreen()
        {
            if (Game1.currentLocation == null)
                return false;

            foreach (NPC npc in Game1.currentLocation.characters)
            {
                if (npc == null)
                    continue;

                if (IsCurrentSpouse(npc.Name) || IsDatingPartner(npc.Name))
                    continue;

                if (IsNpcOnScreen(npc))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// True if any non-partner NPC is on screen AND has an unobstructed line of sight
        /// to the player. Used to decide between kissReaction (private) and PublicKissReaction.
        /// </summary>
        internal bool HasBystandersWithLineOfSight()
        {
            if (Game1.currentLocation == null)
                return false;

            foreach (NPC npc in Game1.currentLocation.characters)
            {
                if (npc == null)
                    continue;

                if (IsCurrentSpouse(npc.Name) || IsDatingPartner(npc.Name))
                    continue;

                if (IsNpcOnScreen(npc) && HasLineOfSightToPlayer(npc))
                    return true;
            }

            return false;
        }

        private bool IsNpcWalkingTowardPlayer(NPC npc)
        {
            if (npc == null || !npc.isMoving())
                return false;

            Vector2 diff = Game1.player.Position - npc.Position;
            int expectedDir;
            if (Math.Abs(diff.X) > Math.Abs(diff.Y))
                expectedDir = diff.X > 0 ? 1 : 3;
            else
                expectedDir = diff.Y > 0 ? 2 : 0;

            return npc.FacingDirection == expectedDir;
        }

        private bool IsNpcOnScreen(NPC npc)
        {
            if (npc?.currentLocation == null || npc.currentLocation != Game1.currentLocation)
                return false;

            Vector2 worldPos  = npc.Position;
            Vector2 screenPos = Game1.GlobalToLocal(Game1.viewport, worldPos);

            return screenPos.X >= -64 && screenPos.X <= Game1.viewport.Width  + 64
                && screenPos.Y >= -64 && screenPos.Y <= Game1.viewport.Height + 64;
        }

        private int GetDirectionTowardPlayer(NPC npc)
        {
            Vector2 diff = Game1.player.Position - npc.Position;

            if (Math.Abs(diff.X) > Math.Abs(diff.Y))
                return diff.X > 0 ? 1 : 3; // right : left
            else
                return diff.Y > 0 ? 2 : 0; // down : up
        }
    }
}
