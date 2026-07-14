using StardewValley;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        private string GetDialogueLine(string prefix, NPC partner)
        {
            if (string.IsNullOrEmpty(prefix))
                return null;

            string partnerName = partner?.Name;
            string playerGender = Game1.player != null && Game1.player.IsMale ? "male" : "female";
            string[] keyPrefixes =
            {
                $"{prefix}.{partnerName}.{playerGender}",
                $"{prefix}.{partnerName}",
                $"{prefix}.{playerGender}",
                prefix
            };
            List<int> availableNumbers = GetAvailableDialogueNumbers(keyPrefixes);

            while (availableNumbers.Count > 0)
            {
                int index = random.Next(availableNumbers.Count);
                int number = availableNumbers[index];
                availableNumbers.RemoveAt(index);
                string value = TryGetDialogueKey(prefix, number, partnerName, playerGender);

                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            return null;
        }

        /// <summary>
        /// Finds every positive numeric suffix available for any requested dialogue prefix.
        /// Keys may have gaps and have no fixed upper limit.
        /// </summary>
        private List<int> GetAvailableDialogueNumbers(params string[] prefixes)
        {
            string[] validPrefixes = prefixes
                .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            HashSet<int> numbers = new HashSet<int>();

            foreach (string key in this.Helper.Translation.GetKeys())
                TryAddDialogueNumber(key, validPrefixes, numbers);

            foreach (string key in contentPackLoader.GetKeys())
                TryAddDialogueNumber(key, validPrefixes, numbers);

            return numbers.OrderBy(number => number).ToList();
        }

        private static void TryAddDialogueNumber(string key, IEnumerable<string> prefixes, HashSet<int> numbers)
        {
            if (string.IsNullOrEmpty(key))
                return;

            foreach (string prefix in prefixes)
            {
                string keyStart = prefix + ".";
                if (!key.StartsWith(keyStart, StringComparison.Ordinal))
                    continue;

                string suffix = key.Substring(keyStart.Length);
                if (int.TryParse(suffix, out int number) && number > 0)
                    numbers.Add(number);

                return;
            }
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
