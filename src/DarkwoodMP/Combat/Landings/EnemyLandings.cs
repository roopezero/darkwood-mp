using DarkwoodMP.Combat.Landings;
using DarkwoodMP.World.Inventories;
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
    internal sealed class EnemyLandings
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
        /// Client: the facts of a message about the host's enemies' throws.
        /// </summary>
        private HostSendFacts SendOf(ulong from)
        {
            var s = S;
            return new HostSendFacts
            {
                HasSession = true,
                InWorld = WorldShare.InWorld,
                IsClient = s.IsClient,
                FromHost = from == s.HostId,
            };
        }

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
            if (thrower != null && NetEnemies.Instance != null)
            {
                tracked = NetEnemies.Instance.NetIdOf(thrower, out by);
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

        // ---- the throw (a look on clients) ----------------------------------

        /// <summary>
        /// Host: the projectile type one of our shared enemies is throwing,
        /// while spawnProjectile runs.
        /// </summary>
        private string _throwing;
        private uint _throwingBy;
        private readonly List<Transform> _thrown = new List<Transform>();

        /// <summary>
        /// Client: how many of the host's enemies' projectiles were shown here
        /// (for the tests).
        /// </summary>
        internal int Shown { get; private set; }

        /// <summary>
        /// Client: copies of the host's enemies' projectiles, flying for the
        /// look only.
        /// </summary>
        private readonly HashSet<ThrownItem> _copies =
            new HashSet<ThrownItem>();

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

        /// <summary>
        /// Client: a copy of the host's enemy's projectile, made harmless and
        /// sent flying for the look.
        /// </summary>
        private void Fly(
            ThrownItem ti, Rigidbody rb, Character thrower, Vector3 vel)
        {
            _copies.Add(ti);
            Shown++;
            var col = ti.GetComponent<Collider>();
            if (thrower != null && thrower.collider != null && col != null)
            {
                Physics.IgnoreCollision(col, thrower.collider);
            }

            if (thrower != null)
            {
                ti.objectThatSpawnedMe = thrower.transform;
            }
            else
            {
                ti.objectThatSpawnedMe = null;
            }

            ti.damage = 0;
            rb.velocity = vel;
            rb.AddTorque(0f, LandingRules.CopySpin, 0f);
        }

        private void OnProjectile(ulong from, PacketReader r)
        {
            if (!LandingRules.FromHost(SendOf(from)))
            {
                return;
            }

            var thrown = EnemyProjectileMessage.Read(r);
            uint by = thrown.By;
            string type = thrown.Name;
            var pos = VectorOf(thrown.At);
            float yaw = thrown.Yaw;
            var vel = VectorOf(thrown.Velocity);
            Transform t;
            try
            {
                t = Core.AddPooledPrefab(
                    type, pos, Quaternion.Euler(90f, yaw, 0f));
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't show enemy #{by}'s {type}: {e.Message}");
                return;
            }

            if (t == null)
            {
                return;
            }

            var ti = t.GetComponent<ThrownItem>();
            var rb = t.GetComponent<Rigidbody>();
            if (!LandingRules.ShowsCopy(ti != null, rb != null))
            {
                Core.RemovePooledPrefab(t);
                return;
            }

            NetEnemies instance = NetEnemies.Instance;
            Character thrower = null;
            if (instance != null)
            {
                thrower = instance.CharacterOf(by);
            }

            Fly(ti, rb, thrower, vel);
            ti.thrown = true;
            Plugin.Instance.StartCoroutine(Expire(ti, t));
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] got enemy #{by}'s {type} thrown at {pos}");
            }
        }

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

        /// <summary>
        /// Client: make what the host's enemy threw from our copy's own sensor,
        /// and let it fly for the look.
        /// </summary>
        private void OnSensorThrown(ulong from, PacketReader r)
        {
            if (!LandingRules.FromHost(SendOf(from)))
            {
                return;
            }

            var thrown = EnemySensorThrownMessage.Read(r);
            uint by = thrown.Throw.By;
            string name = thrown.Throw.Name;
            var pos = VectorOf(thrown.Throw.At);
            float yaw = thrown.Throw.Yaw;
            var vel = VectorOf(thrown.Throw.Velocity);
            var land = new Vector3(thrown.Land.X, 0f, thrown.Land.Z);
            NetEnemies instance = NetEnemies.Instance;
            Character thrower = null;
            if (instance != null)
            {
                thrower = instance.CharacterOf(by);
            }

            if (thrower == null)
            {
                return; // not shown here: nobody would see it
            }

            var types = new List<Character.SensorType>();
            var choices = new List<SensorChoice>();
            foreach (var x in thrower.sensorTypes)
            {
                types.Add(x);
                choices.Add(ChoiceOf(x));
            }

            int i = LandingRules.SensorIndex(choices, name);
            if (i < 0)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't show enemy #{by}'s {name}: our {thrower.name} "
                        + "has no such ranged sensor");
                return;
            }

            var st = types[i];
            GameObject go;
            try
            {
                go = Core.AddPrefab(
                    st.sensor, pos, Quaternion.Euler(90f, yaw, 0f), null,
                    worldSpace: true);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't show enemy #{by}'s {name}: {e.Message}");
                return;
            }

            if (go == null)
            {
                return;
            }

            var ti = go.GetComponent<ThrownItem>();
            var rb = go.GetComponent<Rigidbody>();
            if (!LandingRules.ShowsCopy(ti != null, rb != null))
            {
                Object.Destroy(go);
                return;
            }

            _sensorCopies.Add(ti);
            rb.drag = LandingRules.SensorDrag;
            Fly(ti, rb, thrower, vel);
            ti.landTarget = new Vector3(land.x, pos.y, land.z);
            ti.setFallSpeed(st.throwStrength);
            ti.thrown = true;
            Plugin.Instance.StartCoroutine(Expire(ti, go.transform));
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] got enemy #{by}'s {name} ({go.name}) thrown at "
                        + $"{pos}");
            }
        }

        /// <summary>
        /// Client: one of our copy's sensor types, for SensorIndex.
        /// </summary>
        private static SensorChoice ChoiceOf(Character.SensorType x)
        {
            if (x == null)
            {
                return new SensorChoice();
            }

            return new SensorChoice
            {
                Ranged = x.isRanged,
                Name = x.name,
                HasSensor = x.sensor != null,
            };
        }

        /// <summary>
        /// Client: copies made with Core.AddPrefab (not pooled): destroyed when
        /// they go, not put back.
        /// </summary>
        private readonly HashSet<ThrownItem> _sensorCopies =
            new HashSet<ThrownItem>();

        /// <summary>
        /// Client: a copy that hit nothing in a few seconds goes anyway.
        /// </summary>
        private System.Collections.IEnumerator Expire(
            ThrownItem ti, Transform t)
        {
            yield return new WaitForSeconds(LandingRules.CopyLife);
            if (Coop.Current == _coop && ti != null && _copies.Remove(ti))
            {
                Gone(t);
            }
        }

        private void Gone(Transform t)
        {
            if (t == null)
            {
                return;
            }

            var sti = t.GetComponent<ThrownItem>();
            if (sti != null && _sensorCopies.Remove(sti))
            {
                // it checks for landing each frame until it's destroyed
                sti.thrown = false;
                t.gameObject.SetActive(false);
                Object.Destroy(t.gameObject);
                return;
            }

            try
            {
                Core.RemovePooledPrefab(t);
            }
            catch (System.Exception e)
            {
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        "[world] couldn't put back a projectile copy: "
                            + $"{e.Message}");
                }
            }

            if (t != null && t.gameObject.activeSelf)
            {
                t.gameObject.SetActive(false);
            }
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
        /// Client: whether this character may throw (Character
        /// .spawnProjectile): not a copy of the host's enemy.
        /// </summary>
        internal bool MayThrow(Character c)
        {
            if (c == null)
            {
                return true;
            }

            bool copy = NetEnemies.Instance != null
                && NetEnemies.Instance.IsPuppet(c, out _);
            return LandingRules.MayThrow(
                NetEnemies.ClientMode, NetEnemies.Frozen.Contains(c), copy);
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
        /// Client: ThrownItem.onCollide starts. Whether it's one of our
        /// copies of the host's enemies' projectiles, which then just goes.
        /// </summary>
        internal bool CopyEnds(ThrownItem t)
        {
            if (t == null || !_copies.Remove(t))
            {
                return false;
            }

            Gone(t.transform);
            return true;
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

        private void OnLandedGone(ulong from, PacketReader r)
        {
            if (!LandingRules.FromHost(SendOf(from)))
            {
                return;
            }

            long id = EnemyLandedGoneMessage.Read(r);
            if (!ObjRef.Spawned.TryGetValue(id, out var go) || go == null)
            {
                return;
            }

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] got enemy landed Spawned:{id} gone: {go.name} "
                        + "removed");
            }

            Containers.CloseIfOpen(go);
            Object.Destroy(go);
        }

        /// <summary>
        /// Client: our copy of what the host's enemy left doesn't run out by
        /// itself (WaitAndDie is made permanent); the host says when it's gone.
        /// What it does as a look (the swamper's splat chain spreading its
        /// splats, played the same in every game) still runs.
        /// </summary>
        private static void MakeInert(GameObject go)
        {
            foreach (var w in go.GetComponentsInChildren<WaitAndDie>(true))
            {
                w.permanent = true; // its Update returns at once
            }

            // A thing with a state of its own (an Item: the human spider's
            // Fov_Mob_Spawner) reacts to being seen by the player (its
            // animation's events spawn a mob and end it). That's the host's
            // world: its events are off here.
            if (go.GetComponent<Item>() != null)
            {
                foreach (var m in go.GetComponentsInChildren<MonoBehaviour>(
                    true))
                {
                    if (m is AnimationTriggerListener || m is GameEvents
                        || m is EventTriggers)
                    {
                        m.enabled = false;
                    }
                }
            }
        }

        // What each name's thrown thing leaves (the prefab), found among the
        // loaded ThrownItems (pooled projectiles).
        private readonly Dictionary<string, Object> _prefabs =
            new Dictionary<string, Object>();

        private Object PrefabNamed(string name)
        {
            if (_prefabs.TryGetValue(name, out var o) && o != null)
            {
                return o;
            }

            foreach (var t in Resources.FindObjectsOfTypeAll<ThrownItem>())
            {
                if (t != null && t.prefabToSpawnOnLand != null
                    && t.prefabToSpawnOnLand.name == name)
                {
                    _prefabs[name] = t.prefabToSpawnOnLand;
                    return t.prefabToSpawnOnLand;
                }
            }

            return null;
        }

        private void OnLanded(ulong from, PacketReader r)
        {
            if (!LandingRules.FromHost(SendOf(from)))
            {
                return;
            }

            var landed = EnemyLandedMessage.Read(r);
            long id = landed.Id;
            uint by = landed.By;
            string name = landed.Name;
            var pos = VectorOf(landed.At);
            float yaw = landed.Yaw;
            bool had = ObjRef.Spawned.TryGetValue(id, out var old)
                && old != null;
            Object prefab = null;
            if (!had)
            {
                prefab = PrefabNamed(name);
            }

            var way = LandingRules.Landed(had, prefab != null);
            if (way == LandedWay.Ignore)
            {
                return;
            }

            if (way == LandedWay.Unknown)
            {
                Plugin.Log.LogWarning(
                    $"[world] enemy #{by}'s {name} landed on the host, but no "
                        + "thrown thing here leaves one");
                return;
            }

            _coop.Guards.Replaying = true;
            try
            {
                var go = Core.AddPrefab(
                    prefab, pos, Quaternion.Euler(90f, yaw, 0f), null);
                if (go == null)
                {
                    return;
                }

                go.transform.position = pos;
                Core.addToSaveable(go, isDynamic: true, assignID: true);
                ObjRef.RegisterSpawned(id, go);
                MakeInert(go);
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] got enemy #{by}'s {name} landed: "
                            + $"Spawned:{id} at {pos}");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't make the host's {name}: {e.Message}");
            }
            finally
            {
                _coop.Guards.Replaying = false;
            }
        }
    }
}
