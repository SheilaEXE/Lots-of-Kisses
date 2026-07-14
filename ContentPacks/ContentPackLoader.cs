using StardewModdingAPI;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace LotsOfKisses
{
    /// <summary>
    /// Discovers and loads Lots of Kisses content packs from the SMAPI
    /// content pack pipeline. Each pack may supply a dialogue file per locale
    /// (e.g. "pt.json", "en.json"). Keys follow the same
    /// format as the mod's own i18n and are merged at runtime, with packs taking
    /// priority over built-in translations.
    /// </summary>
    internal class ContentPackLoader
    {
        // ── Constants ────────────────────────────────────────────────────────

        /// <summary>Fallback locale used when the current locale has no dialogue file.</summary>
        private const string FallbackLocale = "en";

        // ── State ────────────────────────────────────────────────────────────

        private readonly IModHelper _helper;
        private readonly IMonitor _monitor;

        /// <summary>
        /// Merged dialogue entries from all loaded packs, with later packs
        /// overwriting earlier ones on key conflicts. Ready to be queried by
        /// <see cref="TryGetEntry"/>.
        /// </summary>
        private Dictionary<string, string> _merged = new();

        /// <summary>How many packs were loaded in the last <see cref="Load"/> call.</summary>
        public int LoadedPackCount { get; private set; }

        // ── Constructor ──────────────────────────────────────────────────────

        public ContentPackLoader(IModHelper helper, IMonitor monitor)
        {
            _helper = helper;
            _monitor = monitor;
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Discovers all content packs registered for this mod, loads the best
        /// available dialogue file from each one, and rebuilds the merged table.
        /// Safe to call multiple times (e.g. on game launch and on save loaded).
        /// </summary>
        public void Load()
        {
            _merged = new Dictionary<string, string>();
            LoadedPackCount = 0;

            string currentLocale = _helper.Translation.Locale ?? FallbackLocale;

            foreach (IContentPack pack in _helper.ContentPacks.GetOwned())
            {
                try
                {
                    LoadSinglePack(pack, currentLocale);
                }
                catch (Exception ex)
                {
                    _monitor.Log(
                        $"[CONTENT PACK] Failed to load pack \"{pack.Manifest.Name}\" ({pack.Manifest.UniqueID}): {ex.Message}",
                        LogLevel.Warn);
                }
            }

            if (LoadedPackCount > 0)
                _monitor.Log($"[CONTENT PACK] {LoadedPackCount} dialogue pack(s) loaded. {_merged.Count} total key(s) available.", LogLevel.Info);
        }

        /// <summary>
        /// Tries to retrieve a dialogue value from the merged content pack table.
        /// Returns <c>false</c> if no pack has an entry for <paramref name="key"/>.
        /// </summary>
        public bool TryGetEntry(string key, out string value)
        {
            return _merged.TryGetValue(key, out value);
        }

        /// <summary>Returns all dialogue keys currently loaded from content packs.</summary>
        public IEnumerable<string> GetKeys()
        {
            return _merged.Keys;
        }

        // ── Internal helpers ─────────────────────────────────────────────────

        private void LoadSinglePack(IContentPack pack, string currentLocale)
        {
            // Resolve the best available dialogue file for the current locale.
            // Priority: current locale → base language → "en".
            string filePath = ResolveDialogueFile(pack, currentLocale);

            if (filePath is null)
            {
                _monitor.Log(
                    $"[CONTENT PACK] Pack \"{pack.Manifest.Name}\" has no dialogue file. Skipping.",
                    LogLevel.Warn);
                return;
            }

            // Read and deserialise the JSON file.
            Dictionary<string, string> entries;
            try
            {
                entries = pack.ModContent.Load<Dictionary<string, string>>(filePath);
            }
            catch (Exception ex)
            {
                _monitor.Log(
                    $"[CONTENT PACK] Could not read \"{filePath}\" from pack \"{pack.Manifest.Name}\": {ex.Message}",
                    LogLevel.Warn);
                return;
            }

            if (entries is null || entries.Count == 0)
            {
                _monitor.Log(
                    $"[CONTENT PACK] Pack \"{pack.Manifest.Name}\" dialogue file \"{filePath}\" is empty. Skipping.",
                    LogLevel.Warn);
                return;
            }

            // Merge into the global table — pack entries override built-in i18n.
            int added = 0;
            int overwritten = 0;
            foreach (var (key, value) in entries)
            {
                if (string.IsNullOrWhiteSpace(key) || value is null)
                    continue;

                if (_merged.ContainsKey(key))
                    overwritten++;
                else
                    added++;

                _merged[key] = value;
            }

            LoadedPackCount++;
            _monitor.Log(
                $"[CONTENT PACK] Loaded \"{pack.Manifest.Name}\" ({pack.Manifest.UniqueID}) " +
                $"from \"{filePath}\" — {added} new key(s), {overwritten} overwrite(s).",
                LogLevel.Trace);
        }

        /// <summary>
        /// Returns the relative path of the best dialogue file inside <paramref name="pack"/>
        /// for the given locale, or <c>null</c> if no suitable file is found.
        /// </summary>
        private string ResolveDialogueFile(IContentPack pack, string currentLocale)
        {
            // 1. Exact locale match: "pt.json", "pt-BR.json", etc.
            string localeFile = $"{currentLocale}.json";
            if (pack.HasFile(localeFile))
                return localeFile;

            // 2. Base language match for regional locales: "pt-BR" → try "pt".
            int dashIndex = currentLocale.IndexOf('-');
            if (dashIndex > 0)
            {
                string baseLocale = currentLocale.Substring(0, dashIndex);
                string baseFile = $"{baseLocale}.json";
                if (pack.HasFile(baseFile))
                    return baseFile;
            }

            // 3. English fallback.
            string enFile = $"{FallbackLocale}.json";
            if (pack.HasFile(enFile))
                return enFile;

            return null;
        }
    }
}
