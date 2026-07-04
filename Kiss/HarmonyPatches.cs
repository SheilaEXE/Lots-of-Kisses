using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Characters;
using System;

namespace LotsOfKisses
{
    // ===================================================================================================================================================================================================================================================
    // HARMONYPATCH PARA AUMENTAR O DELAY DO BEIJO NORMAL DO CONJUGE, DEIXANDO O MOMENTO MAIS LONGO OU MAIS CURTO
    // ===================================================================================================================================================================================================================================================
    [HarmonyPatch(typeof(NPC), nameof(NPC.checkAction), new[] { typeof(Farmer), typeof(GameLocation) })]
    public static class NPC_CheckAction_KissDelay_Patch
    {
        static void Postfix(NPC __instance, Farmer who, GameLocation l, ref bool __result)
        {
            try
            {
                if (!__result || __instance == null || who == null)
                    return;

                if (ModEntry.Instance?.Config?.ModEnabled != true)
                    return;

                // Core guard:
                // Lots of Kisses only modifies the delay when the Spouse themselves initiated the kiss.
                if (ModEntry.Instance.LotsOfKissesKissPatchActive != true)
                    return;

                if (ModEntry.Instance?.IsSupportedRomanticPartner(__instance.Name) != true)
                    return;

                if (Game1.dialogueUp || Game1.activeClickableMenu != null)
                    return;

                CharacterData data = __instance.GetData();
                if (data == null || __instance.Sprite == null)
                    return;

                int spouseFrame = data.KissSpriteIndex;

                bool currentFrameIsKiss = __instance.Sprite.CurrentFrame == spouseFrame;
                bool animationContainsKissFrame = false;

                if (__instance.Sprite.CurrentAnimation != null)
                {
                    foreach (FarmerSprite.AnimationFrame frame in __instance.Sprite.CurrentAnimation)
                    {
                        if (frame.frame == spouseFrame)
                        {
                            animationContainsKissFrame = true;
                            break;
                        }
                    }
                }

                bool vanillaStartedKiss = currentFrameIsKiss || animationContainsKissFrame;
                if (!vanillaStartedKiss)
                    return;

                int newSinglePlayerKissDelay = ModEntry.Instance?.activeKissVisualDelayMs ?? 1000;
                __instance.movementPause = newSinglePlayerKissDelay;

            }
            catch (Exception ex)
            {
                ModEntry.Instance?.Monitor.Log($"[KISS PATCH] Erro no Postfix: {ex}", LogLevel.Error);
            }
        }
    }

    // ===================================================================================================================================================================================================================================================
    // HARMONYPATCH PARA AUMENTAR O DELAY DO BEIJO NORMAL DO JOGADOR, DEIXANDO O MOMENTO MAIS LONGO OU MAIS CURTO
    // ===================================================================================================================================================================================================================================================
    [HarmonyPatch(typeof(Farmer), nameof(Farmer.PerformKiss))]
    public static class Farmer_PerformKiss_KissDelay_Patch
    {
        static bool Prefix(Farmer __instance, int facingDirection)
        {
            if (ModEntry.Instance?.Config?.ModEnabled != true)
                return true;

            // MUITO IMPORTANTE:
            // Without this flag, this patch interferes with Attentive Lovers and the vanilla kiss.
            if (ModEntry.Instance.LotsOfKissesKissPatchActive != true)
                return true;

            try
            {
                if (Game1.eventUp ||
                    __instance.UsingTool ||
                    (__instance.IsLocalPlayer && Game1.activeClickableMenu != null) ||
                    __instance.isRidingHorse() ||
                    __instance.IsSitting() ||
                    __instance.IsEmoting ||
                    !__instance.CanMove)
                {
                    return false;
                }

                int newPlayerKissDelay = ModEntry.Instance?.activeKissVisualDelayMs ?? 1000;

                __instance.CanMove = false;
                __instance.FarmerSprite.PauseForSingleAnimation = false;
                __instance.faceDirection(facingDirection);

                __instance.FarmerSprite.animateOnce(new FarmerSprite.AnimationFrame[]
                {
                new FarmerSprite.AnimationFrame(
                    101,
                    newPlayerKissDelay,
                    0,
                    false,
                    __instance.FacingDirection == 3,
                    null,
                    false,
                    0
                ),
                new FarmerSprite.AnimationFrame(
                    6,
                    1,
                    false,
                    __instance.FacingDirection == 3,
                    new AnimatedSprite.endOfAnimationBehavior(farmer => Farmer.completelyStopAnimating(farmer)),
                    false
                )
                }, null);

                return false;
            }
            catch (Exception ex)
            {
                ModEntry.Instance?.Monitor.Log($"[KISS PATCH] Erro no beijo do jogador: {ex}", LogLevel.Error);
                return true;
            }
        }
    }
    // ===================================================================================================================================================================================================================================================
    // BLOCK DIALOGUE OPENED BY THE AUTOMATIC KISS CLICK
    // ===================================================================================================================================================================================================================================================
    [HarmonyPatch(typeof(Game1), nameof(Game1.drawDialogue), new[] { typeof(NPC) })]
    public static class Game1_DrawDialogue_SuppressAutoKissDialogue_Patch
    {
        static bool Prefix(NPC speaker)
        {
            try
            {
                if (ModEntry.Instance?.Config?.ModEnabled != true)
                    return true;

                if (!ModEntry.suppressDialogueFromAutoKissClick)
                    return true;

                if (speaker == null)
                    return true;

                NPC targetNpc = ModEntry.suppressDialogueAutoKissNpc;

                if (targetNpc != null && speaker.Name != targetNpc.Name)
                    return true;

                ModEntry.suppressedDialogueDuringAutoKissClick = true;

                // Do NOT clear CurrentDialogue here.
                // Only blocks the automatic opening caused by the kiss action.
                // The dialogue stays pending for the player to read manually.

                return false;
            }
            catch (Exception ex)
            {
                ModEntry.Instance?.Monitor.Log($"[AUTO KISS CLICK] Error blocking automatic dialogue: {ex}", LogLevel.Error);
                return true;
            }
        }
    }

    // ===================================================================================================================================================================================================================================================
    // HARMONYPATCH TO KEEP THE IN-GAME CLOCK RUNNING WHILE THE MULTI-KISS IS ACTIVE
    // ===================================================================================================================================================================================================================================================
    // Each continuous-kiss cycle re-triggers the vanilla kiss animation, which (via
    // Farmer_PerformKiss_KissDelay_Patch above) sets Game1.player.CanMove = false for the
    // duration of the animation. Game1.shouldTimePass() treats a player who can't move as
    // "busy" and holds the clock, so back-to-back cycles never leave enough of a gap for the
    // clock to tick — the in-game time freezes for as long as the multi-kiss keeps going.
    // This patch forces the result back to true, but ONLY while this mod's own continuous
    // kiss is actually running, so it never affects any other reason the game or another mod
    // might have for pausing time.
    [HarmonyPatch(typeof(Game1), nameof(Game1.shouldTimePass))]
    public static class Game1_ShouldTimePass_KeepClockRunningDuringMultiKiss_Patch
    {
        static void Postfix(ref bool __result)
        {
            try
            {
                if (__result)
                    return;

                if (ModEntry.Instance?.Config?.ModEnabled != true)
                    return;

                if (ModEntry.Instance.continuousKissActive)
                    __result = true;
            }
            catch (Exception ex)
            {
                ModEntry.Instance?.Monitor.Log($"[TIME PATCH] Error in shouldTimePass Postfix: {ex}", LogLevel.Error);
            }
        }
    }
}