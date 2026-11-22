using System;
using System.Collections.Generic;
using DarkwoodMP.Combat.Joiners;
using DarkwoodMP.Combat.Targeting;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Host: what a joiner can see, for the two enemies whose behaviour depends
    /// on being seen (gap 17 in docs/INTERACTIONS.md). The host has no field of
    /// view for a proxy, so this is Player.isInSight's test with the proxy in
    /// the player's place: within 800, and in front of them (inside the host's
    /// own field of view angle) or within the host's light radius, with
    /// nothing in between (Core.canSee).
    /// </summary>
    static class JoinerSight
    {
        private static readonly AccessTools.FieldRef<Player, float> Fov =
            AccessTools.FieldRefAccess<Player, float>("currentFOV");
        private static readonly MethodInfo LostEnemy =
            AccessTools.Method(typeof(Character), "lostEnemy");
        private static readonly MethodInfo Check =
            AccessTools.Method(typeof(Character), "checkIfInSightOfPlayer");

        public static bool Sees(NetPlayerProxy p, Transform t)
        {
            if (!JoinerSightRules.InView(FactsOf(p, t)))
            {
                return false;
            }

            return Core.canSee(p.transform, t);
        }

        private static ViewFacts FactsOf(NetPlayerProxy p, Transform t)
        {
            if (p == null || !p.alive || t == null)
            {
                return new ViewFacts
                {
                    StandInAlive = p != null && p.alive,
                    Target = t != null,
                };
            }

            var host = Player.Instance;
            float fov = 0f;
            float light = 0f;
            bool hasLight = false;
            if (host != null)
            {
                fov = Fov(host);
                hasLight = host.FOVDot != null;
            }

            if (hasLight)
            {
                light = host.FOVDot.LightRadius;
            }

            return new ViewFacts
            {
                StandInAlive = true,
                Target = true,
                Distance = Core.trueDistance(p.transform.position, t.position),
                HasHost = host != null,
                HostFov = fov,
                HasLight = hasLight,
                LightRadius = light,
                Angle = Vector3.Angle(
                    t.position - p.transform.position, p.transform.up),
            };
        }

        /// <summary>A living joiner who sees this, or null.</summary>
        public static NetPlayerProxy AnySees(Transform t)
        {
            foreach (var p in NetPlayerProxy.All.Values)
            {
                if (Sees(p, t))
                {
                    return p;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether any player sees it: the host's player (the game's own test),
        /// or a joiner.
        /// </summary>
        public static bool AnyPlayerSees(Transform t) => (Player.Instance
                    != null && Player.Instance.isInSight(t))
            || AnySees(t) != null;

        public static float NearestPlayer(Transform t)
        {
            var host = Player.Instance;
            float hostDistance = 0f;
            if (host != null)
            {
                hostDistance = Core.trueDistance(
                    host._transform.position, t.position);
            }

            var standIns = new List<StandInSpot>();
            foreach (var p in NetPlayerProxy.All.Values)
            {
                if (p == null)
                {
                    continue;
                }

                standIns.Add(new StandInSpot
                {
                    Alive = p.alive,
                    Distance = Core.trueDistance(
                        p.transform.position, t.position),
                });
            }

            return JoinerSightRules.Nearest(
                host != null, hostDistance, standIns);
        }

        /// <summary>
        /// The forest spirit's teleport, read before the rolls.
        /// </summary>
        public static TeleportFacts SpiritFactsOf(Character c)
        {
            var spirit = new TeleportFacts
            {
                On = Plugin.Instance.Host.Threats.Enabled,
                ClientMode = NetEnemies.ClientMode,
                HasTarget = c.target != null,
            };

            if (!spirit.On || spirit.ClientMode || !spirit.HasTarget)
            {
                return spirit;
            }

            bool seen = AnyPlayerSees(c.transform);
            float nearest = 0f;
            if (!seen)
            {
                nearest = NearestPlayer(c.transform);
            }

            return spirit with
            {
                Seen = seen,
                Nearest = nearest,
                TargetAt = VecOf(c.target.position),
                TargetUp = VecOf(c.target.up),
            };
        }

        /// <summary>
        /// A banshee's sight check, and the stand-in who sees it (null when
        /// the rule doesn't need to know).
        /// </summary>
        public static BansheeFacts BansheeFactsOf(
            Character c, out NetPlayerProxy seer)
        {
            seer = null;
            var banshee = new BansheeFacts
            {
                On = Plugin.Instance.Host.Threats.Enabled,
                ClientMode = NetEnemies.ClientMode,
                Banshee = c.banshee,
                Alive = c.alive,
            };

            if (!banshee.On || banshee.ClientMode || !banshee.Banshee
                || !banshee.Alive)
            {
                return banshee;
            }

            bool hostSees = Player.Instance != null
                && Player.Instance.isInSight(c.transform);
            if (!hostSees)
            {
                seer = AnySees(c.transform);
            }

            return banshee with
            {
                HostSees = hostSees,
                JoinerSees = seer != null,
            };
        }

        /// <summary>
        /// A banshee out of its player's sight, and its target's stand-in.
        /// </summary>
        public static BansheeFacts ChaseFactsOf(
            Character c, out NetPlayerProxy target)
        {
            target = null;
            var banshee = new BansheeFacts
            {
                On = Plugin.Instance.Host.Threats.Enabled,
                ClientMode = NetEnemies.ClientMode,
                HasTarget = c.target != null,
            };

            if (!banshee.On || banshee.ClientMode || !banshee.HasTarget)
            {
                return banshee;
            }

            target = c.target.GetComponent<NetPlayerProxy>();
            return banshee with
            {
                TargetStandIn = target != null,
                TargetAlive = target != null && target.alive,
            };
        }

        /// <summary>
        /// The forest spirit's teleport (Character.waitToTeleport, every
        /// 8-15 s): it jumps to beside its target when the host's player
        /// doesn't see it and is more than 200 away. With others playing,
        /// that's any player: it doesn't jump while a joiner looks at it or
        /// is near it, even when the host is far off. False skips the
        /// game's method.
        /// </summary>
        public static bool WaitToTeleport(Character c)
        {
            var spirit = SpiritFactsOf(c);
            var step = JoinerSightRules.Teleport(spirit);
            if (step == TeleportStep.Game)
            {
                return true;
            }

            if (step == TeleportStep.Teleport)
            {
                int x = UnityEngine.Random.Range(
                    JoinerSightRules.SpreadMin, JoinerSightRules.SpreadMax);
                int z = UnityEngine.Random.Range(
                    JoinerSightRules.SpreadMin, JoinerSightRules.SpreadMax);
                spirit = spirit with { RollX = x, RollZ = z };
                c.teleportWithEffect(
                    VectorOf(JoinerSightRules.SpotOf(spirit)),
                    JoinerSightRules.SpiritEffect,
                    JoinerSightRules.SpiritEffectTime);
            }

            return false;
        }

        /// <summary>
        /// The banshee: Character.checkIfInSightOfPlayer (twice a second once
        /// it's been seen) agitates it and sends it at the host's player
        /// whenever the host sees it. With others playing, a joiner seeing it
        /// does the same for that joiner: it goes for them, its sight light
        /// comes on, and it keeps checking. The screen effects of being seen
        /// are each player's own (Combat/EnemyFx.cs), so the host's screen
        /// isn't touched for a joiner. When the host's player stops seeing a
        /// banshee that is after a joiner, it keeps after the joiner
        /// (onBansheeOutOfSightOfPlayer would send it at the host).
        /// </summary>
        public static bool CheckIfInSight(Character c)
        {
            var banshee = BansheeFactsOf(c, out var proxy);
            if (!JoinerSightRules.BansheeGoesForJoiner(banshee))
            {
                return true; // the game's own, or nobody sees it
            }

            c.target = proxy.transform;
            if (LostEnemy != null)
            {
                c.stopRoutine(
                    (Action)Delegate.CreateDelegate(
                        typeof(Action), c, LostEnemy),
                    all: true);
            }

            if (Check != null)
            {
                c.startRoutine(
                    (Action)Delegate.CreateDelegate(typeof(Action), c, Check),
                    JoinerSightRules.BansheeCheckEvery);
            }

            var light = c.transform.Find("SightLight");
            if (light != null)
            {
                light.gameObject.SetActive(true);
            }

            c.canSeeEnemyFar = JoinerSightRules.SeesFar(
                Core.trueDistance(proxy.transform, c.transform),
                c.farViewDistance);
            bool defensive = c.behaviour == Character.Behaviour.defensive;
            if (JoinerSightRules.Approaches(defensive))
            {
                c.goToPos(proxy.transform);
            }

            if (Plugin.Instance.Host.Threats.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[threat] banshee {c.name} seen by {proxy.OwnerName}: "
                        + "going for them");
            }

            return false;
        }

        /// <summary>
        /// After onBansheeOutOfSightOfPlayer: a banshee after a joiner
        /// keeps after them.
        /// </summary>
        public static void OutOfSight(Character c)
        {
            var banshee = ChaseFactsOf(c, out var proxy);
            if (JoinerSightRules.BansheeKeepsChasing(banshee))
            {
                c.goToPos(proxy.transform);
            }
        }

        private static Vector3 VectorOf(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);
    }
}
