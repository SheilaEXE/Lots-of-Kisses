using StardewModdingAPI;
using StardewValley;
using System;
using System.Collections.Generic;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        private string GetDialogueLine(string prefix, int min, int max, NPC partner)
        {
            if (string.IsNullOrEmpty(prefix))
                return null;

            string partnerName = partner?.Name;
            string playerGender = Game1.player != null && Game1.player.IsMale ? "male" : "female";

            for (int i = 0; i < 10; i++)
            {
                int number = random.Next(min, max + 1);
                string value = TryGetDialogueKey(prefix, number, partnerName, playerGender);

                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            for (int number = min; number <= max; number++)
            {
                string value = TryGetDialogueKey(prefix, number, partnerName, playerGender);

                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            return null;
        }
        /// <summary>
        /// Extracts all {itemId} or {itemId:quantity} tokens from a dialogue line,
        /// creates the corresponding Item objects, and returns the cleaned line (tokens removed).
        /// Supports one or more tokens per line, e.g. "Here you go! {66} {213:3}"
        /// </summary>
        private List<Item> ExtractItemTokens(ref string line)
        {
            var items = new List<Item>();

            if (string.IsNullOrEmpty(line))
                return items;

            var matches = System.Text.RegularExpressions.Regex.Matches(line, @"\{(\d+)(?::(\d+))?\}");

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                string itemId = match.Groups[1].Value;
                int quantity = match.Groups[2].Success && int.TryParse(match.Groups[2].Value, out int q) && q > 0 ? q : 1;

                try
                {
                    Item item = ItemRegistry.Create("(O)" + itemId, quantity);
                    if (item != null)
                        items.Add(item);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"[ITEM TOKEN] Could not create item '{itemId}' x{quantity}: {ex.Message}", LogLevel.Warn);
                }
            }

            // Remove all tokens from the line
            line = System.Text.RegularExpressions.Regex.Replace(line, @"\{\d+(?::\d+)?\}", "").Trim();

            return items;
        }

        private string TryGetDialogueKey(string prefix, int number, string partnerName, string playerGender)
        {
            if (!IsSupportedRomanticNpc(partnerName))
                return null;

            List<string> keys = new List<string>
            {
                $"{prefix}.{partnerName}.{playerGender}.{number}",
                $"{prefix}.{partnerName}.{number}",
                $"{prefix}.{playerGender}.{number}",
                $"{prefix}.{number}"
            };

            foreach (string key in keys)
            {
                // Content packs take priority over built-in i18n.
                if (contentPackLoader.TryGetEntry(key, out string packValue) && !string.IsNullOrEmpty(packValue))
                    return packValue.Replace("@", Game1.player.Name);

                var translation = this.Helper.Translation.Get(key);
                if (translation.HasValue())
                    return translation.ToString().Replace("@", Game1.player.Name);
            }

            return null;
        }

    }
} //👈 FINAL DO NAMESPACE