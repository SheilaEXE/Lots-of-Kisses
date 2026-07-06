using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using System;
using System.Collections.Generic;
using System.Reflection;

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
                // NOTE: this used to also fold in `hadController` here. That seemed harmless at
                // capture time, but WasPausedByMod is later used during restore to decide whether
                // an NPC is a "route NPC" safe to release with just a movementPause reset instead
                // of actually restoring CurrentAnimation/CurrentFrame. Stationary special poses
                // (billiards, sitting on the beach, washing dishes, pier fishing) often still have
                // a non-null controller even while holding still, so folding hadController in here
                // made WasPausedByMod=true for them too — silently reintroducing the same "route
                // release only, no real restore" bug even after removing HadController from the
                // restore-time check directly. Only actual movement should count as "route".
                bool wasRouteNpc = wasMoving || isWalkingToward;

                // No matter how a bystander qualifies below, they must actually be able to see
                // the player — an NPC behind a wall or in a separate room of the same
                // GameLocation (e.g. Pierre behind the shop counter) shouldn't react at all,
                // even if they're technically "on screen" or walking in the player's direction.
                if (!HasLineOfSightToPlayer(npc))
                    continue;

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

                this.Monitor.Log($"[FRAME DEBUG] {npc.Name}: CAPTURED — Position={snapshot.Position} Tile={npc.TilePoint} Frame={snapshot.CurrentFrame} HasAnim={(snapshot.CurrentAnimation != null)} HadController={hadController} WasMoving={wasMoving} WasWalkingToward={isWalkingToward}", LogLevel.Debug);

                // Mark this NPC as watching so other mods (e.g. Outfit Reactions) can skip
                // starting their own reactions on them until they're released below.
                npc.modData[BystanderWatchingModDataKey] = "1";

                HoldBystanderWatching(snapshot);

                // Roll the emote now, but don't play it yet — faceGeneralDirection/faceDirection take
                // a few ticks to visually finish the turn. Stash it on the snapshot and let
                // UpdateBystanderRestore's tick loop fire it once the NPC has actually turned.
                // Skipped entirely for NPCs listed in "Ignored Reactions.json" (e.g. cats, Shane's
                // chicken) — they still notice and watch, just never emote.
                if (!IsReactionIgnoredForNpc(npc) && random.NextDouble() < BystanderEmoteChance)
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
            // Skipped while their previous line's speech bubble cooldown hasn't elapsed yet (see
            // CrowdReactionCooldownTicks, decremented every real tick in TickCrowdReactionCooldowns).
            foreach (var snapshot in activeBystanderSnapshots)
            {
                if (snapshot.CrowdReactionCooldownTicks > 0)
                    continue;

                if (IsReactionIgnoredForNpc(snapshot.Npc))
                    continue;

                TryShowCrowdReactionLine(snapshot);
            }
        }

        /// <summary>
        /// Decrements every active bystander's crowd-reaction cooldown by one real tick.
        /// Must run every game update (60/sec), NOT once per kiss cycle — a kiss cycle only
        /// lasts a fraction of a second, so ticking the cooldown there made it take ~150 cycles
        /// to expire instead of ~2.5 real seconds, effectively silencing bystanders after their
        /// first line for the rest of the kiss sequence.
        /// </summary>
        /// <summary>Real ticks over which the bubble fades out smoothly before closing (0.3s at 60 ticks/sec).</summary>
        private const int CrowdReactionBubbleFadeTicks = 18;

        private void TickCrowdReactionCooldowns()
        {
            foreach (var snapshot in activeBystanderSnapshots)
            {
                if (snapshot.CrowdReactionCooldownTicks > 0)
                    snapshot.CrowdReactionCooldownTicks--;

                if (snapshot.CrowdReactionBubbleCloseTicks > 0)
                {
                    snapshot.CrowdReactionBubbleCloseTicks--;

                    // Ease the bubble's alpha down smoothly over the last CrowdReactionBubbleFadeTicks
                    // ticks instead of snapping it to 0 in a single frame, so it visually fades out
                    // the same way the game's own (pause-sensitive) timer would — just driven by our
                    // own real-tick counter so it isn't stuck open during the paused valley.
                    if (snapshot.CrowdReactionBubbleCloseTicks <= CrowdReactionBubbleFadeTicks)
                    {
                        float fadeProgress = snapshot.CrowdReactionBubbleCloseTicks / (float)CrowdReactionBubbleFadeTicks;
                        SetSpeechBubbleAlpha(snapshot.Npc, fadeProgress);
                    }

                    if (snapshot.CrowdReactionBubbleCloseTicks == 0 && snapshot.Npc != null)
                    {
                        ForceCloseSpeechBubble(snapshot.Npc);
                    }
                }
            }
        }

        /// <summary>
        /// Sets the NPC's speech bubble opacity directly (0 = invisible, 1 = fully visible),
        /// using the same textAboveHeadAlpha field confirmed from the game's compiled metadata
        /// that ForceCloseSpeechBubble zeroes out at the end of the fade.
        /// </summary>
        private void SetSpeechBubbleAlpha(NPC npc, float alpha)
        {
            if (npc == null)
                return;

            try
            {
                FieldInfo field = npc.GetType().GetField("textAboveHeadAlpha", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                field?.SetValue(npc, alpha);
            }
            catch
            {
                // Internal field name/shape may differ between game versions — skip silently.
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
            {
                snapshot?.Npc?.modData?.Remove(BystanderWatchingModDataKey);

                // Hand back any controller we suspended — this is an emergency/early clear path,
                // so it wouldn't otherwise go through RestoreAllBystanders' normal handoff.
                if (snapshot?.SavedController != null && snapshot.Npc != null)
                {
                    snapshot.Npc.controller = snapshot.SavedController;
                    snapshot.SavedController = null;
                }
            }

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
        /// Controller is left alone for route NPCs; only stationary special-pose NPCs have theirs
        /// suspended (see HoldBystanderWatching/RestoreAllBystanders).
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

            // NOTE: only actual movement counts as "route NPC" here — same fix as the capture and
            // restore classifications elsewhere in this file. HadController alone used to count
            // too, but a stationary NPC doing a scripted activity (like fishing at the pier) can
            // still have a non-null controller while standing still. Misclassifying them as
            // "route NPCs" sent them through the gentle branch below, which never stops their
            // CurrentAnimation — so the fishing loop kept animating underneath the forced
            // "looking at player" pose, showing as two overlapping sprite frames at once.
            bool wasRouteNpc = snapshot.WasPausedByMod
                            || snapshot.WasMoving
                            || snapshot.WasWalkingTowardPlayer;

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
                // Static/special-action NPCs, like someone sitting in the Saloon or fishing at a
                // fixed spot, don't have a walking route to protect the way moving NPCs do. But
                // some of them still have a non-null controller (e.g. a small back-and-forth path
                // tied to their fishing animation) that, left running behind our forced idle pose,
                // kept silently repositioning them — showing up as the NPC appearing frozen in
                // their old spot AND at a second, controller-driven position at the same time.
                // Suspend it here and hand it back in RestoreAllBystanders.
                // HoldBystanderWatching runs every tick while held — only capture the controller
                // the first time (when it's still non-null). Without this guard, the very next
                // tick would overwrite SavedController with the null we just set below, losing
                // the real reference for good.
                if (npc.controller != null)
                    snapshot.SavedController = npc.controller;
                npc.controller = null;

                int lookDirection = GetDirectionTowardPlayer(npc);

                if (Game1.ticks % 15 == 0)
                    this.Monitor.Log($"[FRAME DEBUG] {npc.Name}: BEFORE hold — Position={npc.Position} Tile={npc.TilePoint} Frame={npc.Sprite.CurrentFrame} HasAnim={(npc.Sprite.CurrentAnimation != null)} FacingDir={npc.FacingDirection} savedController={(snapshot.SavedController != null)} liveControllerNow={(npc.controller != null)}", LogLevel.Debug);

                npc.Sprite.StopAnimation();
                npc.Sprite.ClearAnimation();
                npc.Sprite.CurrentAnimation = null;
                npc.flip = false;
                npc.FacingDirection = lookDirection;
                npc.faceDirection(lookDirection);
                npc.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(lookDirection);
                npc.Sprite.UpdateSourceRect();

                if (Game1.ticks % 15 == 0)
                    this.Monitor.Log($"[FRAME DEBUG] {npc.Name}: AFTER hold — Position={npc.Position} Tile={npc.TilePoint} Frame={npc.Sprite.CurrentFrame} LookDir={lookDirection}", LogLevel.Debug);
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

                // Only treat this as a "route NPC" (safe to let vanilla resume on its own) when it
                // was actually moving or walking toward the player. NPCs in a stationary special
                // pose (billiards, sitting on the beach, washing dishes, fishing off the pier) can
                // still have a leftover HadController=true even while standing still doing their
                // loop animation — that's not an ongoing route vanilla will resume, so relying on
                // HadController alone was skipping the real CurrentAnimation/CurrentFrame restore
                // below and leaving them stuck in the idle frame.
                bool wasRouteNpc = snapshot.WasPausedByMod || snapshot.WasMoving || snapshot.WasWalkingTowardPlayer;

                if (wasRouteNpc)
                {
                    ReleaseRouteBystanderPauseOnly(snapshot);
                    routeSnapshots.Add(snapshot);
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

                // Hand the controller back, if we suspended one while holding this NPC watching.
                if (snapshot.SavedController != null)
                {
                    npc.controller = snapshot.SavedController;
                    snapshot.SavedController = null;
                }

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
        /// Force-closes an NPC's speech bubble immediately, instead of leaving an empty bubble
        /// shell on screen (which is what passing an empty string to showTextAboveHead did — the
        /// bubble shape stayed visible with no text inside, and skipped the game's normal
        /// fade-out). Field names and types confirmed directly from the game's compiled
        /// Stardew Valley.dll metadata: textAboveHeadTimer (int), textAboveHeadPreTimer (int),
        /// textAboveHead (string), textAboveHeadAlpha (float, controls the bubble's fade opacity).
        /// Zeroing the timer and alpha together closes the bubble on the very next draw call,
        /// the same as when its timer naturally reaches zero.
        /// </summary>
        private void ForceCloseSpeechBubble(NPC npc)
        {
            if (npc == null)
                return;

            bool foundAnyField = false;

            foreach (string fieldName in new[] { "textAboveHeadTimer", "textAboveHeadPreTimer", "textAboveHead", "textAboveHeadAlpha" })
            {
                try
                {
                    FieldInfo field = npc.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field == null)
                        continue;

                    foundAnyField = true;

                    if (field.FieldType == typeof(int))
                        field.SetValue(npc, 0);
                    else if (field.FieldType == typeof(float))
                        field.SetValue(npc, 0f);
                    else if (field.FieldType == typeof(string))
                        field.SetValue(npc, null);
                }
                catch
                {
                    // Internal field name/shape may differ between game versions — skip silently.
                }
            }

            // Fallback: if none of the confirmed internal field names exist in this game version
            // (e.g. a future update renames them), at least clear the visible text so it's not
            // stuck showing the same line forever — strictly better than a permanently stuck line.
            if (!foundAnyField)
                ShowTextAboveHeadWithPipeSupport(npc, "");
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
        private void TryShowCrowdReactionLine(BystanderSnapshot snapshot)
        {
            NPC npc = snapshot?.Npc;

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
            {
                ShowTextAboveHeadWithPipeSupport(npc, line);

                // Keep this bystander quiet for a few cycles after speaking, giving the bubble
                // time to fully close before they're eligible to speak again.
                snapshot.CrowdReactionCooldownTicks = 150; // ~2.5s at 60 ticks/sec

                // The valley pauses during each vanilla kiss cycle, which stalls the game's own
                // bubble timer — force-close it after a fixed 3 real seconds instead of relying
                // on that timer to count down on its own.
                snapshot.CrowdReactionBubbleCloseTicks = 180; // 3s at 60 ticks/sec
            }
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
