using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using System;
using System.Collections.Generic;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        private const string PlayerKissRequestMessageType = "PlayerSpouseKiss.Request";
        private const string PlayerKissAcceptMessageType = "PlayerSpouseKiss.Accept";
        private const string PlayerKissCycleMessageType = "PlayerSpouseKiss.Cycle";
        private const string PlayerKissStopMessageType = "PlayerSpouseKiss.Stop";
        private const int PlayerKissRequestTimeoutTicks = 60;
        private const int PlayerKissCooldownTicks = 75;
        private const int PlayerBumpKissCooldownTicks = 100;
        private const int PlayerMultiKissHoldTicks = 60;
        private const int PlayerMultiKissGapTicks = 25;
        private const int PlayerKissFollowerSafetyTicks = 600;
        private const float PlayerKissMaximumRequestDistance = 120f;
        private const float PlayerMultiKissRestartDistance = 72f;
        private const float PlayerMultiKissStopDistance = 90f;
        private const int PlayerKissMaximumVerticalDifference = 32;

        private readonly PerScreen<PlayerKissState> playerKissState = new(() => new PlayerKissState());
        private readonly Dictionary<long, string> activePlayerKissSequenceByParticipant = new();
        private readonly Dictionary<long, PlayerKissCycleMessage> pendingLocalSplitKissCycles = new();
        private readonly Dictionary<long, PlayerKissStopMessage> pendingLocalSplitKissStops = new();

        private void InitializePlayerSpouseKissSupport(IModHelper helper)
        {
            helper.Events.Multiplayer.ModMessageReceived += OnPlayerKissModMessageReceived;
            helper.Events.Multiplayer.PeerDisconnected += OnPlayerKissPeerDisconnected;
            helper.Events.GameLoop.UpdateTicked += OnPlayerKissUpdateTicked;
            helper.Events.GameLoop.ReturnedToTitle += OnPlayerKissReturnedToTitle;
            helper.Events.Player.Warped += OnPlayerKissWarped;
        }

        private bool TryHandlePlayerSpouseKissClick(ButtonPressedEventArgs e, bool allowFrontTileTarget)
        {
            if (!Context.IsMultiplayer || !CanLocalPlayerStartPlayerKiss(requireStill: true))
                return false;

            Farmer spouse = FindClickedPlayerSpouse(e.Cursor.AbsolutePixels, allowFrontTileTarget);
            if (spouse == null)
                return false;

            Helper.Input.Suppress(e.Button);

            PlayerKissState state = playerKissState.Value;
            if (state.CooldownTicksRemaining > 0 || state.HasOutgoingRequest
                || IsPlayerSpouseKissActiveFor(Game1.player.UniqueMultiplayerID))
            {
                return true;
            }

            PlayerKissMode mode = PlayerKissMode.Simple;
            int tier = 1;

            if (Config.MultiKissEnabled && Config.ManualKissStartsMultiKiss)
            {
                mode = PlayerKissMode.Multi;
                tier = RollContinuousKissTier();
            }
            else if (!Config.MultiKissEnabled && Config.RandomManualKissTier)
            {
                mode = PlayerKissMode.SingleTier;
                tier = RollManualPlayerKissTier();
            }

            RequestOrStartPlayerKiss(spouse, mode, tier);
            return true;
        }

        private Farmer FindClickedPlayerSpouse(Vector2 cursorPixels, bool allowFrontTileTarget)
        {
            Farmer spouse = GetOnlinePlayerSpouse(Game1.player);
            if (!CanPlayersStartKiss(Game1.player, spouse, allowMovement: false))
                return null;

            Rectangle clickArea = spouse.GetBoundingBox();
            clickArea.Inflate(24, 40);
            if (clickArea.Contains((int)cursorPixels.X, (int)cursorPixels.Y))
                return spouse;

            if (!allowFrontTileTarget)
                return null;

            Point interactionTile = Game1.player.TilePoint;
            switch (Game1.player.FacingDirection)
            {
                case 0: interactionTile.Y--; break;
                case 1: interactionTile.X++; break;
                case 2: interactionTile.Y++; break;
                case 3: interactionTile.X--; break;
            }

            Rectangle interactionArea = new(
                interactionTile.X * Game1.tileSize,
                interactionTile.Y * Game1.tileSize,
                Game1.tileSize,
                Game1.tileSize
            );
            interactionArea.Inflate(16, 16);
            return spouse.GetBoundingBox().Intersects(interactionArea) ? spouse : null;
        }

        private Farmer GetOnlinePlayerSpouse(Farmer player)
        {
            if (player == null)
                return null;

            long? spouseId = player.team.GetSpouse(player.UniqueMultiplayerID);
            return spouseId.HasValue ? Game1.GetPlayer(spouseId.Value, true) : null;
        }

        private int RollManualPlayerKissTier()
        {
            int roll = random.Next(100);
            return roll < 50 ? 1 : roll < 80 ? 2 : 3;
        }

        private void RequestOrStartPlayerKiss(Farmer spouse, PlayerKissMode mode, int tier)
        {
            if (spouse == null || playerKissState.Value.HasOutgoingRequest)
                return;

            string requestId = Guid.NewGuid().ToString("N");

            if (IsLocalSplitScreenPlayer(spouse))
            {
                if (Config.AcceptPlayerSpouseKisses == true)
                    StartApprovedPlayerKiss(spouse, mode, tier, requestId);

                return;
            }

            PlayerKissState state = playerKissState.Value;
            state.OutgoingRequestId = requestId;
            state.OutgoingTargetId = spouse.UniqueMultiplayerID;
            state.OutgoingRequestTicksRemaining = PlayerKissRequestTimeoutTicks;
            state.OutgoingMode = mode;
            state.OutgoingTier = tier;

            Helper.Multiplayer.SendMessage(
                new PlayerKissRequestMessage
                {
                    RequestId = requestId,
                    InitiatorId = Game1.player.UniqueMultiplayerID,
                    TargetId = spouse.UniqueMultiplayerID,
                    LocationName = Game1.currentLocation.NameOrUniqueName,
                    Mode = mode,
                    Tier = tier
                },
                PlayerKissRequestMessageType,
                modIDs: new[] { ModManifest.UniqueID },
                playerIDs: new[] { spouse.UniqueMultiplayerID }
            );
        }

        private bool IsLocalSplitScreenPlayer(Farmer player)
        {
            if (!Context.IsSplitScreen || player == null)
                return false;

            IMultiplayerPeer peer = Helper.Multiplayer.GetConnectedPlayer(player.UniqueMultiplayerID);
            if (peer?.IsSplitScreen == true || peer?.ScreenID != null)
                return true;

            return Context.IsOnHostComputer
                && Game1.MasterPlayer?.UniqueMultiplayerID == player.UniqueMultiplayerID;
        }

        private void OnPlayerKissModMessageReceived(object sender, ModMessageReceivedEventArgs e)
        {
            if (e.FromModID != ModManifest.UniqueID || !Context.IsWorldReady || Config?.ModEnabled != true)
                return;

            try
            {
                switch (e.Type)
                {
                    case PlayerKissRequestMessageType:
                        HandlePlayerKissRequest(e.FromPlayerID, e.ReadAs<PlayerKissRequestMessage>());
                        break;
                    case PlayerKissAcceptMessageType:
                        HandlePlayerKissAccept(e.FromPlayerID, e.ReadAs<PlayerKissAcceptMessage>());
                        break;
                    case PlayerKissCycleMessageType:
                        HandlePlayerKissCycle(e.FromPlayerID, e.ReadAs<PlayerKissCycleMessage>());
                        break;
                    case PlayerKissStopMessageType:
                        HandlePlayerKissStop(e.FromPlayerID, e.ReadAs<PlayerKissStopMessage>());
                        break;
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"[PLAYER KISS] Could not process a multiplayer kiss message: {ex}", LogLevel.Warn);
            }
        }

        private void HandlePlayerKissRequest(long senderId, PlayerKissRequestMessage request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId)
                || Game1.player == null || request.InitiatorId != senderId
                || request.TargetId != Game1.player.UniqueMultiplayerID
                || !Enum.IsDefined(typeof(PlayerKissMode), request.Mode)
                || request.Tier < 1 || request.Tier > 3
                || !string.Equals(request.LocationName, Game1.currentLocation?.NameOrUniqueName, StringComparison.Ordinal)
                || Config.AcceptPlayerSpouseKisses != true
                || IsLocalNpcKissSystemBusy()
                || IsPlayerSpouseKissActiveFor(Game1.player.UniqueMultiplayerID))
            {
                return;
            }

            Farmer initiator = Game1.GetPlayer(request.InitiatorId, true);
            bool allowMovement = request.Mode is PlayerKissMode.Bump or PlayerKissMode.Multi;
            if (!CanPlayersStartKiss(Game1.player, initiator, allowMovement))
                return;

            PlayerKissState state = playerKissState.Value;
            if (state.HasOutgoingRequest)
            {
                if (Game1.player.UniqueMultiplayerID < request.InitiatorId)
                    return;

                state.ClearOutgoingRequest();
            }

            if (state.CooldownTicksRemaining > 0)
                return;

            state.CooldownTicksRemaining = 30;

            Helper.Multiplayer.SendMessage(
                new PlayerKissAcceptMessage
                {
                    RequestId = request.RequestId,
                    InitiatorId = request.InitiatorId,
                    TargetId = request.TargetId
                },
                PlayerKissAcceptMessageType,
                modIDs: new[] { ModManifest.UniqueID },
                playerIDs: new[] { request.InitiatorId }
            );
        }

        private void HandlePlayerKissAccept(long senderId, PlayerKissAcceptMessage accept)
        {
            PlayerKissState state = playerKissState.Value;
            if (accept == null || Game1.player == null
                || accept.TargetId != senderId
                || accept.InitiatorId != Game1.player.UniqueMultiplayerID
                || !state.HasOutgoingRequest
                || state.OutgoingRequestId != accept.RequestId
                || state.OutgoingTargetId != accept.TargetId
                || IsLocalNpcKissSystemBusy()
                || IsPlayerSpouseKissActiveFor(Game1.player.UniqueMultiplayerID))
            {
                return;
            }

            PlayerKissMode mode = state.OutgoingMode;
            int tier = state.OutgoingTier;
            string requestId = state.OutgoingRequestId;
            state.ClearOutgoingRequest();

            Farmer spouse = Game1.GetPlayer(accept.TargetId, true);
            bool allowMovement = mode is PlayerKissMode.Bump or PlayerKissMode.Multi;
            if (!CanPlayersStartKiss(Game1.player, spouse, allowMovement))
                return;

            StartApprovedPlayerKiss(spouse, mode, tier, requestId);
        }

        private void StartApprovedPlayerKiss(Farmer spouse, PlayerKissMode mode, int tier, string sequenceId)
        {
            if (spouse == null)
                return;

            PlayerKissState state = playerKissState.Value;
            state.CooldownTicksRemaining = PlayerKissCooldownTicks;

            if (mode is PlayerKissMode.Simple or PlayerKissMode.Bump)
            {
                if (mode == PlayerKissMode.Bump)
                {
                    state.BumpCooldownTicksRemaining = PlayerBumpKissCooldownTicks;
                    state.AutomaticMultiKissHoldTicks = PlayerMultiKissHoldTicks;
                }

                StartVanillaPlayerKiss(spouse);
                return;
            }

            StartCustomPlayerKissSequence(spouse, mode, tier, sequenceId, isAuthority: true);
        }

        private void StartVanillaPlayerKiss(Farmer spouse)
        {
            if (Game1.player == null || spouse == null)
                return;

            Game1.player.Halt();
            spouse.Halt();
            FacePlayersTowardEachOther(Game1.player, spouse);
            spouse.checkAction(Game1.player, Game1.currentLocation);
        }

        private void StartCustomPlayerKissSequence(Farmer spouse, PlayerKissMode mode, int tier, string sequenceId, bool isAuthority)
        {
            PlayerKissState state = playerKissState.Value;
            state.SequenceId = sequenceId;
            state.InitiatorId = isAuthority ? Game1.player.UniqueMultiplayerID : spouse.UniqueMultiplayerID;
            state.TargetId = isAuthority ? spouse.UniqueMultiplayerID : Game1.player.UniqueMultiplayerID;
            state.Mode = mode;
            state.Tier = tier;
            state.IsAuthority = isAuthority;
            state.FollowerSafetyTicks = PlayerKissFollowerSafetyTicks;

            MarkPlayerKissParticipants(state.SequenceId, state.InitiatorId, state.TargetId);

            if (isAuthority)
                BeginPlayerKissCycle(state);
        }

        private void BeginPlayerKissCycle(PlayerKissState state)
        {
            Farmer initiator = Game1.GetPlayer(state.InitiatorId, true);
            Farmer target = Game1.GetPlayer(state.TargetId, true);
            if (!CanPlayersRemainInKiss(initiator, target))
            {
                StopPlayerKissSequence(state, notifyOtherPlayer: true);
                return;
            }

            state.CycleNumber++;
            state.CycleTimerTicks = MsToTicks(GetContinuousKissTierDurationMs(state.Tier));
            state.GapTimerTicks = 0;
            state.IsCycleActive = true;
            state.HeartTriggered = false;
            state.SmokeTriggered = false;
            ResetPlayerKissLean(state, initiator, restorePosition: true);

            PlayerKissCycleMessage cycleMessage = new()
            {
                SequenceId = state.SequenceId,
                InitiatorId = state.InitiatorId,
                TargetId = state.TargetId,
                LocationName = Game1.currentLocation.NameOrUniqueName,
                Mode = state.Mode,
                Tier = state.Tier,
                CycleNumber = state.CycleNumber
            };

            if (IsLocalSplitScreenPlayer(target))
            {
                // SMAPI doesn't send mod messages back to another local split-screen
                // screen. Queue the cycle so it is applied while that player's screen
                // is the active context too; otherwise only the initiating viewport
                // sees the Farmer animation even though shared effects still appear.
                pendingLocalSplitKissCycles[state.TargetId] = cycleMessage;
            }
            else
            {
                Helper.Multiplayer.SendMessage(
                    cycleMessage,
                    PlayerKissCycleMessageType,
                    modIDs: new[] { ModManifest.UniqueID },
                    playerIDs: new[] { state.TargetId }
                );
            }

            ApplyPlayerKissAnimation(state, initiator, target);
            Game1.playSound("dwop");
        }

        private void HandlePlayerKissCycle(long senderId, PlayerKissCycleMessage cycle)
        {
            if (cycle == null || string.IsNullOrWhiteSpace(cycle.SequenceId)
                || cycle.InitiatorId != senderId
                || Game1.player == null || cycle.TargetId != Game1.player.UniqueMultiplayerID
                || cycle.Mode is not (PlayerKissMode.Multi or PlayerKissMode.SingleTier)
                || cycle.Tier < 1 || cycle.Tier > 3
                || !string.Equals(cycle.LocationName, Game1.currentLocation?.NameOrUniqueName, StringComparison.Ordinal)
                || Config.AcceptPlayerSpouseKisses != true)
            {
                return;
            }

            Farmer initiator = Game1.GetPlayer(cycle.InitiatorId, true);
            if (!CanPlayersRemainInKiss(Game1.player, initiator))
                return;

            PlayerKissState state = playerKissState.Value;
            if (state.HasActiveSequence && state.SequenceId != cycle.SequenceId)
                StopPlayerKissSequence(state, notifyOtherPlayer: false);

            if (!state.HasActiveSequence)
                StartCustomPlayerKissSequence(initiator, cycle.Mode, cycle.Tier, cycle.SequenceId, isAuthority: false);

            if (cycle.CycleNumber <= state.CycleNumber)
                return;

            state.Mode = cycle.Mode;
            state.Tier = cycle.Tier;
            state.CycleNumber = cycle.CycleNumber;
            state.CycleTimerTicks = MsToTicks(GetContinuousKissTierDurationMs(cycle.Tier));
            state.IsCycleActive = true;
            state.HeartTriggered = false;
            state.SmokeTriggered = false;
            state.FollowerSafetyTicks = PlayerKissFollowerSafetyTicks;

            ApplyPlayerKissAnimation(state, initiator, Game1.player);
        }

        private void ApplyPlayerKissAnimation(PlayerKissState state, Farmer initiator, Farmer target)
        {
            if (state == null || initiator == null || target == null)
                return;

            initiator.Halt();
            target.Halt();
            FacePlayersTowardEachOther(initiator, target);

            state.IsApplyingAnimation = true;
            state.ApplyingAnimationDurationMs = GetContinuousKissTierDurationMs(state.Tier);
            state.ApplyingContinuousAnimation = state.Mode == PlayerKissMode.Multi;

            try
            {
                bool initiatorIsLeft = initiator.StandingPixel.X < target.StandingPixel.X;
                initiator.PerformKiss(initiatorIsLeft ? 1 : 3);
                target.PerformKiss(initiatorIsLeft ? 3 : 1);
            }
            finally
            {
                state.IsApplyingAnimation = false;
                state.ApplyingAnimationDurationMs = 0;
                state.ApplyingContinuousAnimation = false;
            }
        }

        internal bool TryGetPlayerSpouseKissAnimationSettings(out int durationMs, out bool keepMovementUnlocked)
        {
            PlayerKissState state = playerKissState.Value;
            durationMs = state.ApplyingAnimationDurationMs;
            keepMovementUnlocked = state.ApplyingContinuousAnimation;
            return state.IsApplyingAnimation;
        }

        private static void FacePlayersTowardEachOther(Farmer first, Farmer second)
        {
            bool firstIsLeft = first.StandingPixel.X < second.StandingPixel.X;
            first.faceDirection(firstIsLeft ? 1 : 3);
            second.faceDirection(firstIsLeft ? 3 : 1);
        }

        private void OnPlayerKissUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            PlayerKissState state = playerKissState.Value;

            if (!Context.IsWorldReady || Config?.ModEnabled != true)
            {
                if (state.HasActiveSequence)
                    StopPlayerKissSequence(state, notifyOtherPlayer: Context.IsWorldReady);
                state.Reset();
                return;
            }

            ProcessPendingLocalSplitKissMessages();
            state = playerKissState.Value;

            if (Game1.eventUp)
            {
                if (state.HasActiveSequence)
                    StopPlayerKissSequence(state, notifyOtherPlayer: true);
                state.ClearOutgoingRequest();
                return;
            }

            // Match the NPC multi-kiss behavior: changing window focus pauses the sequence
            // instead of letting its timers and tier effects finish in the background.
            if (Game1.game1 != null && !Game1.game1.IsActive)
                return;

            if (state.CooldownTicksRemaining > 0)
                state.CooldownTicksRemaining--;
            if (state.BumpCooldownTicksRemaining > 0)
                state.BumpCooldownTicksRemaining--;

            if (state.OutgoingRequestTicksRemaining > 0)
            {
                state.OutgoingRequestTicksRemaining--;
                if (state.OutgoingRequestTicksRemaining == 0)
                    state.ClearOutgoingRequest();
            }

            if (state.HasActiveSequence)
                UpdateActivePlayerKissSequence(state);
            else
                UpdateAutomaticPlayerSpouseKissTriggers(state);
        }

        private void ProcessPendingLocalSplitKissMessages()
        {
            long localPlayerId = Game1.player?.UniqueMultiplayerID ?? 0;
            if (localPlayerId == 0)
                return;

            // Process an ending before a new cycle so a stale sequence can never
            // overwrite a newer one when both local screens update on the same tick.
            if (pendingLocalSplitKissStops.Remove(localPlayerId, out PlayerKissStopMessage stop))
                HandlePlayerKissStop(stop.InitiatorId, stop);

            if (pendingLocalSplitKissCycles.Remove(localPlayerId, out PlayerKissCycleMessage cycle))
                HandlePlayerKissCycle(cycle.InitiatorId, cycle);
        }

        private void UpdateAutomaticPlayerSpouseKissTriggers(PlayerKissState state)
        {
            Farmer spouse = GetOnlinePlayerSpouse(Game1.player);
            if (spouse == null || spouse.currentLocation != Game1.currentLocation)
            {
                state.WasTouchingSpouse = false;
                state.AutomaticMultiKissHoldTicks = 0;
                return;
            }

            float distance = Vector2.Distance(Game1.player.getStandingPosition(), spouse.getStandingPosition());
            bool horizontallyAligned = Game1.player.TilePoint.Y == spouse.TilePoint.Y;
            bool touching = distance <= 64f && horizontallyAligned;
            bool justStartedTouching = touching && !state.WasTouchingSpouse;
            state.WasTouchingSpouse = touching;

            if (!touching)
                state.AutomaticMultiKissHoldTicks = 0;

            if (state.HasOutgoingRequest || IsLocalNpcKissSystemBusy()
                || IsPlayerSpouseKissActiveFor(Game1.player.UniqueMultiplayerID)
                || !CanLocalPlayerStartPlayerKiss(requireStill: false)
                || !CanPlayersStartKiss(Game1.player, spouse, allowMovement: true))
            {
                return;
            }

            if (Config.BumpKissEnabled && justStartedTouching && Game1.player.isMoving()
                && state.BumpCooldownTicksRemaining <= 0 && Game1.timeOfDay < 2400)
            {
                state.AutomaticMultiKissHoldTicks = PlayerMultiKissHoldTicks;
                RequestOrStartPlayerKiss(spouse, PlayerKissMode.Bump, 1);
                return;
            }

            if (!Config.MultiKissEnabled || Config.ManualKissStartsMultiKiss || !touching)
            {
                state.AutomaticMultiKissHoldTicks = 0;
                return;
            }

            if (state.AutomaticMultiKissHoldTicks <= 0)
                state.AutomaticMultiKissHoldTicks = PlayerMultiKissHoldTicks;
            else
                state.AutomaticMultiKissHoldTicks--;

            if (state.AutomaticMultiKissHoldTicks == 0)
                RequestOrStartPlayerKiss(spouse, PlayerKissMode.Multi, RollContinuousKissTier());
        }

        private void UpdateActivePlayerKissSequence(PlayerKissState state)
        {
            Farmer initiator = Game1.GetPlayer(state.InitiatorId, true);
            Farmer target = Game1.GetPlayer(state.TargetId, true);
            if (!CanPlayersRemainInKiss(initiator, target))
            {
                StopPlayerKissSequence(state, notifyOtherPlayer: true);
                return;
            }

            float distance = Vector2.Distance(initiator.getStandingPosition(), target.getStandingPosition());
            if (distance > PlayerMultiKissStopDistance || initiator.TilePoint.Y != target.TilePoint.Y)
            {
                StopPlayerKissSequence(state, notifyOtherPlayer: true);
                return;
            }

            if (Game1.activeClickableMenu != null)
                return;

            if (!state.IsAuthority)
            {
                if (state.FollowerSafetyTicks > 0)
                    state.FollowerSafetyTicks--;
                else
                {
                    StopPlayerKissSequence(state, notifyOtherPlayer: false);
                    return;
                }

                if (state.CycleTimerTicks > 0)
                    state.CycleTimerTicks--;

                UpdatePlayerKissTierEffects(state, initiator, target);
                if (state.CycleTimerTicks == 0)
                    state.IsCycleActive = false;
                return;
            }

            if (state.IsCycleActive)
            {
                if (state.CycleTimerTicks > 0)
                    state.CycleTimerTicks--;

                UpdatePlayerKissTierEffects(state, initiator, target);
                UpdatePlayerKissLean(state, initiator, target);

                if (state.CycleTimerTicks > 0 || state.LeanPhase != 0)
                    return;

                state.IsCycleActive = false;

                if (state.Mode == PlayerKissMode.SingleTier)
                {
                    StopPlayerKissSequence(state, notifyOtherPlayer: true);
                    return;
                }

                TriggerBystanderReactions(state.Tier, partner: null);
                ScheduleBystanderRestore();
                state.Tier = RollContinuousKissTier();
                state.GapTimerTicks = PlayerMultiKissGapTicks;
                return;
            }

            if (state.GapTimerTicks > 0)
            {
                state.GapTimerTicks--;
                return;
            }

            if (distance <= PlayerMultiKissRestartDistance)
                BeginPlayerKissCycle(state);
            else
                StopPlayerKissSequence(state, notifyOtherPlayer: true);
        }

        private void UpdatePlayerKissTierEffects(PlayerKissState state, Farmer initiator, Farmer target)
        {
            int totalTicks = MsToTicks(GetContinuousKissTierDurationMs(state.Tier));
            int elapsedTicks = totalTicks - state.CycleTimerTicks;

            if (state.Tier == 2 && !state.HeartTriggered && state.CycleTimerTicks <= totalTicks / 2)
            {
                if (state.IsAuthority)
                {
                    initiator.doEmote(20);
                    target.doEmote(20);
                }
                state.HeartTriggered = true;
            }
            else if (state.Tier == 3)
            {
                if (!state.HeartTriggered && elapsedTicks >= MsToTicks(1000))
                {
                    if (state.IsAuthority)
                    {
                        initiator.doEmote(20);
                        target.doEmote(20);
                    }
                    state.HeartTriggered = true;
                }

                if (!state.SmokeTriggered && elapsedTicks >= MsToTicks(3000))
                {
                    ShowBlushSmoke(target);
                    state.SmokeTriggered = true;
                }
            }
        }

        private void UpdatePlayerKissLean(PlayerKissState state, Farmer initiator, Farmer target)
        {
            if (!state.IsAuthority || state.Tier != 3)
                return;

            int totalTicks = MsToTicks(GetContinuousKissTierDurationMs(3));
            int elapsedTicks = totalTicks - state.CycleTimerTicks;

            if (state.LeanPhase == 0 && state.CycleTimerTicks > 0
                && elapsedTicks >= MsToTicks(ContinuousKissTier3LeanInDelayMs))
            {
                Vector2 direction = target.Position - initiator.Position;
                if (direction != Vector2.Zero)
                {
                    direction.Normalize();
                    state.LeanDirection = direction;
                    state.LeanPhase = 1;
                    state.LeanTimerTicks = 0;
                    PlayHugSound();
                }
            }

            if (state.CycleTimerTicks == 0 && state.LeanPhase is 1 or 2)
            {
                state.LeanPhase = 3;
                state.LeanTimerTicks = 0;
            }

            if (state.LeanPhase is not (1 or 3))
                return;

            state.LeanTimerTicks++;
            float progress = Math.Min(1f, state.LeanTimerTicks / (float)ContinuousKissTier3LeanAnimationTicks);
            Vector2 desiredOffset = state.LeanPhase == 1
                ? Vector2.Lerp(Vector2.Zero, state.LeanDirection * ContinuousKissTier3LeanPixels, progress)
                : Vector2.Lerp(state.LeanDirection * ContinuousKissTier3LeanPixels, Vector2.Zero, progress);

            initiator.Position += desiredOffset - state.LeanAppliedOffset;
            state.LeanAppliedOffset = desiredOffset;

            if (progress < 1f)
                return;

            if (state.LeanPhase == 1)
            {
                state.LeanPhase = 2;
                state.LeanTimerTicks = 0;
            }
            else
            {
                state.LeanPhase = 0;
                state.LeanTimerTicks = 0;
                state.LeanDirection = Vector2.Zero;
                state.LeanAppliedOffset = Vector2.Zero;
            }
        }

        private void ResetPlayerKissLean(PlayerKissState state, Farmer initiator, bool restorePosition)
        {
            if (restorePosition && initiator != null && state.LeanAppliedOffset != Vector2.Zero)
                initiator.Position -= state.LeanAppliedOffset;

            state.LeanPhase = 0;
            state.LeanTimerTicks = 0;
            state.LeanDirection = Vector2.Zero;
            state.LeanAppliedOffset = Vector2.Zero;
        }

        private void StopPlayerKissSequence(PlayerKissState state, bool notifyOtherPlayer)
        {
            if (state == null || !state.HasActiveSequence)
                return;

            string sequenceId = state.SequenceId;
            long initiatorId = state.InitiatorId;
            long targetId = state.TargetId;
            Farmer initiator = Game1.GetPlayer(initiatorId, true);
            Farmer target = Game1.GetPlayer(targetId, true);

            ResetPlayerKissLean(state, initiator, restorePosition: state.IsAuthority);

            if (state.IsCycleActive)
            {
                StopOwnedPlayerKissAnimation(initiator);
                StopOwnedPlayerKissAnimation(target);
            }

            if (notifyOtherPlayer)
            {
                long localId = Game1.player?.UniqueMultiplayerID ?? 0;
                long otherId = localId == initiatorId ? targetId : initiatorId;
                Farmer other = Game1.GetPlayer(otherId, true);
                if (other != null && IsLocalSplitScreenPlayer(other))
                {
                    pendingLocalSplitKissCycles.Remove(otherId);
                    pendingLocalSplitKissStops[otherId] = new PlayerKissStopMessage
                    {
                        SequenceId = sequenceId,
                        InitiatorId = initiatorId,
                        TargetId = targetId
                    };
                }
                else if (other != null)
                {
                    Helper.Multiplayer.SendMessage(
                        new PlayerKissStopMessage
                        {
                            SequenceId = sequenceId,
                            InitiatorId = initiatorId,
                            TargetId = targetId
                        },
                        PlayerKissStopMessageType,
                        modIDs: new[] { ModManifest.UniqueID },
                        playerIDs: new[] { otherId }
                    );
                }
            }

            UnmarkPlayerKissParticipants(sequenceId, initiatorId, targetId);
            state.ClearActiveSequence();
            ScheduleBystanderRestore();
        }

        private static void StopOwnedPlayerKissAnimation(Farmer player)
        {
            if (player == null || player.FarmerSprite == null)
                return;

            if (player.FarmerSprite.CurrentFrame == 101)
                Farmer.completelyStopAnimating(player);
        }

        private void HandlePlayerKissStop(long senderId, PlayerKissStopMessage stop)
        {
            PlayerKissState state = playerKissState.Value;
            if (stop == null || !state.HasActiveSequence || state.SequenceId != stop.SequenceId
                || state.InitiatorId != stop.InitiatorId || state.TargetId != stop.TargetId
                || (senderId != stop.InitiatorId && senderId != stop.TargetId))
            {
                return;
            }

            StopPlayerKissSequence(state, notifyOtherPlayer: false);
        }

        private void MarkPlayerKissParticipants(string sequenceId, long initiatorId, long targetId)
        {
            activePlayerKissSequenceByParticipant[initiatorId] = sequenceId;
            activePlayerKissSequenceByParticipant[targetId] = sequenceId;
        }

        private void UnmarkPlayerKissParticipants(string sequenceId, long initiatorId, long targetId)
        {
            if (activePlayerKissSequenceByParticipant.TryGetValue(initiatorId, out string initiatorSequence)
                && initiatorSequence == sequenceId)
            {
                activePlayerKissSequenceByParticipant.Remove(initiatorId);
            }

            if (activePlayerKissSequenceByParticipant.TryGetValue(targetId, out string targetSequence)
                && targetSequence == sequenceId)
            {
                activePlayerKissSequenceByParticipant.Remove(targetId);
            }
        }

        internal bool IsPlayerSpouseKissActiveForLocalPlayer()
        {
            return Game1.player != null && IsPlayerSpouseKissActiveFor(Game1.player.UniqueMultiplayerID);
        }

        private bool IsPlayerSpouseKissActiveFor(long playerId)
        {
            return activePlayerKissSequenceByParticipant.ContainsKey(playerId);
        }

        private bool CanLocalPlayerStartPlayerKiss(bool requireStill)
        {
            return Game1.player != null && Game1.currentLocation != null
                && !Game1.eventUp && !Game1.dialogueUp && Game1.activeClickableMenu == null
                && !IsLocalNpcKissSystemBusy()
                && CanPlayerParticipateInPlayerKiss(Game1.player, requireLocalScreenReady: true, allowMovement: !requireStill);
        }

        private bool CanPlayersStartKiss(Farmer first, Farmer second, bool allowMovement)
        {
            if (first == null || second == null || first == second
                || first.currentLocation == null || first.currentLocation != second.currentLocation
                || !ArePlayersMarriedToEachOther(first, second)
                || !CanPlayerParticipateInPlayerKiss(first, first.IsLocalPlayer, allowMovement)
                || !CanPlayerParticipateInPlayerKiss(second, second.IsLocalPlayer, allowMovement))
            {
                return false;
            }

            return Vector2.Distance(first.getStandingPosition(), second.getStandingPosition()) <= PlayerKissMaximumRequestDistance
                && Math.Abs(first.StandingPixel.Y - second.StandingPixel.Y) <= PlayerKissMaximumVerticalDifference;
        }

        private bool CanPlayersRemainInKiss(Farmer first, Farmer second)
        {
            return first != null && second != null && first != second
                && first.currentLocation != null && first.currentLocation == second.currentLocation
                && ArePlayersMarriedToEachOther(first, second)
                && !first.IsSitting() && !second.IsSitting()
                && !first.UsingTool && !second.UsingTool
                && !first.isRidingHorse() && !second.isRidingHorse();
        }

        private static bool ArePlayersMarriedToEachOther(Farmer first, Farmer second)
        {
            long? firstSpouse = first.team.GetSpouse(first.UniqueMultiplayerID);
            long? secondSpouse = second.team.GetSpouse(second.UniqueMultiplayerID);
            return firstSpouse == second.UniqueMultiplayerID && secondSpouse == first.UniqueMultiplayerID;
        }

        private static bool CanPlayerParticipateInPlayerKiss(Farmer player, bool requireLocalScreenReady, bool allowMovement)
        {
            if (player == null || !player.CanMove || (!allowMovement && player.isMoving())
                || player.UsingTool || player.IsSitting() || player.isRidingHorse() || player.IsEmoting)
            {
                return false;
            }

            if (requireLocalScreenReady && (Game1.eventUp || Game1.dialogueUp
                || Game1.activeClickableMenu != null || player.ActiveObject != null))
            {
                return false;
            }

            return true;
        }

        private bool IsLocalNpcKissSystemBusy()
        {
            return continuousKissActive
                || continuousKissPendingRestart
                || kissSequenceActive
                || kissPostSequenceActive
                || pendingKissNpc != null
                || OutsideBumpPause.IsActive;
        }

        private void OnPlayerKissPeerDisconnected(object sender, PeerDisconnectedEventArgs e)
        {
            PlayerKissState state = playerKissState.Value;
            if (state.OutgoingTargetId == e.Peer.PlayerID)
                state.ClearOutgoingRequest();

            if (state.HasActiveSequence
                && (state.InitiatorId == e.Peer.PlayerID || state.TargetId == e.Peer.PlayerID))
            {
                StopPlayerKissSequence(state, notifyOtherPlayer: false);
            }
        }

        private void OnPlayerKissWarped(object sender, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer)
                return;

            PlayerKissState state = playerKissState.Value;
            if (state.HasActiveSequence)
                StopPlayerKissSequence(state, notifyOtherPlayer: true);
            state.ClearOutgoingRequest();
            state.WasTouchingSpouse = false;
            state.AutomaticMultiKissHoldTicks = 0;
        }

        private void OnPlayerKissReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            PlayerKissState state = playerKissState.Value;
            if (state.HasActiveSequence)
                StopPlayerKissSequence(state, notifyOtherPlayer: false);
            state.Reset();
            activePlayerKissSequenceByParticipant.Clear();
            pendingLocalSplitKissCycles.Clear();
            pendingLocalSplitKissStops.Clear();
        }
    }
}
