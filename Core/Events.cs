using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            lastDayChecked = Game1.dayOfMonth;
            talkedToSpouseToday = false;
            didReactThisTick = false;
            wasInNoticeZone = false;
            lastNoticeDistance = -1f;
            outsideBumpPauseActive = false;
            outsideBumpPauseNpc = null;
            outsideBumpPauseTimer = 0;
            outsideBumpPauseToken++;
            pendingPublicMultiKissShyEmote = false;
            pendingPublicMultiKissShyNpc = null;
            pendingPublicMultiKissShyEmoteTimer = 0;
            approachKissBlockTimer = 0;
            approachKissDialogueLastTimeOfDay = -1;
            kissBlockAfterDialogueTimer = 0;
            wasDialogueOrMenuOpenLastTick = false;

            ClearActiveBystanderSnapshots();
            bystanderRestorePending = false;
            bystanderRestoreCountdownStarted = false;
            bystanderRestoreTimer = 0;
            bystanderRestoreSafetyTimer = 0;
            bystanderRestorePartner = null;

            ResetKissState();
            ResetContinuousKissPlayerLeanEffect(false);
            ResetContinuousKissState();
            ResetPostKissState();

            contentPackLoader.Load();
            if (Game1.player?.spouse != null)
            {
                if (IsOfficialSpouse(Game1.player.spouse))
                    this.Monitor.Log($"[PARTNER DETECTION] Official spouse detected: {Game1.player.spouse}. Compatible with mod.", LogLevel.Info);
                else
                    this.Monitor.Log($"[PARTNER DETECTION] Official spouse detected: {Game1.player.spouse}. Not compatible with mod at this time.", LogLevel.Warn);
            }
            else
            {
                this.Monitor.Log("[PARTNER DETECTION] No official spouse found.", LogLevel.Trace);
            }

            // Polyamory spouses
            if (this.Config?.PolyamorySupport == true)
            {
                this.Monitor.Log("[PARTNER DETECTION] Polyamory compatibility enabled. Scanning friendship data for extra married NPCs...", LogLevel.Info);

                if (Game1.player?.friendshipData != null)
                {
                    var polySpouses = new System.Collections.Generic.List<string>();
                    foreach (string npcName in Game1.player.friendshipData.Keys)
                    {
                        if (IsPolyamorySpouse(npcName))
                            polySpouses.Add(npcName);
                    }

                    if (polySpouses.Count > 0)
                        this.Monitor.Log($"[PARTNER DETECTION] Polyamory spouses found: {string.Join(", ", polySpouses)}.", LogLevel.Info);
                    else
                        this.Monitor.Log("[PARTNER DETECTION] No polyamory spouses found in friendship data.", LogLevel.Info);
                }
            }

            // Dating partners
            if (this.Config?.PolyamorySupport == true)
            {
                this.Monitor.Log("[PARTNER DETECTION] Dating support enabled. Scanning friendship data for dating/engaged NPCs...", LogLevel.Info);

                if (Game1.player?.friendshipData != null)
                {
                    var datingPartners = new System.Collections.Generic.List<string>();
                    foreach (string npcName in Game1.player.friendshipData.Keys)
                    {
                        if (IsDatingPartner(npcName))
                            datingPartners.Add(npcName);
                    }

                    if (datingPartners.Count > 0)
                        this.Monitor.Log($"[PARTNER DETECTION] Dating partners found: {string.Join(", ", datingPartners)}.", LogLevel.Info);
                    else
                        this.Monitor.Log("[PARTNER DETECTION] No dating or engaged partners found in friendship data.", LogLevel.Info);
                }
            }
        }
        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            lastDayChecked = Game1.dayOfMonth;
            talkedToSpouseToday = false;

            lastNoticeDistance = -1f;
            outsideBumpPauseActive = false;
            outsideBumpPauseNpc = null;
            outsideBumpPauseTimer = 0;
            outsideBumpPauseToken++;
            pendingPublicMultiKissShyEmote = false;
            pendingPublicMultiKissShyNpc = null;
            pendingPublicMultiKissShyEmoteTimer = 0;
            approachKissBlockTimer = 0;
            approachKissDialogueLastTimeOfDay = -1;
            kissBlockAfterDialogueTimer = 0;
            wasDialogueOrMenuOpenLastTick = false;

            ClearActiveBystanderSnapshots();
            bystanderRestorePending = false;
            bystanderRestoreCountdownStarted = false;
            bystanderRestoreTimer = 0;
            bystanderRestoreSafetyTimer = 0;
            bystanderRestorePartner = null;

            ResetKissState();
            ResetContinuousKissState();
            ResetPostKissState();

        }
        private void OnTimeChanged(object sender, TimeChangedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (Game1.dayOfMonth != lastDayChecked)
            {
                lastDayChecked = Game1.dayOfMonth;
                talkedToSpouseToday = false;
    
                ResetKissState();
                ResetContinuousKissState();
                ResetPostKissState();
        
            }

        }
        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            lastDayChecked = -1;
            lastLocation = "";
            cooldown = 0;
            didReactThisTick = false;
            dialogueCooldown = 0;
            noticeEmoteCooldown = 0;
            lastNoticeDistance = -1f;

            outsideBumpPauseActive = false;
            outsideBumpPauseNpc = null;
            outsideBumpPauseTimer = 0;
            outsideBumpPauseToken++;
            pendingPublicMultiKissShyEmote = false;
            pendingPublicMultiKissShyNpc = null;
            pendingPublicMultiKissShyEmoteTimer = 0;
            approachKissBlockTimer = 0;
            approachKissDialogueLastTimeOfDay = -1;
            kissBlockAfterDialogueTimer = 0;
            wasDialogueOrMenuOpenLastTick = false;

            wasInNoticeZone = false;
            talkedToSpouseToday = false;

            ClearActiveBystanderSnapshots();
            bystanderRestorePending = false;
            bystanderRestoreCountdownStarted = false;
            bystanderRestoreTimer = 0;
            bystanderRestoreSafetyTimer = 0;
            bystanderRestorePartner = null;



            ResetKissState();
            ResetContinuousKissState();
            ResetPostKissState();
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            try
            {
                var gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
                ModConfigMenu.Register(this, gmcm);
            }
            catch
            {
            }

            this.passingGreetingsApi =
            this.Helper.ModRegistry.GetApi<INpcPassingGreetingsApi>("NatrollEXE.NpcPassingGreetings");

            contentPackLoader.Load();
        }

        /// <summary>
        /// Re-applies the movementPause hold on the spouse and any active bystanders right after
        /// the game window regains focus. The game's own movementPause countdown keeps running
        /// even while the window is unfocused (it's driven by Game1's internal clock, not by this
        /// mod's update loop), so a hold that was supposed to last through a multi-kiss cycle may
        /// have already run out during the time the window was in the background. Without this,
        /// the NPC can end up "free" while the mod still believes the kiss/hold is active, causing
        /// the player and NPC to visually desync or lock up when focus returns.
        /// </summary>
        private void ReinforceHoldAfterFocusRegained()
        {
            if (continuousKissActive && continuousKissNpc != null)
                continuousKissNpc.movementPause = System.Math.Max(continuousKissNpc.movementPause, 60);

            NPC spouse = GetSpouse();
            if (spouse != null && (kissSequenceActive || kissPostSequenceActive))
                spouse.movementPause = System.Math.Max(spouse.movementPause, 60);

            foreach (var snapshot in activeBystanderSnapshots)
            {
                if (snapshot?.Npc != null)
                    snapshot.Npc.movementPause = System.Math.Max(snapshot.Npc.movementPause, 60);
            }
        }

        /// <summary>
        /// Real-time ticks (at 60 ticks/sec) equivalent to the game's default pace of
        /// 10 in-game minutes per ~7 real seconds. Matches vanilla speed so multi-kiss
        /// sequences don't make time pass faster or slower than normal play.
        /// </summary>
        private const int TicksPerTenGameMinutes = 420; // 7 seconds * 60 ticks/sec

        /// <summary>
        /// Advances Game1.timeOfDay by 10 in-game minutes once enough real-time ticks have
        /// accumulated, but only while the continuous kiss is active. This runs independently
        /// of Game1.shouldTimePass(), so it never affects any other time-pausing logic from the
        /// base game or other mods — it only compensates for the freeze this mod's own repeated
        /// kiss animations would otherwise cause.
        /// </summary>
        private void AdvanceClockDuringMultiKissIfNeeded()
        {
            // continuousKissActive briefly drops to false between cycles while
            // continuousKissPendingRestart is set (right before the next tier starts), so both
            // flags count as "multi-kiss in progress" here — otherwise the accumulator would
            // reset on every single cycle transition and never reach the threshold to advance
            // the clock, even during a long unbroken kissing session.
            if (!continuousKissActive && !continuousKissPendingRestart)
            {
                multiKissClockAccumulatorTicks = 0;
                return;
            }

            multiKissClockAccumulatorTicks++;

            if (multiKissClockAccumulatorTicks < TicksPerTenGameMinutes)
                return;

            multiKissClockAccumulatorTicks = 0;

            if (Game1.timeOfDay >= 2600)
                return; // Let the game's own forced pass-out logic handle end-of-day naturally.

            Game1.timeOfDay += 10;
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !e.IsMultipleOf(1))
                return;

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

            // Keep the in-game clock moving while the continuous kiss is active. Each cycle
            // re-triggers the vanilla kiss animation, which holds Game1.shouldTimePass() at
            // false for its duration — with cycles chaining back-to-back, the clock would
            // otherwise freeze for as long as the multi-kiss keeps going. This advances
            // Game1.timeOfDay directly on the same real-time cadence the game already uses
            // (about 7 seconds of real time per in-game 10 minutes), instead of touching
            // shouldTimePass or any other game/mod time-control logic.
            AdvanceClockDuringMultiKissIfNeeded();
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

            if (approachKissBlockTimer > 0)
                approachKissBlockTimer--;

            if (outsideBumpPauseTimer > 0)
                outsideBumpPauseTimer--;

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

            if (continuousKissTimer > 0)
                continuousKissTimer--;

            if (continuousKissGapTimer > 0)
                continuousKissGapTimer--;

            if (pendingNpcKissResetTimer > 0)
                pendingNpcKissResetTimer--;

            didReactThisTick = false;

            UpdateContinuousKissPlayerLeanEffect();
            UpdateDeferredNpcSpecialActionRestore();
            UpdateBystanderRestore();

            NPC spouse = GetSpouse();
            if (spouse == null)
                return;

            UpdateOutsideBumpPause(spouse);
            UpdatePendingNpcKissReset(spouse);

            // Cross-mod block: while an Outfit Reactions reaction is in progress (noticing, generating,
            // or dialogue open), do not start or run the automatic kiss systems. Any kiss already
            // mid-animation is allowed to finish naturally; only NEW automatic kisses are held off.
            // The flag lives in the Farmer's modData, so this needs no hard dependency or load order.
            if (IsOutfitReactionActive() && !continuousKissActive && !kissSequenceActive && !kissPostSequenceActive)
            {
                UpdatePostKissSystem(spouse);
                UpdateDailySpouseSystems(spouse);
                return;
            }

            UpdateKissSystem(spouse);
            UpdateContinuousKissSystem(spouse);
            UpdatePostKissSystem(spouse);
            UpdatePendingPublicMultiKissShyEmote();
            UpdateDailySpouseSystems(spouse);
        }

        private void OnWarped(object sender, WarpedEventArgs e)
        {
            if (!Context.IsWorldReady || e == null || !e.IsLocalPlayer)
                return;

            lastLocation = e.NewLocation?.NameOrUniqueName ?? "";

            ResetKissState();
            ResetContinuousKissPlayerLeanEffect(false);
            ResetContinuousKissState();
            ResetPostKissState();
            ClearNpcPreKissSpecialAction();

            wasInNoticeZone = false;
            didReactThisTick = false;
            lastNoticeDistance = -1f;

            ClearActiveBystanderSnapshots();
            bystanderRestorePending = false;
            bystanderRestoreCountdownStarted = false;
            bystanderRestoreTimer = 0;
            bystanderRestoreSafetyTimer = 0;
            bystanderRestorePartner = null;

            outsideBumpPauseActive = false;
            outsideBumpPauseNpc = null;
            outsideBumpPauseTimer = 0;
            outsideBumpPauseToken++;
            approachKissBlockTimer = 0;
            approachKissDialogueLastTimeOfDay = -1;
        }
        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (this.Config != null && !this.Config.ModEnabled)
                return;

            if (!e.Button.IsActionButton())
                return;

            NPC spouse = GetSpouse();
            if (spouse == null)
                return;

            if (!Game1.player.canMove)
                return;

            float distance = DistanceToPlayer(spouse);
            if (distance > 120f)
                return;

            talkedToSpouseToday = true;
        }
    }
}