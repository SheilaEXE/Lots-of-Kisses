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
                getValue: () => mod.Config.AtivarMod,
                setValue: value => mod.Config.AtivarMod = value
            );

            configMenu.AddBoolOption(
                mod: mod.ModManifest,
                name: () => T(mod, "gmcm.option.enable-polyamory-support.name", "Enable polyamory support"),
                tooltip: () => T(mod, "gmcm.option.enable-polyamory-support.tooltip", "Treat all romantic NPCs as valid kiss partners for this mod, including NPCs from other mods."),
                getValue: () => mod.Config.AtivarCompatibilidadePoliamor,
                setValue: value => mod.Config.AtivarCompatibilidadePoliamor = value
            );

            configMenu.AddBoolOption(
                mod: mod.ModManifest,
                name: () => mod.Helper.Translation.Get("gmcm.option.multi-kisses.name"),
                tooltip: () => mod.Helper.Translation.Get("gmcm.option.multi-kisses.tooltip"),
                getValue: () => mod.Config.AtivarTrocaDeBeijos,
                setValue: value => mod.Config.AtivarTrocaDeBeijos = value
            );

            configMenu.AddBoolOption(
                mod: mod.ModManifest,
                name: () => mod.Helper.Translation.Get("gmcm.option.bump-kiss.name"),
                tooltip: () => mod.Helper.Translation.Get("gmcm.option.bump-kiss.tooltip"),
                getValue: () => mod.Config.AtivarBeijoEsbarrao,
                setValue: value => mod.Config.AtivarBeijoEsbarrao = value
            );

            configMenu.AddTextOption(
                mod: mod.ModManifest,
                name: () => T(mod, "gmcm.option.blush-smoke-style.name", "Blush smoke style"),
                tooltip: () => T(mod, "gmcm.option.blush-smoke-style.tooltip", "Choose which blush smoke animation plays during kiss sequences."),
                getValue: () => mod.Config.EstiloBlushSmoke.ToString(),
                setValue: value =>
                {
                    if (System.Enum.TryParse<BlushSmokeStyle>(value, out var parsed))
                        mod.Config.EstiloBlushSmoke = parsed;
                },
                allowedValues: new[] { "Style1", "Style2" },
                formatAllowedValue: value => T(mod, $"gmcm.option.blush-smoke-style.{value.ToLower()}", value)
            );
        }
    }
}

