using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using System;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
// ======================================================================
        // LOOK-AT-PLAYER / DISTANCE NOTICE SYSTEM
// ======================================================================
        private void UpdateDailySpouseSystems(NPC spouse)
        {
            if (spouse == null || !Context.IsWorldReady)
                return;

            float distance = DistanceToPlayer(spouse);

            // CONTINUOUS KISS SYSTEM
            if (!continuousKissActive &&
                !kissSequenceActive &&
                !kissPostSequenceActive &&
                !spouse.isSleeping.Value &&
                Game1.activeClickableMenu == null &&
                Game1.player.canMove &&
                talkedToSpouseToday &&
                this.Config.MultiKissEnabled)
            {
                float touchDistance = DistanceToPlayer(spouse);
                bool touchingSpouseNow = touchDistance <= 64f;

                // Key check: whether the player and NPC are aligned horizontally (side by side).
                Vector2 diff = Game1.player.Position - spouse.Position;
                bool isHorizontal = Math.Abs(diff.X) > Math.Abs(diff.Y);

                // Only allows the kiss if they are touching AND horizontally aligned.
                if (!touchingSpouseNow || !isHorizontal)
                {
                    continuousKissTouchHoldTimer = 0;
                    continuousKissWasTouchingSpouse = false;
                }
                else if (!continuousKissWasTouchingSpouse)
                {
                    continuousKissTouchHoldTimer = ContinuousKissHoldTicks;
                    continuousKissWasTouchingSpouse = true;
                }
                else if (continuousKissTouchHoldTimer <= 0)
                {
                    StartContinuousKiss(spouse, RollContinuousKissTier(), true);
                    didReactThisTick = true;
                    return;
                }
            }
            else
            {
                continuousKissTouchHoldTimer = 0;
                continuousKissWasTouchingSpouse = false;
            }

            UpdateBumpKissSystem(spouse, distance);
            UpdateSpouseLookAtPlayer(spouse, distance);
            UpdateSpouseNoticeFromDistance(spouse, distance);
        }
        private bool IsNpcBusyForPassiveReaction(NPC npc)
        {
            if (npc == null)
                return true;

            // If any system or mod is moving the NPC, don't let Lots of Kisses
            // flip the sprite on top of it.
            if (npc.controller != null)
                return true;

            if (npc.isMoving())
                return true;

            if (npc.movementPause > 0)
                return true;

            if (npc.CurrentDialogue != null && npc.CurrentDialogue.Count > 0)
                return true;

            if (HasSpecialSpriteAnimation(npc))
                return true;

            return false;
        }
        private bool HasSpecialSpriteAnimation(NPC npc)
        {
            if (npc == null || npc.Sprite == null)
                return false;

            return npc.Sprite.CurrentAnimation != null;
        }

        private bool CanSafelyTurnNpcForPassiveLook(NPC npc)
        {
            if (npc == null)
                return false;

            if (passiveLookBlockAfterDialogueTimer > 0)
                return false;

            if (npc.controller != null)
                return false;

            if (npc.isMoving())
                return false;

            if (npc.movementPause > 0)
                return false;

            if (npc.CurrentDialogue != null && npc.CurrentDialogue.Count > 0)
                return false;

            if (Game1.dialogueUp || Game1.activeClickableMenu != null)
                return false;

            if (HasSpecialSpriteAnimation(npc))
                return false;

            return true;
        }

        private void RememberPassiveLookOriginalPose(NPC npc)
        {
            if (npc == null || npc.Sprite == null || npc.currentLocation == null)
                return;

            // If an original pose is already saved for this NPC,
            // never overwrite it while the NPC hasn't moved or changed state.
            if (passiveLookRestoreActive && passiveLookRestoreNpcName == npc.Name)
            {
                passiveLookRestoreTimer = Math.Max(passiveLookRestoreTimer, 900);
                return;
            }

            passiveLookRestoreActive = true;
            passiveLookRestoreNpcName = npc.Name;

            // First real pose before turning to face the player.
            passiveLookRestoreFacing = npc.FacingDirection;
            passiveLookRestoreFrame = npc.Sprite.CurrentFrame;

            // Store where the NPC was when this pose was saved.
            passiveLookRestoreTile = npc.TilePoint;
            passiveLookRestoreLocationName = npc.currentLocation.NameOrUniqueName;

            passiveLookRestoreTimer = 900;
        }
        private void ClearPassiveLookOriginalPose()
        {
            passiveLookRestoreActive = false;
            passiveLookRestoreNpcName = "";
            passiveLookRestoreFacing = -1;
            passiveLookRestoreFrame = -1;
            passiveLookRestoreTimer = 0;
            passiveLookRestoreTile = Point.Zero;
            passiveLookRestoreLocationName = "";
        }

        private void RestorePassiveLookOriginalPose(NPC npc)
        {
            if (npc == null || npc.Sprite == null)
                return;

            if (!passiveLookRestoreActive)
                return;

            if (passiveLookRestoreNpcName != npc.Name)
                return;

            // Don't restore over active movement or a live controller.
            // If the NPC started walking, the saved pose is no longer valid.
            if (npc.controller != null || npc.isMoving())
            {
                ClearPassiveLookOriginalPose();
                return;
            }

            // If a special animation started, treat it as a state change.
            if (HasSpecialSpriteAnimation(npc))
            {
                ClearPassiveLookOriginalPose();
                return;
            }

            if (passiveLookRestoreFacing >= 0)
                npc.FacingDirection = passiveLookRestoreFacing;

            if (passiveLookRestoreFrame >= 0)
                npc.Sprite.CurrentFrame = passiveLookRestoreFrame;

            npc.Sprite.UpdateSourceRect();

            // Do NOT clear here.
            // The original pose stays saved until the NPC moves or changes state.
        }
        private void UpdatePassiveLookRestoreTimer(NPC npc, float distance)
        {
            bool dialogueOrMenuOpenNow = Game1.dialogueUp || Game1.activeClickableMenu != null;

            if (passiveLookBlockAfterDialogueTimer > 0)
                passiveLookBlockAfterDialogueTimer--;

            if (passiveLookDialogueWasOpenLastTick && !dialogueOrMenuOpenNow)
            {
                // After a dialogue or menu closes, briefly block the pose save
                // to avoid saving the post-dialogue pose as the original.
                passiveLookBlockAfterDialogueTimer = 180;
            }

            passiveLookDialogueWasOpenLastTick = dialogueOrMenuOpenNow;

            if (!passiveLookRestoreActive)
                return;

            if (npc == null || npc.Name != passiveLookRestoreNpcName)
            {
                ClearPassiveLookOriginalPose();
                return;
            }

            // If a dialogue or menu is open, don't restore and don't clear.
            // Keep the first saved original pose.
            if (dialogueOrMenuOpenNow)
            {
                passiveLookRestoreTimer = Math.Max(passiveLookRestoreTimer, 900);
                return;
            }

            // If the NPC gained a controller, started walking, changed tile or location,
            // or entered a special animation, treat it as a state change.
            bool changedState =
                npc.controller != null ||
                npc.isMoving() ||
                npc.currentLocation == null ||
                npc.currentLocation.NameOrUniqueName != passiveLookRestoreLocationName ||
                npc.TilePoint != passiveLookRestoreTile ||
                HasSpecialSpriteAnimation(npc);

            if (changedState)
            {
                ClearPassiveLookOriginalPose();
                return;
            }

            bool shouldRestore =
                distance >= 1000f;

            if (shouldRestore)
            {
                RestorePassiveLookOriginalPose(npc);
                return;
            }

            // Keep the memory alive while the NPC remains idle in the same state.
            passiveLookRestoreTimer = Math.Max(passiveLookRestoreTimer, 900);
        }
