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

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !e.IsMultipleOf(1))
                return;

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