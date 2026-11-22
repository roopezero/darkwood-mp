using System.Collections.Generic;
using DarkwoodMP.Combat.Landings;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using DarkwoodMP.World.Inventories;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Client: the host's enemies' throws shown as harmless copies, and what
    /// they left made here with the host's ids.
    /// </summary>
    internal sealed partial class EnemyLandings
    {
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
        /// Client: copies made with Core.AddPrefab (not pooled): destroyed when
        /// they go, not put back.
        /// </summary>
        private readonly HashSet<ThrownItem> _sensorCopies =
            new HashSet<ThrownItem>();

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

            bool copy = _coop.Enemies.IsPuppet(c, out _);
            return LandingRules.MayThrow(
                NetEnemies.ClientMode,
                _coop.Enemies.Frozen.Contains(c),
                copy);
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

            NetEnemies instance = _coop.Enemies;
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
            NetEnemies instance = _coop.Enemies;
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
