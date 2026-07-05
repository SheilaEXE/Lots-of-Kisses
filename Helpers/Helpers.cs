using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using System;
using System.Reflection;
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

        private bool FriendshipBoolMethod(object friendship, string methodName)
        {
            try
            {
                MethodInfo method = friendship.GetType().GetMethod(methodName, Type.EmptyTypes);
                if (method == null || method.ReturnType != typeof(bool))
                    return false;

                object result = method.Invoke(friendship, null);
                return result is bool value && value;
            }
            catch
            {
                return false;
            }
        }

        private bool FriendshipStatusEquals(object friendship, params string[] expectedStatuses)
        {
            try
            {
                object status = null;

                PropertyInfo property = friendship.GetType().GetProperty("Status");
                if (property != null)
                    status = property.GetValue(friendship);

                if (status == null)
                {
                    FieldInfo field = friendship.GetType().GetField("Status");
                    if (field != null)
                        status = field.GetValue(friendship);
                }

                string statusText = status?.ToString();
                if (string.IsNullOrWhiteSpace(statusText))
                    return false;

                foreach (string expected in expectedStatuses)
                {
                    if (statusText.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        internal bool IsChildNpc(NPC npc)
        {
            return npc != null && npc.Age == NPC.child;
        }

        internal bool IsPrivateKissMoment(NPC spouse)
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
                if (npc == null || npc == spouse)
                    continue;

                if (IsSupportedRomanticPartner(npc.Name))
                    continue;

                if (HasLineOfSightToPlayer(npc))
                    return false; // non-partner can see the kiss → not private
            }

            return true;
        }

        /// <summary>
        /// True when there is an unobstructed tile path between the NPC and the player —
        /// walls, closed doors, and solid tiles block the line. Mirrors the same check used
        /// in Outfit Reactions so both mods treat room visibility consistently.
        /// </summary>
        private static bool HasLineOfSightToPlayer(NPC npc)
        {
            if (npc == null || Game1.player == null || npc.currentLocation == null)
                return false;

            GameLocation location = npc.currentLocation;
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

                try
                {
                    if (IsStructuralWallOrDoor(location, tileX, tileY))
                        return false;
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True only for real map structure — walls, solid building tiles on the "Buildings"
        /// layer — never for furniture, tables, chairs, fences, arcade machines, or any other
        /// placed object. Those all use isTilePassable's broader collision check (which also
        /// covers dynamic objects/furniture), but a pool table between two NPCs shouldn't block
        /// them from noticing a kiss the same way an actual wall would.
        /// Checks the "Passable" tile property directly on the Buildings layer, which is where
        /// vanilla maps define structural collision (walls, door frames, pillars) — separate
        /// from the Objects/Furniture layers that placed items and furniture live on.
        /// </summary>
        private static bool IsStructuralWallOrDoor(GameLocation location, int tileX, int tileY)
        {
            // Off the map entirely (e.g. outside the building bounds) — treat as blocking,
            // matching the old isTilePassable behavior for out-of-bounds tiles.
            if (!location.isTileOnMap(new Vector2(tileX, tileY)))
                return true;

            // The actual structural check: if the Buildings layer explicitly marks this tile
            // passable (walkway, open doorway, etc), it's not a wall.
            string passable = location.doesTileHaveProperty(tileX, tileY, "Passable", "Buildings");
            if (passable != null)
                return false; // explicitly passable — not a wall

            bool hasBuildingsTile = location.map?.GetLayer("Buildings")?.PickTile(
                new xTile.Dimensions.Location(tileX * Game1.tileSize, tileY * Game1.tileSize),
                Game1.viewport.Size) != null;

            // A solid tile on the Buildings layer with no explicit Passable property is a wall
            // or other structural piece — that's what should block the raycast.
            return hasBuildingsTile;
        }

        private NPC GetSpouse()
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

            // Fallback to preserve legacy behaviour when the official spouse is not in the current map.
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
                outsideBumpPauseNpc,
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
    }
}
