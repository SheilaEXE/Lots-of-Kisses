using Microsoft.Xna.Framework;
using StardewValley;
using System.Collections.Generic;

namespace LotsOfKisses
{
    /// <summary>
    /// Saves the visual state of a bystander NPC before they turn to watch
    /// a public multi-kiss, so we can restore it cleanly afterward.
    /// Controller is left alone for genuinely walking/route NPCs; only paused (via
    /// SavedController below) for stationary NPCs holding a special pose.
    /// </summary>
    internal class BystanderSnapshot
    {
        public NPC Npc                              { get; set; }
        public GameLocation Location                { get; set; }
        public Vector2 Position                     { get; set; }
        public int FacingDirection                  { get; set; }
        public int CurrentFrame                     { get; set; }
        public List<FarmerSprite.AnimationFrame> CurrentAnimation { get; set; }
        public bool Flip                            { get; set; }

        /// <summary>Original movementPause value — restored so the NPC resumes walking naturally.</summary>
        public int MovementPause                    { get; set; }

        /// <summary>Whether this NPC had an active route/controller when they noticed the kiss.</summary>
        public bool HadController                   { get; set; }

        /// <summary>
        /// The NPC's actual controller object, saved only for stationary "special pose" NPCs
        /// (e.g. fishing at a fixed spot) so it can be nulled out while they're held watching and
        /// restored afterward. A controller left running behind a forced idle pose can keep
        /// silently repositioning the NPC (e.g. a small back-and-forth path tied to their fishing
        /// animation), which showed up as the NPC appearing in two different spots at once.
        /// Genuinely walking/route NPCs never populate this — their controller is left alone on
        /// purpose so vanilla can resume their walk naturally.
        /// </summary>
        public StardewValley.Pathfinding.PathFindController SavedController { get; set; }

        /// <summary>Whether this NPC was walking toward the player when they noticed the kiss.</summary>
        public bool WasWalkingTowardPlayer          { get; set; }

        /// <summary>Whether this NPC was moving (any direction) when they noticed the kiss.</summary>
        public bool WasMoving                       { get; set; }

        /// <summary>Whether this mod applied a movement pause to this NPC.</summary>
        public bool WasPausedByMod                  { get; set; }

        /// <summary>
        /// Emote id to play once the NPC has finished turning to face the player.
        /// Null/empty means no emote was rolled for this bystander.
        /// </summary>
        public int? PendingEmote                    { get; set; }

        /// <summary>
        /// Ticks left to wait before playing PendingEmote, giving faceGeneralDirection/faceDirection
        /// time to finish the visual turn before the emote pops, instead of firing in the same tick.
        /// </summary>
        public int PendingEmoteDelayTicks            { get; set; }

        /// <summary>
        /// Ticks left before this NPC is eligible to roll for another crowd reaction line.
        /// Prevents a new showTextAboveHead call from re-triggering on the same NPC while
        /// their current speech bubble is still visible — each call resets the bubble's own
        /// timer, so back-to-back rolls across kiss cycles could otherwise make it look like
        /// the bubble never disappears.
        /// </summary>
        public int CrowdReactionCooldownTicks        { get; set; }

        /// <summary>
        /// Real ticks left before this bystander's speech bubble is force-closed by the mod.
        /// The game's own bubble timer relies on the world being unpaused to count down, but
        /// the valley pauses during each vanilla kiss cycle — so without this, the bubble can
        /// get stuck on screen for the whole multi-kiss sequence. Ticked every real update
        /// regardless of pause state (see TickCrowdReactionCooldowns), guaranteeing the bubble
        /// closes after a fixed real-time duration.
        /// </summary>
        public int CrowdReactionBubbleCloseTicks     { get; set; }
    }
}
