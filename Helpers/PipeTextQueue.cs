using System.Collections.Generic;
using StardewValley;
using StardewModdingAPI;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        // Delay (in ticks) between each part of a "|"-split speech bubble. 60 ticks ≈ 1 second.
        private const int PipeTextPartDelayTicks = 108; // ~1.8s, matching how long a bubble is comfortably readable.

        // Per-NPC queue of remaining bubble parts still waiting to be shown, and how many ticks
        // are left before the next one shows.
        private readonly Dictionary<string, Queue<string>> pendingPipeTextQueueByNpc = new Dictionary<string, Queue<string>>();
        private readonly Dictionary<string, int> pipeTextDelayTimerByNpc = new Dictionary<string, int>();

        // Drop-in replacement for npc.showTextAboveHead(text). If the text contains "|", it's
        // split into separate speech bubbles shown one after another (not a line break inside a
        // single bubble) — e.g. "Oi!|Que bom te ver!" shows "Oi!" first, then a bit later "Que
        // bom te ver!". Text with no "|" behaves exactly like calling showTextAboveHead directly.
        private void ShowTextAboveHeadWithPipeSupport(NPC npc, string text)
        {
            if (npc == null)
                return;

            // An empty string is a valid, intentional call elsewhere in this mod — it clears the
            // bubble shell instead of showing text. Pass it straight through instead of treating
            // it as "nothing to show".
            if (string.IsNullOrEmpty(text) || !text.Contains("|"))
            {
                npc.showTextAboveHead(text);
                return;
            }

            string[] parts = text.Split('|');

            // Show the first part right away, same as a normal call would.
            npc.showTextAboveHead(parts[0].Trim());

            if (parts.Length <= 1)
                return;

            // Queue the rest to show one at a time as the update tick allows.
            var queue = new Queue<string>();
            for (int i = 1; i < parts.Length; i++)
                queue.Enqueue(parts[i].Trim());

            pendingPipeTextQueueByNpc[npc.Name] = queue;
            pipeTextDelayTimerByNpc[npc.Name] = PipeTextPartDelayTicks;
        }

        // Called every tick from Events.cs. Counts down each waiting NPC's timer and shows the
        // next queued bubble part once it reaches zero.
        private void UpdatePipeTextQueues()
        {
            if (pendingPipeTextQueueByNpc.Count == 0)
                return;

            List<string> finishedNpcNames = null;

            foreach (KeyValuePair<string, Queue<string>> entry in pendingPipeTextQueueByNpc)
            {
                string npcName = entry.Key;
                Queue<string> queue = entry.Value;

                int timer = pipeTextDelayTimerByNpc.TryGetValue(npcName, out int t) ? t : 0;
                if (timer > 0)
                {
                    pipeTextDelayTimerByNpc[npcName] = timer - 1;
                    continue;
                }

                NPC npc = Game1.getCharacterFromName(npcName);
                if (npc != null && queue.Count > 0)
                    npc.showTextAboveHead(queue.Dequeue());

                if (queue.Count > 0)
                {
                    pipeTextDelayTimerByNpc[npcName] = PipeTextPartDelayTicks;
                }
                else
                {
                    (finishedNpcNames ??= new List<string>()).Add(npcName);
                }
            }

            if (finishedNpcNames != null)
            {
                foreach (string npcName in finishedNpcNames)
                {
                    pendingPipeTextQueueByNpc.Remove(npcName);
                    pipeTextDelayTimerByNpc.Remove(npcName);
                }
            }
        }
    }
}
