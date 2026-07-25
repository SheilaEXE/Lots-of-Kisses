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
using xTile.Dimensions;

namespace LotsOfKisses
{
    public partial class ModEntry : Mod
    {
        public static ModEntry Instance;
        public ModConfig Config;
        private Random random = new Random();
        private INpcPassingGreetingsApi? passingGreetingsApi;
        private ITileMarkerApi? tileMarkerApi;
        private ContentPackLoader contentPackLoader;
        // =====================================================================
        // ENTRY
        // =====================================================================

        public override void Entry(IModHelper helper)
        {
            Instance = this;
            Config = helper.ReadConfig<ModConfig>();
            contentPackLoader = new ContentPackLoader(helper, Monitor);

            InitBlushSmokeEffect();
            InitCustomSounds();

            var harmony = new Harmony(this.ModManifest.UniqueID);

            harmony.Patch(
                original: AccessTools.Method(typeof(NPC), nameof(NPC.checkSchedule)),
                prefix: new HarmonyMethod(typeof(NPC_CheckSchedule_ContinuousKissHold_Patch), nameof(NPC_CheckSchedule_ContinuousKissHold_Patch.CheckSchedule_Prefix))
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

            helper.ConsoleCommands.Add(
                "lok_debug_state",
                "Print the current Lots of Kisses diagnostic state. Enable debug logging in GMCM first.",
                OnDebugStateCommand
            );

            InitializePlayerSpouseKissSupport(helper);
        }

        // =====================================================================
        // UTILITY HELPERS
        // =====================================================================

        private void ForceScheduleCheckNow(NPC partner)
        {
            if (partner == null || partner.currentLocation == null)
                return;

            try
            {
                DebugLog("SCHEDULE", () => $"Requesting vanilla checkSchedule at {Game1.timeOfDay}: {DescribeNpcDebugState(partner)}.");
                allowForcedScheduleCheck = true;
                partner.checkSchedule(Game1.timeOfDay);
                DebugLog("SCHEDULE", () => $"Vanilla checkSchedule completed: {DescribeNpcDebugState(partner)}.");
            }
            finally
            {
                allowForcedScheduleCheck = false;
            }
        }

    } //👈 FINAL DO MOD ENTRY

} //👈 FINAL DO NAMESPACE
