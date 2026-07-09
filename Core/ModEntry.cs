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
        }

        // =====================================================================
        // UTILITY HELPERS
        // =====================================================================

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
