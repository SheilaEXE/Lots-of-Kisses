using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.GameData.Characters;
using System;
using System.Collections.Generic;

namespace LotsOfKisses
{
    // Multi-kiss (continuous kiss) system: starting/ending a chain, tiers, public dialogue, and the tier-3 player lean-in visual effect.
    public partial class ModEntry
    {
        // Returns true if the vanilla kiss fired on this attempt (informational only — no longer
        // gates whether the chain activates, see the revert note near TryCheckActionForAutoKiss...
        // in Kiss.cs: npc.checkAction()'s raw boolean return isn't a reliable "a kiss specifically
        // happened" signal, since checkAction is the general click-interaction handler covering
        // dialogue/gifts/quests/etc too — gating the whole chain on it broke normal kissing).
        private bool StartContinuousKiss(NPC npc, int kissTier, bool isNewSequence = false)
        {
            if (npc == null || npc.currentLocation != Game1.player.currentLocation)
                return false;

            if (isNewSequence)
            {
                // publicMultiKiss should be limited to once per multi-kiss chain, not once forever.
                // The flag is set when the dialogue opens; rearm it only when the player starts
                // a brand-new multi-kiss chain, so repeated cycles in the same chain don't spam it.
                publicMultiKissDialogueTriggered = false;
                continuousKissCyclesDone = 0;
            }

            // A queued NPC dialogue is preserved by the simulated click; only an open dialogue/menu
            // should block starting a new continuous kiss.
            if (IsAutoKissBlockedByOpenDialogueOrMenu())
                return false;

            if (GetApproachKissBlockTimer(npc) > 0)
                return false;

            CaptureNpcPreKissSpecialAction(npc);

            int durationMs = kissTier switch
            {
                1 => 700,
                2 => 2000,
                3 => 4000,
                _ => bumpKissVisualDelayMs
            };

            activeKissVisualDelayMs = durationMs;

            // The dialogue itself is intentionally left alone here (never cleared) so it stays
            // available for the player to open manually afterward — only the automatic pop-up
            // triggered by this simulated click is suppressed by the Harmony patch.
            //
            // BUGFIX (ghost kiss): this used to activate the whole multi-kiss chain (hearts,
            // dialogue, movementPause hold) unconditionally regardless of whether the real
            // vanilla kiss actually fired — a "ghost" multi-kiss could run entirely through this
            // mod's own effects with no real vanilla kiss animation ever happening. Confirmed via
            // diagnostic logging that TryTriggerRomanticContinuousKiss's return value is reliable
            // now (it's built on the same CanMove-based confirmation as the bump kiss trigger —
            // see TryCheckActionForAutoKissWithoutDialogue in Kiss.cs) — abort cleanly if it's
            // false, restoring the captured pose since nothing else has touched the NPC yet.
            bool vanillaTriggered = TryTriggerRomanticContinuousKiss(npc);
            if (!vanillaTriggered)
            {
                TryRestoreNpcPreKissSpecialAction(clearAfterRestore: true);
                return false;
            }

            ResetContinuousKissPlayerLeanEffect(true);

            continuousKissActive = true;
            continuousKissNpc = npc;
            continuousKissTimer = MsToTicks(durationMs);
            continuousKissHeartTriggered = false;
            continuousKissSmokeTriggered = false;
            continuousKissTier = kissTier;
            continuousKissPendingRestart = false;
            continuousKissGapTimer = 0;
            continuousKissTouchHoldTimer = 0;
            continuousKissWasTouchingPartner = true;
            return true;
        }

