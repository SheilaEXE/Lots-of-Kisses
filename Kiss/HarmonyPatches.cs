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
                // Lots of Kisses only modifies the delay when the Partner themselves initiated the kiss.
                if (ModEntry.Instance.LotsOfKissesKissPatchActive != true)
                    return;

                if (ModEntry.Instance?.IsSupportedRomanticPartner(__instance.Name) != true)
                    return;

                if (Game1.dialogueUp || Game1.activeClickableMenu != null)
                    return;

                CharacterData data = __instance.GetData();
                if (data == null || __instance.Sprite == null)
                    return;

                int partnerFrame = data.KissSpriteIndex;

                bool currentFrameIsKiss = __instance.Sprite.CurrentFrame == partnerFrame;
                bool animationContainsKissFrame = false;

                if (__instance.Sprite.CurrentAnimation != null)
                {
                    foreach (FarmerSprite.AnimationFrame frame in __instance.Sprite.CurrentAnimation)
                    {
                        if (frame.frame == partnerFrame)
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

                // Only lock player movement for a single, standalone kiss (bump kiss / one-off
                // vanilla kiss). During the mod's own continuous multi-kiss, each cycle re-triggers
                // this same vanilla animation — if CanMove stayed false through every cycle back
                // to back, the game treats the player as "busy" for the whole sequence and pauses
                // every NPC's schedule/movement across the entire valley (not just the local
                // bystanders this mod manages). Leaving CanMove untouched here keeps the visual
                // kiss animation intact while letting the rest of the world keep moving normally.
                bool isPartOfContinuousMultiKiss = ModEntry.Instance?.continuousKissActive == true;

                if (!isPartOfContinuousMultiKiss)
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

    // ── Suppresses NPC.checkAction's "location override dialogue" branch during a
    // simulated auto-kiss click (e.g. Sebastian's "watching the water" line at the pier).
    // This line is recalculated fresh every call — unlike CurrentDialogue, it can't be
    // stashed — and it otherwise blocks the kiss branch entirely, causing a "ghost kiss"
    // (mod effects fire, pose never changes). Only suppressed during our own simulated
    // click; shows normally again on the player's next manual click.
    [HarmonyPatch(typeof(GameLocation), nameof(GameLocation.HasLocationOverrideDialogue))]
    public static class GameLocation_HasLocationOverrideDialogue_SuppressDuringAutoKiss_Patch
    {
        static bool Prefix(ref bool __result)
        {
            try
            {
                if (!ModEntry.suppressLocationOverrideDialogueDuringAutoKissClick)
                    return true; // run the original method normally

                __result = false;
                return false; // skip the original method, we already set the result
            }
            catch (Exception ex)
            {
                ModEntry.Instance?.Monitor.Log($"[AUTO KISS CLICK] Error suppressing location override dialogue: {ex}", LogLevel.Error);
                return true;
            }
        }
    }

    // ── Same idea as above, for NPC.hasTemporaryMessageAvailable(): it also returns true
    // from "end of route" pose fields (Sebastian gaming, Abigail on the couch) independently
    // of HasLocationOverrideDialogue, blocking the kiss branch the same way. Suppressed only
    // during our own simulated click; nothing on the NPC needs restoring afterward.
    [HarmonyPatch(typeof(NPC), nameof(NPC.hasTemporaryMessageAvailable))]
    public static class NPC_HasTemporaryMessageAvailable_SuppressDuringAutoKiss_Patch
    {
        static bool Prefix(ref bool __result)
        {
            try
            {
                if (!ModEntry.suppressLocationOverrideDialogueDuringAutoKissClick)
                    return true; // run the original method normally

                __result = false;
                return false; // skip the original method, we already set the result
            }
            catch (Exception ex)
            {
                ModEntry.Instance?.Monitor.Log($"[AUTO KISS CLICK] Error suppressing temporary message: {ex}", LogLevel.Error);
                return true;
            }
        }
    }

    // Prevents checkAction from rebuilding a queued daily/marriage dialogue after
    // TryCheckActionForAutoKissWithoutDialogue temporarily stashes CurrentDialogue.
    // This guard is active only for the target NPC during the simulated kiss click;
    // a real player click still loads the pending dialogue normally.
    [HarmonyPatch(typeof(NPC), nameof(NPC.checkForNewCurrentDialogue))]
    public static class NPC_CheckForNewCurrentDialogue_SuppressDuringAutoKiss_Patch
    {
        static bool Prefix(NPC __instance, ref bool __result)
        {
            try
            {
                if (!ModEntry.suppressDialogueFromAutoKissClick)
                    return true;

                NPC targetNpc = ModEntry.suppressDialogueAutoKissNpc;
                if (__instance == null || (targetNpc != null && __instance.Name != targetNpc.Name))
                    return true;

                __result = false;
                return false;
            }
            catch (Exception ex)
            {
                ModEntry.Instance?.Monitor.Log($"[AUTO KISS CLICK] Error suppressing daily dialogue reload: {ex}", LogLevel.Error);
                return true;
            }
        }
    }

    // Suppresses NPC.doMiddleAnimation for a fishing/special-pose bystander while
    // that NPC is temporarily held watching a kiss. This prevents vanilla's delayed
    // route animation from overwriting the idle frame with the wrong spritesheet row.
    [HarmonyPatch(typeof(NPC), "doMiddleAnimation")]
    public static class NPC_DoMiddleAnimation_SuppressForHeldFishingBystander_Patch
    {
        static bool Prefix(NPC __instance)
        {
            try
            {
                if (__instance == null || ModEntry.Instance == null)
                    return true;

                if (!ModEntry.Instance.IsHeldAsFishingBystander(__instance))
                    return true; // not a held fishing bystander — run normally

                return false; // skip — this NPC's pose is being held for a kiss reaction
            }
            catch (Exception ex)
            {
                ModEntry.Instance?.Monitor.Log($"[BYSTANDER FISHING HOLD] Error suppressing doMiddleAnimation: {ex}", LogLevel.Error);
                return true;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // Harmony prefix controlling whether the partner NPC's schedule check runs normally or is
    // suppressed to hold them in a continuous kiss without the game forcibly interrupting the
    // sequence. Registered manually in ModEntry.Entry (not via [HarmonyPatch] attribute), since
    // it needs to target NPC.checkSchedule directly rather than through PatchAll's auto-discovery.
    // ═══════════════════════════════════════════════════════════════════════════════════════
    public static class NPC_CheckSchedule_ContinuousKissHold_Patch
    {
        public static bool CheckSchedule_Prefix(NPC __instance)
        {
            if (ModEntry.allowForcedScheduleCheck)
            {
                if (ModEntry.Instance != null && __instance != null)
                {
                    ModEntry.Instance.Monitor.Log(
                        $"[CHECKSCHEDULE PREFIX] ALLOWED BY FORCE | npc={__instance.Name} currentLoc={__instance.currentLocation?.NameOrUniqueName ?? "null"}",
                        LogLevel.Trace
                    );
                }

                return true;
            }

            if (ModEntry.Instance == null || !Context.IsWorldReady || Game1.player == null)
                return true;

            if (__instance == null)
                return true;

            if (ModEntry.Instance.OutsideBumpPause.Npc != __instance)
                return true;

            if (!ModEntry.Instance.IsSupportedRomanticPartner(__instance.Name))
                return true;

            bool shouldBlock =
                ModEntry.Instance.OutsideBumpPause.IsActive &&
                !ModEntry.Instance.IsHomeOrFarmLocation();

            return !shouldBlock;
        }
    }

}
