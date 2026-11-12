using DarkwoodMP.Sync;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Host: shadow armour (ShadowArmor.checkIfInLight) is hurt by light, and a flashlight shone on it sets its
    /// character on the player holding it. The game finds the flashlight by raycasting for the host's player's
    /// "FlashlightCollider", so another player's beam neither counted as "a flashlight" nor set the enemy on them.
    /// This does the game's check, and then the same for each other player whose flashlight is on: a beam ahead
    /// of their puppet as large as the game's flashlight box gets (BeamOn). Their beam hurts the armour and sets
    /// the character on that player.
    /// </summary>
    [HarmonyPatch(typeof(ShadowArmor), nameof(ShadowArmor.checkIfInLight))]
    static class ShadowArmorJoinerLightPatch
    {
        private static readonly AccessTools.FieldRef<ShadowArmor, float> RefreshRate = AccessTools.FieldRefAccess<ShadowArmor, float>("damageRefreshRate");
        private static readonly System.Action<ShadowArmor, bool> Particles =
            AccessTools.MethodDelegate<System.Action<ShadowArmor, bool>>(AccessTools.Method(typeof(ShadowArmor), "switchDamageParticles"));

        static void Postfix(ShadowArmor __instance)
        {
            var a = __instance;
            if (a == null || !(a.health > 0f) || NetPlayerProxy.All.Count == 0) return;
            var s = Net.Session.Instance;
            if (s == null || !s.IsHost) return;
            var proxy = BeamOn(a.transform.position);
            if (proxy == null) return;
            // The game's own check already hurt it if the spot is lit (the puppet's beam lights the graph too); a beam
            // the graph doesn't show as lit still counts, as the host's flashlight's box does.
            bool hurtAlready = Core.isInLight(a.transform.position, mustBeWalkable: false) || a.GetComponent<Burn>() != null;
            if (!hurtAlready)
            {
                Particles(a, true);
                a.damageMe(a.lightDamage * a.normalDamageMod * RefreshRate(a));
            }
            var c = a.GetComponent<Character>();
            if (c != null && c.alive && !c.dummy)
            {
                if (c.aggressiveness != Aggressiveness.defensive) c.aggressiveness = Aggressiveness.attackOnSight;
                if (c.target != proxy.transform) c.attackCharacter(proxy.transform);
                if (Threat.Debug) Plugin.Log.LogInfo($"[threat] {c.name}'s shadow armour is in {proxy.OwnerName}'s flashlight: goes for them");
            }
        }

        /// <summary>
        /// The other player whose flashlight beam covers this spot. The game's box (Player.flashlightCollider) is the
        /// host's own and is only sized while the host holds a flashlight (a square of side clamp(distance to the
        /// cursor, 100, 1000), that far ahead; 1 x 1 otherwise), so it can't stand for a joiner's beam. A joiner's
        /// is taken as the largest such box ahead of their puppet: from beside them to MaxBeam ahead, widening
        /// with distance (the cursor's distance isn't sent).
        /// </summary>
        internal static NetPlayerProxy BeamOn(Vector3 pos)
        {
            var ps = PlayerSync.Instance;
            if (ps == null) return null;
            foreach (var proxy in NetPlayerProxy.All.Values)
            {
                if (proxy == null || !proxy.alive) continue;
                if (!ps.Remotes.TryGetValue(proxy.Owner, out var rp) || rp.Root == null) continue;
                var last = rp.Last;
                if (last == null || !last.Flashlight || !last.Alive || last.Spectating) continue;
                if (InBeam(rp.Root.transform, pos, out _, out _)) return proxy;
            }
            return null;
        }

        /// <summary>How far ahead a joiner's flashlight reaches (the game's box: up to 1000 ahead and 500 past that).</summary>
        public const float MaxBeam = 1500f;

        /// <summary>Whether a spot is in the beam of a flashlight held by a player standing as `root` (players are rotated Euler(90, yaw, 0): local up is ahead).</summary>
        internal static bool InBeam(Transform root, Vector3 pos, out float ahead, out float aside)
        {
            Vector3 fwd = root.TransformDirection(Vector3.up); fwd.y = 0f;
            Vector3 rel = pos - root.position; rel.y = 0f;
            ahead = aside = 0f;
            if (fwd.sqrMagnitude < 0.01f) return false;
            fwd.Normalize();
            ahead = Vector3.Dot(rel, fwd);
            aside = Vector3.Cross(fwd, rel).magnitude;
            return ahead >= -50f && ahead <= MaxBeam && aside <= Mathf.Max(100f, ahead * 0.5f + 50f);
        }
    }
}
