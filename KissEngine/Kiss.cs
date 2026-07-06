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
    public partial class ModEntry
    {
        private bool TryCheckActionForAutoKissWithoutDialogue(NPC npc)
        {
            lastAutoKissClickWasBlockedDialogue = false;

            if (npc == null || Game1.player == null || npc.currentLocation != Game1.player.currentLocation)
                return false;

            bool previousSuppress = suppressDialogueFromAutoKissClick;
            NPC previousNpc = suppressDialogueAutoKissNpc;
            bool previousSuppressed = suppressedDialogueDuringAutoKissClick;
            bool previousKissPatchFlag = LotsOfKissesKissPatchActive;

            suppressDialogueFromAutoKissClick = true;
            suppressDialogueAutoKissNpc = npc;
            suppressedDialogueDuringAutoKissClick = false;
            LotsOfKissesKissPatchActive = true;

            // Some NPCs have a fixed "location override" line tied to their current pose/location
            // (e.g. Sebastian playing video games, Abigail sitting on the couch) that never clears
            // on its own — unlike a regular queued dialogue, it's recalculated fresh every time
            // checkAction runs, so stashing CurrentDialogue alone doesn't help here. Suppressing it
            // through NPC.HasLocationOverrideDialogue (Harmony Prefix below) makes checkAction take
            // the kiss branch instead, without needing to touch or restore anything on the NPC itself
            // — the same override line is recalculated fresh next time the player clicks normally.
            bool previousSuppressLocationOverride = suppressLocationOverrideDialogueDuringAutoKissClick;
            suppressLocationOverrideDialogueDuringAutoKissClick = true;

            // The vanilla checkAction, when it sees a pending CurrentDialogue on the NPC, opens
            // that dialogue instead of running the kiss logic — it never reaches the animation
            // code at all, even with the pop-up suppressed by our Harmony patch above. That's
            // what caused "ghost kisses": the mod's own effects (heart, smoke) fired because they
            // don't depend on checkAction, but the actual pose animation never played, since the
            // native method took the "show dialogue" branch instead of the "kiss" branch.
            // Temporarily stash the queued dialogue out of the NPC during this simulated click so
            // checkAction takes the kiss branch, then restore it right after — the player still
            // gets to read it manually later, exactly as before.
            List<Dialogue> stashedDialogue = null;
            if (npc.CurrentDialogue != null && npc.CurrentDialogue.Count > 0)
            {
                // Pop preserves top-to-bottom order; storing in that same order lets us Push
                // back in reverse to reconstruct the identical stack afterward.
                stashedDialogue = new List<Dialogue>(npc.CurrentDialogue.Count);
                while (npc.CurrentDialogue.Count > 0)
                    stashedDialogue.Add(npc.CurrentDialogue.Pop());
            }

            try
            {
                bool result = npc.checkAction(Game1.player, npc.currentLocation);
                bool dialogueWasBlocked = suppressedDialogueDuringAutoKissClick;

                lastAutoKissClickWasBlockedDialogue = dialogueWasBlocked;

                // NOTE: dialogueWasBlocked being true does NOT mean the kiss failed — checkAction
                // can successfully process the kiss (result = true) while also, somewhere in the
                // same call, tripping our dialogue-suppression patch for an unrelated reason (e.g.
                // an internal dialogue check that always runs alongside the kiss branch). Treating
                // "a dialogue was blocked" as "the kiss must have failed" was silently discarding
                // successful kisses — the mod's own heart/smoke effects still fired (they don't
                // depend on this return value), but the actual pose animation never played,
                // because this method returned false even though checkAction had already
                // performed the real kiss. Trust checkAction's own result instead.
                if (dialogueWasBlocked && !result)
                {
                    kissBlockAfterDialogueTimer = Math.Max(kissBlockAfterDialogueTimer, 120);
                    return false;
                }

                return result;
            }
            finally
            {
                suppressDialogueFromAutoKissClick = previousSuppress;
                suppressDialogueAutoKissNpc = previousNpc;
                suppressedDialogueDuringAutoKissClick = previousSuppressed;
                LotsOfKissesKissPatchActive = previousKissPatchFlag;
                suppressLocationOverrideDialogueDuringAutoKissClick = previousSuppressLocationOverride;

                // Restore the original pending dialogue, unless checkAction pushed a brand new
                // one of its own (rare, but don't stomp on it if it did).
                if (stashedDialogue != null && npc.CurrentDialogue != null && npc.CurrentDialogue.Count == 0)
                {
                    // stashedDialogue[0] was the original top of the stack (read first), so push
                    // it last to keep it back on top.
                    for (int i = stashedDialogue.Count - 1; i >= 0; i--)
                        npc.CurrentDialogue.Push(stashedDialogue[i]);
                }
            }
        }
// ======================================================================
        // CONTINUOUS KISS LOGIC — START, MAINTENANCE AND END (including bump kiss, which can escalate outside the farm)
// ======================================================================

        private int RollContinuousKissTier()
        {
            // Tier 1: 60% | Tier 2: 30% | Tier 3: 10%
            double roll = random.NextDouble();

            if (roll < 0.10) return 3;
            if (roll < 0.40) return 2;
            return 1;
        }

        private sealed class NpcPreKissSpecialActionSnapshot
        {
            public NPC Npc;
            public GameLocation Location;
            public int FacingDirection;
            public int CurrentFrame;
            public bool Flip;
            public int MovementPause;
            public int AddedSpeed;
            public List<FarmerSprite.AnimationFrame> CurrentAnimation;
        }

        private System.Collections.Generic.Dictionary<string, NpcPreKissSpecialActionSnapshot> preKissSpecialActionSnapshotsByNpc = new();
        private System.Collections.Generic.Dictionary<string, int> preKissSpecialActionRestoreDelayTicksByNpc = new();
        private const float NpcSpecialActionRestoreDistance = 300f;

        private bool HasNpcPreKissSpecialAction(NPC npc)
        {
            return npc != null && preKissSpecialActionSnapshotsByNpc.ContainsKey(npc.Name);
        }

        private void ClearNpcPreKissSpecialAction(NPC npc = null, string npcNameOverride = null)
        {
            if (npc == null && npcNameOverride == null)
            {
                // No specific NPC given — clear everything (used on day change, warp, etc).
                preKissSpecialActionSnapshotsByNpc.Clear();
                preKissSpecialActionRestoreDelayTicksByNpc.Clear();
                return;
            }

            string key = npc?.Name ?? npcNameOverride;
            preKissSpecialActionSnapshotsByNpc.Remove(key);
            preKissSpecialActionRestoreDelayTicksByNpc.Remove(key);
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

            if (outsideBumpPauseActive && outsideBumpPauseNpc == npc)
                return true;

            return false;
        }

        private void TrySetSpritePrivateField(object target, string fieldName, object value)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
                return;

            try
            {
                FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                    field.SetValue(target, value);
            }
            catch
            {
                // Campo interno opcional.
            }
        }

        private void CaptureNpcPreKissSpecialAction(NPC npc)
        {
            if (npc == null || npc.Sprite == null || npc.currentLocation == null)
                return;

            // Never overwrite an existing snapshot for this NPC — the FIRST captured pose is the
            // real "original" state, and every later cycle of the same multi-kiss chain should
            // restore back to that one, not to whatever pose the NPC happened to be forced into
            // mid-chain (e.g. the idle frame we snap them to right below, or a later kiss cycle's
            // intermediate state).
            if (preKissSpecialActionSnapshotsByNpc.ContainsKey(npc.Name))
                return;

            // NOTE: UpdateSpouseLookAtPlayer (DailySystems.cs) turns the NPC to face the player
            // as soon as they're close, well before any kiss starts — so by now the NPC's pose
            // may already be "looking at player", not the real original (e.g. Sebastian fishing,
            // facing north). That system keeps its own memory of the true original pose to undo
            // its own turn later, but loses it too easily (see below). Adopt it here instead, so
            // the two systems don't fight over the same NPC.
            int? passiveLookFacingOverride = null;
            int? passiveLookFrameOverride = null;
            bool hasPassiveLookOverride = false;
            if (passiveLookRestoreActive && passiveLookRestoreNpcName == npc.Name)
            {
                if (passiveLookRestoreFacing >= 0)
                    passiveLookFacingOverride = passiveLookRestoreFacing;
                if (passiveLookRestoreFrame >= 0)
                    passiveLookFrameOverride = passiveLookRestoreFrame;

                hasPassiveLookOverride = passiveLookFacingOverride.HasValue || passiveLookFrameOverride.HasValue;

                // Don't clear the passive-look memory yet — only once it's actually saved into
                // the kiss snapshot below. Clearing early risks losing it if the capture below
                // ends up skipped (e.g. a frame-0 pose wrongly read as "nothing special").
            }

            List<FarmerSprite.AnimationFrame> animation = null;
            if (npc.Sprite.CurrentAnimation != null && npc.Sprite.CurrentAnimation.Count > 0)
                animation = new List<FarmerSprite.AnimationFrame>(npc.Sprite.CurrentAnimation);

            bool isWalking = npc.isMoving();
            bool hasSpecialAnimation = animation != null && animation.Count > 0;
            int effectiveFrame = passiveLookFrameOverride ?? npc.Sprite.CurrentFrame;
            int effectiveFacing = passiveLookFacingOverride ?? npc.FacingDirection;
            // NOTE: used to also require CurrentFrame >= 16 to count as "special enough to
            // save" — but many special poses (billiards, sitting, washing dishes) use lower
            // frame indices, so that silently skipped capturing them. A pose confirmed by the
            // passive-look system always counts, even at frame 0 (e.g. just standing north) —
            // there the "specialness" is the facing direction, not the frame index.
            bool hasSpecialStaticFrame = hasPassiveLookOverride || effectiveFrame != 0;

            // If the NPC is walking with no special animation or frame, skip capture —
            // unless it's late night (22h+) where walking means going home and we still want the kiss to work.
            if (isWalking && !hasSpecialAnimation && !hasSpecialStaticFrame && Game1.timeOfDay < 2200)
            {
                return;
            }

            // If truly idle with no special state at all, nothing to capture.
            if (!isWalking && !hasSpecialAnimation && !hasSpecialStaticFrame)
            {
                return;
            }

            if (hasPassiveLookOverride)
                ClearPassiveLookOriginalPose();

            var snapshot = new NpcPreKissSpecialActionSnapshot
            {
                Npc = npc,
                Location = npc.currentLocation,
                FacingDirection = effectiveFacing,
                CurrentFrame = effectiveFrame,
                Flip = npc.flip,
                MovementPause = (int)npc.movementPause,
                AddedSpeed = (int)npc.addedSpeed,
                CurrentAnimation = animation
            };

            preKissSpecialActionSnapshotsByNpc[npc.Name] = snapshot;

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

            this.Monitor.Log(
                $"[SPECIAL ACTION SAVED BEFORE KISS] npc={npc.Name} frame={snapshot.CurrentFrame} anim={(animation != null ? animation.Count : 0)} walking={isWalking}",
                LogLevel.Debug
            );
        }

        private bool TryRestoreNpcPreKissSpecialAction(NPC npc, bool clearAfterRestore)
        {
            if (npc == null || !preKissSpecialActionSnapshotsByNpc.TryGetValue(npc.Name, out var snapshot))
                return false;

            if (npc.Sprite == null || npc.currentLocation == null || npc.currentLocation != snapshot.Location)
            {
                ClearNpcPreKissSpecialAction(npc);
                return false;
            }

            try
            {
                npc.FacingDirection = snapshot.FacingDirection;
                npc.flip = snapshot.Flip;
                npc.movementPause = snapshot.MovementPause;
                npc.addedSpeed = snapshot.AddedSpeed;

                if (snapshot.CurrentAnimation != null && snapshot.CurrentAnimation.Count > 0)
                {
                    npc.Sprite.CurrentAnimation = new List<FarmerSprite.AnimationFrame>(snapshot.CurrentAnimation);
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

                this.Monitor.Log(
                    $"[SPECIAL ACTION RESTORED AFTER KISS] npc={npc.Name} frame={snapshot.CurrentFrame} anim={(snapshot.CurrentAnimation != null ? snapshot.CurrentAnimation.Count : 0)}",
                    LogLevel.Debug
                );

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
            if (preKissSpecialActionSnapshotsByNpc.Count == 0)
            {
                return;
            }

            // Snapshot the key list since we may mutate the dictionary (via ClearNpcPreKissSpecialAction)
            // while iterating.
            var npcNames = new List<string>(preKissSpecialActionSnapshotsByNpc.Keys);

            foreach (string npcName in npcNames)
            {
                if (!preKissSpecialActionSnapshotsByNpc.TryGetValue(npcName, out var snapshot))
                    continue;

                NPC npc = snapshot.Npc;
                if (npc == null || npc.Sprite == null || npc.currentLocation == null || Game1.player == null)
                {
                    ClearNpcPreKissSpecialAction(npc, npcName);
                    continue;
                }

                // Player left the NPC's location entirely (e.g. walked out of the Saloon, off the
                // beach/pier). This is just as much a sign that the player moved away as the
                // distance check below — actually restore the pose here, don't just discard the
                // snapshot. Discarding without restoring is what left the NPC frozen in the idle
                // frame forever whenever "walking far away" meant leaving the room instead of
                // walking to the other side of it.
                if (npc.currentLocation != Game1.player.currentLocation)
                {
                    if (!IsKissSystemHoldingNpc(npc))
                        TryRestoreNpcPreKissSpecialAction(npc, clearAfterRestore: true);
                    else
                        ClearNpcPreKissSpecialAction(npc, npcName);
                    continue;
                }

                if (IsKissSystemHoldingNpc(npc))
                {
                    continue;
                }

                int delayTicks = preKissSpecialActionRestoreDelayTicksByNpc.TryGetValue(npcName, out int t) ? t : 0;
                if (delayTicks > 0)
                {
                    preKissSpecialActionRestoreDelayTicksByNpc[npcName] = delayTicks - 1;
                    continue;
                }

                if (Game1.activeClickableMenu != null || Game1.dialogueUp)
                {
                    continue;
                }

                float dist = DistanceToPlayer(npc);
                if (dist < NpcSpecialActionRestoreDistance)
                {
                    continue;
                }

                TryRestoreNpcPreKissSpecialAction(npc, clearAfterRestore: true);
            }
        }

        /// <summary>Ticks left before this specific NPC can trigger a new bump-kiss cooldown line/gift. Per-NPC, so kissing one partner doesn't block trying with another right after.</summary>
        private int GetApproachKissBlockTimer(NPC npc)
        {
            if (npc == null)
                return 0;

            return approachKissBlockTimerByNpc.TryGetValue(npc.Name, out int value) ? value : 0;
        }

        private void SetApproachKissBlockTimer(NPC npc, int value)
        {
            if (npc == null)
                return;

            approachKissBlockTimerByNpc[npc.Name] = value;
        }

        private void TickApproachKissBlockTimers()
        {
            if (approachKissBlockTimerByNpc.Count == 0)
                return;

            var keys = new System.Collections.Generic.List<string>(approachKissBlockTimerByNpc.Keys);
            foreach (string key in keys)
            {
                if (approachKissBlockTimerByNpc[key] > 0)
                    approachKissBlockTimerByNpc[key]--;
            }
        }

        private void StartContinuousKiss(NPC npc, int kissTier, bool isNewSequence = false)
        {
            if (npc == null || npc.currentLocation != Game1.player.currentLocation)
                return;

            if (isNewSequence)
            {
                // publicMultiKiss should be limited to once per multi-kiss chain, not once forever.
                // The flag is set when the dialogue opens; rearm it only when the player starts
                // a brand-new multi-kiss chain, so repeated cycles in the same chain don't spam it.
                publicMultiKissDialogueTriggered = false;
                continuousKissCyclesDone = 0;
            }

            // Cross-mod block: if the Outfit Reactions mod has an outfit reaction in progress
            // (noticing, generating, or dialogue still open), hold off on kisses entirely until it
            // finishes. The flag is written by Outfit Reactions into the Farmer's modData, so this
            // works regardless of mod load order and without a hard dependency between the mods.
            if (IsOutfitReactionActive())
                return;

            if (GetApproachKissBlockTimer(npc) > 0)
                return;

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
            bool vanillaTriggered = TryTriggerRomanticContinuousKiss(npc);

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
            continuousKissWasTouchingSpouse = true;
            continuousKissVanillaTriggered = vanillaTriggered;
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
            continuousKissVanillaTriggered = false;

            npc.CurrentDialogue.Clear();
            npc.CurrentDialogue.Push(new Dialogue(npc, "", line));
            Game1.drawDialogue(npc);

            pendingPublicMultiKissShyEmote = true;
            pendingPublicMultiKissShyNpc = npc;
            pendingPublicMultiKissShyEmoteTimer = 30;

            ScheduleBystanderRestore(npc);
            bystanderRestoreForceStart = true;

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
        private void StartContinuousKissPlayerLeanIn(NPC spouse)
        {
            if (Game1.player == null || spouse == null)
                return;

            Vector2 direction = GetContinuousKissPlayerLeanDirection(spouse);
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

        private Vector2 GetContinuousKissPlayerLeanDirection(NPC spouse)
        {
            if (Game1.player == null || spouse == null)
                return Vector2.Zero;

            // First tries to use the direction the farmer is currently facing.
            if (Game1.player.FacingDirection == 1)
                return new Vector2(1f, 0f); // direita

            if (Game1.player.FacingDirection == 3)
                return new Vector2(-1f, 0f); // esquerda

            // Fallback: if the farmer isn't facing left or right for any reason,
            // fall back to the horizontal position of the partner.
            float diffX = spouse.Position.X - Game1.player.Position.X;

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
            playerWasTouchingSpouse = false;
            continuousKissTouchHoldTimer = 0;
            continuousKissWasTouchingSpouse = false;
            continuousKissVanillaTriggered = false;

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
            npc.movementPause = 0;
            npc.addedSpeed = 0;

            // If a snapshot is still saved for this NPC, skip forcing the idle frame/animation
            // reset here — UpdateDeferredNpcSpecialActionRestore will restore the real
            // pose/animation once the player walks far enough away. Forcing the idle frame in
            // this "wake up" step (which always ran immediately when a multi-kiss ends,
            // regardless of a saved snapshot) was overwriting the special pose before the
            // distance-based restore ever got a chance to run.
            if (!HasNpcPreKissSpecialAction(npc))
            {
                npc.Sprite.StopAnimation();
                npc.Sprite.ClearAnimation();
                npc.Sprite.CurrentAnimation = null;

                // Hard reset — fully clears controller and animation state.
                npc.flip = false;
                npc.faceDirection(npc.FacingDirection);
                npc.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(npc.FacingDirection);
                npc.Sprite.UpdateSourceRect();
            }

            playerWasTouchingSpouse = false;
            kissProximityTimer = 0;
            activeKissVisualDelayMs = bumpKissVisualDelayMs;
            continuousKissTouchHoldTimer = 0;
            continuousKissWasTouchingSpouse = false;

            DelayedAction.functionAfterDelay(() =>
            {
                if (npc == null)
                    return;

                Game1.player.CanMove = true;
                Game1.player.completelyStopAnimatingOrDoingAction();

                npc.controller = null;
                npc.Halt();
                npc.movementPause = 0;

                // Same reasoning as above: don't stomp a pose that's still waiting to be restored.
                if (HasNpcPreKissSpecialAction(npc))
                    return;

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

// ======================================================================
        // Harmony prefix controlling whether the partner NPC's schedule check runs normally or is suppressed to hold them in a continuous kiss without the game forcibly interrupting the sequence.
// ======================================================================
        public static bool CheckSchedule_Prefix(NPC __instance)
        {
            if (allowForcedScheduleCheck)
            {
                if (Instance != null && __instance != null)
                {
                    Instance.Monitor.Log(
                        $"[CHECKSCHEDULE PREFIX] ALLOWED BY FORCE | npc={__instance.Name} currentLoc={__instance.currentLocation?.NameOrUniqueName ?? "null"}",
                        LogLevel.Trace
                    );
                }

                return true;
            }

            if (Instance == null || !Context.IsWorldReady || Game1.player == null)
                return true;

            if (__instance == null)
                return true;

            if (Instance.outsideBumpPauseNpc != __instance)
                return true;

            if (!Instance.IsSupportedRomanticPartner(__instance.Name))
                return true;

            bool shouldBlock =
                Instance.outsideBumpPauseActive &&
                !Instance.IsHomeOrFarmLocation();

            return !shouldBlock;
        }

// ======================================================================
        // Outdoor bump-kiss pause: briefly pauses the NPC after a bump kiss to prevent them from teleporting or walking away before the kiss can escalate into a continuous kiss.
// ======================================================================
        private void UpdateOutsideBumpPause(NPC spouse)
        {
            if (!outsideBumpPauseActive || outsideBumpPauseNpc == null)
                return;

            if (spouse == null || spouse != outsideBumpPauseNpc)
            {
                ResetOutsideBumpPause();
                return;
            }

            if (!Context.IsWorldReady || Game1.player == null)
            {
                ResetOutsideBumpPause();
                return;
            }

            if (spouse.currentLocation != Game1.player.currentLocation)
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

            float distance = DistanceToPlayer(spouse);

            if (distance >= 600f) // Far enough to assume the player moved away or the NPC warped — cancel the pause to avoid locking the NPC unnecessarily.
            {
                this.Monitor.Log(
                    $"[OUTSIDE BUMP PAUSE EXIT] npc={spouse.Name} dist={distance:F1} timer={outsideBumpPauseTimer} loc={spouse.currentLocation?.NameOrUniqueName ?? "null"}",
                    LogLevel.Warn
                );

                ResetOutsideBumpPause();
                return;
            }

            spouse.faceGeneralDirection(Game1.player.getStandingPosition(), 0, false, false);

            // Short pause — doesn't kill the controller.
            if (spouse.movementPause < 6)
                spouse.movementPause = 6;
        }

        // activates the outdoor pause after a bump kiss to prevent NPC teleport or walk-away before the kiss can escalate
        private void ResetOutsideBumpPause()
        {
            outsideBumpPauseActive = false;
            outsideBumpPauseNpc = null;
            outsideBumpPauseTimer = 0;
            outsideBumpPauseToken++;
        }

        private bool TryTriggerRomanticContinuousKiss(NPC npc)
        {
            return TryTriggerVanillaRomanticKiss(npc, playSound: true);
        }

        private bool TryTriggerRomanticBumpKiss(NPC npc)
        {
            return TryTriggerVanillaRomanticKiss(npc, playSound: false);
        }

        // Also triggers the vanilla kiss for dating partners and extra polyamory mod spouses.
        // First tries the normal checkAction, since polyamory mods may unlock the kiss via their own patch.
        // The legacy fallback that temporarily swaps Game1.player.spouse is OFF by default,
        // because some polyamory mods react to the Married/spouse state and may try to load their own maps.
        private bool TryTriggerVanillaRomanticKiss(NPC npc, bool playSound)
        {
            lastAutoKissClickWasBlockedDialogue = false;

            if (npc == null || Game1.player == null || npc.currentLocation != Game1.player.currentLocation)
                return false;

            if (!IsSupportedRomanticPartner(npc.Name))
                return false;

            // Cross-mod block: hold off on any automatic kiss while an Outfit Reactions reaction is
            // in progress (noticing, generating, or dialogue open) for any boyfriend/spouse. The flag
            // lives in the Farmer's modData so this works without a hard dependency or load order.
            if (IsOutfitReactionActive())
                return false;

            if (GetApproachKissBlockTimer(npc) > 0)
                return false;

            if (Game1.activeClickableMenu != null)
                return false;

            float distance = DistanceToPlayer(npc);
            if (distance > 72f)
                return false;

            CaptureNpcPreKissSpecialAction(npc);

            FaceEachOther(npc);

            bool triggered = TryCheckActionForAutoKissWithoutDialogue(npc);

            // checkAction only plays the real vanilla kiss pose for the player's actual spouse —
            // for a dating partner (not yet married) it does nothing, unless some other mod (e.g.
            // Hugs and Kisses, or a polyamory mod) happens to be installed and patches it. If it's
            // not installed and this is a dating partner, do it ourselves: manually play the same
            // kiss pose the game already ships on every datable NPC's tilesheet, and call the real
            // Farmer.PerformKiss so the player's own animation matches too. This mod's existing
            // friendship/sound/emote logic elsewhere is untouched — this only supplies the missing
            // vanilla pose + PerformKiss call.
            if (!triggered
                && this.Config?.EnableBoyfriendKisses == true
                && IsDatingPartner(npc.Name)
                && !IsCurrentSpouse(npc.Name))
            {
                triggered = TryPerformManualVanillaDatingKiss(npc);
            }

            if (triggered && playSound)
                Game1.playSound("dwop");

            return triggered;
        }

        // Manually replicates the piece of the vanilla spouse-kiss flow that checkAction never
        // exposes for dating partners: setting the NPC's kiss pose frame (flipped to face the
        // player correctly) and calling Farmer.PerformKiss so the player's own kiss animation
        // plays for real, instead of just this mod's own effects running on top of nothing.
        private bool TryPerformManualVanillaDatingKiss(NPC npc)
        {
            if (npc == null || npc.Sprite == null || Game1.player == null)
                return false;

            int kissFrame = GetVanillaKissFrame(npc.Name);
            bool poseFacesRight = GetVanillaKissFacingRight(npc.Name);

            bool flip = (poseFacesRight && npc.FacingDirection == 3)
                     || (!poseFacesRight && npc.FacingDirection == 1);

            int delay = Game1.IsMultiplayer ? 1000 : 10;
            npc.movementPause = delay;

            npc.Sprite.setCurrentAnimation(new List<FarmerSprite.AnimationFrame>
            {
                new FarmerSprite.AnimationFrame(kissFrame, delay, false, flip, npc.haltMe, true)
            });
            npc.Sprite.UpdateSourceRect();

            int playerFaceDirection = 1; // right
            if ((poseFacesRight && !flip) || (!poseFacesRight && flip))
                playerFaceDirection = 3; // left

            Game1.player.PerformKiss(playerFaceDirection);

            return true;
        }

        // Releases the NPC from the continuous kiss state (animation, controller, etc.) after the sequence ends. Staged with delays to prevent teleports or schedule interruptions before the NPC is fully freed.
        private void ReleaseNpcAfterMultiKiss(NPC spouse)
        {
            if (spouse == null)
                return;

            WakeNpcAfterMultiKiss(spouse, true);

            DelayedAction.functionAfterDelay(() =>
            {
                if (spouse == null || spouse.currentLocation == null)
                    return;

                // If a snapshot is still saved for this NPC, don't overwrite the idle frame —
                // UpdateDeferredNpcSpecialActionRestore will restore the animation when the player moves away.
                if (HasNpcPreKissSpecialAction(spouse))
                    return;

                spouse.Halt();
                spouse.controller = null;
                spouse.movementPause = 0;
                spouse.addedSpeed = 0;

                spouse.Sprite.StopAnimation();
                spouse.Sprite.ClearAnimation();
                spouse.Sprite.CurrentAnimation = null;
                spouse.flip = false;
                spouse.faceDirection(spouse.FacingDirection);
                spouse.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(spouse.FacingDirection);
                spouse.Sprite.UpdateSourceRect();
            }, 80);

            DelayedAction.functionAfterDelay(() =>
            {
                if (spouse == null || spouse.currentLocation != Game1.player.currentLocation)
                    return;

                didReactThisTick = false;
                kissProximityTimer = 0;
                continuousKissPendingRestart = false;
                continuousKissGapTimer = 0;
                continuousKissActive = false;
                continuousKissNpc = null;
                kissPostSequenceActive = false;
                kissPostSequenceNpc = null;

                // If a snapshot is still saved for this NPC, don't overwrite the idle frame —
                // UpdateDeferredNpcSpecialActionRestore will restore the special pose/animation
                // once the player moves far enough away. Forcing the idle frame here would
                // permanently overwrite that restoration, which is exactly what was happening to
                // NPCs with a location-override pose (e.g. Sebastian playing video games): this
                // delayed cleanup ran a moment after the kiss ended and stomped the idle frame
                // back on top before the distance-based restore ever got a chance to run.
                if (HasNpcPreKissSpecialAction(spouse))
                    return;

                spouse.Halt();
                spouse.controller = null;
                spouse.movementPause = 0;
                spouse.addedSpeed = 0;

                spouse.Sprite.StopAnimation();
                spouse.Sprite.ClearAnimation();
                spouse.Sprite.CurrentAnimation = null;
                spouse.flip = false;
                spouse.faceDirection(spouse.FacingDirection);
                spouse.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(spouse.FacingDirection);
                spouse.Sprite.UpdateSourceRect();
            }, 220);

            DelayedAction.functionAfterDelay(() =>
            {
                if (spouse == null)
                    return;

                RestoreSpouseScheduleAfterMultiKiss(spouse);
            }, 420);
        }

        // Restores the partner NPC's schedule after a continuous kiss to ensure they resume their routine normally. Called with a delay to avoid the game interrupting the schedule mid-sequence.
        private void RestoreSpouseScheduleAfterMultiKiss(NPC spouse)
        {
            if (spouse == null || spouse.currentLocation == null)
                return;

            // Same reasoning as the 220ms delayed cleanup above: don't stomp the idle frame on
            // top of a pose/animation that's still waiting to be restored by
            // UpdateDeferredNpcSpecialActionRestore once the player walks away.
            if (HasNpcPreKissSpecialAction(spouse))
                return;

            spouse.Halt();
            spouse.controller = null;
            spouse.movementPause = 0;
            spouse.addedSpeed = 0;
            spouse.queuedSchedulePaths.Clear();

            spouse.Sprite.StopAnimation();
            spouse.Sprite.ClearAnimation();
            spouse.Sprite.CurrentAnimation = null;
            spouse.flip = false;
            spouse.faceDirection(spouse.FacingDirection);
            spouse.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(spouse.FacingDirection);
            spouse.Sprite.UpdateSourceRect();

            // Do NOT clear CurrentDialogue here.
            // Another mod may have queued dialogue on the NPC.

            ForceScheduleCheckNow(spouse);
        }
// ======================================================================
        // NPC position reset after a kiss (prevents stuck animations or unexpected teleports)
// ======================================================================
        private void UpdatePendingNpcKissReset(NPC spouse)
        {
            if (!pendingNpcKissResetQueued || pendingNpcKissResetNpc == null)
                return;

            if (pendingNpcKissResetTimer > 0)
                return;

            NPC npc = pendingNpcKissResetNpc;

            pendingNpcKissResetQueued = false;
            pendingNpcKissResetNpc = null;
            pendingNpcKissResetTimer = 0;

            if (npc.currentLocation != Game1.currentLocation)
                return;

            npc.controller = null;
            npc.Halt();

            if (pendingNpcKissResetDirection >= 0)
                npc.FacingDirection = pendingNpcKissResetDirection;

            if (pendingNpcKissResetFrame >= 0)
                npc.Sprite.CurrentFrame = pendingNpcKissResetFrame;

            npc.flip = false;
            npc.Sprite.UpdateSourceRect();

            pendingNpcKissResetDirection = -1;
            pendingNpcKissResetFrame = -1;
        }
        private void UpdatePendingPublicMultiKissShyEmote()
        {
            if (!pendingPublicMultiKissShyEmote)
                return;

            if (pendingPublicMultiKissShyNpc == null)
            {
                pendingPublicMultiKissShyEmote = false;
                pendingPublicMultiKissShyEmoteTimer = 0;
                return;
            }

            if (pendingPublicMultiKissShyNpc.currentLocation != Game1.currentLocation)
            {
                pendingPublicMultiKissShyEmote = false;
                pendingPublicMultiKissShyNpc = null;
                pendingPublicMultiKissShyEmoteTimer = 0;
                return;
            }

            if (Game1.activeClickableMenu != null || Game1.dialogueUp)
                return;

            if (pendingPublicMultiKissShyEmoteTimer > 0)
            {
                pendingPublicMultiKissShyEmoteTimer--;
                return;
            }

            pendingPublicMultiKissShyNpc.doEmote(60);

            pendingPublicMultiKissShyEmote = false;
            pendingPublicMultiKissShyNpc = null;
            pendingPublicMultiKissShyEmoteTimer = 0;
        }
// ======================================================================
        // MAIN SYSTEMS / UPDATE LOOPS
// ======================================================================
// ======================================================================
        // SINGLE KISS LOGIC
// ======================================================================
        private void UpdateKissSystem(NPC spouse)
        {
            if (!kissSequenceActive || pendingKissNpc == null)
                return;

            if (spouse.currentLocation != Game1.currentLocation || pendingKissNpc != spouse)
            {
                ResetKissState();
                return;
            }

            float distance = DistanceToPlayer(spouse);

            if (distance > 120f)
            {
                ResetKissState();
                return;
            }

            FaceEachOther(spouse);

            if (!holdingKissPose)
            {
                if (kissDelayTimer <= 0)
                {
                    holdingKissPose = true;
                    kissRepeatTimer = 30;
                }
            }
            else
            {
                if (kissRepeatTimer <= 0)
                {
                    kissPostSequenceActive = true;
                    kissPostSequenceNpc = spouse;
                    lastKissPostDistance = distance;
                    kissPostLineTriggered = false;
                    kissPostLine = pendingKissCycleLine;
                    ResetKissState();
                }
            }
        }
// ======================================================================
        // CONTINUOUS KISS LOGIC
// ======================================================================
        private void UpdateContinuousKissSystem(NPC spouse)
        {
            if (spouse == null)
                return;

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
                    ForceEndContinuousKiss(spouse ?? continuousKissNpc);
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

                    DelayedAction.functionAfterDelay(() =>
                    {
                        if (postNpc == null || postNpc.currentLocation != Game1.player.currentLocation)
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
                StartContinuousKiss(continuousKissNpc, continuousKissTier, false);
                return;
            }

            if (!continuousKissActive || continuousKissNpc == null)
                return;

            if (continuousKissNpc != spouse || spouse.currentLocation != Game1.player.currentLocation)
            {
                ForceEndContinuousKiss(spouse);
                return;
            }

            float distance = DistanceToPlayer(spouse);

            if (distance > 90f)
            {
                NPC postNpc = spouse;
                string postLine = GetDialogueLine("kissReaction", 1, 10, postNpc);
                string publicLine = GetDialogueLine("PublicKissReaction", 1, 2, postNpc);

                ScheduleBystanderRestore(spouse);
                ResetContinuousKissState();
                activeKissVisualDelayMs = bumpKissVisualDelayMs;

                DelayedAction.functionAfterDelay(() =>
                {
                    if (postNpc == null || postNpc.currentLocation != Game1.player.currentLocation)
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

            if (!continuousKissVanillaTriggered && distance <= 72f)
            {
                bool vanillaTriggered = TryTriggerRomanticContinuousKiss(spouse);
                continuousKissVanillaTriggered = vanillaTriggered;
            }

            if (continuousKissTimer > 0)
            {
                if (continuousKissTier == 2 && !continuousKissHeartTriggered)
                {
                    // Tier 2 inherited the old tier 3 mechanic:
                    // 2-second kiss with a heart effect mid-cycle.
                    int totalTicks = MsToTicks(2000);
                    if (continuousKissTimer <= totalTicks / 2)
                    {
                        spouse.doEmote(20);
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
                        StartContinuousKissPlayerLeanIn(spouse);

                    // After 1 second: heart effect on both the partner and the farmer.
                    if (!continuousKissHeartTriggered && continuousKissTimer <= totalTicks - MsToTicks(1000))
                    {
                        spouse.doEmote(20);
                        Game1.player.doEmote(20);
                        continuousKissHeartTriggered = true;
                    }

                    // After 3 seconds: custom blush smoke on the partner.
                    if (!continuousKissSmokeTriggered && continuousKissTimer <= totalTicks - MsToTicks(3000))
                    {
                        ShowBlushSmoke(spouse, 68);
                        continuousKissSmokeTriggered = true;
                    }
                }

                return;
            }

            if (continuousKissTier == 3 && !continuousKissPlayerLeanOutTriggered)
                StartContinuousKissPlayerLeanOut();

            continuousKissActive = false;
            continuousKissVanillaTriggered = false;
            continuousKissCyclesDone++;

            // Bystanders react based on current kiss tier.
            TriggerBystanderReactions(continuousKissTier, spouse);

            if (TryTriggerPublicMultiKissDialogue(spouse))
                return;

            // publicMultiKiss did not fire — kiss will restart. Schedule restore so bystanders
            // don't stay frozen if the player moves away before publicMultiKiss ever triggers.
            ScheduleBystanderRestore(spouse);

            continuousKissPendingRestart = true;
            continuousKissGapTimer = 25;
            continuousKissTier = RollContinuousKissTier();
        }
// ======================================================================
        // POST-KISS LOGIC (DIALOGUE, ROUTE DEVIATION, ETC.)
// ======================================================================
        private void UpdatePostKissSystem(NPC spouse)
        {
            if (!kissPostSequenceActive || kissPostSequenceNpc == null)
                return;

            if (kissPostSequenceNpc != spouse)
            {

                ReleaseNpcAfterMultiKiss(kissPostSequenceNpc);
                ResetPostKissState();
                return;
            }

            if (spouse.currentLocation != Game1.currentLocation)
            {

                ReleaseNpcAfterMultiKiss(spouse);
                ResetPostKissState();
                return;
            }

            float distance = DistanceToPlayer(spouse);

            if (lastKissPostDistance < 0f)
                lastKissPostDistance = distance;

            bool playerStartedMovingAway = distance > lastKissPostDistance + 2f;

            if (!kissPostLineTriggered &&
                !string.IsNullOrEmpty(kissPostLine) &&
                distance >= 120f &&
                dialogueCooldown <= 0 &&
                Game1.activeClickableMenu == null)
            {

                ShowTextAboveHeadWithPipeSupport(spouse, kissPostLine);
                kissPostLineTriggered = true;
                dialogueCooldown = 120;
            }
            else if (!kissPostLineTriggered &&
                     !string.IsNullOrEmpty(kissPostLine) &&
                     playerStartedMovingAway &&
                     distance >= 72f &&
                     dialogueCooldown <= 0 &&
                     Game1.activeClickableMenu == null)
            {

                ShowTextAboveHeadWithPipeSupport(spouse, kissPostLine);
                kissPostLineTriggered = true;
                dialogueCooldown = 120;
            }

            lastKissPostDistance = distance;

            if (distance > 160f)
            {
                // Not yet at restore distance — hold here.
                if (HasNpcPreKissSpecialAction(spouse) && distance < NpcSpecialActionRestoreDistance)
                    return;

                // Past the restore distance — restore first, then release.
                // Prevents WakeNpcAfterMultiKiss from overwriting the idle frame on top of the restored animation.
                if (HasNpcPreKissSpecialAction(spouse))
                    TryRestoreNpcPreKissSpecialAction(spouse, clearAfterRestore: true);

                ResetPostKissState();
                ReleaseNpcAfterMultiKiss(spouse);

                return;
            }
        }
// ======================================================================
        // BEIJO AO ESBARRAR
// ======================================================================
        private void UpdateBumpKissSystem(NPC npc, float distance)
        {
            if (npc == null || !Context.IsWorldReady)
                return;

            // Same reasoning as UpdateContinuousKissSystem: a menu being open shouldn't let the
            // bump kiss trigger or continue behind it.
            if (Game1.activeClickableMenu != null)
                return;

            bool touching = distance <= 64f;
            bool useOutsideApproachKissHold = !IsHomeOrFarmLocation();
            bool justStartedTouchingSpouse = touching && !playerWasTouchingSpouse;

            // Inside the farm/house: no long lock or special hold.
            if (!useOutsideApproachKissHold)
            {
                approachKissHoldActive = false;
                approachKissHoldNpc = null;
                approachKissTriggered = false;
                approachKissRearmPending = false;
            }

            // The bump kiss only loses priority if the multi-kiss sequence is already
            // actually running.
            bool multiKissBusy =
                this.Config.MultiKissEnabled &&
                (
                    continuousKissActive ||
                    continuousKissPendingRestart
                );

            if (multiKissBusy)
            {
                approachKissHoldActive = false;
                approachKissHoldNpc = null;
                approachKissHoldToken++;

                playerWasTouchingSpouse = touching;
                return;
            }

            Vector2 diff = Game1.player.Position - npc.Position;
            bool isHorizontal = Math.Abs(diff.X) > Math.Abs(diff.Y);

            if (this.Config.BumpKissEnabled &&
                justStartedTouchingSpouse &&
                isHorizontal &&
                Game1.player.isMoving() &&
                Game1.timeOfDay < 2400 &&
                cooldown <= 0 &&
                GetApproachKissBlockTimer(npc) <= 0 &&
                !kissPostSequenceActive &&
                !kissSequenceActive &&
                pendingKissNpc == null &&
                !continuousKissActive &&
                talkedToSpouseToday)
            {
                CaptureNpcPreKissSpecialAction(npc);

                npc.faceDirection(diff.X > 0 ? 1 : 3);
                Game1.player.faceDirection(diff.X > 0 ? 3 : 1);

                activeKissVisualDelayMs = bumpKissVisualDelayMs;

                Game1.player.Halt();
                npc.Halt();
                npc.movementPause = 1500;

                npc.faceDirection(diff.X > 0 ? 1 : 3);
                Game1.player.faceDirection(diff.X > 0 ? 3 : 1);

                activeKissVisualDelayMs = bumpKissVisualDelayMs;

                TryTriggerRomanticBumpKiss(npc);

                Game1.playSound("dwop");
                npc.doEmote(20);

                DelayedAction.functionAfterDelay(() =>
                {
                    if (!Context.IsWorldReady)
                        return;

                    Game1.player.CanMove = true;
                    Game1.player.completelyStopAnimatingOrDoingAction();

                    if (npc != null && npc.currentLocation == Game1.player.currentLocation)
                    {
                        npc.Halt();
                        npc.Sprite.StopAnimation();
                        npc.Sprite.UpdateSourceRect();
                    }
                }, activeKissVisualDelayMs);

                string locName = npc.currentLocation.Name;

                bool suppressBumpKissDialogue =
                    this.passingGreetingsApi?.ShouldSuppressBumpKissDialogue(npc.Name) == true;

                // O beijo sempre pode acontecer.
                // Only the approachKiss dialogue and gift are restricted to outdoors.
                // If Npc Passing Greetings just triggered a balloon, the kiss still fires,
                // but the bump kiss dialogue is silenced for a few seconds.
                if (locName != "FarmHouse" && locName != "Farm" && !approachKissTriggered && !suppressBumpKissDialogue)
                {
                    bool giftDrop = IsCurrentSpouse(npc.Name) &&
                                    random.NextDouble() < 0.05;
                    string surpriseLine = null;
                    List<Item> giftItems = null;
                    string fullBagLine = null;
                    string presentLine = null;

                    // Capture time at the moment of the kiss for the cooldown check.
                    int kissTimeOfDay = Game1.timeOfDay;
                    bool approachDialogueCooledDown = approachKissDialogueLastTimeOfDay < 0
                        || kissTimeOfDay < approachKissDialogueLastTimeOfDay // new day rollover
                        || (kissTimeOfDay - approachKissDialogueLastTimeOfDay) >= 100;

                    if (giftDrop)
                    {
                        if (Game1.player.isInventoryFull())
                        {
                            fullBagLine = GetDialogueLine("fullBag", 1, 2, npc);
                        }
                        else
                        {
                            // Gift lines live in i18n as e.g. "giftAlex.1": "Here! {201}"
                            // Fallback to generic "surpriseGift" key for unknown NPCs.
                            string dialogueKey = $"gift{npc.Name}";
                            presentLine = GetDialogueLine(dialogueKey, 1, 3, npc);

                            if (string.IsNullOrEmpty(presentLine))
                                presentLine = GetDialogueLine("surpriseGift", 1, 3, npc);

                            if (!string.IsNullOrEmpty(presentLine))
                                giftItems = ExtractItemTokens(ref presentLine);
                        }
                    }

                    DelayedAction.functionAfterDelay(() =>
                    {
                        if (giftDrop)
                        {
                            if (!string.IsNullOrEmpty(fullBagLine))
                            {
                                ShowTextAboveHeadWithPipeSupport(npc, fullBagLine);
                            }
                            else if (giftItems != null && giftItems.Count > 0)
                            {
                                foreach (Item gift in giftItems)
                                {
                                    Game1.player.addItemToInventoryBool(gift);
                                    Game1.player.holdUpItemThenMessage(gift);
                                }

                                if (!string.IsNullOrEmpty(presentLine))
                                    ShowTextAboveHeadWithPipeSupport(npc, presentLine);
                            }
                        }
                        else
                        {
                            if (approachDialogueCooledDown)
                            {
                                surpriseLine = GetDialogueLine("approachKiss", 1, 30, npc);
                                if (!string.IsNullOrEmpty(surpriseLine))
                                {
                                    ShowTextAboveHeadWithPipeSupport(npc, surpriseLine);
                                    SetApproachKissBlockTimer(npc, 300);
                                    approachKissDialogueLastTimeOfDay = kissTimeOfDay;
                                }
                            }
                        }
                    }, 1200);

                    // only locks approachKiss outside the farm/house
                    approachKissTriggered = true;
                    approachKissRearmPending = true;
                }

                // Give priority to the bump kiss:
                // Clear the multi-kiss hold and prevent it from re-entering immediately on the same touch.
                continuousKissTouchHoldTimer = 0;
                continuousKissWasTouchingSpouse = false;
                continuousKissPendingRestart = false;

                // Only outside the farm/house does the partner pause after the bump kiss
                if (useOutsideApproachKissHold)
                {
                    approachKissTriggered = true;
                    approachKissRearmPending = true;

                    NPC pauseNpc = npc;
                    int pauseToken = ++outsideBumpPauseToken;

                    DelayedAction.functionAfterDelay(() =>
                    {
                        if (!Context.IsWorldReady)
                            return;

                        if (pauseToken != outsideBumpPauseToken)
                            return;

                        if (pauseNpc == null)
                            return;

                        if (Game1.player == null)
                            return;

                        if (pauseNpc.currentLocation != Game1.player.currentLocation)
                            return;

                        if (IsHomeOrFarmLocation())
                            return;

                        if (continuousKissActive || continuousKissPendingRestart)
                            return;

                        if (DistanceToPlayer(pauseNpc) >= 600f) // Player moved too far away — skip the pause.
                            return;

                        outsideBumpPauseActive = true;
                        outsideBumpPauseNpc = pauseNpc;
                        outsideBumpPauseTimer = 360; // 6-second pause
                        pauseNpc.faceGeneralDirection(Game1.player.getStandingPosition(), 0, false, false);

                    }, activeKissVisualDelayMs);
                }
                else
                {
                    // Indoors / farm — no outdoor pause needed, continue normally.
                    approachKissHoldActive = false;
                    approachKissHoldNpc = null;
                    approachKissTriggered = false;
                    approachKissRearmPending = false;
                    approachKissHoldToken++;
                }

                cooldown = 100; // Brief cooldown before allowing another touch reaction, so the partner doesn't react repeatedly if the player stands still next to them.
                dialogueCooldown = 350;
                didReactThisTick = true;
                playerWasTouchingSpouse = true;
                return;
            }

            float currentDistance = DistanceToPlayer(npc);

            // Only rearms the approachKiss DIALOGUE outside the farm/house
            if (!IsHomeOrFarmLocation())
            {
                if (approachKissRearmPending && currentDistance >= 1000f) // Player moved far enough away — re-arm the approach kiss.
                {
                    approachKissTriggered = false;
                    approachKissRearmPending = false;
                }
            }
            else
            {
                approachKissTriggered = false;
                approachKissRearmPending = false;
            }

            playerWasTouchingSpouse = touching;
        }
    }
} //👈 FINAL DO NAMESPACE