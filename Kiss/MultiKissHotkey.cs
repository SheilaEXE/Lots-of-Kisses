using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        private NPC hotkeyStoppedMultiKissNpc;
        private bool hotkeyStoppedMultiKissAwaitingMoveAway;
        private float hotkeyStoppedMultiKissInitialDistance = -1f;
        private Vector2 hotkeyStoppedMultiKissPlayerStartPosition;
        private string hotkeyStoppedMultiKissLastDebugWaitReason;

        private bool IsMultiKissHotkeyConfigured()
        {
            string keyText = Config?.MultiKissToggleKey?.ToString();
            return !string.IsNullOrWhiteSpace(keyText)
                && !string.Equals(keyText, "None", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryHandleMultiKissHotkey(ButtonPressedEventArgs e)
        {
            if (!IsMultiKissHotkeyConfigured() || Config.MultiKissToggleKey.JustPressed() == false)
                return false;

            Helper.Input.Suppress(e.Button);
            DebugLog("HOTKEY", $"Multi-Kiss toggle pressed: button={e.Button}, npcSequenceActive={continuousKissActive || continuousKissPendingRestart}, playerSequenceActive={playerKissState.Value.HasActiveSequence}.");

            // Stopping always wins over starting. This lets the player end the chain before
            // it reaches the public-interruption dialogue without opening another interaction.
            if (continuousKissActive || continuousKissPendingRestart)
            {
                EndNpcMultiKissFromHotkey();
                return true;
            }

            PlayerKissState playerState = playerKissState.Value;
            if (playerState.HasActiveSequence && playerState.Mode == PlayerKissMode.Multi)
            {
                StopPlayerKissSequence(playerState, notifyOtherPlayer: true, reason: "local player pressed Multi-Kiss toggle");
                return true;
            }

            if (hotkeyStoppedMultiKissAwaitingMoveAway)
            {
                DebugLog("HOTKEY", $"Start ignored because the previous NPC Multi-Kiss is still waiting for move-away: npc={hotkeyStoppedMultiKissNpc?.Name ?? "null"}.");
                return true;
            }

            // Outdoors, a completed bump kiss deliberately keeps its NPC facing the player for
            // a few seconds. Treat pressing the hotkey during that window as an escalation into
            // multi-kiss instead of rejecting the input as another busy interaction.
            NPC pausedBumpPartner = OutsideBumpPause.IsActive
                ? OutsideBumpPause.Npc
                : null;

            if (!Config.MultiKissEnabled || Game1.player == null || Game1.currentLocation == null
                || Game1.eventUp || Game1.dialogueUp || Game1.activeClickableMenu != null
                || !Game1.player.canMove || Game1.player.IsSitting()
                || Game1.player.ActiveObject != null || kissSequenceActive
                || pendingKissNpc != null
                || playerState.HasOutgoingRequest || playerState.HasActiveSequence)
            {
                DebugLog("HOTKEY", () =>
                    $"Start rejected: enabled={Config.MultiKissEnabled}, player={(Game1.player != null)}, location={(Game1.currentLocation != null)}, " +
                    $"event={Game1.eventUp}, dialogue={Game1.dialogueUp}, menu={(Game1.activeClickableMenu != null)}, canMove={Game1.player?.canMove}, " +
                    $"sitting={Game1.player?.IsSitting()}, holdingItem={(Game1.player?.ActiveObject != null)}, npcKissSequence={kissSequenceActive}, " +
                    $"pendingNpc={pendingKissNpc?.Name ?? "null"}, outgoingPlayerRequest={playerState.HasOutgoingRequest}, activePlayerSequence={playerState.HasActiveSequence}."
                );
                return true;
            }

            if (kissPostSequenceActive)
                ResetPostKissState();

            NPC npcPartner = pausedBumpPartner != null
                && pausedBumpPartner.currentLocation == Game1.currentLocation
                && DistanceToPlayer(pausedBumpPartner) <= 120f
                    ? pausedBumpPartner
                    : GetNearestRomanticPartnerInCurrentLocation();
            float npcDistance = npcPartner == null ? float.MaxValue : DistanceToPlayer(npcPartner);
            if (npcDistance > 120f)
                npcPartner = null;

            Farmer playerSpouse = Context.IsMultiplayer ? GetOnlinePlayerSpouse(Game1.player) : null;
            bool canKissPlayerSpouse = playerSpouse != null
                && CanPlayersStartKiss(Game1.player, playerSpouse, allowMovement: false);
            float playerDistance = canKissPlayerSpouse
                ? Vector2.Distance(Game1.player.getStandingPosition(), playerSpouse.getStandingPosition())
                : float.MaxValue;

            if (pausedBumpPartner != npcPartner && playerDistance < npcDistance)
            {
                PlayerKissState state = playerKissState.Value;
                if (state.CooldownTicksRemaining <= 0 && !state.HasOutgoingRequest
                    && !IsPlayerSpouseKissActiveFor(Game1.player.UniqueMultiplayerID))
                {
                    DebugLog("HOTKEY", $"Starting player-spouse Multi-Kiss with {playerSpouse.Name}; distance={playerDistance:0}.");
                    RequestOrStartPlayerKiss(playerSpouse, PlayerKissMode.Multi, RollContinuousKissTier());
                }
                else
                    DebugLog("HOTKEY", $"Player-spouse start blocked: cooldown={state.CooldownTicksRemaining}, outgoingRequest={state.HasOutgoingRequest}, participantBusy={IsPlayerSpouseKissActiveFor(Game1.player.UniqueMultiplayerID)}.");

                return true;
            }

            if (npcPartner != null)
            {
                if (pausedBumpPartner == npcPartner)
                    ResetOutsideBumpPause("Multi-Kiss hotkey escalated the bump kiss");

                talkedToPartnerToday = true;
                int tier = RollContinuousKissTier();
                bool started = StartContinuousKiss(npcPartner, tier, isNewSequence: true, manualRightClick: true);
                DebugLog("HOTKEY", $"NPC Multi-Kiss start result: npc={npcPartner.Name}, tier={tier}, distance={npcDistance:0}, started={started}.");
            }
            else
                DebugLog("HOTKEY", $"No eligible romantic partner found within 120 pixels (nearestNpcDistance={npcDistance:0}, playerSpouseDistance={playerDistance:0}).");

            return true;
        }

        private void EndNpcMultiKissFromHotkey()
        {
            NPC partner = continuousKissNpc;
            if (partner == null)
            {
                ForceEndContinuousKiss(null, "hotkey stop had no active NPC reference");
                return;
            }

            DebugLog("HOTKEY", () => $"Stopping NPC Multi-Kiss and entering move-away wait: {DescribeNpcDebugState(partner)}.");

            ScheduleBystanderRestore(partner);
            ReleasePlayerAfterKissWithoutOverridingCurrentPose();
            ResetContinuousKissState();
            ResetPostKissState();

            kissProximityTimer = 0;
            playerWasTouchingPartner = false;
            continuousKissTouchHoldTimer = 0;
            continuousKissWasTouchingPartner = false;
            activeKissVisualDelayMs = bumpKissVisualDelayMs;

            hotkeyStoppedMultiKissNpc = partner;
            hotkeyStoppedMultiKissAwaitingMoveAway = true;
            hotkeyStoppedMultiKissInitialDistance = DistanceToPlayer(partner);
            hotkeyStoppedMultiKissPlayerStartPosition = Game1.player.Position;
            hotkeyStoppedMultiKissLastDebugWaitReason = null;
            partner.movementPause = Math.Max(partner.movementPause, 60);
            partner.faceGeneralDirection(Game1.player.getStandingPosition(), 0, false, false);
        }

        private void UpdateHotkeyStoppedMultiKiss()
        {
            if (!hotkeyStoppedMultiKissAwaitingMoveAway || hotkeyStoppedMultiKissNpc == null)
                return;

            NPC partner = hotkeyStoppedMultiKissNpc;
            if (!Context.IsWorldReady || Game1.player == null
                || partner.currentLocation == null
                || partner.currentLocation != Game1.player.currentLocation)
            {
                ClearHotkeyStoppedMultiKissWait(releaseNpc: true, reason: "world, player, NPC, or location became unavailable");
                return;
            }

            float distance = DistanceToPlayer(partner);
            bool useExtendedSnapshotWait = CanUsePostMultiKissLookWait(partner);
            float requiredDistance = useExtendedSnapshotWait
                ? PostMultiKissLookRestoreDistance
                : 90f;
            bool playerActuallyMoved = Vector2.Distance(
                Game1.player.Position,
                hotkeyStoppedMultiKissPlayerStartPosition
            ) > 2f;
            bool playerMovedAway = hotkeyStoppedMultiKissInitialDistance < 0f
                || distance > hotkeyStoppedMultiKissInitialDistance + 2f;

            if (distance < requiredDistance || !playerActuallyMoved || !playerMovedAway)
            {
                string waitReason = distance < requiredDistance
                    ? $"player remains within required distance ({requiredDistance:0} pixels)"
                    : !playerActuallyMoved
                        ? "player has not moved"
                        : "player moved but not farther from NPC";
                if (hotkeyStoppedMultiKissLastDebugWaitReason != waitReason)
                {
                    hotkeyStoppedMultiKissLastDebugWaitReason = waitReason;
                    DebugLog("HOTKEY", $"Waiting to release {partner.Name}: {waitReason}; distance={distance:0}, initialDistance={hotkeyStoppedMultiKissInitialDistance:0}, extendedSnapshotWait={useExtendedSnapshotWait}.");
                }
                if (useExtendedSnapshotWait)
                    ApplyPostMultiKissLookAtPlayer(partner, 60);
                else
                {
                    partner.movementPause = Math.Max(partner.movementPause, 60);
                    partner.faceGeneralDirection(Game1.player.getStandingPosition(), 0, false, false);
                }
                return;
            }

            string postLine = GetDialogueLine(
                HasBystandersWithLineOfSight() ? "PublicKissReaction" : "kissReaction",
                partner
            );

            // Match the normal distance-based ending: stop reinforcing the hold, but don't zero
            // movementPause or force a schedule check. A walking NPC's existing controller resumes
            // naturally when the remaining pause expires; saved idle/special states still use the
            // normal deferred restoration distance.
            ClearHotkeyStoppedMultiKissWait(releaseNpc: false, reason: $"player moved away to {distance:0} pixels");

            if (string.IsNullOrEmpty(postLine))
                return;

            int delayedActionToken = delayedActionContextToken;
            DelayedAction.functionAfterDelay(() =>
            {
                if (!IsCurrentDelayedAction(delayedActionToken)
                    || partner.currentLocation != Game1.player?.currentLocation
                    || DistanceToPlayer(partner) < 72f
                    || Game1.activeClickableMenu != null)
                {
                    DebugLog("DELAYED", $"Skipped hotkey post-kiss line for stale token {delayedActionToken}, location/distance mismatch, or open menu.");
                    return;
                }

                ShowTextAboveHeadWithPipeSupport(partner, postLine);
                dialogueCooldown = 120;
                DebugLog("HOTKEY", $"Displayed post-kiss line for {partner.Name} after hotkey release.");
            }, 200);
        }

        private void ClearHotkeyStoppedMultiKissWait(bool releaseNpc, string reason = "cleared")
        {
            NPC partner = hotkeyStoppedMultiKissNpc;
            if (hotkeyStoppedMultiKissAwaitingMoveAway)
                DebugLog("HOTKEY", $"Cleared move-away wait ({reason}): npc={partner?.Name ?? "null"}, releaseNpc={releaseNpc}.");
            if (releaseNpc && partner != null)
                partner.movementPause = 0;

            hotkeyStoppedMultiKissNpc = null;
            hotkeyStoppedMultiKissAwaitingMoveAway = false;
            hotkeyStoppedMultiKissInitialDistance = -1f;
            hotkeyStoppedMultiKissPlayerStartPosition = Vector2.Zero;
            hotkeyStoppedMultiKissLastDebugWaitReason = null;
        }
    }
}
