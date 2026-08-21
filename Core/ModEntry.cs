using HarmonyLib;
using Microsoft.Xna.Framework;
using LotsOfKisses;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.GameData.Characters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
            KissClickPreference? legacyManualKissButton = ReadLegacyManualKissButton(helper);
            Config = ReadConfigSafely(helper);
            if (legacyManualKissButton.HasValue)
            {
                Config.ManualKissButton = new KeybindList(
                    legacyManualKissButton == KissClickPreference.Left
                        ? SButton.MouseLeft
                        : SButton.MouseRight
                );
                TryWriteConfig();
            }
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

        private static KissClickPreference? ReadLegacyManualKissButton(IModHelper helper)
        {
            try
            {
                string configPath = Path.Combine(helper.DirectoryPath, "config.json");
                if (!File.Exists(configPath))
                    return null;

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
                JsonElement root = document.RootElement;

                // A config already using the new binding always wins over the retired dropdown.
                if (root.TryGetProperty(nameof(ModConfig.ManualKissButton), out _))
                    return null;

                if (!root.TryGetProperty("ManualKissButtonPreference", out JsonElement legacyValue)
                    || legacyValue.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                return Enum.TryParse(legacyValue.GetString(), ignoreCase: true, out KissClickPreference parsed)
                    ? parsed
                    : null;
            }
            catch
            {
                // Let SMAPI's normal config reader report malformed files in its usual way.
                return null;
            }
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
