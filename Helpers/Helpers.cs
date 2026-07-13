using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using System;
using System.Collections.Generic;
using xTile.Dimensions;

namespace LotsOfKisses
{
    public partial class ModEntry
    {
        internal bool IsSupportedRomanticNpc(string npcName)
        {
            return !string.IsNullOrWhiteSpace(npcName);
        }

        internal bool IsOfficialSpouse(string npcName)
        {
            return Context.IsWorldReady &&
                   Game1.player != null &&
                   !string.IsNullOrWhiteSpace(npcName) &&
                   string.Equals(Game1.player.spouse, npcName, StringComparison.OrdinalIgnoreCase) &&
                   IsSupportedRomanticNpc(npcName);
        }

        internal bool IsCurrentSpouse(string npcName)
        {
            return IsOfficialSpouse(npcName) || IsPolyamorySpouse(npcName);
        }

        internal bool IsPolyamorySpouse(string npcName)
        {
            if (this.Config?.PolyamorySupport != true)
                return false;

            if (!Context.IsWorldReady || Game1.player == null)
                return false;

            if (!IsSupportedRomanticNpc(npcName))
                return false;

            if (IsOfficialSpouse(npcName))
                return false;

            try
            {
                if (Game1.player.friendshipData == null ||
                    !Game1.player.friendshipData.TryGetValue(npcName, out Friendship friendship) ||
                    friendship == null)
                {
                    return false;
                }

                return FriendshipBoolMethod(friendship, "IsMarried") ||
                       FriendshipStatusEquals(friendship, "Married");
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"[POLYAMORY CHECK] Could not check extra marriage status for {npcName}: {ex.Message}", LogLevel.Trace);
                return false;
            }
        }