// ======================================================================
        // SEGUINDO COM O OLHAR
// ======================================================================
        private void UpdateSpouseLookAtPlayer(NPC npc, float distance)
        {
            if (npc == null || !Context.IsWorldReady)
                return;

            UpdatePassiveLookRestoreTimer(npc, distance);

            if (passiveLookBlockAfterDialogueTimer > 0)
                return;

            if (IsNpcBusyForPassiveReaction(npc))
                return;

            if (!CanSafelyTurnNpcForPassiveLook(npc))
                return;

            bool taDormindo =
                (Game1.timeOfDay >= 2200 && Game1.currentLocation.Name == "FarmHouse");

            bool shouldLook =
                distance < 220f &&
                distance > 60f &&
                !taDormindo &&
                !didReactThisTick;

            if (!shouldLook)
            {
                // Don't restore immediately.
                // Let UpdatePassiveLookRestoreTimer decide when to return to the original pose.
                return;
            }

            RememberPassiveLookOriginalPose(npc);

            Vector2 diff = Game1.player.Position - npc.Position;

            if (Math.Abs(diff.X) > Math.Abs(diff.Y))
                npc.faceDirection(diff.X > 0 ? 1 : 3);
            else
                npc.faceDirection(diff.Y > 0 ? 2 : 0);

            passiveLookRestoreTimer = Math.Max(passiveLookRestoreTimer, 180); // 3 segundos
        }