        private bool TryTriggerPublicMultiKissDialogue(NPC npc)
        {
            if (npc == null || Game1.player == null)
                return false;

            if (publicMultiKissDialogueTriggered)
                return false;

            if (IsPrivateKissMoment(npc))
                return false;

            string locName = npc.currentLocation?.Name ?? "";
            if (locName == "Farm" || locName == "FarmHouse")
                return false;

            if (continuousKissCyclesDone < 3)
                return false;

            int chance = continuousKissCyclesDone switch
            {
                3 => 10,
                4 => 20,
                _ => 30
            };

            if (random.Next(100) >= chance)
                return false;

            // publicMultiKiss was split into two separate dialogue pools: OutdoorKisses for
            // outside locations and IndoorKisses for anything enclosed (houses, shops, etc),
            // each NPC having 13 lines per pool instead of the old single 15-line pool.
            bool isOutdoors = npc.currentLocation?.IsOutdoors ?? true;
            string dialoguePrefix = isOutdoors ? "OutdoorKisses" : "IndoorKisses";

            string line = GetDialogueLine(dialoguePrefix, 1, 13, npc);
            if (string.IsNullOrEmpty(line))
                return false;

            publicMultiKissDialogueTriggered = true;

            continuousKissActive = false;
            continuousKissPendingRestart = false;
            continuousKissGapTimer = 0;

            // Push our line on top of existing dialogue instead of clearing the stack.
            // Daily, quest, event, and other-mod dialogue must remain available afterward.
            npc.CurrentDialogue.Push(new Dialogue(npc, "", line));
            Game1.drawDialogue(npc);

            pendingPublicMultiKissShyEmote = true;
            pendingPublicMultiKissShyNpc = npc;
            pendingPublicMultiKissShyEmoteTimer = 30;

            ScheduleBystanderRestore(npc);
            bystanderRestore.ForceStart = true;

            didReactThisTick = true;
            cooldown = 120;
            dialogueCooldown = 90;

            this.Monitor.Log(
                $"[PUBLIC MULTIKISS] npc={npc.Name} cycles={continuousKissCyclesDone} chance={chance} loc={locName}",
                LogLevel.Trace
            );

            return true;
        }

        // Slides the farmer a few pixels toward their partner during tier 3.
        // Applies only the delta between the previous and the new offset,
        // so the player doesn't get locked to an absolute position if they try to move.
        private void StartContinuousKissPlayerLeanIn(NPC partner)
        {
            if (Game1.player == null || partner == null)
                return;

            Vector2 direction = GetContinuousKissPlayerLeanDirection(partner);
            if (direction == Vector2.Zero)
                return;

            continuousKissPlayerLeanDirection = direction;
            StartContinuousKissPlayerLeanAnimation(direction * ContinuousKissTier3LeanPixels, ContinuousKissTier3LeanAnimationTicks);

            // Play sound at the moment the farmer starts leaning in.
            PlayHugSound();

            continuousKissPlayerLeanInTriggered = true;
        }

        private void StartContinuousKissPlayerLeanOut()
        {
            if (Game1.player == null)
                return;

            StartContinuousKissPlayerLeanAnimation(Vector2.Zero, ContinuousKissTier3LeanAnimationTicks);
            continuousKissPlayerLeanOutTriggered = true;
        }

        private Vector2 GetContinuousKissPlayerLeanDirection(NPC partner)
        {
            if (Game1.player == null || partner == null)
                return Vector2.Zero;

            // First tries to use the direction the farmer is currently facing.
            if (Game1.player.FacingDirection == 1)
                return new Vector2(1f, 0f); // direita

            if (Game1.player.FacingDirection == 3)
                return new Vector2(-1f, 0f); // esquerda

            // Fallback: if the farmer isn't facing left or right for any reason,
            // fall back to the horizontal position of the partner.
            float diffX = partner.Position.X - Game1.player.Position.X;

            if (Math.Abs(diffX) > 0.1f)
                return new Vector2(Math.Sign(diffX), 0f);

            return Vector2.Zero;
        }

        private void StartContinuousKissPlayerLeanAnimation(Vector2 targetOffset, int durationTicks)
        {
            continuousKissPlayerLeanAnimationActive = true;
            continuousKissPlayerLeanStartOffset = continuousKissPlayerLeanAppliedOffset;
            continuousKissPlayerLeanTargetOffset = targetOffset;
            continuousKissPlayerLeanTimer = 0;
            continuousKissPlayerLeanDuration = Math.Max(1, durationTicks);
        }

