using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Characters;
using System;

namespace LotsOfKisses
{
// ======================================================================
    // HARMONYPATCH PARA AUMENTAR O DELAY DO BEIJO NORMAL DO CONJUGE, DEIXANDO O MOMENTO MAIS LONGO OU MAIS CURTO
// ======================================================================
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

// ======================================================================
    // HARMONYPATCH PARA AUMENTAR O DELAY DO BEIJO NORMAL DO JOGADOR, DEIXANDO O MOMENTO MAIS LONGO OU MAIS CURTO
// ======================================================================
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
// ======================================================================
    // BLOCK DIALOGUE OPENED BY THE AUTOMATIC KISS CLICK
// ======================================================================
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

    // ── The REAL source of the per-tick tug-of-war: NPC.doMiddleAnimation is the private method
    // NPC.reallyDoAnimationAtEndOfScheduleRoute schedules itself into via a self-rescheduling
    // Game1 DelayedAction chain — completely separate from NPC.update, so patching update alone
    // (see the Postfix below) can win most ticks but still loses on whichever tick this callback
    // fires, since Game1's DelayedAction queue is processed independently of NPC.update. Skipping
    // this method entirely while the NPC is held as a stationary bystander stops the fishing-pose
    // frame sequence from ever touching Sprite during that window, at the source.
    [HarmonyPatch(typeof(NPC), "doMiddleAnimation")]
    public static class NPC_DoMiddleAnimation_SuppressForHeldBystander_Patch
    {
        static bool Prefix(NPC __instance)
        {
            try
            {
                if (__instance == null || ModEntry.Instance == null)
                    return true;

                BystanderSnapshot snapshot = ModEntry.Instance.GetActiveStaticBystanderSnapshot(__instance);
                if (snapshot == null)
                    return true; // not held — run normally

                if (__instance.Name == "Willy")
                    ModEntry.Instance.Monitor.Log("[POSTFIX DEBUG] doMiddleAnimation SUPPRESSED for Willy (held as bystander).", LogLevel.Debug);

                return false; // skip — this NPC is currently held watching a kiss
            }
            catch (Exception ex)
            {
                ModEntry.Instance?.Monitor.Log($"[BYSTANDER POSE ENFORCE] Error suppressing doMiddleAnimation: {ex}", LogLevel.Error);
                return true;
            }
        }
    }

    // ── Ensures a held bystander's "looking at player" pose always has the last word for the
    // tick. Vanilla's own NPC.update can re-assert a scheduled/route-end animation (e.g. a
    // fishing idle loop) on the very same tick after our own hold code already ran, turning into
    // a per-tick tug-of-war where whichever side writes last is what actually gets drawn. Running
    // this as a Postfix on NPC.update guarantees we always write last, for NPCs currently held as
    // a stationary bystander.
    [HarmonyPatch(typeof(NPC), nameof(NPC.update), new[] { typeof(Microsoft.Xna.Framework.GameTime), typeof(GameLocation) })]
    public static class NPC_Update_BystanderPoseEnforce_Patch
    {
        static void Postfix(NPC __instance)
        {
            try
            {
                if (__instance == null || ModEntry.Instance == null)
                    return;

                bool isWilly = __instance.Name == "Willy";

                BystanderSnapshot snapshot = ModEntry.Instance.GetActiveStaticBystanderSnapshot(__instance);

                if (isWilly)
                    ModEntry.Instance.Monitor.Log($"[POSTFIX DEBUG] NPC.update ran for Willy. snapshotFound={(snapshot != null)}", LogLevel.Debug);

                if (snapshot == null)
                    return;

                ModEntry.Instance.ForceStaticBystanderPose(__instance, snapshot);

                if (isWilly)
                    ModEntry.Instance.Monitor.Log($"[POSTFIX DEBUG] ForceStaticBystanderPose applied after vanilla update. Frame now={__instance.Sprite?.CurrentFrame}", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                ModEntry.Instance?.Monitor.Log($"[BYSTANDER POSE ENFORCE] Error re-applying held pose: {ex}", LogLevel.Error);
            }
        }
    }

}