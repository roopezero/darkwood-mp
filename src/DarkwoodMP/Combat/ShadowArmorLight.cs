using DarkwoodMP.Combat.Beams;
using DarkwoodMP.Combat.Joiners;
using DarkwoodMP.Sync;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Host: shadow armour (ShadowArmor.checkIfInLight) is hurt by light, and a
    /// flashlight shone on it sets its character on the player holding it. The
    /// game finds the flashlight by raycasting for the host's player's
    /// "FlashlightCollider", so another player's beam neither counted as
    /// "a flashlight" nor set the enemy on them. This does the game's check,
    /// and then the same for each other player whose flashlight is on: a beam
    /// ahead of their puppet as large as the game's flashlight box gets
    /// (BeamOn). Their beam hurts the armour and sets the character on that
    /// player.
    /// </summary>
    [HarmonyPatch(typeof(ShadowArmor), nameof(ShadowArmor.checkIfInLight))]
    static class ShadowArmorJoinerLightPatch
    {
        private static readonly
            AccessTools.FieldRef<ShadowArmor, float> RefreshRate =
            AccessTools.FieldRefAccess<ShadowArmor, float>("damageRefreshRate");
        private static readonly System.Action<ShadowArmor, bool> Particles =
            AccessTools.MethodDelegate<System.Action<ShadowArmor, bool>>(
                AccessTools.Method(
                    typeof(ShadowArmor), "switchDamageParticles"));

        static void Postfix(ShadowArmor __instance)
        {
            var a = __instance;
            if (!BeamRules.ChecksArmour(FactsOf(a)))
            {
                return;
            }

            var proxy = BeamOn(a.transform.position);
            if (proxy == null)
            {
                return;
            }

            // The game's own check already hurt it if the spot is lit (the
            // puppet's beam lights the graph too); a beam the graph doesn't
            // show as lit still counts, as the host's flashlight's box does.
            bool lit = Core.isInLight(
                a.transform.position, mustBeWalkable: false);
            bool burning = false;
            if (!lit)
            {
                burning = a.GetComponent<Burn>() != null;
            }

            if (BeamRules.HurtsArmour(lit, burning))
            {
                Particles(a, true);
                a.damageMe(BeamRules.ArmourDamage(
                    a.lightDamage, a.normalDamageMod, RefreshRate(a)));
            }

            var c = a.GetComponent<Character>();
            bool alive = false;
            bool dummy = false;
            if (c != null)
            {
                alive = c.alive;
                dummy = c.dummy;
            }

            if (BeamRules.ArmourTurns(c != null, alive, dummy))
            {
                bool defensive =
                    c.aggressiveness == Aggressiveness.defensive;
                if (JoinerAiRules.AttacksOnSight(defensive))
                {
                    c.aggressiveness = Aggressiveness.attackOnSight;
                }

                if (c.target != proxy.transform)
                {
                    c.attackCharacter(proxy.transform);
                }

                if (Threat.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[threat] {c.name}'s shadow armour is in "
                            + $"{proxy.OwnerName}'s flashlight: goes for them");
                }
            }
        }

        private static ArmorFacts FactsOf(ShadowArmor a)
        {
            if (a == null)
            {
                return default;
            }

            var s = Net.Session.Instance;
            bool hosting = false;
            if (s != null)
            {
                hosting = s.IsHost;
            }

            return new ArmorFacts
            {
                Exists = true,
                Health = a.health,
                StandIns = NetPlayerProxy.All.Count,
                Hosting = hosting,
            };
        }

        private static BeamOwnerFacts FactsOf(
            NetPlayerProxy proxy, RemotePlayer rp)
        {
            var facts = new BeamOwnerFacts
            {
                StandInAlive = proxy != null && proxy.alive,
                Root = rp != null && rp.Root != null,
            };

            if (rp == null || rp.Last == null)
            {
                return facts;
            }

            var last = rp.Last;
            return facts with
            {
                HasSnapshot = true,
                Flashlight = last.Flashlight,
                Alive = last.Alive,
                Spectating = last.Spectating,
            };
        }

        /// <summary>
        /// The other player whose flashlight beam covers this spot. The game's
        /// box (Player.flashlightCollider) is the host's own and is only sized
        /// while the host holds a flashlight (a square of side clamp(distance
        /// to the cursor, 100, 1000), that far ahead; 1 x 1 otherwise), so it
        /// can't stand for a joiner's beam. A joiner's is taken as the largest
        /// such box ahead of their puppet: from beside them to MaxBeam ahead,
        /// widening with distance (the cursor's distance isn't sent).
        /// </summary>
        internal static NetPlayerProxy BeamOn(Vector3 pos)
        {
            var ps = PlayerSync.Instance;
            if (ps == null)
            {
                return null;
            }

            foreach (var proxy in NetPlayerProxy.All.Values)
            {
                RemotePlayer rp = null;
                if (proxy != null)
                {
                    ps.Remotes.TryGetValue(proxy.Owner, out rp);
                }

                if (!BeamRules.LightsBeam(FactsOf(proxy, rp)))
                {
                    continue;
                }

                if (InBeam(rp.Root.transform, pos).In)
                {
                    return proxy;
                }
            }

            return null;
        }

        /// <summary>
        /// Where a spot is in the beam of a flashlight held by a player
        /// standing as `root`.
        /// </summary>
        internal static BeamSpot InBeam(Transform root, Vector3 pos)
        {
            return BeamRules.InBeam(
                VecOf(root.position),
                VecOf(root.TransformDirection(Vector3.up)),
                VecOf(pos));
        }

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);
    }

    /// <summary>
    /// Client: shadow armour on a copy of the host's enemy
    /// (NetEnemies.SetArmor) or on one of our frozen characters only shows its
    /// effect. Its light check (which would hurt it, set it on our player and
    /// show its health bar) and its damage do nothing: the host's armour takes
    /// them, and the host says when it dies (EnemyArmor).
    /// </summary>
    [HarmonyPatch]
    static class ShadowArmorCopyPatch
    {
        static System.Collections.Generic.IEnumerable<
            System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(
                typeof(ShadowArmor), nameof(ShadowArmor.checkIfInLight));
            yield return AccessTools.Method(
                typeof(ShadowArmor), nameof(ShadowArmor.damageMe),
                new[] { typeof(float), typeof(bool) });
        }

        static bool Prefix(ShadowArmor __instance) =>
            !NetEnemies.IsCopy(__instance);
    }
}
