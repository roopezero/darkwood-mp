using DarkwoodMP.Combat.Landings;
using System.Collections.Generic;
using DarkwoodMP.World;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// What the host's enemies throw and leave behind, shared. An enemy's
    /// projectile (a ThrownItem: a pooled one from Character.spawnProjectile,
    /// or its sensor's prefab from Character.rangedAttack, as the human spider
    /// throws) flies and lands only in the host's game, since a client's copies
    /// of the host's enemies don't act. Where it lands, the game makes its
    /// prefabToSpawnOnLand (ThrownItem.onCollide: the human spider's leaves a
    /// Fov_Mob_Spawner) and saves it. On the host, that thing is sent as
    /// EnemyLanded with a new spawned id; the clients make the same prefab
    /// there with that id (ObjRef Spawned), so hits on it and its breaking are
    /// shared like any other item's (BreakSync). What it spawns later is the
    /// host's world doing it: enemies come to clients as the host's enemies.
    ///
    /// A client's copy of a host enemy never throws (spawnProjectile is skipped
    /// for it), so it can't make anything of its own.
    /// </summary>
    internal sealed partial class EnemyLandings
    {
        private readonly CoopSession _coop;

        public EnemyLandings(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.EnemyLanded, OnLanded);
            coop.Handle(MsgType.EnemyProjectile, OnProjectile);
            coop.Handle(MsgType.EnemyLandedGone, OnLandedGone);
            coop.Handle(MsgType.EnemySensorThrown, OnSensorThrown);
        }

        private Session S => _coop.Net;

        /// <summary>
        /// Host: the landing thrown thing of one of our enemies, while
        /// ThrownItem.onCollide runs.
        /// </summary>
        private ThrownItem _landing;
        private uint _landingBy;
        private readonly List<GameObject> _made = new List<GameObject>();

        /// <summary>
        /// Host: the facts of a throw, ranged attack or landing starting by
        /// thrower (null when what it starts from isn't there); by is its
        /// net id when it's one of our tracked enemies.
        /// </summary>
        private ThrowWatchFacts WatchOf(
            Character thrower, bool busy, out uint by)
        {
            by = 0;
            var s = S;
            bool tracked = false;
            if (thrower != null)
            {
                tracked = _coop.Enemies.NetIdOf(thrower, out by);
            }

            return new ThrowWatchFacts
            {
                Busy = busy,
                HasSource = thrower != null,
                Host = s.IsHost,
                Peers = s.Peers.Count,
                Sharing = _coop.Guards.Sharing,
                Tracked = tracked,
            };
        }

        // ---- a projectile thrown -------------------------------------------

        /// <summary>
        /// Host: the projectile type one of our shared enemies is throwing,
        /// while spawnProjectile runs.
        /// </summary>
        private string _throwing;
        private uint _throwingBy;
        private readonly List<Transform> _thrown = new List<Transform>();

        /// <summary>
        /// Host: after spawnProjectile, its projectile (now aimed and moving)
        /// is sent.
        /// </summary>
        private void Threw()
        {
            foreach (var t in _thrown)
            {
                if (t == null)
                {
                    continue;
                }

                var w = S.Begin(MsgType.EnemyProjectile);
                EnemyProjectileMessage.Write(
                    w, ThrowOf(_throwingBy, _throwing, t));
                S.Broadcast(w, Channel.State, reliable: true);
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] sent enemy #{_throwingBy}'s {_throwing} "
                            + $"thrown at {t.position}");
                }
            }

            _thrown.Clear();
        }

        /// <summary>
        /// Host: what EnemyProjectile and EnemySensorThrown say of a thrown
        /// thing: thrower, name, position, yaw, velocity.
        /// </summary>
        private static EnemyThrow ThrowOf(uint by, string name, Transform t)
        {
            var rb = t.GetComponent<Rigidbody>();
            Vector3 v;
            if (rb != null)
            {
                v = rb.velocity;
            }
            else
            {
                v = Vector3.zero;
            }

            return new EnemyThrow
            {
                By = by,
                Name = name,
                At = VecOf(t.position),
                Yaw = t.eulerAngles.y,
                Velocity = VecOf(v),
            };
        }

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        private static Vector3 VectorOf(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        // ---- a ranged sensor thrown -----------------------------------------
        // Character.rangedAttack: the human spider's projectile.

        /// <summary>
        /// Host: the ranged sensor one of our shared enemies is throwing, while
        /// rangedAttack runs.
        /// </summary>
        private string _sensorThrowing;
        private uint _sensorThrowingBy;
        private readonly List<ThrownItem> _sensorThrown =
            new List<ThrownItem>();

        /// <summary>
        /// Host: after rangedAttack, what it threw (now aimed and moving) is
        /// sent as EnemySensorThrown.
        /// </summary>
        private void SensorThrew()
        {
            foreach (var ti in _sensorThrown)
            {
                if (ti == null)
                {
                    continue;
                }

                var w = S.Begin(MsgType.EnemySensorThrown);
                EnemySensorThrownMessage.Write(w, new EnemySensorThrow
                {
                    Throw = ThrowOf(
                        _sensorThrowingBy, _sensorThrowing, ti.transform),
                    Land = new Vec2(ti.landTarget.x, ti.landTarget.z),
                });

                S.Broadcast(w, Channel.State, reliable: true);
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] sent enemy #{_sensorThrowingBy}'s "
                            + $"{_sensorThrowing} ({ti.name}) thrown at "
                            + $"{ti.transform.position}");
                }
            }

            _sensorThrown.Clear();
        }

        // ---- what the patches hand on --------------------------------------

        /// <summary>
        /// Host: ThrownItem.onCollide starts. Whether it's the landing of
        /// one of our shared enemies' throws, noted until it ends.
        /// </summary>
        internal bool LandingStarts(ThrownItem t)
        {
            Character c = null;
            if (t != null && t.objectThatSpawnedMe != null)
            {
                c = t.objectThatSpawnedMe.GetComponent<Character>();
            }

            var start = WatchOf(c, _landing != null, out uint by);
            if (!LandingRules.Notes(start))
            {
                return false;
            }

            _landingBy = by;
            _landing = t;
            _made.Clear();
            return true;
        }

        /// <summary>Host: the noted landing ended; send what it left.</summary>
        internal void LandingEnds()
        {
            _landing = null;
            Landed();
        }

        /// <summary>Host: the noted landing threw; forget it.</summary>
        internal void LandingFailed()
        {
            _landing = null;
            _made.Clear();
        }

        /// <summary>
        /// Host: Core.AddPrefab made this of that prefab: noted when it's
        /// what the noted landing leaves.
        /// </summary>
        internal void PrefabMade(Object prefab, GameObject made)
        {
            var t = _landing;
            bool noting = t != null && made != null;
            bool land = noting && prefab != null
                && prefab == t.prefabToSpawnOnLand;
            if (LandingRules.TakesLanded(noting, land))
            {
                _made.Add(made);
            }
        }

        /// <summary>
        /// Host: Character.spawnProjectile starts. Whether it's one of our
        /// shared enemies' throws, noted until it ends.
        /// </summary>
        internal bool ThrowStarts(Character c, string type)
        {
            var start = WatchOf(c, _throwing != null, out uint by);
            if (!LandingRules.Notes(start))
            {
                return false;
            }

            _throwingBy = by;
            _throwing = type;
            _thrown.Clear();
            return true;
        }

        /// <summary>Host: the noted throw ended; send what it threw.</summary>
        internal void ThrowEnds()
        {
            Threw();
            _throwing = null;
        }

        /// <summary>Host: the noted throw threw; forget it.</summary>
        internal void ThrowFailed()
        {
            _throwing = null;
            _thrown.Clear();
        }

        /// <summary>
        /// Host: Core.AddPooledPrefab made this: noted when the noted throw
        /// threw it.
        /// </summary>
        internal void PooledMade(Transform made)
        {
            bool noting = _throwing != null && made != null;
            bool thrown = noting && made.GetComponent<ThrownItem>() != null;
            bool listed = noting && _thrown.Contains(made);
            if (LandingRules.TakesThrown(noting, thrown, listed))
            {
                _thrown.Add(made);
            }
        }

        /// <summary>
        /// Host: Character.rangedAttack starts. Whether it's one of our
        /// shared enemies' ranged attacks, noted until it ends.
        /// </summary>
        internal bool SensorThrowStarts(
            Character c, Character.SensorType sensor)
        {
            Character thrower = null;
            if (sensor != null)
            {
                thrower = c;
            }

            var start = WatchOf(thrower, _sensorThrowing != null, out uint by);
            if (!LandingRules.Notes(start))
            {
                return false;
            }

            _sensorThrowingBy = by;
            string name = sensor.name;
            if (name == null)
            {
                name = "";
            }

            _sensorThrowing = name;
            _sensorThrown.Clear();
            return true;
        }

        /// <summary>
        /// Host: the noted ranged attack ended; send what it threw.
        /// </summary>
        internal void SensorThrowEnds()
        {
            SensorThrew();
            _sensorThrowing = null;
        }

        /// <summary>Host: the noted ranged attack threw; forget it.</summary>
        internal void SensorThrowFailed()
        {
            _sensorThrowing = null;
            _sensorThrown.Clear();
        }

        /// <summary>
        /// Host: Core.AddPrefab made this: noted when the noted ranged attack
        /// threw it.
        /// </summary>
        internal void SensorMade(GameObject made)
        {
            if (_sensorThrowing == null || made == null)
            {
                return;
            }

            var ti = made.GetComponent<ThrownItem>();
            bool thrown = ti != null;
            bool listed = thrown && _sensorThrown.Contains(ti);
            if (LandingRules.TakesThrown(true, thrown, listed))
            {
                _sensorThrown.Add(ti);
            }
        }

        /// <summary>Host: after the landing, send what it left.</summary>
        private void Landed()
        {
            foreach (var go in _made)
            {
                if (go == null)
                {
                    continue;
                }

                long id = HostIds.NewId();
                ObjRef.RegisterSpawned(id, go);
                var p = go.transform.position;
                var w = S.Begin(MsgType.EnemyLanded);
                EnemyLandedMessage.Write(w, new EnemyLanding
                {
                    Id = id,
                    By = _landingBy,
                    Name = go.name,
                    At = VecOf(p),
                    Yaw = go.transform.eulerAngles.y,
                });

                S.Broadcast(w, Channel.Control, reliable: true);
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] sent enemy #{_landingBy}'s {go.name} landed: "
                            + $"Spawned:{id} at {p}");
                }

                _landed[id] = go;
            }

            _made.Clear();
            if (LandingRules.StartsWatch(_landed.Count, _watching))
            {
                _watching = true;
                Plugin.Instance.StartCoroutine(WatchLanded());
            }
        }

        // Host: what our enemies' projectiles left, until it's gone (then
        // EnemyLandedGone).
        private readonly Dictionary<long, GameObject> _landed =
            new Dictionary<long, GameObject>();
        private bool _watching;

        private System.Collections.IEnumerator WatchLanded()
        {
            var gone = new List<long>();
            while (_landed.Count > 0)
            {
                yield return new WaitForSeconds(LandingRules.WatchEvery);
                var s = S;
                if (Coop.Current != _coop || !s.IsHost)
                {
                    _landed.Clear();
                    break;
                }

                gone.Clear();
                foreach (var kv in _landed)
                {
                    var go = kv.Value;
                    bool exists = go != null;
                    if (LandingRules.Gone(exists, exists && go.activeSelf))
                    {
                        gone.Add(kv.Key);
                    }
                }

                foreach (long id in gone)
                {
                    _landed.Remove(id);
                    var w = s.Begin(MsgType.EnemyLandedGone);
                    EnemyLandedGoneMessage.Write(w, id);
                    s.Broadcast(w, Channel.Control, reliable: true);
                    if (WorldActions.Debug)
                    {
                        Plugin.Log.LogInfo(
                            $"[world] sent enemy landed Spawned:{id} gone");
                    }
                }
            }

            _watching = false;
        }
    }
}