        private void UpdateContinuousKissPlayerLeanEffect()
        {
            if (!continuousKissPlayerLeanAnimationActive || Game1.player == null)
                return;

            // Keep the visual offset exactly where it is while inventory, map, GMCM, or
            // another clickable menu is open. The animation resumes from the same tick.
            if (Game1.activeClickableMenu != null)
                return;

            continuousKissPlayerLeanTimer++;

            float progress = Math.Min(1f, continuousKissPlayerLeanTimer / (float)continuousKissPlayerLeanDuration);

            // Eases in/out to avoid a pixel-teleport feel.
            progress = progress * progress * (3f - 2f * progress);

            Vector2 desiredOffset = Vector2.Lerp(continuousKissPlayerLeanStartOffset, continuousKissPlayerLeanTargetOffset, progress);
            ApplyContinuousKissPlayerLeanOffset(desiredOffset);

            if (continuousKissPlayerLeanTimer >= continuousKissPlayerLeanDuration)
                continuousKissPlayerLeanAnimationActive = false;
        }

        private void ApplyContinuousKissPlayerLeanOffset(Vector2 desiredOffset)
        {
            if (Game1.player == null)
                return;

            Vector2 delta = desiredOffset - continuousKissPlayerLeanAppliedOffset;
            if (delta.LengthSquared() <= 0.0001f)
                return;

            Game1.player.Position = Game1.player.Position + delta;
            continuousKissPlayerLeanAppliedOffset = desiredOffset;
        }

        private void ResetContinuousKissPlayerLeanEffect(bool applyInverseOffset = true)
        {
            if (applyInverseOffset && Game1.player != null)
                ApplyContinuousKissPlayerLeanOffset(Vector2.Zero);

            continuousKissPlayerLeanInTriggered = false;
            continuousKissPlayerLeanOutTriggered = false;
            continuousKissPlayerLeanAnimationActive = false;
            continuousKissPlayerLeanDirection = Vector2.Zero;
            continuousKissPlayerLeanStartOffset = Vector2.Zero;
            continuousKissPlayerLeanTargetOffset = Vector2.Zero;
            continuousKissPlayerLeanAppliedOffset = Vector2.Zero;
            continuousKissPlayerLeanTimer = 0;
            continuousKissPlayerLeanDuration = 0;
        }

        // Forces the continuous kiss to end even when normal end conditions are not met (e.g. NPC teleported, player moved too far). Used to ensure the NPC never gets stuck in an animation or with a locked controller if something goes wrong.
        private void ForceEndContinuousKiss(NPC npc)
        {
            ScheduleBystanderRestore(npc);
            ResetContinuousKissState();
            ResetPostKissState();

            kissProximityTimer = 0;
            playerWasTouchingPartner = false;
            continuousKissTouchHoldTimer = 0;
            continuousKissWasTouchingPartner = false;

            activeKissVisualDelayMs = bumpKissVisualDelayMs;
        }

        // Hard-reset helper: ensures the NPC returns to a clean state (no controller, no stuck animation) after a continuous kiss, especially a bump kiss, even if something goes wrong mid-sequence. Called on both natural kiss end and emergency cleanup.
        private void WakeNpcAfterMultiKiss(NPC npc, bool reviveBrain = false)
        {
            if (npc == null)
                return;

            Game1.player.CanMove = true;
            Game1.player.completelyStopAnimatingOrDoingAction();

            npc.controller = null;
            npc.Halt();
            npc.Sprite.StopAnimation();
            npc.Sprite.ClearAnimation();
            npc.Sprite.CurrentAnimation = null;

            // Hard reset — fully clears controller and animation state.
            npc.flip = false;
            npc.faceDirection(npc.FacingDirection);
            npc.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(npc.FacingDirection);

            npc.movementPause = 0;
            npc.addedSpeed = 0;
            npc.Sprite.UpdateSourceRect();

            playerWasTouchingPartner = false;
            kissProximityTimer = 0;
            activeKissVisualDelayMs = bumpKissVisualDelayMs;
            continuousKissTouchHoldTimer = 0;
            continuousKissWasTouchingPartner = false;

            int delayedActionToken = delayedActionContextToken;
            DelayedAction.functionAfterDelay(() =>
            {
                if (!IsCurrentDelayedAction(delayedActionToken) || npc == null)
                    return;

                Game1.player.CanMove = true;
                Game1.player.completelyStopAnimatingOrDoingAction();

                npc.controller = null;
                npc.Halt();
                npc.Sprite.StopAnimation();
                npc.Sprite.ClearAnimation();
                npc.Sprite.CurrentAnimation = null;

                npc.flip = false; // Garantia dupla!
                npc.faceDirection(npc.FacingDirection);
                npc.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(npc.FacingDirection);

                npc.movementPause = 0;
                npc.addedSpeed = 0;
                npc.Sprite.UpdateSourceRect();

                this.Monitor.Log(
                    $"[MULTIKISS HARD CLEAR] npc={npc.Name} pause={npc.movementPause} controller={(npc.controller != null)} moving={npc.isMoving()}",
                    LogLevel.Trace
                );
            }, 150);

            if (reviveBrain)
            {
                this.Monitor.Log(
                    $"[MULTIKISS REVIVE BRAIN IGNORADO] npc={npc?.Name ?? "null"}",
                    LogLevel.Trace
                );
            }
        }

