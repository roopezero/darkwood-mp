using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Host: what a joiner can see, for the two enemies whose behaviour depends on being seen (gap 17 in
    /// docs/INTERACTIONS.md). The host has no field of view for a proxy, so this is Player.isInSight's test with
    /// the proxy in the player's place: within 800, and in front of them (inside the host's own field of view
    /// angle) or within the host's light radius, with nothing in between (Core.canSee).
    /// </summary>
    static class JoinerSight
    {
        private static readonly AccessTools.FieldRef<Player, float> Fov = AccessTools.FieldRefAccess<Player, float>("currentFOV");

        public static bool Sees(NetPlayerProxy p, Transform t)
        {
            if (p == null || !p.alive || t == null) return false;
            var host = Player.Instance;
            float d = Core.trueDistance(p.transform.position, t.position);
            if (d >= 800f) return false;
            float fov = host != null ? Fov(host) : 90f;
            float light = host != null && host.FOVDot != null ? host.FOVDot.LightRadius : 0f;
            if (Vector3.Angle(t.position - p.transform.position, p.transform.up) >= fov / 2f && d >= light) return false;
            return Core.canSee(p.transform, t);
        }

        /// <summary>A living joiner who sees this, or null.</summary>
        public static NetPlayerProxy AnySees(Transform t)
        {
            foreach (var p in NetPlayerProxy.All.Values) if (Sees(p, t)) return p;
            return null;
        }

        /// <summary>Whether any player sees it: the host's player (the game's own test), or a joiner.</summary>
        public static bool AnyPlayerSees(Transform t) => (Player.Instance != null && Player.Instance.isInSight(t)) || AnySees(t) != null;

        public static float NearestPlayer(Transform t)
        {
            float best = Player.Instance != null ? Core.trueDistance(Player.Instance._transform.position, t.position) : float.MaxValue;
            foreach (var p in NetPlayerProxy.All.Values)
                if (p != null && p.alive) best = Mathf.Min(best, Core.trueDistance(p.transform.position, t.position));
            return best;
        }
    }

    /// <summary>
    /// The forest spirit's teleport (Character.waitToTeleport, every 8-15 s): it jumps to beside its target when
    /// the host's player doesn't see it and is more than 200 away. With others playing, that's any player: it
    /// doesn't jump while a joiner looks at it or is near it, even when the host is far off.
    /// </summary>
    [HarmonyPatch(typeof(Character), "waitToTeleport")]
    static class ForestSpiritTeleportPatch
    {
        static bool Prefix(Character __instance)
        {
            if (!Threat.Enabled || NetEnemies.ClientMode) return true;
            var c = __instance;
            if (c.target != null && !JoinerSight.AnyPlayerSees(c.transform) && JoinerSight.NearestPlayer(c.transform) > 200f)
                c.teleportWithEffect(c.target.position + c.target.up * 500f + new Vector3(UnityEngine.Random.Range(-100, 100), 0f, UnityEngine.Random.Range(-100, 100)),
                    "ForestSpirit_fastSpawnEff", 4f);
            return false;
        }
    }

    /// <summary>
    /// The banshee: Character.checkIfInSightOfPlayer (twice a second once it's been seen) agitates it and sends it
    /// at the host's player whenever the host sees it. With others playing, a joiner seeing it does the same for
    /// that joiner: it goes for them, its sight light comes on, and it keeps checking. The screen effects of
    /// being seen are each player's own (Combat/EnemyFx.cs), so the host's screen isn't touched for a joiner.
    /// When the host's player stops seeing a banshee that is after a joiner, it keeps after the joiner
    /// (onBansheeOutOfSightOfPlayer would send it at the host).
    /// </summary>
    [HarmonyPatch(typeof(Character), "checkIfInSightOfPlayer")]
    static class BansheeSightPatch
    {
        private static readonly MethodInfo LostEnemy = AccessTools.Method(typeof(Character), "lostEnemy");
        private static readonly MethodInfo Check = AccessTools.Method(typeof(Character), "checkIfInSightOfPlayer");

        static bool Prefix(Character __instance)
        {
            var c = __instance;
            if (!Threat.Enabled || NetEnemies.ClientMode || !c.banshee || !c.alive) return true;
            if (Player.Instance != null && Player.Instance.isInSight(c.transform)) return true; // the game's own
            var proxy = JoinerSight.AnySees(c.transform);
            if (proxy == null) return true; // nobody sees it: the game does nothing either
            c.target = proxy.transform;
            if (LostEnemy != null) c.stopRoutine((Action)Delegate.CreateDelegate(typeof(Action), c, LostEnemy), all: true);
            if (Check != null) c.startRoutine((Action)Delegate.CreateDelegate(typeof(Action), c, Check), 0.5f);
            var light = c.transform.Find("SightLight");
            if (light != null) light.gameObject.SetActive(true);
            c.canSeeEnemyFar = Core.trueDistance(proxy.transform, c.transform) < c.farViewDistance;
            if (c.behaviour != Character.Behaviour.defensive) c.goToPos(proxy.transform);
            if (Threat.Debug) Plugin.Log.LogInfo($"[threat] banshee {c.name} seen by {proxy.OwnerName}: going for them");
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), "onBansheeOutOfSightOfPlayer")]
    static class BansheeOutOfSightPatch
    {
        static void Postfix(Character __instance)
        {
            var c = __instance;
            if (!Threat.Enabled || NetEnemies.ClientMode || c.target == null) return;
            var proxy = c.target.GetComponent<NetPlayerProxy>();
            if (proxy != null && proxy.alive) c.goToPos(proxy.transform);
        }
    }
}
