using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            ResetTransientKissContext();
            lastDayChecked = Game1.dayOfMonth;
            talkedToPartnerToday = false;
            didReactThisTick = false;
            wasInNoticeZone = false;
            lastNoticeDistance = -1f;

            contentPackLoader.Load();
        }
        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            ResetTransientKissContext();
            lastDayChecked = Game1.dayOfMonth;
            talkedToPartnerToday = false;

            lastNoticeDistance = -1f;
        }
        private void OnTimeChanged(object sender, TimeChangedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (Game1.dayOfMonth != lastDayChecked)
            {
                lastDayChecked = Game1.dayOfMonth;
                talkedToPartnerToday = false;
    
                ResetKissState();
                ResetContinuousKissState();
                ResetPostKissState();
        
            }

        }
        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            ResetTransientKissContext();
            lastDayChecked = -1;
            lastLocation = "";
            cooldown = 0;
            didReactThisTick = false;
            dialogueCooldown = 0;
            noticeEmoteCooldown = 0;
            lastNoticeDistance = -1f;

            wasInNoticeZone = false;
            talkedToPartnerToday = false;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            InitializeStardewSquadSupport();

            try
            {
                var gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
                ModConfigMenu.Register(this, gmcm);
            }
            catch (System.Exception ex)
            {
                this.Monitor.Log($"[GMCM] Could not register the configuration menu: {ex}", LogLevel.Warn);
            }

            this.passingGreetingsApi =
            this.Helper.ModRegistry.GetApi<INpcPassingGreetingsApi>("NatrollEXE.NpcPassingGreetings");

            try
            {
                string displayName = Helper.Translation.Get("tile-marker.vision-ignored.name").ToString();
                try
                {
                    ISharedTileMarkerApi sharedApi = Helper.ModRegistry.GetApi<ISharedTileMarkerApi>(TileMarkerModId);
                    if (sharedApi != null)
                    {
                        sharedApi.RegisterCategoryWithSharedGroup(
                            ModManifest.UniqueID,
                            TileMarkerVisionCategory,
                            displayName,
                            TileMarkerSharedVisionGroup
                        );
                        tileMarkerApi = sharedApi;
                    }
                    else
                    {
                        tileMarkerApi = Helper.ModRegistry.GetApi<ITileMarkerApi>(TileMarkerModId);
                        tileMarkerApi?.RegisterCategory(ModManifest.UniqueID, TileMarkerVisionCategory, displayName);
                    }
                }
                catch
                {
                    // Tile Marker 1.0.x has no shared-group method, but its original individual
                    // category remains fully usable until the player updates the framework.
                    tileMarkerApi = Helper.ModRegistry.GetApi<ITileMarkerApi>(TileMarkerModId);
                    tileMarkerApi?.RegisterCategory(ModManifest.UniqueID, TileMarkerVisionCategory, displayName);
                }
            }
            catch (System.Exception ex)
            {
                tileMarkerApi = null;
                Monitor.Log($"[Tile Marker] Could not register the vision-ignored tile category: {ex}", LogLevel.Warn);
            }

        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (Config?.ModEnabled != true)
            {
                if (!modDisabledCleanupApplied)
                {
                    AbortActiveModState(releasePlayer: !Game1.eventUp);
                    modDisabledCleanupApplied = true;
                }

                return;
            }

            // Rearm the one-time shutdown cleanup after the mod is enabled again.
            modDisabledCleanupApplied = false;

            if (Game1.eventUp)
            {
                AbortActiveModState(releasePlayer: false);
                return;
            }

            // Freeze the entire mod while the game window isn't focused (player alt-tabbed,
            // switched to another app/browser, etc). Timers, kiss cycles, and NPC holds all
            // pause here and pick up exactly where they left off once focus returns — this
            // avoids the desync that made the player and NPC get stuck when the window
            // regained focus mid multi-kiss.
            bool isGameWindowActiveNow = Game1.game1 == null || Game1.game1.IsActive;

            if (!isGameWindowActiveNow)
            {
                wasGameWindowActiveLastTick = false;
                return;
            }

            if (!wasGameWindowActiveLastTick)
            {
                // Window just regained focus. The game's own movementPause counter keeps
                // ticking down even while unfocused (it's driven by Game1, not by this mod's
                // update loop), so an NPC that was being held during a multi-kiss may have
                // already "escaped" the hold without this mod's timers moving at all.
                // Re-apply the hold immediately so the NPC and player stay in sync.
                ReinforceHoldAfterFocusRegained();
            }

            wasGameWindowActiveLastTick = true;

            // The vanilla kiss checkAction sets Game1.freezeControls = true for the duration of
            // the kiss "cutscene", which is what actually locks every NPC's schedule/movement
            // across the whole valley — not shouldTimePass or CanMove. Since the mod's continuous
            // multi-kiss re-triggers that same vanilla kiss on every cycle, freezeControls stays
            // true back-to-back and the valley never gets a gap to un-pause. Forcing it back to
            // false here — only while our own continuous kiss is running — keeps the rest of the
            // world moving without touching anything else the game or another mod might freeze
            // controls for (menus, events, cutscenes, etc. are untouched since this only fires
            // during our own kiss sequence).
            if (!Game1.eventUp &&
                Game1.activeClickableMenu == null &&
                (continuousKissActive || continuousKissPendingRestart))
                Game1.freezeControls = false;

            // The bystander speech bubbles run on their own real-tick timer (TickCrowdReactionCooldowns),
            // independent of Game1.timeOfDay — so reverting the clock to its normal paused-during-kiss
            // behavior doesn't affect how or when those bubbles fade out and close.
            TickCrowdReactionCooldowns();

            if (kissBlockAfterDialogueTimer > 0)
                kissBlockAfterDialogueTimer--;

            bool dialogueOrMenuOpenNow = Game1.dialogueUp || Game1.activeClickableMenu != null;

            if (wasDialogueOrMenuOpenLastTick && !dialogueOrMenuOpenNow)
            {
                // Block bump kiss / multi-kiss for 1 second after a dialogue or menu closes.
                // Gives location-fixed dialogues time to rearm without the kiss re-opening them.
                kissBlockAfterDialogueTimer = 60; // 1 segundo
            }

            wasDialogueOrMenuOpenLastTick = dialogueOrMenuOpenNow;

            TickApproachKissBlockTimers();
            TickBumpKissCooldowns();

            if (OutsideBumpPause.Timer > 0)
                OutsideBumpPause.Timer--;

            if (continuousKissTouchHoldTimer > 0)
                continuousKissTouchHoldTimer--;

            if (dialogueCooldown > 0)
                dialogueCooldown--;

            if (noticeEmoteCooldown > 0)
                noticeEmoteCooldown--;

            if (cooldown > 0)
                cooldown--;

            if (kissDelayTimer > 0)
                kissDelayTimer--;

            if (kissDialogueTimer > 0)
                kissDialogueTimer--;

            if (kissCycleTimer > 0)
                kissCycleTimer--;

            if (kissRepeatTimer > 0)
                kissRepeatTimer--;

            if (kissProximityTimer > 0)
                kissProximityTimer--;

            // A menu pauses the multi-kiss in place. UpdateContinuousKissSystem also returns
            // while a menu is open, so these timers must not run ahead of that paused logic.
            if (Game1.activeClickableMenu == null)
            {
                if (continuousKissTimer > 0)
                    continuousKissTimer--;

                if (continuousKissGapTimer > 0)
                    continuousKissGapTimer--;
            }

            if (pendingNpcKissResetTimer > 0)
                pendingNpcKissResetTimer--;

            UpdateStardewSquadKissHold();

            didReactThisTick = false;

            UpdateKissSystems();
            RefreshBumpKissTouchStates();
        }

        private void OnWarped(object sender, WarpedEventArgs e)
        {
            if (!Context.IsWorldReady || e == null || !e.IsLocalPlayer)
                return;

            ResetTransientKissContext();

            lastLocation = e.NewLocation?.NameOrUniqueName ?? "";

            wasInNoticeZone = false;
            didReactThisTick = false;
            lastNoticeDistance = -1f;
        }

        private bool IsCurrentDelayedAction(int token)
        {
            return token == delayedActionContextToken && Context.IsWorldReady && Game1.player != null;
        }

        private void InvalidateDelayedActions()
        {
            delayedActionContextToken++;
        }

        private void ResetTransientKissContext()
        {
            ResetStardewSquadSupportState();
            RestoreBystandersBeforeContextReset();
            InvalidateDelayedActions();
            ClearPipeTextQueues();
            ResetOutsideBumpPause();

            ClearPendingPublicMultiKissShyEmote(releaseNpc: true);
            approachKissBlockTimerByNpc.Clear();
            bumpKissCooldownByNpc.Clear();
            bumpKissTouchingByNpc.Clear();
            approachKissDialogueLastTimeOfDay = -1;
            kissBlockAfterDialogueTimer = 0;
            wasDialogueOrMenuOpenLastTick = false;

            ClearActiveBystanderSnapshots();
            bystanderRestore.IsPending = false;
            bystanderRestore.CountdownStarted = false;
            bystanderRestore.Timer = 0;
            bystanderRestore.SafetyTimer = 0;
            bystanderRestore.Partner = null;
            bystanderRestore.ForceStart = false;

            ClearDirectRomanticKissVisual(directAutoKissVisualNpc, holdForNextCycle: false);
            TryRestoreNpcPreKissSpecialAction(clearAfterRestore: true);
            ResetKissState();
            ResetContinuousKissState();
            ResetPostKissState();
            ClearNpcPreKissSpecialAction();
        }

        private void AbortActiveModState(bool releasePlayer)
        {
            if (releasePlayer && Game1.player != null)
                ReleasePlayerAfterKissWithoutOverridingCurrentPose();

            NPC heldNpc = continuousKissNpc ?? kissPostSequenceNpc ?? pendingKissNpc ?? OutsideBumpPause.Npc;
            if (heldNpc != null && heldNpc.currentLocation != null)
                heldNpc.movementPause = 0;

            ResetTransientKissContext();
        }
        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (this.Config != null && !this.Config.ModEnabled)
                return;

            if (TryHandleMultiKissHotkey(e))
                return;

            SButton manualKissButton = Config.ManualKissButtonPreference == KissClickPreference.Left
                ? SButton.MouseLeft
                : SButton.MouseRight;
            bool controllerPlayerKissButton = Context.IsSplitScreen
                && e.Button.IsActionButton()
                && e.Button.TryGetController(out _);
            if ((e.Button == manualKissButton || controllerPlayerKissButton)
                && TryHandlePlayerSpouseKissClick(e, allowFrontTileTarget: controllerPlayerKissButton))
                return;

            if (e.Button == manualKissButton && TryHandleManualKissClick(e))
                return;

            if (!e.Button.IsActionButton())
                return;

            NPC partner = GetPartner();
            if (partner == null)
                return;

            if (!Game1.player.canMove)
                return;

            float distance = DistanceToPlayer(partner);
            if (distance > 120f)
                return;

            talkedToPartnerToday = true;
        }

        private bool TryHandleManualKissClick(ButtonPressedEventArgs e)
        {
            bool startFullMultiKiss = Config.MultiKissEnabled
                && Config.ManualKissStartsMultiKiss
                && !IsMultiKissHotkeyConfigured();
            bool useOneRandomTier = Config.RandomManualKissTier
                && (!Config.MultiKissEnabled || IsMultiKissHotkeyConfigured());
            if (!startFullMultiKiss && !useOneRandomTier)
                return false;

            if (Game1.player == null || Game1.currentLocation == null || Game1.eventUp
                || Game1.dialogueUp || Game1.activeClickableMenu != null
                || !Game1.player.canMove || Game1.player.IsSitting()
                || Game1.player.ActiveObject != null)
            {
                return false;
            }

            Vector2 cursorPixels = e.Cursor.AbsolutePixels;
            NPC clickedPartner = null;
            float nearestDistance = float.MaxValue;

            foreach (NPC npc in Game1.currentLocation.characters)
            {
                if (npc == null || !IsSupportedRomanticPartner(npc.Name))
                    continue;

                float distance = DistanceToPlayer(npc);
                if (distance > 120f || distance >= nearestDistance)
                    continue;

                Rectangle clickArea = npc.GetBoundingBox();
                clickArea.Inflate(24, 40);
                if (!clickArea.Contains((int)cursorPixels.X, (int)cursorPixels.Y))
                    continue;

                clickedPartner = npc;
                nearestDistance = distance;
            }

            // Match vanilla interaction behavior when the cursor isn't directly over the NPC:
            // use the tile immediately in front of the player as the intended action target.
            if (clickedPartner == null)
            {
                Point interactionTile = Game1.player.TilePoint;
                switch (Game1.player.FacingDirection)
                {
                    case 0:
                        interactionTile.Y--;
                        break;
                    case 1:
                        interactionTile.X++;
                        break;
                    case 2:
                        interactionTile.Y++;
                        break;
                    case 3:
                        interactionTile.X--;
                        break;
                }

                Rectangle interactionArea = new Rectangle(
                    interactionTile.X * Game1.tileSize,
                    interactionTile.Y * Game1.tileSize,
                    Game1.tileSize,
                    Game1.tileSize
                );
                interactionArea.Inflate(16, 16);

                foreach (NPC npc in Game1.currentLocation.characters)
                {
                    if (npc == null || !IsSupportedRomanticPartner(npc.Name))
                        continue;

                    float distance = DistanceToPlayer(npc);
                    if (distance > 120f || distance >= nearestDistance || !npc.GetBoundingBox().Intersects(interactionArea))
                        continue;

                    clickedPartner = npc;
                    nearestDistance = distance;
                }
            }

            if (clickedPartner == null)
                return false;

            // This press belongs to the configured kiss action. Don't let vanilla also
            // open dialogue, give a gift, or start a second interaction on the same press.
            Helper.Input.Suppress(e.Button);
            talkedToPartnerToday = true;

            if (continuousKissActive || continuousKissPendingRestart || kissSequenceActive)
                return true;

            if (kissPostSequenceActive)
                ResetPostKissState();

            int tier;
            if (useOneRandomTier)
            {
                int manualTierRoll = random.Next(100);
                tier = manualTierRoll < 50 ? 1 : manualTierRoll < 80 ? 2 : 3;
            }
            else
            {
                tier = RollContinuousKissTier();
            }
            bool started = StartContinuousKiss(clickedPartner, tier, isNewSequence: true, manualRightClick: true);

            if (started && useOneRandomTier)
            {
                continuousKissSingleCycle = true;
                continuousKissSingleCycleFinishing = false;
            }

            Monitor.Log(
                $"[MANUAL KISS] {e.Button} on {clickedPartner.Name}: tier={tier}, mode={(useOneRandomTier ? "single-random" : "start-multi")}, started={started}.",
                LogLevel.Trace
            );

            return true;
        }
    }
}
