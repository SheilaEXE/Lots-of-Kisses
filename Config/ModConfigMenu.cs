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
                name: () => T(mod, "gmcm.option.enable-boyfriend-kisses.name", "Enable kisses with dating partners"),
                tooltip: () => T(mod, "gmcm.option.enable-boyfriend-kisses.tooltip", "If off, this mod only does anything for your spouse — dating partners (not yet married) are treated as regular NPCs. Note: the actual vanilla kiss animation for a dating partner still needs a separate mod like Hugs and Kisses; this only controls this mod's own kiss system (multi-kiss, bump kiss, bystander reactions, etc.) for them."),
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

