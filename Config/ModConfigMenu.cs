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
                name: () => T(mod, "gmcm.option.enable-polyamory-support.name", "Enable multiple partners"),
                tooltip: () => T(mod, "gmcm.option.enable-polyamory-support.tooltip", "Lets this mod's kiss system work with more than one romantic partner at once (for save files using a polyamory mod), instead of only your one official spouse."),
                getValue: () => mod.Config.PolyamorySupport,
                setValue: value => mod.Config.PolyamorySupport = value
            );

            configMenu.AddBoolOption(
                mod: mod.ModManifest,
                name: () => T(mod, "gmcm.option.enable-boyfriend-kisses.name", "Enable kisses with dating partners"),
                tooltip: () => T(mod, "gmcm.option.enable-boyfriend-kisses.tooltip", "Lets this mod's kiss system also work with a boyfriend/girlfriend you're dating, not just a spouse you're married to. Turn off to only allow kisses with your spouse."),
                getValue: () => mod.Config.EnableBoyfriendKisses,
                setValue: value => mod.Config.EnableBoyfriendKisses = value
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
        }
    }
}

