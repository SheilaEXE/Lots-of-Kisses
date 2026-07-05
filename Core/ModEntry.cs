using HarmonyLib;
using Microsoft.Xna.Framework;
using LotsOfKisses;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.GameData.Characters;
using System;
using System.Collections.Generic;
using System.Reflection;
using xTile.Dimensions;

namespace LotsOfKisses
{
    public partial class ModEntry : Mod
    {
        public static ModEntry Instance;
        public ModConfig Config;
        private Random random = new Random();
        private INpcPassingGreetingsApi? passingGreetingsApi;
        private ContentPackLoader contentPackLoader;

        // NPC names loaded from "Ignored Reactions.json" (mod root folder) — these NPCs can still
        // notice a kiss and turn to watch as bystanders normally, but never get a crowd emote or
        // a spoken reaction line. Meant for NPCs that are technically NPCs but read oddly with
        // emotes/dialogue (e.g. cats, Shane's chicken).
        private HashSet<string> ignoredReactionNpcNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private const string IgnoredReactionsFileName = "Ignored Reactions.json";

        private void LoadIgnoredReactionNpcNames()
        {
            ignoredReactionNpcNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                List<string> names = this.Helper.Data.ReadJsonFile<List<string>>(IgnoredReactionsFileName);

                if (names == null)
                {
                    // File doesn't exist yet (or is empty) — create it with a couple of examples
                    // commented out via a "_comment" style first entry isn't valid JSON, so we just
                    // ship an empty array plus a friendly example the user can uncomment/edit.
                    names = new List<string> { "Cat", "Chicken" };
                    this.Helper.Data.WriteJsonFile(IgnoredReactionsFileName, names);
                    this.Monitor.Log($"[Ignored Reactions] No {IgnoredReactionsFileName} found — created a starter one with example names. Edit it and use the NPC's exact name.", LogLevel.Info);
                }

                foreach (string rawName in names)
                {
                    if (string.IsNullOrWhiteSpace(rawName))
                        continue;

                    ignoredReactionNpcNames.Add(rawName.Trim());
                }

                this.Monitor.Log($"[Ignored Reactions] Loaded {ignoredReactionNpcNames.Count} NPC name(s): {string.Join(", ", ignoredReactionNpcNames)}", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                // Never break the mod over a malformed/unreadable file — just fall back to no exclusions.
                this.Monitor.Log($"[Ignored Reactions] Failed to read {IgnoredReactionsFileName}, ignoring the file this session: {ex.Message}", LogLevel.Warn);
                ignoredReactionNpcNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private bool IsReactionIgnoredForNpc(NPC npc)
        {
            return npc != null && ignoredReactionNpcNames.Contains(npc.Name);
        }
// ======================================================================
        // ENTRY
// ======================================================================

        public override void Entry(IModHelper helper)
        {
            Instance = this;
            Config = helper.ReadConfig<ModConfig>();
            contentPackLoader = new ContentPackLoader(helper, Monitor);
            LoadIgnoredReactionNpcNames();

            InitBlushSmokeEffect();
            InitCustomSounds();

            var harmony = new Harmony(this.ModManifest.UniqueID);

            harmony.Patch(
                original: AccessTools.Method(typeof(NPC), nameof(NPC.checkSchedule)),
                prefix: new HarmonyMethod(typeof(ModEntry), nameof(CheckSchedule_Prefix))
            );

            harmony.PatchAll();

            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;

            helper.Events.Player.Warped += OnWarped;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
        }

// ======================================================================
        // UTILITY HELPERS
// ======================================================================

        private void ForceScheduleCheckNow(NPC spouse)
        {
            if (spouse == null || spouse.currentLocation == null)
                return;

            try
            {
                allowForcedScheduleCheck = true;
                spouse.checkSchedule(Game1.timeOfDay);
            }
            finally
            {
                allowForcedScheduleCheck = false;
            }
        }

    } //👈 FINAL DO MOD ENTRY

} //👈 FINAL DO NAMESPACE
