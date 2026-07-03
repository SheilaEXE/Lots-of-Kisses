using System.Collections.Generic;

namespace LotsOfKisses
{
    /// <summary>
    /// Represents a loaded Lots of Kisses content pack and its dialogue entries.
    /// </summary>
    internal class ContentPackDialogues
    {
        /// <summary>The unique ID from the pack's manifest.json.</summary>
        public string PackId { get; set; }

        /// <summary>The display name from the pack's manifest.json.</summary>
        public string PackName { get; set; }

        /// <summary>
        /// All dialogue keys loaded from this pack, merged from the best available
        /// language file (current locale → "en" → first file found).
        /// Keys follow the same format as the mod's i18n: e.g. "publicMultiKiss.Shane.1".
        /// </summary>
        public Dictionary<string, string> Entries { get; set; } = new();
    }
}
