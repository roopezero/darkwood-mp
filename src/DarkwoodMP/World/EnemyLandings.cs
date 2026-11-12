using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// What the host's enemies throw and leave behind, shared. An enemy's projectile (a ThrownItem: a pooled one from
    /// Character.spawnProjectile, or its sensor's prefab from Character.rangedAttack, as the human spider throws) flies
    /// and lands only in the host's game, since a client's
    /// copies of the host's enemies don't act. Where it lands, the game makes its prefabToSpawnOnLand
    /// (ThrownItem.onCollide: the human spider's leaves a Fov_Mob_Spawner) and saves it. On the host, that thing is
    /// sent as EnemyLanded with a new spawned id; the clients make the same prefab there with that id (ObjRef
    /// Spawned), so hits on it and its breaking are shared like any other item's (BreakSync). What it spawns later
    /// is the host's world doing it: enemies come to clients as the host's enemies.
    ///
    /// A client's copy of a host enemy never throws (spawnProjectile is skipped for it), so it can't make anything
    /// of its own.
    /// </summary>
    public static class EnemyLandings
    {
        private static Session S => Session.Instance;
        private static readonly System.Random _rng = new System.Random();

        /// <summary>Host: the landing thrown thing of one of our enemies, while ThrownItem.onCollide runs.</summary>
        internal static ThrownItem Landing;
        internal static uint LandingBy;
        internal static readonly List<GameObject> Made = new List<GameObject>();

        public static void Register(Session s)
        {
            s.Register(MsgType.EnemyLanded, OnLanded);
            s.Register(MsgType.EnemyProjectile, OnProjectile);
            s.Register(MsgType.EnemyLandedGone, OnLandedGone);
            s.Register(MsgType.EnemySensorThrown, OnSensorThrown);
            s.OnSessionEnded += () =>
            {
                Landing = null; Made.Clear(); _landed.Clear(); Throwing = null; Thrown.Clear(); Copies.Clear();
                SensorThrowing = null; SensorThrown.Clear(); SensorCopies.Clear();
            };
        }

        // ---- the throw (a look on clients) ------------------------------------------------------------

        /// <summary>Host: the projectile type one of our shared enemies is throwing, while spawnProjectile runs.</summary>
        internal static string Throwing;
        internal static uint ThrowingBy;
        internal static readonly List<Transform> Thrown = new List<Transform>();

        /// <summary>Client: how many of the host's enemies' projectiles were shown here (for the tests).</summary>
        internal static int Shown;

        /// <summary>Client: copies of the host's enemies' projectiles, flying for the look only.</summary>
        internal static readonly HashSet<ThrownItem> Copies = new HashSet<ThrownItem>();

        /// <summary>Host: after spawnProjectile, its projectile (now aimed and moving) is sent.</summary>
        internal static void Threw()
        {
            foreach (var t in Thrown)
            {
                if (t == null) continue;
                var rb = t.GetComponent<Rigidbody>();
                var v = rb != null ? rb.velocity : Vector3.zero;
                var p = t.position;
                var w = S.Begin(MsgType.EnemyProjectile);
                w.W.Write(ThrowingBy);
                w.WriteStr(Throwing);
                w.W.Write(p.x); w.W.Write(p.y); w.W.Write(p.z);
                w.W.Write(t.eulerAngles.y);
                w.W.Write(v.x); w.W.Write(v.y); w.W.Write(v.z);
                S.Broadcast(w, Channel.State, reliable: true);
                if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] sent enemy #{ThrowingBy}'s {Throwing} thrown at {p}");
            }
            Thrown.Clear();
        }

        private static void OnProjectile(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !WorldShare.InWorld || !s.IsClient || from != s.HostId) return;
            uint by = r.R.ReadUInt32();
            string type = r.ReadStr();
            var pos = new Vector3(r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle());
            float yaw = r.R.ReadSingle();
            var vel = new Vector3(r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle());
            Transform t;
            try { t = Core.AddPooledPrefab(type, pos, Quaternion.Euler(90f, yaw, 0f)); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't show enemy #{by}'s {type}: {e.Message}"); return; }
            if (t == null) return;
            var ti = t.GetComponent<ThrownItem>();
            var rb = t.GetComponent<Rigidbody>();
            if (ti == null || rb == null) { Core.RemovePooledPrefab(t); return; } // only thrown things are shown
            Copies.Add(ti);
            Shown++;
            var thrower = NetEnemies.Instance?.CharacterOf(by);
            var col = t.GetComponent<Collider>();
            if (thrower != null && thrower.collider != null && col != null) Physics.IgnoreCollision(col, thrower.collider);
            ti.objectThatSpawnedMe = thrower != null ? thrower.transform : null;
            ti.damage = 0;
            rb.velocity = vel;
            rb.AddTorque(0f, 225000f, 0f);
            ti.thrown = true;
            Plugin.Instance.StartCoroutine(Expire(ti, t));
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] got enemy #{by}'s {type} thrown at {pos}");
        }

        // ---- a ranged sensor thrown (Character.rangedAttack: the human spider's projectile) -------------

        /// <summary>Host: the ranged sensor one of our shared enemies is throwing, while rangedAttack runs.</summary>
        internal static string SensorThrowing;
        internal static uint SensorThrowingBy;
        internal static readonly List<ThrownItem> SensorThrown = new List<ThrownItem>();

        /// <summary>Host: after rangedAttack, what it threw (now aimed and moving) is sent as EnemySensorThrown.</summary>
        internal static void SensorThrew()
        {
            foreach (var ti in SensorThrown)
            {
                if (ti == null) continue;
                var t = ti.transform;
                var rb = t.GetComponent<Rigidbody>();
                var v = rb != null ? rb.velocity : Vector3.zero;
                var p = t.position;
                var w = S.Begin(MsgType.EnemySensorThrown);
                w.W.Write(SensorThrowingBy);
                w.WriteStr(SensorThrowing);
                w.W.Write(p.x); w.W.Write(p.y); w.W.Write(p.z);
                w.W.Write(t.eulerAngles.y);
                w.W.Write(v.x); w.W.Write(v.y); w.W.Write(v.z);
                w.W.Write(ti.landTarget.x); w.W.Write(ti.landTarget.z);
                S.Broadcast(w, Channel.State, reliable: true);
                if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] sent enemy #{SensorThrowingBy}'s {SensorThrowing} ({ti.name}) thrown at {p}");
            }
            SensorThrown.Clear();
        }

        /// <summary>Client: make what the host's enemy threw from our copy's own sensor, and let it fly for the look.</summary>
        private static void OnSensorThrown(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !WorldShare.InWorld || !s.IsClient || from != s.HostId) return;
            uint by = r.R.ReadUInt32();
            string name = r.ReadStr();
            var pos = new Vector3(r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle());
            float yaw = r.R.ReadSingle();
            var vel = new Vector3(r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle());
            var land = new Vector3(r.R.ReadSingle(), 0f, r.R.ReadSingle());
            var thrower = NetEnemies.Instance?.CharacterOf(by);
            if (thrower == null) return; // not shown here: nobody would see it
            Character.SensorType st = null;
            foreach (var x in thrower.sensorTypes)
                if (x != null && x.isRanged && x.name == name && x.sensor != null) { st = x; break; }
            if (st == null) { Plugin.Log.LogWarning($"Couldn't show enemy #{by}'s {name}: our {thrower.name} has no such ranged sensor"); return; }
            GameObject go;
            try { go = Core.AddPrefab(st.sensor, pos, Quaternion.Euler(90f, yaw, 0f), null, worldSpace: true); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't show enemy #{by}'s {name}: {e.Message}"); return; }
            if (go == null) return;
            var ti = go.GetComponent<ThrownItem>();
            var rb = go.GetComponent<Rigidbody>();
            if (ti == null || rb == null) { Object.Destroy(go); return; } // only thrown things are shown
            SensorCopies.Add(ti);
            Copies.Add(ti);
            Shown++;
            var col = go.GetComponent<Collider>();
            if (thrower.collider != null && col != null) Physics.IgnoreCollision(col, thrower.collider);
            ti.objectThatSpawnedMe = thrower.transform;
            ti.damage = 0;
            rb.drag = 2f; // as rangedAttack sets it
            rb.velocity = vel;
            rb.AddTorque(0f, 225000f, 0f);
            ti.landTarget = new Vector3(land.x, pos.y, land.z);
            ti.setFallSpeed(st.throwStrength);
            ti.thrown = true;
            Plugin.Instance.StartCoroutine(Expire(ti, go.transform));
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] got enemy #{by}'s {name} ({go.name}) thrown at {pos}");
        }

        /// <summary>Client: copies made with Core.AddPrefab (not pooled): destroyed when they go, not put back.</summary>
        internal static readonly HashSet<ThrownItem> SensorCopies = new HashSet<ThrownItem>();

        /// <summary>Client: a copy that hit nothing in a few seconds goes anyway.</summary>
        private static System.Collections.IEnumerator Expire(ThrownItem ti, Transform t)
        {
            yield return new WaitForSeconds(4f);
            if (ti != null && Copies.Remove(ti)) Gone(t);
        }

        internal static void Gone(Transform t)
        {
            if (t == null) return;
            var sti = t.GetComponent<ThrownItem>();
            if (sti != null && SensorCopies.Remove(sti))
            {
                sti.thrown = false; // it checks for landing each frame until it's destroyed
                t.gameObject.SetActive(false);
                Object.Destroy(t.gameObject);
                return;
            }
            try { Core.RemovePooledPrefab(t); } catch { }
            if (t != null && t.gameObject.activeSelf) t.gameObject.SetActive(false);
        }

        /// <summary>Host: whether this landing thrown thing was thrown by one of the host's shared enemies.</summary>
        internal static bool ByHostEnemy(ThrownItem t, out uint by)
        {
            by = 0;
            var s = S;
            if (t == null || t.objectThatSpawnedMe == null || s == null || !s.IsHost || s.Peers.Count == 0 || !WorldActions.Sharing) return false;
            var c = t.objectThatSpawnedMe.GetComponent<Character>();
            return c != null && NetEnemies.Instance != null && NetEnemies.Instance.NetIdOf(c, out by);
        }

        /// <summary>Host: after the landing, send what it left.</summary>
        internal static void Landed()
        {
            foreach (var go in Made)
            {
                if (go == null) continue;
                long id = ((long)_rng.Next() << 31) | (uint)_rng.Next();
                ObjRef.RegisterSpawned(id, go);
                var p = go.transform.position;
                var w = S.Begin(MsgType.EnemyLanded);
                w.W.Write(id);
                w.W.Write(LandingBy);
                w.WriteStr(go.name);
                w.W.Write(p.x); w.W.Write(p.y); w.W.Write(p.z);
                w.W.Write(go.transform.eulerAngles.y);
                S.Broadcast(w, Channel.Control, reliable: true);
                if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] sent enemy #{LandingBy}'s {go.name} landed: Spawned:{id} at {p}");
                _landed[id] = go;
            }
            Made.Clear();
            if (_landed.Count > 0 && !_watching) { _watching = true; Plugin.Instance.StartCoroutine(WatchLanded()); }
        }

        // Host: what our enemies' projectiles left, until it's gone (then EnemyLandedGone).
        private static readonly Dictionary<long, GameObject> _landed = new Dictionary<long, GameObject>();
        private static bool _watching;

        private static System.Collections.IEnumerator WatchLanded()
        {
            var gone = new List<long>();
            while (_landed.Count > 0)
            {
                yield return new WaitForSeconds(0.5f);
                var s = S;
                if (s == null || !s.IsHost) { _landed.Clear(); break; }
                gone.Clear();
                foreach (var kv in _landed)
                {
                    var go = kv.Value;
                    if (go == null || !go.activeSelf) gone.Add(kv.Key); // destroyed, or switched off as it ended
                }
                foreach (long id in gone)
                {
                    _landed.Remove(id);
                    var w = s.Begin(MsgType.EnemyLandedGone);
                    w.W.Write(id);
                    s.Broadcast(w, Channel.Control, reliable: true);
                    if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] sent enemy landed Spawned:{id} gone");
                }
            }
            _watching = false;
        }

        private static void OnLandedGone(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !WorldShare.InWorld || !s.IsClient || from != s.HostId) return;
            long id = r.R.ReadInt64();
            if (!ObjRef.Spawned.TryGetValue(id, out var go) || go == null) return;
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] got enemy landed Spawned:{id} gone: {go.name} removed");
            Object.Destroy(go);
        }

        /// <summary>
        /// Client: our copy of what the host's enemy left doesn't run out by itself (WaitAndDie is made permanent);
        /// the host says when it's gone. What it does as a look (the swamper's splat chain spreading its splats,
        /// played the same in every game) still runs.
        /// </summary>
        private static void MakeInert(GameObject go)
        {
            foreach (var w in go.GetComponentsInChildren<WaitAndDie>(true)) w.permanent = true; // its Update returns at once
            // A thing with a state of its own (an Item: the human spider's Fov_Mob_Spawner) reacts to being seen by
            // the player (its animation's events spawn a mob and end it). That's the host's world: its events are off here.
            if (go.GetComponent<Item>() != null)
                foreach (var m in go.GetComponentsInChildren<MonoBehaviour>(true))
                    if (m is AnimationTriggerListener || m is GameEvents || m is EventTriggers) m.enabled = false;
        }

        // What each name's thrown thing leaves (the prefab), found among the loaded ThrownItems (pooled projectiles).
        private static readonly Dictionary<string, Object> _prefabs = new Dictionary<string, Object>();

        private static Object PrefabNamed(string name)
        {
            if (_prefabs.TryGetValue(name, out var o) && o != null) return o;
            foreach (var t in Resources.FindObjectsOfTypeAll<ThrownItem>())
                if (t != null && t.prefabToSpawnOnLand != null && t.prefabToSpawnOnLand.name == name) { _prefabs[name] = t.prefabToSpawnOnLand; return t.prefabToSpawnOnLand; }
            return null;
        }

        private static void OnLanded(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !WorldShare.InWorld || !s.IsClient || from != s.HostId) return;
            long id = r.R.ReadInt64();
            uint by = r.R.ReadUInt32();
            string name = r.ReadStr();
            var pos = new Vector3(r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle());
            float yaw = r.R.ReadSingle();
            if (ObjRef.Spawned.TryGetValue(id, out var had) && had != null) return;
            var prefab = PrefabNamed(name);
            if (prefab == null) { Plugin.Log.LogWarning($"[world] enemy #{by}'s {name} landed on the host, but no thrown thing here leaves one"); return; }
            WorldActions.Replaying = true;
            try
            {
                var go = Core.AddPrefab(prefab, pos, Quaternion.Euler(90f, yaw, 0f), null);
                if (go == null) return;
                go.transform.position = pos;
                Core.addToSaveable(go, isDynamic: true, assignID: true);
                ObjRef.RegisterSpawned(id, go);
                MakeInert(go);
                if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] got enemy #{by}'s {name} landed: Spawned:{id} at {pos}");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't make the host's {name}: {e.Message}"); }
            finally { WorldActions.Replaying = false; }
        }
    }

    [HarmonyPatch(typeof(ThrownItem), nameof(ThrownItem.onCollide))]
    static class EnemyLandingPatch
    {
        static void Prefix(ThrownItem __instance, out bool __state)
        {
            __state = EnemyLandings.Landing == null && EnemyLandings.ByHostEnemy(__instance, out EnemyLandings.LandingBy);
            if (__state) { EnemyLandings.Landing = __instance; EnemyLandings.Made.Clear(); }
        }
        static void Postfix(bool __state)
        {
            if (!__state) return;
            EnemyLandings.Landing = null;
            EnemyLandings.Landed();
        }
        static System.Exception Finalizer(bool __state, System.Exception __exception)
        {
            if (__state && __exception != null) { EnemyLandings.Landing = null; EnemyLandings.Made.Clear(); }
            return __exception;
        }
    }

    /// <summary>Host: what the landing makes of its prefabToSpawnOnLand is noted, to send after it.</summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[] { typeof(Object), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool) })]
    static class EnemyLandingSpawnPatch
    {
        static void Postfix(Object prefab, GameObject __result)
        {
            var t = EnemyLandings.Landing;
            if (t != null && __result != null && prefab != null && prefab == t.prefabToSpawnOnLand) EnemyLandings.Made.Add(__result);
        }
    }

    /// <summary>Client: a copy of one of the host's enemies never throws anything (the host's throw lands here as EnemyLanded).</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.spawnProjectile))]
    static class ClientNoEnemyThrowPatch
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Character __instance)
        {
            if (!NetEnemies.ClientMode || __instance == null) return true;
            return !(NetEnemies.Frozen.Contains(__instance) || (NetEnemies.Instance != null && NetEnemies.Instance.IsPuppet(__instance, out _)));
        }
    }

    /// <summary>Host: one of our shared enemies throwing; its projectile is sent once it's aimed (EnemyProjectile).</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.spawnProjectile))]
    static class EnemyThrowPatch
    {
        static void Prefix(Character __instance, string _projectileType, out bool __state)
        {
            var s = Session.Instance;
            __state = EnemyLandings.Throwing == null && s != null && s.IsHost && s.Peers.Count > 0 && WorldActions.Sharing
                      && NetEnemies.Instance != null && NetEnemies.Instance.NetIdOf(__instance, out EnemyLandings.ThrowingBy);
            if (__state) { EnemyLandings.Throwing = _projectileType; EnemyLandings.Thrown.Clear(); }
        }
        static void Postfix(bool __state)
        {
            if (!__state) return;
            EnemyLandings.Threw();
            EnemyLandings.Throwing = null;
        }
        static System.Exception Finalizer(bool __state, System.Exception __exception)
        {
            if (__state && __exception != null) { EnemyLandings.Throwing = null; EnemyLandings.Thrown.Clear(); }
            return __exception;
        }
    }

    [HarmonyPatch]
    static class EnemyThrowSpawnPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
            AccessTools.GetDeclaredMethods(typeof(Core)).FindAll(m => m.Name == nameof(Core.AddPooledPrefab) && m.ReturnType == typeof(Transform));
        static void Postfix(Transform __result)
        {
            if (EnemyLandings.Throwing != null && __result != null && __result.GetComponent<ThrownItem>() != null && !EnemyLandings.Thrown.Contains(__result))
                EnemyLandings.Thrown.Add(__result);
        }
    }

    /// <summary>Client: a copy of the host's enemy's projectile hits nothing and leaves nothing: it just goes.</summary>
    [HarmonyPatch(typeof(ThrownItem), nameof(ThrownItem.onCollide))]
    static class EnemyProjectileCopyPatch
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(ThrownItem __instance)
        {
            if (__instance == null || !EnemyLandings.Copies.Remove(__instance)) return true;
            EnemyLandings.Gone(__instance.transform);
            return false;
        }
    }

    /// <summary>
    /// Host: one of our shared enemies' ranged attack (Character.rangedAttack, which makes its sensor's prefab with
    /// Core.AddPrefab: the human spider's projectile); what it threw is sent once it's aimed (EnemySensorThrown).
    /// Where it lands and whom it hurts are the host's, as for spawnProjectile's.
    /// </summary>
    [HarmonyPatch(typeof(Character), "rangedAttack")]
    static class EnemySensorThrowPatch
    {
        static void Prefix(Character __instance, Character.SensorType sensorType, out bool __state)
        {
            var s = Session.Instance;
            __state = EnemyLandings.SensorThrowing == null && sensorType != null && s != null && s.IsHost && s.Peers.Count > 0
                      && WorldActions.Sharing && NetEnemies.Instance != null && NetEnemies.Instance.NetIdOf(__instance, out EnemyLandings.SensorThrowingBy);
            if (__state) { EnemyLandings.SensorThrowing = sensorType.name ?? ""; EnemyLandings.SensorThrown.Clear(); }
        }
        static void Postfix(bool __state)
        {
            if (!__state) return;
            EnemyLandings.SensorThrew();
            EnemyLandings.SensorThrowing = null;
        }
        static System.Exception Finalizer(bool __state, System.Exception __exception)
        {
            if (__state && __exception != null) { EnemyLandings.SensorThrowing = null; EnemyLandings.SensorThrown.Clear(); }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[] { typeof(Object), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool) })]
    static class EnemySensorSpawnPatch
    {
        static void Postfix(GameObject __result)
        {
            if (EnemyLandings.SensorThrowing == null || __result == null) return;
            var ti = __result.GetComponent<ThrownItem>();
            if (ti != null && !EnemyLandings.SensorThrown.Contains(ti)) EnemyLandings.SensorThrown.Add(ti);
        }
    }
}
