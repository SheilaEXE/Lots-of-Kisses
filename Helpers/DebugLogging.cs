using StardewModdingAPI;
using StardewValley;
using System;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        private int nextDebugSnapshotId = 1;

        internal bool IsDebugLoggingEnabled => Config?.EnableDebugLogging == true;

        /// <summary>Writes an opt-in diagnostic entry. Use the factory overload for state-heavy messages.</summary>
        internal void DebugLog(string category, string message)
        {
            if (!IsDebugLoggingEnabled)
                return;

            Monitor.Log($"[DEBUG][{category}] {message}", LogLevel.Info);
        }

        /// <summary>Lazily builds diagnostic text, avoiding reflection/string work when debug is disabled.</summary>
        internal void DebugLog(string category, Func<string> messageFactory)
        {
            if (!IsDebugLoggingEnabled || messageFactory == null)
                return;

            DebugLog(category, messageFactory());
        }

        private int GetNextDebugSnapshotId()
        {
            int id = nextDebugSnapshotId++;
            if (nextDebugSnapshotId <= 0)
                nextDebugSnapshotId = 1;
            return id;
        }

        internal void LogDebugSessionHeader(string reason)
        {
            DebugLog("SESSION", () =>
                $"Diagnostic session ({reason}): mod={ModManifest.Version}, game={Game1.version}, SMAPI={Constants.ApiVersion}, " +
                $"OS={Environment.OSVersion.Platform}, multiplayer={Context.IsMultiplayer}, splitScreen={Context.IsSplitScreen}, screen={Context.ScreenId}, " +
                $"GMCM={Helper.ModRegistry.IsLoaded("spacechase0.GenericModConfigMenu")}, TileMarker={Helper.ModRegistry.IsLoaded("NatrollEXE.TileMarker")}, " +
                $"StardewSquad={Helper.ModRegistry.IsLoaded(StardewSquadModId)}."
            );
        }

        private string DescribeNpcDebugState(NPC npc)
        {
            if (npc == null)
                return "npc=null";

            string location = npc.currentLocation?.NameOrUniqueName ?? "null";
            string tile = $"{npc.TilePoint.X},{npc.TilePoint.Y}";
            int frame = npc.Sprite?.CurrentFrame ?? -1;
            int animationFrames = npc.Sprite?.CurrentAnimation?.Count ?? 0;
            return $"npc={npc.Name}, location={location}, tile={tile}, facing={npc.FacingDirection}, frame={frame}, animationFrames={animationFrames}, moving={npc.isMoving()}, controller={(npc.controller != null)}, movementPause={(int)npc.movementPause}";
        }

        private string DescribeFarmerDebugState(Farmer farmer)
        {
            if (farmer == null)
                return "farmer=null";

            return $"farmer={farmer.Name}, id={farmer.UniqueMultiplayerID}, location={farmer.currentLocation?.NameOrUniqueName ?? "null"}, " +
                $"tile={farmer.TilePoint.X},{farmer.TilePoint.Y}, moving={farmer.isMoving()}, canMove={farmer.CanMove}, sitting={farmer.IsSitting()}, " +
                $"usingTool={farmer.UsingTool}, horse={farmer.isRidingHorse()}, emoting={farmer.IsEmoting}, local={farmer.IsLocalPlayer}";
        }

        private string DescribePlayerKissDebugState(PlayerKissState state)
        {
            if (state == null)
                return "state=null";

            return $"sequence={state.SequenceId ?? "none"}, mode={state.Mode}, tier={state.Tier}, cycle={state.CycleNumber}, " +
                $"authority={state.IsAuthority}, active={state.HasActiveSequence}, cycleActive={state.IsCycleActive}, " +
                $"initiator={state.InitiatorId}, target={state.TargetId}, outgoing={state.HasOutgoingRequest}, cooldown={state.CooldownTicksRemaining}";
        }

        private void OnDebugStateCommand(string command, string[] args)
        {
            if (!IsDebugLoggingEnabled)
            {
                Monitor.Log("Enable 'Debug logging' in GMCM before using lok_debug_state.", LogLevel.Info);
                return;
            }

            DebugLog("STATE", () =>
                $"time={Game1.timeOfDay}, screen={Context.ScreenId}, player={Game1.player?.UniqueMultiplayerID.ToString() ?? "null"}, " +
                $"location={Game1.currentLocation?.NameOrUniqueName ?? "null"}, continuousActive={continuousKissActive}, " +
                $"continuousPending={continuousKissPendingRestart}, continuousNpc={continuousKissNpc?.Name ?? "null"}, " +
                $"cycles={continuousKissCyclesDone}, tier={continuousKissTier}, kissSequence={kissSequenceActive}, " +
                $"postSequence={kissPostSequenceActive}, specialSnapshot={(preKissSpecialActionSnapshot != null)}, " +
                $"bystanderSnapshots={activeBystanderSnapshots.Count}, bystanderRestorePending={bystanderRestore.IsPending}, " +
                $"outsideBumpPause={OutsideBumpPause.IsActive}, hotkeyMoveAwayWait={hotkeyStoppedMultiKissAwaitingMoveAway}, " +
                $"postMultiKissLook={postMultiKissLookActive}, postMultiKissNpc={postMultiKissLookNpc?.Name ?? "null"}, " +
                $"postMultiKissRouteResume={postMultiKissLookResumeRouteNaturally}, sequenceStartedWithController={continuousKissNpcHadControllerAtSequenceStart}, " +
                $"publicDialoguePending={pendingPublicMultiKissDialogue}, publicShyEmotePending={pendingPublicMultiKissShyEmote}, " +
                $"crowdBubbleActive={HasActiveCrowdReactionSpeechBubble()}, passiveLookSnapshot={passiveLookRestoreActive}, " +
                $"squadReady={stardewSquadIntegrationReady}, squadHoldNpc={stardewSquadKissHoldNpc?.Name ?? "null"}, " +
                $"squadHoldTicks={stardewSquadKissHoldTicks}, squadTasks={stardewSquadNpcsWithActiveTasks.Count}, delayedActionToken={delayedActionContextToken}."
            );

            DebugLog("STATE", () =>
                $"Config: mod={Config.ModEnabled}, multi={Config.MultiKissEnabled}, hotkey={Config.MultiKissToggleKey}, " +
                $"manualStartsMulti={Config.ManualKissStartsMultiKiss}, randomManualTier={Config.RandomManualKissTier}, " +
                $"bump={Config.BumpKissEnabled}, acceptPlayerSpouse={Config.AcceptPlayerSpouseKisses}, " +
                $"allowSquadTaskKisses={Config.AllowKissesDuringStardewSquadTasks}, polyamory={Config.PolyamorySupport}."
            );

            PlayerKissState playerState = playerKissState.Value;
            DebugLog("STATE", () => $"Player-spouse kiss: {DescribePlayerKissDebugState(playerState)}; activeParticipantMappings={activePlayerKissSequenceByParticipant.Count}, queuedSplitCycles={pendingLocalSplitKissCycles.Count}, queuedSplitStops={pendingLocalSplitKissStops.Count}.");

            if (hotkeyStoppedMultiKissAwaitingMoveAway)
                DebugLog("STATE", $"Hotkey wait: npc={hotkeyStoppedMultiKissNpc?.Name ?? "null"}, initialDistance={hotkeyStoppedMultiKissInitialDistance:0}, lastReason={hotkeyStoppedMultiKissLastDebugWaitReason ?? "none"}.");

            if (passiveLookRestoreActive)
                DebugLog("STATE", $"Passive-look snapshot #{passiveLookDebugSnapshotId}: npc={passiveLookRestoreNpcName}, location={passiveLookRestoreLocationName}, tile={passiveLookRestoreTile.X},{passiveLookRestoreTile.Y}, facing={passiveLookRestoreFacing}, frame={passiveLookRestoreFrame}, restoredLogged={passiveLookRestoreLogged}.");

            if (preKissSpecialActionSnapshot?.Npc != null)
                DebugLog("STATE", () => $"Special snapshot #{preKissSpecialActionSnapshot.DebugId}: {DescribeNpcDebugState(preKissSpecialActionSnapshot.Npc)}.");

            foreach (BystanderSnapshot snapshot in activeBystanderSnapshots)
            {
                if (snapshot?.Npc != null)
                    DebugLog("STATE", () => $"Bystander snapshot #{snapshot.DebugId}: {DescribeNpcDebugState(snapshot.Npc)}.");
            }
        }
    }
}
