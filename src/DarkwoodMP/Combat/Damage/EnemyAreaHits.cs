using System.Collections.Generic;
using DarkwoodMP.Combat.Damage;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Enemy damage that doesn't come through a melee sensor or a bullet, for
    /// remote players. The game's code for these only looks for a Player
    /// component or at Player.Instance, so on the host it hurts the host's
    /// player and never a remote player's proxy. Here each also sends
    /// PlayerDamage to every proxy it would have caught, with the game's own
    /// falloff:
    ///
    /// - explosions (Explodes.explode: enemy bombs, and every other explosion
    ///   in the host's world, since a client's own game doesn't hurt its player
    ///   with explosions; see ThrowSync.HostReports),
    ///   damage * (radius - distance) / radius, and the explosion's effect;
    /// - damage auras (Character.waitToDamageAroundMe),
    ///   aroundMeDamage * (range / 2 - distance) / range;
    /// - a diving flier (Flier.Update), 5 to whoever it dove at, not always the
    ///   host;
    /// - a Shooter on one of the host's enemies, damage / distance to each
    ///   proxy it can see;
    /// - flames an enemy spawned (its projectiles and what their explosions
    ///   leave burning), which the proxy otherwise drops: Flame calls every hit
    ///   byPlayer.
    ///
    /// The host decides all of it. A client's frozen copies of the host's
    /// enemies don't dive or shoot. Explosions of the host's enemies happen
    /// only in the host's game, so the host also sends EnemyExplosion (the
    /// sound and the flash) to the clients (EnemyExplosions shows them).
    ///
    /// It keeps no state of its own and runs with no session too, for the
    /// test dummy: what enemies and our player's weapon spawned is
    /// ModHost.Spawns.
    /// </summary>
    public static class EnemyAreaHits
    {
        /// <summary>
        /// What enemies and our player's weapon spawned (ModHost.Spawns).
        /// </summary>
        internal static AreaSpawns Spawns => Plugin.Instance.Host.Spawns;

        private static bool Hosting => AreaHitRules.Hosting(
            Proxies.Any, Session.Instance != null && Session.Instance.IsClient);

        /// <summary>
        /// After the game made an object: whether it goes in the sets of
        /// what enemies or our player's weapon spawned (AreaHitRules.Sets).
        /// </summary>
        internal static void Spawned(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var spawns = Spawns;
            var sets = AreaHitRules.Sets(
                spawns.EnemyDepth, spawns.PlayerDepth, Hosting);
            if ((sets & SpawnSets.Enemy) != 0)
            {
                spawns.AddEnemy(go);
            }

            if ((sets & SpawnSets.Player) != 0)
            {
                spawns.AddPlayer(go);
            }
        }

        /// <summary>
        /// Host: whether one of the host's enemies spawned this (or it is one).
        /// </summary>
        public static bool ByEnemy(Transform t, Transform spawnedBy = null)
        {
            bool enemySpawner = spawnedBy != null
                && IsEnemy(spawnedBy.GetComponent<Character>());
            return AreaHitRules.ByEnemy(
                enemySpawner, !enemySpawner && Spawns.ByEnemy(t));
        }

        private static bool IsEnemy(Character c) =>
            c != null && NetEnemies.Eligible(c);

        /// <summary>
        /// Whether the local player's weapon spawned this flame.
        /// </summary>
        public static bool ByLocalPlayer(Flame f) =>
            f != null && Spawns.ByPlayer(f.transform);

        /// <summary>
        /// Test hook: count this as spawned by one of the host's enemies.
        /// </summary>
        internal static void MarkEnemy(GameObject go)
        {
            if (go != null)
            {
                Spawns.AddEnemy(go);
            }
        }

        /// <summary>
        /// Client: whether the local player is somewhere the host doesn't run
        /// (a dream, or a separate location the host didn't place for us), so
        /// nothing the host's enemies do reaches them there.
        /// </summary>
        public static bool PlayerAway() =>
            !World.Places.Whereabouts.HostRunsHere();

        private static IEnumerable<NetPlayerProxy> LivingProxies()
        {
            foreach (var p in NetPlayerProxy.All.Values)
            {
                if (p == null)
                {
                    continue;
                }

                var col = p.GetComponent<Collider>();
                if (!AreaHitRules.Living(
                        p.alive, col != null, col != null && col.enabled))
                {
                    continue;
                }

                yield return p;
            }
        }

        // ---- explosions -----------------------------------------------------

        /// <summary>
        /// Host, before Explodes.explode: the proxies it catches, as explode
        /// does for the host's Player.
        /// </summary>
        internal static void Explosion(Explodes ex)
        {
            if (ex == null || !ExplosionRules.Handles(
                    ex.effect != null, Hosting))
            {
                return;
            }

            var at = ex.transform;
            if (ExplosionRules.HurtsStandIns(ex.affectsPlayer, ex.radius))
            {
                foreach (var p in LivingProxies())
                {
                    if (!ExplosionRules.Hits(
                            FactsOf(ex, p), out float damage))
                    {
                        continue;
                    }

                    Transform attacker;
                    if (ex.objectThatSpawnedMe != null)
                    {
                        attacker = ex.objectThatSpawnedMe;
                    }
                    else
                    {
                        attacker = at;
                    }

                    var owner = p.Owner;
                    List<InvItemEffect> effects = null;
                    if (ex.hasEffect)
                    {
                        effects = new List<InvItemEffect> { ex.effect };
                    }

                    PlayerDamage.Send(owner, damage, attacker, shadow: false,
                        effects: effects);
                }
            }

            SendExplosion(ex);
        }

        /// <summary>
        /// An explosion and one stand-in: the only code reading them for
        /// ExplosionRules.Hits. Its sight is read only within the radius, as
        /// the game does.
        /// </summary>
        private static BlastFacts FactsOf(Explodes ex, NetPlayerProxy p)
        {
            var at = ex.transform;
            float d = Core.trueDistance(p.transform.position, at.position);
            return new BlastFacts
            {
                Radius = ex.radius,
                Damage = ex.damage,
                Distance = d,
                Sees = d < ex.radius && Core.canSee(at, p.transform),
            };
        }

        /// <summary>
        /// EnemyExplosion, host -> clients: vec3 position, str sound (the
        /// game's audio id, "" none), str effect (the explosion prefab's name,
        /// "" none). One of the host's enemies' explosions, which happen only
        /// in the host's game.
        /// </summary>
        private static void SendExplosion(Explodes ex)
        {
            var s = Session.Instance;
            string sound = ex.explodeSound;
            if (sound == null)
            {
                sound = "";
            }

            string fx = "";
            if (ex.explosionPrefab != null)
            {
                fx = ex.explosionPrefab.name;
            }

            var facts = new ExplosionSendFacts
            {
                ByEnemy = ByEnemy(ex.transform, ex.objectThatSpawnedMe),
                Host = s != null && s.IsHost,
                Peers = PeersOf(s),
                Sound = sound.Length > 0,
                Effect = fx.Length > 0,
            };

            if (!ExplosionRules.Sends(facts))
            {
                return;
            }

            var w = s.Begin(MsgType.EnemyExplosion);
            var pos = ex.transform.position;
            var blast = new EnemyExplosion
            {
                At = new Vec3(pos.x, pos.y, pos.z),
                Sound = sound,
                Effect = fx,
            };

            EnemyExplosionMessage.Write(w, blast);
            s.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] enemy explosion ({fx}, {sound}) at "
                        + $"{ex.transform.position}");
            }
        }

        private static int PeersOf(Session s)
        {
            if (s == null)
            {
                return 0;
            }

            return s.Peers.Count;
        }

        // ---- auras, fliers, shooters, flames --------------------------------

        /// <summary>
        /// Host, before Character.waitToDamageAroundMe: the proxies in its
        /// aura.
        /// </summary>
        internal static void Aura(Character c)
        {
            if (c == null || !AreaHitRules.AuraRuns(
                    c.alive, Hosting, c.aroundMeDamageRange))
            {
                return;
            }

            foreach (var p in LivingProxies())
            {
                float d = Core.trueDistance(
                    p.transform.position, c.transform.position);
                if (AreaHitRules.AuraHit(
                        c.aroundMeDamage, c.aroundMeDamageRange, d,
                        out float hit))
                {
                    PlayerDamage.Send(
                        p.Owner, hit, c.transform, shadow: false,
                        effects: null);
                }
            }
        }

        /// <summary>
        /// Flier.Update's "Player.Instance.getHit(5)": hits whoever the flier
        /// dove at.
        /// </summary>
        public static void FlierHit(Flier flier, float damage)
        {
            Character c = null;
            if (flier != null)
            {
                c = flier.GetComponent<Character>();
            }

            Transform target = null;
            if (c != null)
            {
                target = c.target;
            }

            NetPlayerProxy proxy = null;
            if (target != null)
            {
                proxy = target.GetComponent<NetPlayerProxy>();
            }

            var whom = AreaHitRules.Dive(
                proxy != null, Player.Instance != null);
            if (whom == FlierTarget.StandIn)
            {
                PlayerDamage.Send(
                    proxy.Owner, damage, flier.transform, shadow: false,
                    effects: null);
            }
            else if (whom == FlierTarget.OurPlayer)
            {
                Player.Instance.getHit(damage);
            }
        }

        /// <summary>
        /// Host, before Shooter.shoot on one of its enemies: each proxy it can
        /// see is hit as the host's player is.
        /// </summary>
        internal static void Shot(Shooter sh)
        {
            // A Shooter of the world's own runs in every game.
            if (sh == null || !AreaHitRules.Shoots(
                    sh.target != null, Hosting,
                    IsEnemy(sh.GetComponentInParent<Character>())))
            {
                return;
            }

            foreach (var p in LivingProxies())
            {
                bool sees = Core.canSee(sh.transform, p.transform);
                float d = 0f;
                if (sees)
                {
                    d = Core.trueDistance(sh.transform, p.transform);
                }

                if (AreaHitRules.ShotHit(sh.damage, d, sees, out float hit))
                {
                    PlayerDamage.Send(
                        p.Owner, hit, sh.transform, shadow: false,
                        effects: null);
                }
            }
        }

        /// <summary>
        /// Host, in Flame.onCollideWith: an enemy's flame touching a proxy
        /// hurts its player (the proxy drops byPlayer hits).
        /// </summary>
        internal static void FlameTouch(Flame f, Transform t, Collider col)
        {
            if (f == null)
            {
                return;
            }

            NetPlayerProxy p = null;
            if (col != null)
            {
                p = col.transform.GetComponent<NetPlayerProxy>();
            }

            if (p == null && t != null)
            {
                p = t.GetComponent<NetPlayerProxy>();
            }

            var touch = new FlameTouchFacts
            {
                Active = f.isActive,
                Hosting = Hosting,
                StandIn = p != null,
                Alive = p != null && p.alive,
                ByEnemy = p != null && p.alive && ByEnemy(f.transform),
            };

            if (!AreaHitRules.FlameHurts(touch))
            {
                return;
            }

            var owner = p.Owner;
            var damage = f.contactDamage;
            var attacker = f.transform;
            List<InvItemEffect> effects = null;
            if (f.effect != null)
            {
                effects = new List<InvItemEffect> { f.effect };
            }

            PlayerDamage.Send(owner, damage, attacker, shadow: false,
                effects: effects);
        }
    }
}
