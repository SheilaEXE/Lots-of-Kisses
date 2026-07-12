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
