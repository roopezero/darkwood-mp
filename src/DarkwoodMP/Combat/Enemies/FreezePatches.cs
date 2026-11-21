using DarkwoodMP.Combat.Enemies;
using HarmonyLib;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Client: its own enemies never start; the host's are driven by the host
    /// (see NetEnemies).
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Start))]
    public static class ClientFreezeCharacterPatch
    {
        private static bool Prefix(Character __instance)
        {
            NetEnemies enemies = NetEnemies.Instance;
            if (enemies == null
                || !FreezeRules.FreezesAtStart(FactsOf(enemies, __instance)))
            {
                return true;
            }

            bool puppet = enemies.IsPuppet(__instance, out _);
            enemies.Freeze(__instance, hide: !puppet);
            return false;
        }

        /// <summary>
        /// The character as the rule sees it. Whether it's eligible and a
        /// local one are read only on a client, outside Thaw, as before.
        /// </summary>
        private static StartFacts FactsOf(NetEnemies enemies, Character c)
        {
            bool client = NetEnemies.ClientMode;
            if (enemies.Starting || !client)
            {
                return new StartFacts
                {
                    Thawing = enemies.Starting,
                    ClientMode = client,
                };
            }

            bool eligible = NetEnemies.Eligible(c);
            bool local = false;
            if (eligible)
            {
                local = NetEnemies.LocalOnly(c);
            }

            return new StartFacts
            {
                Thawing = false,
                ClientMode = true,
                Eligible = eligible,
                Local = local,
            };
        }
    }

    /// <summary>
    /// Client: a frozen character stays frozen when the game switches its area
    /// on again (Location.activateCharacters as a separate location is entered,
    /// a culling node shown: Character.enableComponents(true) would turn its
    /// AI, pathfinding and animator back on and start its idle sound).
    /// NetEnemies.Thaw unfreezes the ones that are ours.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.enableComponents))]
    public static class ClientFrozenStaysPatch
    {
        private static bool Prefix(Character __instance, bool enable)
        {
            if (!enable || !NetEnemies.ClientMode
                || !NetEnemies.Frozen.Contains(__instance))
            {
                return true;
            }

            // A copy of the host's enemy plays the host's clips on its sprite
            // animator, which enableComponents(false)
            // (Location.activateCharacters as its area was left) switched off:
            // that one is switched back on.
            if (NetEnemies.Instance != null
                && NetEnemies.Instance.IsPuppet(__instance, out _))
            {
                var a = __instance.animator;
                if (a != null)
                {
                    a.enabled = true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Client: a hidden character of ours (NetEnemies.Hidden) starts no loop.
    /// Switching its GameObject on again (its culling node entered,
    /// Location.activateCharacters) runs CharacterSounds.OnEnable, which starts
    /// its idle loop before anything else of ours runs; stopping a loop ("")
    /// still works.
    /// </summary>
    [HarmonyPatch(
        typeof(CharacterSounds), nameof(CharacterSounds.playIdleLoop),
        new[] { typeof(string), typeof(bool) })]
    public static class ClientHiddenSilentPatch
    {
        private static bool Prefix(CharacterSounds __instance, string loopName)
        {
            if (string.IsNullOrEmpty(loopName) || NetEnemies.Hidden.Count == 0
                || !NetEnemies.ClientMode)
            {
                return true;
            }

            var c = __instance.GetComponent<Character>();
            return c == null || !NetEnemies.Hidden.Contains(c);
        }
    }

    /// <summary>
    /// Client: a location's spawn points make no characters of our own where
    /// the host runs the world. The host spawns the location's characters as a
    /// player comes near and sends them (NetEnemies.OnSpawn); ours would only
    /// be frozen, hidden copies that live on beside them (a dog camp's dogs
    /// barking after the host's had been killed). A dream, or a separate
    /// location only we are in (NetEnemies.LocalOnly), spawns its own as in
    /// single player.
    /// </summary>
    [HarmonyPatch(
        typeof(CharacterSpawnPoint), nameof(CharacterSpawnPoint.actuallySpawn))]
    public static class ClientNoOwnSpawnsPatch
    {
        private static bool Prefix(CharacterSpawnPoint __instance)
        {
            if (!NetEnemies.ClientMode
                || NetEnemies.LocalOnly(__instance.gameObject))
            {
                return true;
            }

            Plugin.Log.LogInfo(
                $"[enemy] {__instance.type} at "
                    + $"{__instance.transform.position}: not spawned here, the "
                    + "host's comes from the host");
            return false;
        }
    }

    /// <summary>
    /// Host: a corpse lying outside its own location isn't switched off as that
    /// location is left. Location.activateCharacters(false) switches off every
    /// dead character in its list; a living one that chased our player out is
    /// disconnected first, but in co-op the location stays entered while any
    /// player is near it (SharedCulling), so a savage can chase someone
    /// far out of it, die there still connected, and vanish from beside the
    /// host once everyone has left the location (clients kept showing it). Such
    /// a corpse is let go of the location, as the game does with a character
    /// that left it (disconnectFromLocation), and culled by the world's grid
    /// like any other object from then on.
    /// </summary>
    [HarmonyPatch(typeof(Location), nameof(Location.activateCharacters))]
    public static class HostCorpsesOutsideLocationPatch
    {
        private static void Prefix(Location __instance, bool active)
        {
            var s = Net.Session.Instance;
            if (active || s == null || !s.IsHost || !s.Active
                || __instance.isOutsideLocation
                || Plugin.Instance.Host.Culling.HostAway)
            {
                return;
            }

            var list = __instance.charactersList;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var c = list[i];
                if (c == null || c.alive || c.dummy || c.npc != null
                    || Core.withinLocation(
                        c.transform.position, 0f, __instance))
                {
                    continue;
                }

                try
                {
                    list.RemoveAt(i);
                    __instance.objects.Remove(c.gameObject);
                    c.disconnectFromLocation();
                    CullableObject co = c.GetComponent<CullableObject>();
                    if (co == null)
                    {
                        co = c.gameObject.AddComponent<CullableObject>();
                    }

                    co.registerMe();
                    Plugin.Log.LogInfo(
                        $"[enemy] {c.name}'s corpse at {c.transform.position} "
                            + "lies outside "
                            + $"{Core.getTrueLocationName(__instance.name)}: "
                            + "kept as that location is left");
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't keep {c.name}'s corpse out of "
                            + $"{__instance.name}: {e.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Client: copies of the host's dead enemies made from their prefab are
    /// switched off and on with their location (NetEnemies.LocationCharacters).
    /// </summary>
    [HarmonyPatch(typeof(Location), nameof(Location.activateCharacters))]
    public static class ClientCorpsesWithLocationPatch
    {
        private static void Postfix(Location __instance, bool active)
        {
            if (NetEnemies.ClientMode)
            {
                NetEnemies instance = NetEnemies.Instance;
                if (instance != null)
                {
                    instance.LocationCharacters(__instance, active);
                }
            }
        }
    }

    /// <summary>
    /// Client: frozen and host-driven characters don't hear anything here.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.heardSound))]
    public static class ClientDeafPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Character __instance) =>
            !(NetEnemies.ClientMode && NetEnemies.Frozen.Contains(__instance));
    }
}
