using StardewValley;
using StardewValley.BellsAndWhistles;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace LotsOfKisses
{
    /// <summary>Centralizes every compatibility-sensitive access to game internals.</summary>
    public partial class ModEntry
    {
        private static readonly object ReflectionCacheLock = new();
        private static readonly Dictionary<(Type Type, string Name), FieldInfo> FieldCache = new();
        private static readonly Dictionary<(Type Type, string Name, BindingFlags Flags), PropertyInfo> PropertyCache = new();
        private static readonly Dictionary<(Type Type, string Name, BindingFlags Flags), MethodInfo> MethodCache = new();

        private static FieldInfo GetCachedField(Type type, string name)
        {
            lock (ReflectionCacheLock)
            {
                var key = (type, name);
                if (!FieldCache.TryGetValue(key, out FieldInfo field))
                {
                    field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    FieldCache[key] = field;
                }

                return field;
            }
        }

        private static PropertyInfo GetCachedProperty(Type type, string name, BindingFlags flags)
        {
            lock (ReflectionCacheLock)
            {
                var key = (type, name, flags);
                if (!PropertyCache.TryGetValue(key, out PropertyInfo property))
                {
                    property = type.GetProperty(name, flags);
                    PropertyCache[key] = property;
                }

                return property;
            }
        }

        private static MethodInfo GetCachedMethod(Type type, string name, BindingFlags flags, Type[] parameterTypes = null)
        {
            lock (ReflectionCacheLock)
            {
                var key = (type, name, flags);
                if (!MethodCache.TryGetValue(key, out MethodInfo method))
                {
                    method = parameterTypes == null
                        ? type.GetMethod(name, flags)
                        : type.GetMethod(name, parameterTypes);
                    MethodCache[key] = method;
                }

                return method;
            }
        }

        private bool FriendshipBoolMethod(object friendship, string methodName)
        {
            try
            {
                MethodInfo method = GetCachedMethod(friendship.GetType(), methodName, BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes);
                if (method == null || method.ReturnType != typeof(bool))
                    return false;

                return method.Invoke(friendship, null) is bool value && value;
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
                object status = GetCachedProperty(friendship.GetType(), "Status", BindingFlags.Instance | BindingFlags.Public)?.GetValue(friendship)
                    ?? GetCachedField(friendship.GetType(), "Status")?.GetValue(friendship);
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

        private int TryGetAnimationFrameIndex(FarmerSprite.AnimationFrame frame)
        {
            try
            {
                object boxed = frame;
                if (GetCachedField(boxed.GetType(), "frame")?.GetValue(boxed) is int fieldValue)
                    return fieldValue;

                if (GetCachedProperty(boxed.GetType(), "Frame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(boxed) is int propertyValue)
                    return propertyValue;
            }
            catch
            {
            }

            return -1;
        }

        private void TrySetPrivateField(object target, string fieldName, object value)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
                return;

            try
            {
                GetCachedField(target.GetType(), fieldName)?.SetValue(target, value);
            }
            catch
            {
            }
        }

        internal object TryGetPrivateField(object target, string fieldName)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
                return null;

            try
            {
                return GetCachedField(target.GetType(), fieldName)?.GetValue(target);
            }
            catch
            {
                return null;
            }
        }

        internal void TrySetNetBoolField(object target, string fieldName, bool value)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
                return;

            try
            {
                object netField = GetCachedField(target.GetType(), fieldName)?.GetValue(target);
                PropertyInfo valueProperty = netField == null ? null : GetCachedProperty(netField.GetType(), "Value", BindingFlags.Instance | BindingFlags.Public);
                if (valueProperty?.CanWrite == true)
                    valueProperty.SetValue(netField, value);
            }
            catch
            {
            }
        }

        internal bool? TryGetNetBoolField(object target, string fieldName)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
                return null;

            try
            {
                object netField = GetCachedField(target.GetType(), fieldName)?.GetValue(target);
                return netField == null ? null : GetCachedProperty(netField.GetType(), "Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(netField) as bool?;
            }
            catch
            {
                return null;
            }
        }

        internal string TryGetNetStringField(object target, string fieldName)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
                return null;

            try
            {
                object netField = GetCachedField(target.GetType(), fieldName)?.GetValue(target);
                return netField == null ? null : GetCachedProperty(netField.GetType(), "Value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(netField) as string;
            }
            catch
            {
                return null;
            }
        }

        private bool TrySetNpcSpeechBubbleAlpha(NPC npc, float alpha)
        {
            if (npc == null)
                return false;

            try
            {
                FieldInfo field = GetCachedField(npc.GetType(), "textAboveHeadAlpha");
                if (field == null)
                    return false;

                field.SetValue(npc, alpha);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryClearNpcSpeechBubble(NPC npc)
        {
            if (npc == null)
                return false;

            bool foundAnyField = false;
            foreach (string fieldName in new[] { "textAboveHeadTimer", "textAboveHeadPreTimer", "textAboveHead", "textAboveHeadAlpha" })
            {
                try
                {
                    FieldInfo field = GetCachedField(npc.GetType(), fieldName);
                    if (field == null)
                        continue;

                    foundAnyField = true;
                    if (field.FieldType == typeof(int))
                        field.SetValue(npc, 0);
                    else if (field.FieldType == typeof(float))
                        field.SetValue(npc, 0f);
                    else if (field.FieldType == typeof(string))
                        field.SetValue(npc, null);
                }
                catch
                {
                }
            }

            return foundAnyField;
        }

        private void TryRestartNpcMiddleAnimation(NPC npc, string behaviorName)
        {
            if (npc?.currentLocation == null || npc.Sprite == null)
                return;

            try
            {
                TrySetPrivateField(npc, "_startedEndOfRouteBehavior", behaviorName);
                GetCachedMethod(npc.GetType(), "doMiddleAnimation", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(npc, new object[] { null });
            }
            catch (Exception ex)
            {
                Monitor.Log($"[BYSTANDER] Failed to re-run doMiddleAnimation for {npc.Name}: {ex}", StardewModdingAPI.LogLevel.Error);
            }
        }
    }
}
