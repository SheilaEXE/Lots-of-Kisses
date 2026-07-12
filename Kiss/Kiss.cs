using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.GameData.Characters;
using System;
using System.Collections.Generic;

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

            // Cross-mod signal (mirrors the modData handshake Outfit Reactions already uses):
            // while this simulated click is in flight, tell Outfit Reactions' own NPC.checkAction
            // Harmony prefix (which runs at Priority.First and checks its own pending-reaction
            // state, not npc.CurrentDialogue) to step aside instead of hijacking the click into
            // opening the outfit dialogue. Our own dialogue-stash above only satisfies vanilla's
            // checkAction body and other mods reading CurrentDialogue — it can't reach a patch
            // that ignores CurrentDialogue entirely. Only write/remove it at the outermost call
            // (previousKissPatchFlag was false) so nested calls don't clear it early.
            if (!previousKissPatchFlag && Game1.player?.modData != null)
                Game1.player.modData[AutoKissClickActiveModDataKey] = "1";

            // Some NPCs have a fixed "location override" line tied to their current pose/location
            // (e.g. Sebastian playing video games, Abigail sitting on the couch) that never clears
            // on its own — unlike a regular queued dialogue, it's recalculated fresh every time
            // checkAction runs, so stashing CurrentDialogue alone doesn't help here. Suppressing it
            // through NPC.HasLocationOverrideDialogue (Harmony Prefix) makes checkAction take
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

                // BUGFIX (ghost kiss): checkAction's raw boolean is NOT a reliable "a real kiss
                // just happened" signal on its own — it's the general click-interaction handler,
                // and can return true just because SOME dialogue got queued and immediately
                // suppressed by our own patch above, with no actual kiss animation ever playing.
                // Confirmed via diagnostic logging: across every genuinely successful kiss
                // (multiple NPCs, bump kiss and multi-kiss, many attempts), Game1.player.CanMove
                // was reliably false immediately after checkAction returned — vanilla's own kiss
                // animation freezes the player's movement as a side effect, same as any other
                // scripted animation. A plain dialogue-open-and-suppress does not. The one
                // reproduced "ghost kiss" (result=true, no visible animation) was the single case
                // where CanMove was still true right after checkAction — i.e. nothing had frozen
                // the player, meaning no real kiss animation had actually started. Use that as the
                // authoritative confirmation, on top of checkAction's own result.
                bool playerWasFrozenByRealKiss = !Game1.player.CanMove;
                if (result && !playerWasFrozenByRealKiss)
                    return false;

                return result;
            }
            finally
            {
                suppressDialogueFromAutoKissClick = previousSuppress;
                suppressDialogueAutoKissNpc = previousNpc;
                suppressedDialogueDuringAutoKissClick = previousSuppressed;
                LotsOfKissesKissPatchActive = previousKissPatchFlag;
                suppressLocationOverrideDialogueDuringAutoKissClick = previousSuppressLocationOverride;

                if (!previousKissPatchFlag && Game1.player?.modData != null)
                    Game1.player.modData.Remove(AutoKissClickActiveModDataKey);


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
        // =========================================================================================================================================
        // CONTINUOUS KISS LOGIC — START, MAINTENANCE AND END (including bump kiss, which can escalate outside the farm)
        // =========================================================================================================================================

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

        private NpcPreKissSpecialActionSnapshot preKissSpecialActionSnapshot = null;
        private int preKissSpecialActionRestoreDelayTicks = 0;
        private const float NpcSpecialActionRestoreDistance = 300f;

        /// <summary>Ticks left before this specific NPC can trigger a new bump-kiss cooldown line. Per-NPC, so kissing one partner doesn't block trying with another right after.</summary>
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

            // Cross-mod hold: only wait when an Outfit Reactions dialogue is genuinely on screen —
            // "noticed, waiting for a click" and the post-dialogue linger should not block kissing.
            if (HasReadableDialogueWaiting(npc))
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

            if (triggered && playSound)
                Game1.playSound("dwop");

            return triggered;
        }

        // Tries to trigger the game's built-in continuous kiss reaction (fires when the player kisses while already very close to the NPC) to reuse the game's animation and visual effects. This reaction is temperamental and may not fire every time, but adds significant immersion when it does.
        private bool TryTriggerVanillaContinuousKiss(NPC npc)
        {
            if (npc == null || npc.currentLocation != Game1.player.currentLocation)
                return false;

            if (GetApproachKissBlockTimer(npc) > 0)
                return false;

            if (Game1.activeClickableMenu != null)
                return false;

            float distance = DistanceToPlayer(npc);
            if (distance > 72f)
                return false;

            FaceEachOther(npc);

            bool triggered = TryCheckActionForAutoKissWithoutDialogue(npc);

            if (triggered)
                Game1.playSound("dwop");

            return triggered;
        }

        // Releases the NPC from the continuous kiss state (animation, controller, etc.) after the sequence ends. Staged with delays to prevent teleports or schedule interruptions before the NPC is fully freed.
        private void ReleaseNpcAfterMultiKiss(NPC partner)
        {
            if (partner == null)
                return;

            int delayedActionToken = delayedActionContextToken;

            WakeNpcAfterMultiKiss(partner, true);

            DelayedAction.functionAfterDelay(() =>
            {
                if (!IsCurrentDelayedAction(delayedActionToken) || partner == null || partner.currentLocation == null)
                    return;

                // If a snapshot is still saved for this NPC, don't overwrite the idle frame —
                // UpdateDeferredNpcSpecialActionRestore will restore the animation when the player moves away.
                if (HasNpcPreKissSpecialAction(partner))
                    return;

                partner.Halt();
                partner.controller = null;
                partner.movementPause = 0;
                partner.addedSpeed = 0;

                partner.Sprite.StopAnimation();
                partner.Sprite.ClearAnimation();
                partner.Sprite.CurrentAnimation = null;
                partner.flip = false;
                partner.faceDirection(partner.FacingDirection);
                partner.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(partner.FacingDirection);
                partner.Sprite.UpdateSourceRect();
            }, 80);

            DelayedAction.functionAfterDelay(() =>
            {
                if (!IsCurrentDelayedAction(delayedActionToken) || partner == null || partner.currentLocation != Game1.player.currentLocation)
                    return;

                partner.Halt();
                partner.controller = null;
                partner.movementPause = 0;
                partner.addedSpeed = 0;

                partner.Sprite.StopAnimation();
                partner.Sprite.ClearAnimation();
                partner.Sprite.CurrentAnimation = null;
                partner.flip = false;
                partner.faceDirection(partner.FacingDirection);
                partner.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(partner.FacingDirection);
                partner.Sprite.UpdateSourceRect();

                didReactThisTick = false;
                kissProximityTimer = 0;
                continuousKissPendingRestart = false;
                continuousKissGapTimer = 0;
                continuousKissActive = false;
                continuousKissNpc = null;
                kissPostSequenceActive = false;
                kissPostSequenceNpc = null;
            }, 220);

            DelayedAction.functionAfterDelay(() =>
            {
                if (!IsCurrentDelayedAction(delayedActionToken) || partner == null)
                    return;

                RestorePartnerScheduleAfterMultiKiss(partner);
            }, 420);
        }

        // Restores the partner NPC's schedule after a continuous kiss to ensure they resume their routine normally. Called with a delay to avoid the game interrupting the schedule mid-sequence.
        private void RestorePartnerScheduleAfterMultiKiss(NPC partner)
        {
            if (partner == null || partner.currentLocation == null)
                return;

            partner.Halt();
            partner.controller = null;
            partner.movementPause = 0;
            partner.addedSpeed = 0;
            partner.queuedSchedulePaths.Clear();

            partner.Sprite.StopAnimation();
            partner.Sprite.ClearAnimation();
            partner.Sprite.CurrentAnimation = null;
            partner.flip = false;
            partner.faceDirection(partner.FacingDirection);
            partner.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(partner.FacingDirection);
            partner.Sprite.UpdateSourceRect();

            // Do NOT clear CurrentDialogue here.
            // Another mod may have queued dialogue on the NPC.

            ForceScheduleCheckNow(partner);
        }
        // =========================================================================================================================================
        // NPC position reset after a kiss (prevents stuck animations or unexpected teleports)
        // =========================================================================================================================================
        private void UpdatePendingNpcKissReset(NPC partner)
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
        // =====================================================================
        // MAIN SYSTEMS / UPDATE LOOPS
        // =====================================================================
        // =====================================================================
        // SINGLE KISS LOGIC
        // ====================================================================
        private void UpdateKissSystem(NPC partner)
        {
            if (!kissSequenceActive || pendingKissNpc == null)
                return;

            // Same reasoning as the other kiss systems: don't let a kiss sequence continue
            // running while the player is seated.
            if (Game1.player.IsSitting())
            {
                ResetKissState();
                return;
            }

            if (partner.currentLocation != Game1.currentLocation || pendingKissNpc != partner)
            {
                ResetKissState();
                return;
            }

            float distance = DistanceToPlayer(partner);

            if (distance > 120f)
            {
                ResetKissState();
                return;
            }

            FaceEachOther(partner);

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
                    kissPostSequenceNpc = partner;
                    lastKissPostDistance = distance;
                    kissPostLineTriggered = false;
                    kissPostLine = pendingKissCycleLine;
                    ResetKissState();
                }
            }
        }
        // =====================================================================
        // POST-KISS LOGIC (DIALOGUE, ROUTE DEVIATION, ETC.)
        // ======================================================================
        private void UpdatePostKissSystem(NPC partner)
        {
            if (!kissPostSequenceActive || kissPostSequenceNpc == null)
                return;

            if (kissPostSequenceNpc != partner)
            {

                ReleaseNpcAfterMultiKiss(kissPostSequenceNpc);
                ResetPostKissState();
                return;
            }

            if (partner.currentLocation != Game1.currentLocation)
            {

                ReleaseNpcAfterMultiKiss(partner);
                ResetPostKissState();
                return;
            }

            float distance = DistanceToPlayer(partner);

            if (lastKissPostDistance < 0f)
                lastKissPostDistance = distance;

            bool playerStartedMovingAway = distance > lastKissPostDistance + 2f;

            if (!kissPostLineTriggered &&
                !string.IsNullOrEmpty(kissPostLine) &&
                distance >= 120f &&
                dialogueCooldown <= 0 &&
                Game1.activeClickableMenu == null)
            {

                ShowTextAboveHeadWithPipeSupport(partner, kissPostLine);
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

                ShowTextAboveHeadWithPipeSupport(partner, kissPostLine);
                kissPostLineTriggered = true;
                dialogueCooldown = 120;
            }

            lastKissPostDistance = distance;

            if (distance > 160f)
            {
                // Not yet at restore distance — hold here.
                if (HasNpcPreKissSpecialAction(partner) && distance < NpcSpecialActionRestoreDistance)
                    return;

                // Past the restore distance — restore first, then release.
                // Prevents WakeNpcAfterMultiKiss from overwriting the idle frame on top of the restored animation.
                if (HasNpcPreKissSpecialAction(partner))
                    TryRestoreNpcPreKissSpecialAction(clearAfterRestore: true);

                ResetPostKissState();
                ReleaseNpcAfterMultiKiss(partner);

                return;
            }
        }
        // ===================================================================
        // BEIJO AO ESBARRAR
        // ===================================================================
        private void UpdateBumpKissSystem(NPC npc, float distance)
        {
            if (npc == null || !Context.IsWorldReady)
                return;

            // Don't let a bump kiss trigger while the player is seated (e.g. chair/bench) — they
            // can't meaningfully act or move away normally while sitting.
            if (Game1.player.IsSitting())
                return;

            // Same reasoning as UpdateContinuousKissSystem: a menu being open shouldn't let the
            // bump kiss trigger or continue behind it.
            if (Game1.activeClickableMenu != null)
                return;

            bool touching = distance <= 64f;
            bool useOutsideApproachKissHold = !IsHomeOrFarmLocation();
            bool justStartedTouchingPartner = touching && !playerWasTouchingPartner;

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

                playerWasTouchingPartner = touching;
                return;
            }

            Vector2 diff = Game1.player.Position - npc.Position;
            bool isHorizontal = Math.Abs(diff.X) > Math.Abs(diff.Y);

            if (this.Config.BumpKissEnabled &&
                justStartedTouchingPartner &&
                isHorizontal &&
                Game1.player.isMoving() &&
                Game1.timeOfDay < 2400 &&
                cooldown <= 0 &&
                GetApproachKissBlockTimer(npc) <= 0 &&
                !kissPostSequenceActive &&
                !kissSequenceActive &&
                pendingKissNpc == null &&
                !continuousKissActive &&
                talkedToPartnerToday)
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

                bool vanillaTriggered = TryTriggerRomanticBumpKiss(npc);
                if (!vanillaTriggered)
                {
                    // No real vanilla kiss happened (ghost kiss caught) — cleanly undo what was
                    // already applied above (pose capture, halt, pause, player freeze) instead of
                    // running the rest of this mod's own bump-kiss effects (sound, emote,
                    // dialogue) over nothing.
                    TryRestoreNpcPreKissSpecialAction(clearAfterRestore: true);
                    npc.movementPause = 0;
                    Game1.player.CanMove = true;
                    playerWasTouchingPartner = touching;
                    return;
                }

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
                // Only the approachKiss dialogue is restricted to outdoors.
                // If Npc Passing Greetings just triggered a balloon, the kiss still fires,
                // but the bump kiss dialogue is silenced for a few seconds.
                if (locName != "FarmHouse" && locName != "Farm" && !approachKissTriggered && !suppressBumpKissDialogue)
                {
                    string surpriseLine = null;

                    // Capture time at the moment of the kiss for the cooldown check.
                    int kissTimeOfDay = Game1.timeOfDay;
                    bool approachDialogueCooledDown = approachKissDialogueLastTimeOfDay < 0
                        || kissTimeOfDay < approachKissDialogueLastTimeOfDay // new day rollover
                        || (kissTimeOfDay - approachKissDialogueLastTimeOfDay) >= 100;

                    int delayedActionToken = delayedActionContextToken;
                    DelayedAction.functionAfterDelay(() =>
                    {
                        if (!IsCurrentDelayedAction(delayedActionToken) || npc == null || npc.currentLocation != Game1.player.currentLocation)
                            return;

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
                    }, 1200);

                    // only locks approachKiss outside the farm/house
                    approachKissTriggered = true;
                    approachKissRearmPending = true;
                }

                // Give priority to the bump kiss:
                // Clear the multi-kiss hold and prevent it from re-entering immediately on the same touch.
                continuousKissTouchHoldTimer = 0;
                continuousKissWasTouchingPartner = false;
                continuousKissPendingRestart = false;

                // Only outside the farm/house does the partner pause after the bump kiss
                if (useOutsideApproachKissHold)
                {
                    approachKissTriggered = true;
                    approachKissRearmPending = true;

                    NPC pauseNpc = npc;
                    int pauseToken = ++OutsideBumpPause.Token;

                    DelayedAction.functionAfterDelay(() =>
                    {
                        if (!Context.IsWorldReady)
                            return;

                        if (pauseToken != OutsideBumpPause.Token)
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

                        OutsideBumpPause.IsActive = true;
                        OutsideBumpPause.Npc = pauseNpc;
                        OutsideBumpPause.Timer = 360; // 6-second pause
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
                playerWasTouchingPartner = true;
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

            playerWasTouchingPartner = touching;
        }
    }
} //👈 FINAL DO NAMESPACE
