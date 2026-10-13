using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using System.Collections.Generic;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        private NPC restoredSpecialAnimationNpc;
        private GameLocation restoredSpecialAnimationLocation;
        private List<FarmerSprite.AnimationFrame> restoredSpecialAnimationFrames;
        private Vector2 restoredSpecialAnimationPosition;

        private void TrackRestoredSpecialAnimation(NPC npc)
        {
            ClearRestoredSpecialAnimationTransition();

            if (!Context.IsWorldReady
                || npc?.Sprite?.CurrentAnimation == null
                || npc.currentLocation == null)
            {
                return;
            }

            restoredSpecialAnimationNpc = npc;
            restoredSpecialAnimationLocation = npc.currentLocation;
            restoredSpecialAnimationFrames = npc.Sprite.CurrentAnimation;
            restoredSpecialAnimationPosition = npc.Position;
            DebugLog("SCHEDULE", $"Watching restored special animation for {npc.Name} until the NPC resumes walking.");
        }

        private void UpdateRestoredSpecialAnimationTransition()
        {
            NPC npc = restoredSpecialAnimationNpc;
            if (npc == null)
                return;

            if (npc.Sprite == null
                || npc.currentLocation != restoredSpecialAnimationLocation
                || !ReferenceEquals(npc.Sprite.CurrentAnimation, restoredSpecialAnimationFrames))
            {
                ClearRestoredSpecialAnimationTransition();
                return;
            }

            // A schedule may start moving the NPC while the special animation restored after
            // a kiss is still playing. Release only that exact animation so walking frames can
            // take over. A controller alone isn't enough: stationary activities can have one.
            if (!npc.isMoving()
                && Vector2.DistanceSquared(npc.Position, restoredSpecialAnimationPosition) <= 1f)
            {
                return;
            }

            DebugLog("SCHEDULE", () =>
                $"Released restored special animation as {npc.Name} started walking: {DescribeNpcDebugState(npc)}."
            );
            npc.Sprite.StopAnimation();
            npc.Sprite.ClearAnimation();
            npc.Sprite.CurrentAnimation = null;
            npc.Sprite.CurrentFrame = GetNpcIdleFrameForDirection(npc.FacingDirection);
            npc.Sprite.UpdateSourceRect();
            ClearRestoredSpecialAnimationTransition();
        }

        private void ClearRestoredSpecialAnimationTransition()
        {
            restoredSpecialAnimationNpc = null;
            restoredSpecialAnimationLocation = null;
            restoredSpecialAnimationFrames = null;
            restoredSpecialAnimationPosition = Vector2.Zero;
        }
    }
}
