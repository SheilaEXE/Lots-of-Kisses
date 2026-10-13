using StardewValley;
using StardewValley.GameData.Characters;
using System;

namespace LotsOfKisses
{
    /// <summary>
    /// Narrow compatibility guards that keep automatic kisses from replacing custom special
    /// animations whose sprite layout cannot safely use Stardew Valley's normal kiss frame.
    /// </summary>
    public partial class ModEntry
    {
        internal const string SkipDuringSpecialAnimationCustomField =
            "NatrollEXE.LotsOfKisses/SkipDuringSpecialAnimation";

        private bool ShouldBlockKissForSpecialAnimation(NPC npc)
        {
            if (npc?.Sprite == null)
                return false;

            // Fishing normally uses the standard 16x32 NPC layout. A custom fishing pose may
            // temporarily switch to a taller or wider layout, in which case applying the normal
            // kiss frame index reads the wrong part of its spritesheet. Only reject that exact
            // combination; custom-sized NPCs remain kissable during their ordinary animations.
            if (TryGetActiveFishingBehaviorName(npc, out string fishingBehavior)
                && (npc.Sprite.SpriteWidth != 16 || npc.Sprite.SpriteHeight != 32))
            {
                DebugLog("KISS", () =>
                    $"Romantic kiss rejected for {npc.Name}: active fishing behavior '{fishingBehavior}' uses incompatible sprite geometry {npc.Sprite.SpriteWidth}x{npc.Sprite.SpriteHeight}."
                );
                return true;
            }

            if (!IsNpcPerformingSpecialAnimation(npc))
                return false;

            // A snapshot by itself doesn't mean the current animation belongs to Lots of Kisses.
            // Special schedule poses are also captured so they can be restored later. Only bypass
            // this guard when the saved origin was an ordinary idle/walking pose and the special
            // frame now on-screen was therefore started by the kiss itself.
            bool kissStartedFromOrdinaryPose =
                preKissSpecialActionSnapshot?.Npc == npc
                && !preKissSpecialActionSnapshot.HadSpecialAnimation;
            if (kissStartedFromOrdinaryPose)
                return false;

            CharacterData data = npc.GetData();
            bool isProtectedByAuthor = HasTruthyCustomField(data, SkipDuringSpecialAnimationCustomField);

            if (!isProtectedByAuthor && Config.AllowKissesDuringSpecialAnimations)
                return false;

            string reason = isProtectedByAuthor
                ? $"custom field '{SkipDuringSpecialAnimationCustomField}' protects the active special animation"
                : "the player disabled kisses during special animations";
            DebugLog("KISS", () => $"Romantic kiss rejected for {npc.Name}: {reason}.");
            return true;
        }

        private static bool HasTruthyCustomField(CharacterData data, string key)
        {
            if (data?.CustomFields == null
                || !data.CustomFields.TryGetValue(key, out string value)
                || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return bool.TryParse(value, out bool parsed)
                ? parsed
                : value.Equals("1", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsNpcPerformingSpecialAnimation(NPC npc)
        {
            if (npc?.Sprite == null)
                return false;

            bool isDoingEndOfRouteAnimation = TryGetNetBoolField(npc, "doingEndOfRouteAnimation") == true;
            bool isCurrentlyDoingEndOfRouteAnimation =
                TryGetPrivateField(npc, "currentlyDoingEndOfRouteAnimation") is bool isCurrent && isCurrent;

            return isDoingEndOfRouteAnimation
                || isCurrentlyDoingEndOfRouteAnimation
                || (npc.Sprite.CurrentAnimation != null && npc.Sprite.CurrentAnimation.Count > 0)
                || npc.Sprite.CurrentFrame >= 16;
        }
    }
}
