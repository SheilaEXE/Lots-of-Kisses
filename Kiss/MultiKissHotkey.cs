using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
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

            string postLine = GetDialogueLine(
                HasBystandersWithLineOfSight() ? "PublicKissReaction" : "kissReaction",
                partner
            );
            float endingDistance = DistanceToPlayer(partner);

            // Use the full safety cleanup first, then restore only the lightweight post-kiss
            // state. The public interruption stays cancelled, but the partner can still show
            // their usual little reaction bubble once the player starts moving away.
            ForceEndContinuousKiss(partner);

            if (partner.currentLocation != Game1.currentLocation || string.IsNullOrEmpty(postLine))
                return;

            kissPostSequenceActive = true;
            kissPostSequenceNpc = partner;
            lastKissPostDistance = endingDistance;
            kissPostLineTriggered = false;
            kissPostLine = postLine;
        }
    }
}
