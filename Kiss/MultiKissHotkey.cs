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
                StopPlayerKissSequence(playerState, notifyOtherPlayer: true);
                return true;
            }

            if (hotkeyStoppedMultiKissAwaitingMoveAway)
                return true;

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
                    RequestOrStartPlayerKiss(playerSpouse, PlayerKissMode.Multi, RollContinuousKissTier());
                }

                return true;
            }

            if (npcPartner != null)
            {
                if (pausedBumpPartner == npcPartner)
                    ResetOutsideBumpPause();

                talkedToPartnerToday = true;
                StartContinuousKiss(npcPartner, RollContinuousKissTier(), isNewSequence: true, manualRightClick: true);
            }

            return true;
        }

        private void EndNpcMultiKissFromHotkey()
        {
            NPC partner = continuousKissNpc;
            if (partner == null)
            {
                ForceEndContinuousKiss(null);
                return;
            }

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
                ClearHotkeyStoppedMultiKissWait(releaseNpc: true);
                return;
            }

            float distance = DistanceToPlayer(partner);
            bool playerActuallyMoved = Vector2.Distance(
                Game1.player.Position,
                hotkeyStoppedMultiKissPlayerStartPosition
            ) > 2f;
            bool playerMovedAway = hotkeyStoppedMultiKissInitialDistance < 0f
                || distance > hotkeyStoppedMultiKissInitialDistance + 2f;

            if (distance <= 90f || !playerActuallyMoved || !playerMovedAway)
            {
                partner.movementPause = Math.Max(partner.movementPause, 60);
                partner.faceGeneralDirection(Game1.player.getStandingPosition(), 0, false, false);
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
            ClearHotkeyStoppedMultiKissWait(releaseNpc: false);

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
                    return;
                }

                ShowTextAboveHeadWithPipeSupport(partner, postLine);
                dialogueCooldown = 120;
            }, 200);
        }

        private void ClearHotkeyStoppedMultiKissWait(bool releaseNpc)
        {
            NPC partner = hotkeyStoppedMultiKissNpc;
            if (releaseNpc && partner != null)
                partner.movementPause = 0;

            hotkeyStoppedMultiKissNpc = null;
            hotkeyStoppedMultiKissAwaitingMoveAway = false;
            hotkeyStoppedMultiKissInitialDistance = -1f;
            hotkeyStoppedMultiKissPlayerStartPosition = Vector2.Zero;
        }
    }
}