        internal bool IsDatingPartner(string npcName)
        {
            if (this.Config?.PolyamorySupport != true)
                return false;

            if (!Context.IsWorldReady || Game1.player == null)
                return false;

            if (!IsSupportedRomanticNpc(npcName))
                return false;

            if (IsCurrentSpouse(npcName))
                return false;

            try
            {
                if (Game1.player.friendshipData == null ||
                    !Game1.player.friendshipData.TryGetValue(npcName, out Friendship friendship) ||
                    friendship == null)
                {
                    return false;
                }

                return FriendshipBoolMethod(friendship, "IsDating") ||
                       FriendshipBoolMethod(friendship, "IsEngaged") ||
                       FriendshipStatusEquals(friendship, "Dating", "Engaged");
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"[ROMANCE CHECK] Could not check dating status for {npcName}: {ex.Message}", LogLevel.Trace);
                return false;
            }
        }

        internal bool IsSupportedRomanticPartner(string npcName)
        {
            return IsCurrentSpouse(npcName) || IsDatingPartner(npcName);
        }


        internal bool IsChildNpc(NPC npc)
        {
            return npc != null && npc.Age == NPC.child;
        }

        internal bool IsPrivateKissMoment(NPC partner)
        {
            GameLocation loc = Game1.currentLocation;
            if (loc == null || loc.IsOutdoors)
                return false;

            string locName = loc.Name ?? "";
            if (locName == "FarmHouse" || locName == "Farm")
                return true;

            // Check every NPC in the location: if any non-partner has line of sight to the
            // player, the kiss isn't private — they can see it happening.
            // NPCs behind walls/doors (same GameLocation but different room) are ignored
            // because isTilePassable blocks the raycast, same logic as Outfit Reactions.
            foreach (NPC npc in loc.characters)
            {
                if (npc == null || npc == partner)
                    continue;

                if (IsSupportedRomanticPartner(npc.Name))
                    continue;

                if (HasLineOfSightToPlayer(npc))
                    return false; // non-partner can see the kiss → not private
            }

            return true;
        }

        /// <summary>
        /// Indoor locations where the tile-passability check produces false positives. Outdoor
        /// maps are always exempt below, including custom locations from other mods.
        /// </summary>
        private static readonly HashSet<string> LineOfSightExemptLocations = new(StringComparer.OrdinalIgnoreCase)
        {
            "Saloon",
        };

        /// <summary>
        /// True when there is an unobstructed tile path between the NPC and the player —
        /// walls, closed doors, and solid tiles block the line. Mirrors the same check used
        /// in Outfit Reactions so both mods treat room visibility consistently.
        /// </summary>
        private bool HasLineOfSightToPlayer(NPC npc)
        {
            if (npc == null || Game1.player == null || npc.currentLocation == null)
                return false;

            GameLocation location = npc.currentLocation;

            // Outdoor areas have open sightlines for reaction purposes. This also covers
            // outdoor maps provided by other mods, without needing to list them individually.
            if (location.IsOutdoors || LineOfSightExemptLocations.Contains(location.Name))
                return true;

            Vector2 npcTile    = new Vector2((int)(npc.Position.X / Game1.tileSize), (int)(npc.Position.Y / Game1.tileSize));
            Vector2 playerTile = new Vector2((int)(Game1.player.Position.X / Game1.tileSize), (int)(Game1.player.Position.Y / Game1.tileSize));

            float dx = playerTile.X - npcTile.X;
            float dy = playerTile.Y - npcTile.Y;
            int steps = (int)Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (steps <= 1)
                return true;

            for (int i = 1; i < steps; i++)
            {
                float t = (float)i / steps;
                int tileX = (int)Math.Round(npcTile.X + dx * t);
                int tileY = (int)Math.Round(npcTile.Y + dy * t);

                if (IsVisionIgnoredTile(location, tileX, tileY))
                    continue;

                try
                {
                    xTile.Dimensions.Location tileLoc = new(tileX, tileY);
                    if (!location.isTilePassable(tileLoc, Game1.viewport))
                        return false;
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsVisionIgnoredTile(GameLocation location, int tileX, int tileY)
        {
            if (location == null || Config?.VisionIgnoredTiles == null)
                return false;

            if (!Config.VisionIgnoredTiles.TryGetValue(location.NameOrUniqueName, out List<string> ignoredTiles))
                Config.VisionIgnoredTiles.TryGetValue(location.Name, out ignoredTiles);

            if (ignoredTiles == null)
                return false;

            foreach (string entry in ignoredTiles)
            {
                if (IsTileInsideConfiguredVisionRange(entry, tileX, tileY))
                    return true;
            }

            return false;
        }

        private static bool IsTileInsideConfiguredVisionRange(string entry, int tileX, int tileY)
        {
            if (string.IsNullOrWhiteSpace(entry))
                return false;

            string[] axes = entry.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (axes.Length != 2)
                return false;

            if (!TryParseInclusiveTileRange(axes[0], out int minX, out int maxX)
                || !TryParseInclusiveTileRange(axes[1], out int minY, out int maxY))
                return false;

            return tileX >= minX && tileX <= maxX
                && tileY >= minY && tileY <= maxY;
        }

        private static bool TryParseInclusiveTileRange(string value, out int min, out int max)
        {
            min = 0;
            max = 0;

            string[] bounds = value.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (bounds.Length == 1 && int.TryParse(bounds[0], out int coordinate))
            {
                min = coordinate;
                max = coordinate;
                return true;
            }

            if (bounds.Length != 2
                || !int.TryParse(bounds[0], out int first)
                || !int.TryParse(bounds[1], out int second))
                return false;

            min = Math.Min(first, second);
            max = Math.Max(first, second);
            return true;
        }

        private NPC GetPartner()
        {
            return GetRomanticPartnerForCurrentContext();
        }

        private NPC GetRomanticPartnerForCurrentContext()
        {
            if (!Context.IsWorldReady || Game1.player == null)
                return null;

            NPC activeNpc = GetActiveRomanticNpcFromState();
            if (activeNpc != null)
                return activeNpc;

            NPC nearestPartner = GetNearestRomanticPartnerInCurrentLocation();
            if (nearestPartner != null)
                return nearestPartner;

            // Fallback to preserve legacy behaviour when the official partner is not in the current map.
            if (IsOfficialSpouse(Game1.player.spouse))
                return Game1.getCharacterFromName(Game1.player.spouse);

            return null;
        }

        private NPC GetNearestRomanticPartnerInCurrentLocation()
        {
            if (Game1.currentLocation == null)
                return null;

            NPC nearestPartner = null;
            float nearestDistance = float.MaxValue;

            foreach (NPC npc in Game1.currentLocation.characters)
            {
                if (npc == null || !IsSupportedRomanticPartner(npc.Name))
                    continue;

                float distance = DistanceToPlayer(npc);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestPartner = npc;
                }
            }

            return nearestPartner;
        }

        private NPC GetActiveRomanticNpcFromState()
        {
            NPC[] candidates =
            {
                pendingKissNpc,
                continuousKissNpc,
                kissPostSequenceNpc,
                OutsideBumpPause.Npc,
                approachKissHoldNpc,
                pendingNpcKissResetNpc,
                pendingPublicMultiKissShyNpc
            };

            foreach (NPC npc in candidates)
            {
                if (npc != null && IsSupportedRomanticPartner(npc.Name))
                    return npc;
            }

            return null;
        }

        internal bool IsHomeOrFarmLocation()
        {
            string loc = Game1.currentLocation?.Name ?? "";
            return loc == "FarmHouse" || loc == "Farm";
        }

        internal void FaceEachOther(NPC npc)
        {
            if (npc == null || Game1.player == null)
                return;

            npc.faceGeneralDirection(Game1.player.getStandingPosition(), 0, false, false);
            Game1.player.faceGeneralDirection(npc.getStandingPosition(), 0, false);
        }

        private int MsToTicks(int ms)
        {
            return Math.Max(1, (int)Math.Round(ms / 16.6667));
        }

        private int GetNpcIdleFrameForDirection(int dir)
        {
            switch (dir)
            {
                case 0: return 8;
                case 1: return 4;
                case 2: return 0;
                case 3: return 12;
                default: return 0;
            }
        }

        private float DistanceToPlayer(NPC npc)
        {
            if (npc == null || Game1.player == null)
                return float.MaxValue;

            return Vector2.Distance(npc.Position, Game1.player.Position);
        }

        // Moved here from Fields/GeneralFields.cs — these are logic helpers, not field
        // declarations, so they belong with the rest of the mod's helper methods.
        private bool IsAutoKissBlockedByOpenDialogueOrMenu()
        {
            if (kissBlockAfterDialogueTimer > 0)
                return true;

            return Game1.dialogueUp || Game1.activeClickableMenu != null;
        }

        // Reads the cross-mod flag written by the Outfit Reactions mod (NatrollEXE.OutfitReactions)
        // into the Farmer's modData. Kept here for reference/possible future use, but kiss gating
        // now uses IsAutoKissBlockedByOpenDialogueOrMenu instead — it only blocks while a dialogue box is
        // genuinely on screen, rather than for the whole "noticed, waiting for a click" and
        // post-dialogue linger window, which should allow kissing normally.
        private const string OutfitReactionsActiveModDataKey = "NatrollEXE.OutfitReactions/ReactionActive";

        // Written into the Farmer's modData for the duration of a simulated checkAction click used
        // to trigger the vanilla kiss animation (see TryCheckActionForAutoKissWithoutDialogue).
        // Outfit Reactions' own NPC.checkAction Harmony prefix checks for this key and steps aside
        // instead of hijacking the click into its outfit dialogue.
        internal const string AutoKissClickActiveModDataKey = "NatrollEXE.LotsOfKisses/AutoKissClickActive";
        internal const string PublicMultiKissInterruptionModDataKey = "NatrollEXE.LotsOfKisses/PublicMultiKissInterruption";

    }
}
