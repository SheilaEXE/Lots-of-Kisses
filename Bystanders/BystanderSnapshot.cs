using Microsoft.Xna.Framework;
using StardewValley;
using System.Collections.Generic;

namespace LotsOfKisses
{
    /// <summary>
    /// Saves the visual state of a bystander NPC before they turn to watch
    /// a public multi-kiss, so we can restore it cleanly afterward.
    /// Controller is intentionally never touched.
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

        // ── Fishing-pose fix fields ──────────────────────────────────────────
        // Some end-of-route behaviors (fishing being the main one) call
        // Character.extendSourceRect(...), which inflates/offsets the Sprite's sourceRect to cover
        // two tilesheet rows at once (body + fishing rod) and sets ignoreSourceRectUpdates = true.
        // While that flag is true, AnimatedSprite.UpdateSourceRect() is a complete no-op — so
        // forcing CurrentFrame to the idle frame while held did nothing, leaving the stretched/
        // offset two-row rectangle on screen (showing as two wrong, mismatched frame slices
        // instead of a clean single-row idle pose). These fields let us reset those dimensions
        // before forcing the idle frame, and restore them afterward so fishing resumes correctly.
        public bool HasSavedSpriteDimensions         { get; set; }
        public bool SavedIgnoreSourceRectUpdates     { get; set; }
        public int SavedSpriteWidth                  { get; set; }
        public int SavedTempSpriteHeight             { get; set; }

        // ── Fishing-pose animation resume fields ─────────────────────────────
        // Resetting the sprite dimensions above stops the mismatched-frame glitch, but doesn't
        // make vanilla resume the actual fishing animation loop afterward — that's driven by
        // NPC.doMiddleAnimation, a private method scheduled via a self-rescheduling Game1
        // DelayedAction chain that doesn't restart on its own once interrupted. These fields save
        // what's needed to manually re-invoke it after we let go, so the NPC goes back to actually
        // fishing instead of standing still in the idle "looking at player" pose forever.
        public string SavedStartedEndOfRouteBehavior { get; set; }
        public bool? SavedDoingEndOfRouteAnimation   { get; set; }
        public bool? SavedCurrentlyDoingEndOfRouteAnimation { get; set; }

        // Fishing draws the rod as a SEPARATE sprite layer, drawn independently by NPC.draw
        // whenever it still thinks the NPC's route ended in a "fish" behavior — clearing the main
        // Sprite/CurrentFrame alone never touches whatever condition makes NPC.draw render that
        // second layer. There's also a private "yOffset" field that vertically shifts the drawn
        // position for certain poses (e.g. leaning toward the water while fishing); left alone,
        // the character could still be drawn shifted even with the correct idle frame showing.
        public bool HasSavedRodLayerFields           { get; set; }
        public float SavedYOffset                    { get; set; }
        public string SavedLoadedEndOfRouteBehavior  { get; set; }

        // "drawOffset" is a separate Vector2 that shifts WHERE ON SCREEN the sprite is drawn
        // (not which part of the tilesheet is sampled — that's SourceRect, already handled).
        // Fishing poses apparently use a large vertical drawOffset (observed: Y=96, three full
        // tiles) to visually position the leaning-toward-water pose correctly relative to the
        // dock — left in place while we force the idle frame, it makes the correctly-cropped
        // sprite render shifted far down, overlapping dock/water scenery in a way that looks like
        // a torn/split character (e.g. head lining up with the row above, body with the row
        // below), even though the actual tilesheet crop itself is a single clean row.
        public Microsoft.Xna.Framework.Vector2 SavedDrawOffset { get; set; }
    }
}