        // =====================================================================
        // CONTINUOUS KISS LOGIC
        // =====================================================================
        private void UpdateContinuousKissSystem(NPC partner)
        {
            if (partner == null)
                return;

            // If the player sits down mid-chain (e.g. in a chair the NPC walked them next to),
            // cleanly end the sequence instead of leaving it running with a seated player —
            // ForceEndContinuousKiss releases the NPC properly, same as when they leave the
            // location, rather than just silently skipping updates and leaving them stuck.
            if (Game1.player.IsSitting())
            {
                ForceEndContinuousKiss(partner ?? continuousKissNpc);
                return;
            }

            // Pause (don't cancel) the multi-kiss cycle while any menu is open — inventory,
            // map, GMCM, etc. This is separate from the pending-dialogue block that was
            // intentionally removed: an open menu means the player actively paused to look at
            // something, so hearts/smoke effects shouldn't keep climbing behind it. The cycle
            // picks back up exactly where it left off once the menu closes.
            if (Game1.activeClickableMenu != null)
                return;

            if (continuousKissPendingRestart)
            {
                if (continuousKissNpc == null || continuousKissNpc.currentLocation != Game1.player.currentLocation)
                {
                    ForceEndContinuousKiss(partner ?? continuousKissNpc);
                    return;
                }

                if (continuousKissGapTimer > 0)
                    return;

                float restartDistance = DistanceToPlayer(continuousKissNpc);
                if (restartDistance > 72f)
                {
                    NPC postNpc = continuousKissNpc;
                    string postLine = GetDialogueLine("kissReaction", 1, 10, postNpc);
                    string publicLine = GetDialogueLine("PublicKissReaction", 1, 2, postNpc);

                    ResetContinuousKissState();
                    activeKissVisualDelayMs = bumpKissVisualDelayMs;

                    int delayedActionToken = delayedActionContextToken;
                    DelayedAction.functionAfterDelay(() =>
                    {
                        if (!IsCurrentDelayedAction(delayedActionToken) || postNpc == null || postNpc.currentLocation != Game1.player.currentLocation)
                            return;

                        if (DistanceToPlayer(postNpc) >= 72f && Game1.activeClickableMenu == null)
                        {
                            if (!HasBystandersOnScreen() && !string.IsNullOrEmpty(postLine))
                            {
                                ShowTextAboveHeadWithPipeSupport(postNpc, postLine);
                                dialogueCooldown = 120;
                            }
                            else if (HasBystandersOnScreen() && !string.IsNullOrEmpty(publicLine))
                            {
                                ShowTextAboveHeadWithPipeSupport(postNpc, publicLine);
                                dialogueCooldown = 120;
                            }
                        }
                    }, 200);

                    return;
                }

                continuousKissPendingRestart = false;

                // If the vanilla kiss doesn't fire for this next cycle, don't leave the chain in
                // a stale "still active but nothing progressing" state — cleanly end it, same as
                // any other abort path (releasing the NPC's pose/movementPause properly).
                if (!StartContinuousKiss(continuousKissNpc, continuousKissTier, false))
                    ForceEndContinuousKiss(continuousKissNpc);

                return;
            }

            if (!continuousKissActive || continuousKissNpc == null)
                return;

            if (continuousKissNpc != partner || partner.currentLocation != Game1.player.currentLocation)
            {
                ForceEndContinuousKiss(partner);
                return;
            }

            float distance = DistanceToPlayer(partner);

            if (distance > 90f)
            {
                NPC postNpc = partner;
                string postLine = GetDialogueLine("kissReaction", 1, 10, postNpc);
                string publicLine = GetDialogueLine("PublicKissReaction", 1, 2, postNpc);

                ScheduleBystanderRestore(partner);
                ResetContinuousKissState();
                activeKissVisualDelayMs = bumpKissVisualDelayMs;

                int delayedActionToken = delayedActionContextToken;
                DelayedAction.functionAfterDelay(() =>
                {
                    if (!IsCurrentDelayedAction(delayedActionToken) || postNpc == null || postNpc.currentLocation != Game1.player.currentLocation)
                        return;

                    if (DistanceToPlayer(postNpc) >= 72f && Game1.activeClickableMenu == null)
                    {
                        if (!HasBystandersWithLineOfSight() && !string.IsNullOrEmpty(postLine))
                        {
                            ShowTextAboveHeadWithPipeSupport(postNpc, postLine);
                            dialogueCooldown = 120;
                        }
                        else if (HasBystandersWithLineOfSight() && !string.IsNullOrEmpty(publicLine))
                        {
                            ShowTextAboveHeadWithPipeSupport(postNpc, publicLine);
                            dialogueCooldown = 120;
                        }
                    }
                }, 200);

                return;
            }

            // StartContinuousKiss only marks the sequence active after the vanilla kiss fires,
            // so no retry is needed while the sequence is already active.

            if (continuousKissTimer > 0)
            {
                if (continuousKissTier == 2 && !continuousKissHeartTriggered)
                {
                    // Tier 2 inherited the old tier 3 mechanic:
                    // 2-second kiss with a heart effect mid-cycle.
                    int totalTicks = MsToTicks(2000);
                    if (continuousKissTimer <= totalTicks / 2)
                    {
                        partner.doEmote(20);
                        Game1.player.doEmote(20);
                        continuousKissHeartTriggered = true;
                    }
                }
                else if (continuousKissTier == 3)
                {
                    int totalTicks = MsToTicks(4000);
                    int elapsedTicks = totalTicks - continuousKissTimer;

                    // After 2.5 seconds: farmer slides a few pixels toward the partner.
                    if (!continuousKissPlayerLeanInTriggered && elapsedTicks >= MsToTicks(ContinuousKissTier3LeanInDelayMs))
                        StartContinuousKissPlayerLeanIn(partner);

                    // After 1 second: heart effect on both the partner and the farmer.
                    if (!continuousKissHeartTriggered && continuousKissTimer <= totalTicks - MsToTicks(1000))
                    {
                        partner.doEmote(20);
                        Game1.player.doEmote(20);
                        continuousKissHeartTriggered = true;
                    }

                    // After 3 seconds: custom blush smoke on the partner.
                    if (!continuousKissSmokeTriggered && continuousKissTimer <= totalTicks - MsToTicks(3000))
                    {
                        ShowBlushSmoke(partner, 68);
                        continuousKissSmokeTriggered = true;
                    }
                }

                return;
            }

            if (continuousKissTier == 3 && !continuousKissPlayerLeanOutTriggered)
                StartContinuousKissPlayerLeanOut();

            continuousKissActive = false;
            continuousKissCyclesDone++;

            // Bystanders react based on current kiss tier.
            TriggerBystanderReactions(continuousKissTier, partner);

            if (TryTriggerPublicMultiKissDialogue(partner))
                return;

            // publicMultiKiss did not fire — kiss will restart. Schedule restore so bystanders
            // don't stay frozen if the player moves away before publicMultiKiss ever triggers.
            ScheduleBystanderRestore(partner);

            continuousKissPendingRestart = true;
            continuousKissGapTimer = 25;
            continuousKissTier = RollContinuousKissTier();
        }
    }
}
