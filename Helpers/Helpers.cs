using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using System;
using System.Collections.Generic;
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
        /// Locations where the tile-passability check below produces false positives — furniture,
        /// decorations, and other passable-looking objects get flagged as blocking, so NPCs in
        /// these locations never reacted to a kiss even when clearly visible in the same room.
        /// Skip the line-of-sight check entirely here; distance/on-screen checks still apply.
        /// </summary>
        private static readonly HashSet<string> LineOfSightExemptLocations = new(StringComparer.OrdinalIgnoreCase)
        {
            "Beach",
            "Saloon",
        };

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

            if (LineOfSightExemptLocations.Contains(location.Name))
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

        // Moved here from Fields/GeneralFields.cs — these are logic helpers, not field
        // declarations, so they belong with the rest of the mod's helper methods.
        private bool HasReadableDialogueWaiting(NPC npc)
        {
            if (npc == null || Game1.player == null)
                return false;

            if (kissBlockAfterDialogueTimer > 0)
                return true;

            if (Game1.dialogueUp || Game1.activeClickableMenu != null)
                return true;

            if (npc.CurrentDialogue != null && npc.CurrentDialogue.Count > 0)
                return true;

            return false;
        }

        // Reads the cross-mod flag written by the Outfit Reactions mod (NatrollEXE.OutfitReactions)
        // into the Farmer's modData. While present, an outfit reaction is in progress (noticing,
        // generating, or dialogue open) and kisses must hold off so the two mods don't collide.
        // Using modData means no hard dependency or load-order requirement between the mods.
        private const string OutfitReactionsActiveModDataKey = "NatrollEXE.OutfitReactions/ReactionActive";

        private bool IsOutfitReactionActive()
        {
            return Game1.player?.modData != null
                && Game1.player.modData.ContainsKey(OutfitReactionsActiveModDataKey);
        }

        // Moved here from Kiss/Kiss.cs — generic sprite/NetField reflection helpers used across
        // multiple files (Kiss.cs, Bystanders.cs), not specific to kiss logic itself.
        private int TryGetAnimationFrameIndex(FarmerSprite.AnimationFrame frame)
        {
            try
            {
                object boxed = frame;
                FieldInfo field = boxed.GetType().GetField("frame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.GetValue(boxed) is int fieldValue)
                    return fieldValue;

                PropertyInfo property = boxed.GetType().GetProperty("Frame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.GetValue(boxed) is int propertyValue)
                    return propertyValue;
            }
            catch
            {
                // If the internal structure changes, the CurrentFrame fallback still covers the common cases.
            }

            return -1;
        }

        private void TrySetSpritePrivateField(object target, string fieldName, object value)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
                return;

            try
            {
                FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                    field.SetValue(target, value);
            }
            catch
            {
                // Campo interno opcional.
            }
        }

        internal object TryGetPrivateField(object target, string fieldName)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
                return null;

            try
            {
                FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return field?.GetValue(target);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// "doingEndOfRouteAnimation" is NOT a plain bool — it's a NetBool (a synced netcode field
        /// wrapping the real value in its own ".Value" property). Every read/write of it goes
        /// through get_Value()/set_Value(), never the field directly. TrySetSpritePrivateField
        /// silently fails on it (wrong CLR type, exception swallowed) — so that flag was never
        /// actually being suppressed with the plain-field helper. Leaving it true while we hold an
        /// NPC in an idle pose makes vanilla re-trigger reallyDoAnimationAtEndOfScheduleRoute() —
        /// the route-end INTRO, which includes a brief walk-in animation — on top of our own
        /// forced frame, corrupting the pose (e.g. showing the wrong row of the tilesheet). Use
        /// these NetField-aware helpers for that field specifically.
        /// </summary>
        internal void TrySetNetBoolField(object target, string fieldName, bool value)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
                return;

            try
            {
                FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object netField = field?.GetValue(target);
                if (netField == null)
                    return;

                PropertyInfo valueProp = netField.GetType().GetProperty("Value");
                if (valueProp != null && valueProp.CanWrite)
                    valueProp.SetValue(netField, value);
            }
            catch
            {
                // Campo interno opcional.
            }
        }

        internal bool? TryGetNetBoolField(object target, string fieldName)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
                return null;

            try
            {
                FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object netField = field?.GetValue(target);
                if (netField == null)
                    return null;

                PropertyInfo valueProp = netField.GetType().GetProperty("Value");
                return valueProp?.GetValue(netField) as bool?;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Same NetField unwrapping as TryGetNetBoolField/TrySetNetBoolField, but for NetString
        /// fields (e.g. "endOfRouteBehaviorName") — which unlike "_startedEndOfRouteBehavior" (a
        /// plain string that's only populated transiently, during the route-end intro) holds the
        /// behavior name persistently the entire time the NPC is settled into that pose. Use this
        /// one to recover the behavior name after the fact, not the transient field.
        /// </summary>
        internal string TryGetNetStringField(object target, string fieldName)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
                return null;

            try
            {
                FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object netField = field?.GetValue(target);
                if (netField == null)
                    return null;

                PropertyInfo valueProp = netField.GetType().GetProperty("Value");
                return valueProp?.GetValue(netField) as string;
            }
            catch
            {
                return null;
            }
        }
    }
}
