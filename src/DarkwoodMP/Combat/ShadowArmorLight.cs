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
    static class JoinerBeams
    {
        private static readonly
            AccessTools.FieldRef<ShadowArmor, float> RefreshRate =
            AccessTools.FieldRefAccess<ShadowArmor, float>("damageRefreshRate");
        private static readonly System.Action<ShadowArmor, bool> Particles =
            AccessTools.MethodDelegate<System.Action<ShadowArmor, bool>>(
                AccessTools.Method(
                    typeof(ShadowArmor), "switchDamageParticles"));

        /// <summary>
        /// Host: after the game's light check on this armour.
        /// </summary>
        public static void Checked(ShadowArmor a)
        {
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
        private static NetPlayerProxy BeamOn(Vector3 pos)
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

        /// <summary>
        /// The game lights the path-finding nodes under each player light
        /// every 0.3 s (Controller.updateLogicLights), and shadow creatures
        /// and shadow armour check those nodes (Core.isInLight). A puppet's
        /// torch or lantern is the item's own light prefab, which lights
        /// nodes as the game's do. A puppet's flashlight beam is a copy of
        /// the host's "Flashlight", which the game clips to the host's
        /// player's flashlight collider, so it would light nothing where the
        /// puppet is. That collider is sized and placed only by the host's
        /// own flashlight (Player.processFlashlight, from the host's cursor),
        /// so it isn't used: the clip box is a square 1000 a side centred 500
        /// ahead of the puppet, which covers the beam from the puppet out to
        /// 1000 ahead (we don't know where its player aims).
        /// </summary>
        public static void LightBox(Light2D light, ref Bounds bounds)
        {
            var parent = light.transform.parent;
            string name = null;
            if (parent != null)
            {
                name = parent.name;
            }

            if (!BeamRules.IsPuppetLight(parent != null, name))
            {
                return;
            }

            // players are rotated Euler(90, yaw, 0): local up is ahead
            Vector3 up = parent.TransformDirection(Vector3.up);
            Vector3 at = parent.position;
            BeamBox box = BeamRules.PuppetBox(VecOf(at), VecOf(up));
            if (!box.Changed)
            {
                return;
            }

            bounds = new Bounds(
                new Vector3(box.Center.X, box.Center.Y, box.Center.Z),
                new Vector3(box.Size.X, box.Size.Y, box.Size.Z));
        }

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);
    }

    /// <summary>
    /// Client: shadow armour on a copy of the host's enemy
    /// (NetEnemies.SetArmor) or on one of our frozen characters only shows its
    /// effect. Its light check (which would hurt it, set it on our player and
    /// show its health bar) and its damage do nothing: the host's armour takes
    /// them, and the host says when it dies (EnemyArmor). On the host, a
    /// joiner's flashlight counts after the game's check
    /// (JoinerBeams.Checked).
    /// </summary>
    [HarmonyPatch(typeof(ShadowArmor), nameof(ShadowArmor.checkIfInLight))]
    static class ShadowArmorCheckIfInLight
    {
        static bool Prefix(ShadowArmor __instance)
        {
            return Coop.Current == null || !NetEnemies.IsCopy(__instance);
        }

        static void Postfix(ShadowArmor __instance)
        {
            if (Coop.Current != null)
            {
                JoinerBeams.Checked(__instance);
            }
        }
    }

    [HarmonyPatch(
        typeof(ShadowArmor), nameof(ShadowArmor.damageMe),
        new[] { typeof(float), typeof(bool) })]
    static class ShadowArmorDamageMe
    {
        static bool Prefix(ShadowArmor __instance)
        {
            return Coop.Current == null || !NetEnemies.IsCopy(__instance);
        }
    }

    /// <summary>
    /// A puppet's flashlight lights the light graph ahead of it
    /// (JoinerBeams.LightBox), also with only the test dummy.
    /// </summary>
    [HarmonyPatch(
        typeof(Light2D), nameof(Light2D.lightGraphNodes),
        new[] { typeof(Bounds) })]
    static class Light2DLightGraphNodes
    {
        static void Prefix(Light2D __instance, ref Bounds _bounds)
        {
            JoinerBeams.LightBox(__instance, ref _bounds);
        }
    }
}
