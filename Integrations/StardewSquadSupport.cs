using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace LotsOfKisses
{
    /// <summary>
    /// Optional compatibility with The Stardew Squad.
    ///
    /// The Squad already pauses recruited spouses for vanilla's one-second kiss. Lots of Kisses
    /// can keep a kiss pose for longer tiers, so this integration extends that existing cooldown
    /// only while our kiss owns the NPC. Tasks, paths, schedules, and controllers are never changed.
    /// </summary>
    public partial class ModEntry
    {
        private const string StardewSquadModId = "ThaliaFawnheart.TheStardewSquad";
        private const string StardewSquadRecruiterIdKey = "TheStardewSquad/RecruiterId";

        private bool stardewSquadIntegrationReady;
        private NPC stardewSquadKissHoldNpc;
        private int stardewSquadKissHoldTicks;
        private readonly HashSet<string> stardewSquadNpcsWithActiveTasks = new(StringComparer.Ordinal);

        private void InitializeStardewSquadSupport()
        {
            if (!Helper.ModRegistry.IsLoaded(StardewSquadModId))
                return;

            try
            {
                Type followerManagerType = AccessTools.TypeByName("TheStardewSquad.Framework.FollowerManager");
                MethodInfo updateSquadMember = AccessTools.Method(followerManagerType, "UpdateSquadMember");

                if (updateSquadMember == null)
                    throw new MissingMethodException("TheStardewSquad.Framework.FollowerManager", "UpdateSquadMember");

                var harmony = new Harmony($"{ModManifest.UniqueID}.StardewSquadSupport");
                harmony.Patch(
                    original: updateSquadMember,
                    prefix: new HarmonyMethod(
                        typeof(StardewSquadFollowerUpdatePatch),
                        nameof(StardewSquadFollowerUpdatePatch.Prefix)
                    )
                );

                // Waiting followers are halted every tick through a separate path. Skip only
                // that halt while the recruited NPC is actively owned by a kiss.
                MethodInfo updateWaitingNpc = AccessTools.Method(followerManagerType, "UpdateWaitingNpc");
                if (updateWaitingNpc != null)
                {
                    harmony.Patch(
                        original: updateWaitingNpc,
                        prefix: new HarmonyMethod(
                            typeof(StardewSquadWaitingNpcUpdatePatch),
                            nameof(StardewSquadWaitingNpcUpdatePatch.Prefix)
                        )
                    );
                }

                // Fishing and sitting frames are applied before the normal follower update and
                // don't honor ActionCooldown, so guard that visual-only method separately.
                Type spriteManagerType = AccessTools.TypeByName("TheStardewSquad.Framework.NpcConfig.SpriteManager");
                MethodInfo forceTaskAnimation = AccessTools.Method(spriteManagerType, "ForceApplyTaskAnimation");
                if (forceTaskAnimation != null)
                {
                    harmony.Patch(
                        original: forceTaskAnimation,
                        prefix: new HarmonyMethod(
                            typeof(StardewSquadTaskAnimationPatch),
                            nameof(StardewSquadTaskAnimationPatch.Prefix)
                        )
                    );
                }

                stardewSquadIntegrationReady = true;
                Monitor.Log("[Stardew Squad] Compatibility enabled for recruited romantic partners.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                stardewSquadIntegrationReady = false;
                Monitor.Log(
                    $"[Stardew Squad] Could not enable optional compatibility. Lots of Kisses will continue normally: {ex}",
                    LogLevel.Warn
                );
            }
        }

        private bool IsStardewSquadRecruited(NPC npc)
        {
            return stardewSquadIntegrationReady
                && npc?.modData != null
                && npc.modData.ContainsKey(StardewSquadRecruiterIdKey);
        }

        private void BeginStardewSquadKissHold(NPC npc, int durationMs)
        {
            if (!IsStardewSquadRecruited(npc))
                return;

            stardewSquadKissHoldNpc = npc;
            stardewSquadKissHoldTicks = Math.Max(stardewSquadKissHoldTicks, MsToTicks(durationMs) + 2);
        }

        private void UpdateStardewSquadKissHold()
        {
            if (stardewSquadKissHoldNpc == null)
                return;

            if (!Context.IsWorldReady
                || Game1.player == null
                || stardewSquadKissHoldNpc.currentLocation != Game1.player.currentLocation)
            {
                ResetStardewSquadKissHold();
                return;
            }

            // Keep this timer aligned with the kiss timers: menus and alt-tab already pause
            // OnUpdateTicked before this method is reached.
            if (Game1.activeClickableMenu == null && stardewSquadKissHoldTicks > 0)
                stardewSquadKissHoldTicks--;

            if (stardewSquadKissHoldTicks <= 0 && !IsStardewSquadNpcActivelyHeld(stardewSquadKissHoldNpc))
                ResetStardewSquadKissHold();
        }

        private void ResetStardewSquadKissHold()
        {
            stardewSquadKissHoldNpc = null;
            stardewSquadKissHoldTicks = 0;
        }

        private void ResetStardewSquadSupportState()
        {
            ResetStardewSquadKissHold();
            stardewSquadNpcsWithActiveTasks.Clear();
        }

        internal void UpdateStardewSquadTaskState(NPC npc, bool hasTask)
        {
            string key = GetStardewSquadNpcStateKey(npc);
            if (key == null)
                return;

            if (hasTask)
                stardewSquadNpcsWithActiveTasks.Add(key);
            else
                stardewSquadNpcsWithActiveTasks.Remove(key);
        }

        private bool ShouldBlockKissDuringStardewSquadTask(NPC npc)
        {
            if (Config?.AllowKissesDuringStardewSquadTasks != false || !IsStardewSquadRecruited(npc))
                return false;

            string key = GetStardewSquadNpcStateKey(npc);
            return key != null && stardewSquadNpcsWithActiveTasks.Contains(key);
        }

        private static string GetStardewSquadNpcStateKey(NPC npc)
        {
            if (npc?.modData == null
                || !npc.modData.TryGetValue(StardewSquadRecruiterIdKey, out string recruiterId))
            {
                return null;
            }

            return $"{recruiterId}\u001f{npc.Name}";
        }

        internal void DisableStardewSquadSupportAfterRuntimeError(Exception ex)
        {
            if (!stardewSquadIntegrationReady)
                return;

            stardewSquadIntegrationReady = false;
            ResetStardewSquadSupportState();
            Monitor.Log(
                $"[Stardew Squad] Optional compatibility was disabled after a runtime reflection error. Lots of Kisses will continue normally: {ex}",
                LogLevel.Warn
            );
        }

        private bool IsStardewSquadNpcActivelyHeld(NPC npc)
        {
            if (!IsSameStardewSquadNpc(stardewSquadKissHoldNpc, npc))
                return false;

            if (stardewSquadKissHoldTicks > 0)
                return true;

            if (IsSameStardewSquadNpc(directAutoKissVisualNpc, npc))
                return true;

            if (IsSameStardewSquadNpc(continuousKissNpc, npc)
                && (continuousKissActive || continuousKissPendingRestart || continuousKissSingleCycleFinishing))
            {
                return true;
            }

            if (pendingPublicMultiKissShyEmote && IsSameStardewSquadNpc(pendingPublicMultiKissShyNpc, npc))
                return true;

            if (OutsideBumpPause.IsActive && IsSameStardewSquadNpc(OutsideBumpPause.Npc, npc))
                return true;

            return false;
        }

        internal int GetStardewSquadRequiredKissCooldown(NPC npc)
        {
            if (!IsStardewSquadRecruited(npc) || !IsStardewSquadNpcActivelyHeld(npc))
                return 0;

            // Two ticks are enough for indefinite states (restart gap, shy emote, outdoor
            // pause). The prefix refreshes this every update; once ownership ends, the Squad's
            // own cooldown reaches zero naturally and resumes on its next tick.
            return Math.Max(2, stardewSquadKissHoldTicks + 1);
        }

        internal bool ShouldSuppressStardewSquadTaskAnimation(NPC npc)
        {
            return GetStardewSquadRequiredKissCooldown(npc) > 0;
        }

        private static bool IsSameStardewSquadNpc(NPC expected, NPC candidate)
        {
            if (expected == null || candidate == null)
                return false;

            if (ReferenceEquals(expected, candidate))
                return true;

            if (!string.Equals(expected.Name, candidate.Name, StringComparison.Ordinal))
                return false;

            bool expectedHasRecruiter = expected.modData.TryGetValue(StardewSquadRecruiterIdKey, out string expectedRecruiter);
            bool candidateHasRecruiter = candidate.modData.TryGetValue(StardewSquadRecruiterIdKey, out string candidateRecruiter);

            return expectedHasRecruiter
                && candidateHasRecruiter
                && string.Equals(expectedRecruiter, candidateRecruiter, StringComparison.Ordinal);
        }
    }

    internal static class StardewSquadFollowerUpdatePatch
    {
        internal static void Prefix(object mate)
        {
            try
            {
                if (!StardewSquadReflection.TryGetMateState(mate, out NPC npc, out PropertyInfo cooldownProperty, out bool hasTask))
                    return;

                ModEntry.Instance?.UpdateStardewSquadTaskState(npc, hasTask);

                int requiredCooldown = ModEntry.Instance?.GetStardewSquadRequiredKissCooldown(npc) ?? 0;
                if (requiredCooldown <= 0)
                    return;

                // Set the Squad's own cooldown to the exact remaining hold instead of stacking a
                // second movement system. Its normal update will decrement and return before it can
                // path, teleport, or replace the kiss sprite. No controller/task/path is touched.
                cooldownProperty.SetValue(mate, requiredCooldown);
            }
            catch (Exception ex)
            {
                ModEntry.Instance?.DisableStardewSquadSupportAfterRuntimeError(ex);
            }
        }
    }

    internal static class StardewSquadWaitingNpcUpdatePatch
    {
        internal static bool Prefix(object mate)
        {
            try
            {
                if (!StardewSquadReflection.TryGetMateNpc(mate, out NPC npc))
                    return true;

                return ModEntry.Instance?.GetStardewSquadRequiredKissCooldown(npc) <= 0;
            }
            catch (Exception ex)
            {
                ModEntry.Instance?.DisableStardewSquadSupportAfterRuntimeError(ex);
                return true;
            }
        }
    }

    internal static class StardewSquadTaskAnimationPatch
    {
        internal static bool Prefix(NPC npc)
        {
            return ModEntry.Instance?.ShouldSuppressStardewSquadTaskAnimation(npc) != true;
        }
    }

    internal static class StardewSquadReflection
    {
        private static Type cachedMateType;
        private static PropertyInfo cachedNpcProperty;
        private static PropertyInfo cachedCooldownProperty;
        private static PropertyInfo cachedTaskProperty;

        private static void CacheMateProperties(Type mateType)
        {
            if (cachedMateType == mateType)
                return;

            cachedMateType = mateType;
            cachedNpcProperty = AccessTools.Property(mateType, "Npc");
            cachedCooldownProperty = AccessTools.Property(mateType, "ActionCooldown");
            cachedTaskProperty = AccessTools.Property(mateType, "Task");
        }

        internal static bool TryGetMateNpc(object mate, out NPC npc)
        {
            npc = null;
            if (mate == null)
                return false;

            CacheMateProperties(mate.GetType());
            npc = cachedNpcProperty?.GetValue(mate) as NPC;
            return npc != null;
        }

        internal static bool TryGetMateState(object mate, out NPC npc, out PropertyInfo cooldownProperty, out bool hasTask)
        {
            cooldownProperty = null;
            hasTask = false;
            if (!TryGetMateNpc(mate, out npc))
                return false;

            cooldownProperty = cachedCooldownProperty;
            hasTask = cachedTaskProperty?.GetValue(mate) != null;
            return cooldownProperty?.CanWrite == true;
        }
    }
}