// ======================================================================
        // IF THE PLAYER IS FAR ENOUGH AWAY BUT STILL WITHIN NOTICE RANGE, THE PARTNER REACTS
// ======================================================================
        private void UpdateSpouseNoticeFromDistance(NPC npc, float distance)
        {
            if (npc == null || !Context.IsWorldReady)
                return;

            if (IsNpcBusyForPassiveReaction(npc))
            {
                lastNoticeDistance = distance;
                return;
            }

            const float minNoticeDistance = 300f; // Below this the spouse already notices naturally, no "notice from afar" reaction needed.
            const float maxNoticeDistance = 600f; // Above this the spouse can't see the player, so a "notice from afar" reaction makes no sense.
            const float approachThreshold = 8f;   // Minimum distance the player must close before the spouse reacts, to avoid reactions when just passing by.

            bool isInNoticeZone = distance < maxNoticeDistance && distance > minNoticeDistance;
            bool hadPreviousDistance = lastNoticeDistance >= 0f;
            bool wasPreviouslyOutsideNoticeZone =
                !hadPreviousDistance ||
                lastNoticeDistance >= maxNoticeDistance ||
                lastNoticeDistance <= minNoticeDistance;

            bool playerJustEnteredNoticeZone =
                hadPreviousDistance &&
                wasPreviouslyOutsideNoticeZone &&
                isInNoticeZone &&
                distance < lastNoticeDistance - approachThreshold;

            bool playerIsLeaving =
                hadPreviousDistance &&
                distance > lastNoticeDistance + approachThreshold;

            if (distance <= minNoticeDistance)
            {
                wasInNoticeZone = false;
                lastNoticeDistance = distance;
                return;
            }

            if (!isInNoticeZone)
            {
                wasInNoticeZone = false;
            }

            if (playerIsLeaving)
            {
                wasInNoticeZone = false;
            }

            if (!npc.isSleeping.Value &&
                Game1.timeOfDay < 2400 &&
                isInNoticeZone &&
                !wasInNoticeZone &&
                playerJustEnteredNoticeZone &&
                npc.controller == null &&
                cooldown <= 0 &&
                noticeEmoteCooldown <= 0 &&
                passiveLookBlockAfterDialogueTimer <= 0 &&
                talkedToSpouseToday &&
                !didReactThisTick)
            {
                if (!CanSafelyTurnNpcForPassiveLook(npc))
                {
                    lastNoticeDistance = distance;
                    return;
                }

                RememberPassiveLookOriginalPose(npc);

                Vector2 diff = Game1.player.Position - npc.Position;

                if (Math.Abs(diff.X) > Math.Abs(diff.Y))
                    npc.faceDirection(diff.X > 0 ? 1 : 3);
                else
                    npc.faceDirection(diff.Y > 0 ? 2 : 0);

                passiveLookRestoreTimer = 90;

                int reaction = random.Next(4);
                switch (reaction)
                {
                    case 0: npc.doEmote(20); break;
                    case 1: npc.doEmote(32); break;
                    case 2: npc.doEmote(60); break;
                    case 3: npc.doEmote(56); break;
                }

                wasInNoticeZone = true;

                noticeEmoteCooldown = 3600;
                cooldown = 180;
                didReactThisTick = true;
            }

            lastNoticeDistance = distance;
        }
    }
}