using StardewModdingAPI;

namespace LotsOfKisses
{
    internal static class ModConfigMenu
    {
        private static string T(ModEntry mod, string key, string fallback)
        {
            var translation = mod.Helper.Translation.Get(key);
            return translation.HasValue() ? translation.ToString() : fallback;
        }

        public static void Register(ModEntry mod, IGenericModConfigMenuApi configMenu)
        {
            if (configMenu == null)
                return;

            configMenu.Register(
                mod: mod.ModManifest,
                reset: () => mod.Config = new ModConfig(),
                save: () => mod.Helper.WriteConfig(mod.Config)
            );

            configMenu.AddSectionTitle(
                mod.ModManifest,
                () => mod.Helper.Translation.Get("gmcm.section.features")
            );

            configMenu.AddBoolOption(
                mod: mod.ModManifest,
                name: () => mod.Helper.Translation.Get("gmcm.option.enable-mod.name"),
                tooltip: () => mod.Helper.Translation.Get("gmcm.option.enable-mod.tooltip"),
                getValue: () => mod.Config.ModEnabled,
                setValue: value => mod.Config.ModEnabled = value
            );

            configMenu.AddBoolOption(
                mod: mod.ModManifest,
                name: () => T(mod, "gmcm.option.enable-polyamory-support.name", "Enable polyamory support"),
                tooltip: () => T(mod, "gmcm.option.enable-polyamory-support.tooltip", "Treat all romantic NPCs as valid kiss partners for this mod, including NPCs from other mods."),
                getValue: () => mod.Config.PolyamorySupport,
                setValue: value => mod.Config.PolyamorySupport = value
            );

            configMenu.AddBoolOption(
                mod: mod.ModManifest,
                name: () => mod.Helper.Translation.Get("gmcm.option.multi-kisses.name"),
                tooltip: () => mod.Helper.Translation.Get("gmcm.option.multi-kisses.tooltip"),
                getValue: () => mod.Config.MultiKissEnabled,
                setValue: value => mod.Config.MultiKissEnabled = value
            );

            configMenu.AddBoolOption(
                mod: mod.ModManifest,
                name: () => T(mod, "gmcm.option.manual-kiss-starts-multi-kiss.name", "Manual kiss starts Multi-Kisses"),
                tooltip: () => T(mod, "gmcm.option.manual-kiss-starts-multi-kiss.tooltip", "When Multi-Kisses is enabled, clicking your partner with the preferred kiss button immediately starts the full sequence and replaces the usual proximity start."),
                getValue: () => mod.Config.ManualKissStartsMultiKiss,
                setValue: value =>
                {
                    mod.Config.ManualKissStartsMultiKiss = value;
                    if (value)
                        mod.Config.RandomManualKissTier = false;
                }
            );

            configMenu.AddBoolOption(
                mod: mod.ModManifest,
                name: () => T(mod, "gmcm.option.random-manual-kiss-tier.name", "Random tier for manual kisses"),
                tooltip: () => T(mod, "gmcm.option.random-manual-kiss-tier.tooltip", "When Multi-Kisses is disabled, clicking your partner with the preferred kiss button starts one random tier. Keep Multi-Kisses disabled while using this option."),
                getValue: () => mod.Config.RandomManualKissTier,
                setValue: value =>
                {
                    mod.Config.RandomManualKissTier = value;
                    if (value)
                        mod.Config.ManualKissStartsMultiKiss = false;
                }
            );

            configMenu.AddTextOption(
                mod: mod.ModManifest,
                name: () => T(mod, "gmcm.option.manual-kiss-button.name", "Kiss click preference"),
                tooltip: () => T(mod, "gmcm.option.manual-kiss-button.tooltip", "Choose which mouse button triggers the optional manual kiss features, leaving the other button available for normal dialogue."),
                getValue: () => mod.Config.ManualKissButtonPreference.ToString(),
                setValue: value =>
                {
                    if (System.Enum.TryParse<KissClickPreference>(value, out var parsed))
                        mod.Config.ManualKissButtonPreference = parsed;
                },
                allowedValues: new[] { "Right", "Left" },
                formatAllowedValue: value => T(mod, $"gmcm.option.manual-kiss-button.{value.ToLower()}", value)
            );

            configMenu.AddBoolOption(
                mod: mod.ModManifest,
                name: () => mod.Helper.Translation.Get("gmcm.option.bump-kiss.name"),
                tooltip: () => mod.Helper.Translation.Get("gmcm.option.bump-kiss.tooltip"),
                getValue: () => mod.Config.BumpKissEnabled,
                setValue: value => mod.Config.BumpKissEnabled = value
            );

            configMenu.AddTextOption(
                mod: mod.ModManifest,
                name: () => T(mod, "gmcm.option.blush-smoke-style.name", "Blush smoke style"),
                tooltip: () => T(mod, "gmcm.option.blush-smoke-style.tooltip", "Choose which blush smoke animation plays during kiss sequences."),
                getValue: () => mod.Config.BlushSmokeStyle.ToString(),
                setValue: value =>
                {
                    if (System.Enum.TryParse<BlushSmokeStyle>(value, out var parsed))
                        mod.Config.BlushSmokeStyle = parsed;
                },
                allowedValues: new[] { "Style1", "Style2" },
                formatAllowedValue: value => T(mod, $"gmcm.option.blush-smoke-style.{value.ToLower()}", value)
            );

            configMenu.AddSectionTitle(
                mod.ModManifest,
                () => T(mod, "gmcm.section.multiplayer", "Multiplayer")
            );

            configMenu.AddBoolOption(
                mod: mod.ModManifest,
                name: () => T(mod, "gmcm.option.accept-player-spouse-kisses.name", "Accept kisses from player spouse"),
                tooltip: () => T(mod, "gmcm.option.accept-player-spouse-kisses.tooltip", "Allow the player you married in multiplayer to initiate a synchronized kiss with you. Both players need Lots of Kisses installed."),
                getValue: () => mod.Config.AcceptPlayerSpouseKisses,
                setValue: value => mod.Config.AcceptPlayerSpouseKisses = value
            );

            if (mod.Helper.ModRegistry.IsLoaded("ThaliaFawnheart.TheStardewSquad"))
            {
                configMenu.AddSectionTitle(
                    mod.ModManifest,
                    () => T(mod, "gmcm.section.mod-compatibility", "Compatibility with Other Mods")
                );

                configMenu.AddBoolOption(
                    mod: mod.ModManifest,
                    name: () => T(mod, "gmcm.option.stardew-squad-task-kisses.name", "Allow kisses during Stardew Squad tasks"),
                    tooltip: () => T(mod, "gmcm.option.stardew-squad-task-kisses.tooltip", "Let a kiss temporarily pause a recruited romantic partner's current task. Disable this to prevent Bump Kisses and Multi-Kisses while the partner is working; following and idle partners can still be kissed."),
                    getValue: () => mod.Config.AllowKissesDuringStardewSquadTasks,
                    setValue: value => mod.Config.AllowKissesDuringStardewSquadTasks = value
                );
            }
        }
    }
}
