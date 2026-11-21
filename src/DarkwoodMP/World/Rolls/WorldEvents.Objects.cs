using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Rolls
{
    internal sealed partial class WorldEvents
    {
        // ---- random objects at dawn, night mushrooms ------------------------

        private static readonly
            AccessTools.FieldRef<WorldChunk, List<GameObject>> RandomObjectsOf =
                AccessTools.FieldRefAccess<WorldChunk, List<GameObject>>(
                    "randomObjects");
        private readonly System.Random _rng = new System.Random();

        private long NewId()
        {
            int first = _rng.Next();
            int second = _rng.Next();
            return RollRules.ObjectId(first, second);
        }

        /// <summary>
        /// Host: the dawn's random objects were made; send them once their
        /// Start has filled what they hold.
        /// </summary>
        internal void RandomObjectsMade()
        {
            if (!Client && HostSharing)
            {
                _clock.ObjectsMade(Now);
            }
        }

        private void SendRandomObjects()
        {
            var gen = Singleton<WorldGenerator>.Instance;
            var rwo = Singleton<RandomWorldObjects>.Instance;
            if (gen == null || rwo == null)
            {
                return;
            }

            var all = new List<DawnObject>();
            for (int c = 0; c < gen.worldChunks.Count; c++)
            {
                var chunk = gen.worldChunks[c];
                if (chunk == null)
                {
                    continue;
                }

                foreach (var go in RandomObjectsOf(chunk))
                {
                    if (go == null)
                    {
                        continue;
                    }

                    int oi = rwo.objects.FindIndex(
                        o => o != null && o.prefab != null
                            && o.prefab.name == go.name);
                    if (oi < 0)
                    {
                        continue;
                    }

                    var r = ObjRef.Of(go);
                    long id;
                    if (r.K == ObjRef.Kind.Spawned)
                    {
                        id = r.Id;
                    }
                    else
                    {
                        id = NewId();
                    }

                    if (r.K != ObjRef.Kind.Spawned)
                    {
                        ObjRef.RegisterSpawned(id, go);
                    }

                    var inv = go.GetComponent<Inventory>();
                    Vector3 local = go.transform.localPosition;
                    string contents = "";
                    if (inv != null)
                    {
                        contents = JsonConvert.SerializeObject(
                            new Inventory.SaveState(inv));
                    }

                    all.Add(new DawnObject
                    {
                        Id = id,
                        Chunk = c,
                        Object = oi,
                        Local = new Vec3(local.x, local.y, local.z),
                        Contents = contents,
                    });
                }
            }

            var w = Net.Begin(MsgType.RandomObjects);
            RandomObjectsMessage.WriteDawn(w, all);

            Net.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent {all.Count} random objects of the dawn");
            }
        }

        /// <summary>
        /// WormsSpawner.spawn: a joiner in the shared world makes none, and
        /// the host notes how many night objects the player's chunk has, to
        /// send the one it makes. Whether the game's runs.
        /// </summary>
        public bool NightObjectBefore(out NightCount before)
        {
            before = new NightCount(null, 0);
            if (Follows)
            {
                return false;
            }

            var p = Player.Instance;
            // The game lists the new mushroom in the chunk the player is in.
            WorldChunk chunk = null;
            if (p != null)
            {
                chunk = WorldChunk.getChunkAtPos(p._transform.position);
            }

            if (chunk != null)
            {
                before = new NightCount(
                    chunk, chunk.nightSpawnedObjects.Count);
            }

            return true;
        }

        /// <summary>
        /// After WormsSpawner.spawn: the host sends the night mushroom it
        /// made (RollRules.SendsNightObject).
        /// </summary>
        public void NightObjectAfter(NightCount before)
        {
            if (before.Chunk == null)
            {
                return;
            }

            var list = before.Chunk.nightSpawnedObjects;
            var big = Player.Instance.whereAmI.bigLocation;
            bool mushroom = big != null && big.nightMushroom != null;
            if (RollRules.SendsNightObject(before.Count, list.Count, mushroom))
            {
                SendNightObject(list[list.Count - 1], big.nightMushroom.name);
            }
        }

        /// <summary>
        /// Host: a night mushroom was made (WormsSpawner.spawn).
        /// </summary>
        private void SendNightObject(GameObject go, string prefab)
        {
            if (!HostSharing || go == null)
            {
                return;
            }

            long id = NewId();
            ObjRef.RegisterSpawned(id, go);
            Vector3 pos = go.transform.position;
            var w = Net.Begin(MsgType.RandomObjects);
            var sent = new NightMushroom
            {
                Id = id,
                Prefab = prefab,
                Pos = new Vec3(pos.x, pos.y, pos.z),
            };

            RandomObjectsMessage.WriteNight(w, sent);
            Net.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent night object {prefab} #{id} at "
                        + $"{go.transform.position}");
            }
        }

        private void OnRandomObjects(ulong from, PacketReader r)
        {
            if (!FromHost(from))
            {
                return;
            }

            RandomObjectsBody body = RandomObjectsMessage.Read(r);
            if (body.Kind == RandomObjectsMessage.Dawn)
            {
                MakeDawnObjects(body.Dawn);
            }
            else if (body.Kind == RandomObjectsMessage.Night)
            {
                MakeNightObject(body.Night);
            }
        }

        /// <summary>
        /// A joiner, away or not, replaces its dawn's random objects with the
        /// host's.
        /// </summary>
        private void MakeDawnObjects(List<DawnObject> list)
        {
            int n = list.Count;
            if (!NetEnemies.ClientMode)
            {
                return; // away or not: the world's objects are the host's
            }

            var gen = Singleton<WorldGenerator>.Instance;
            var rwo = Singleton<RandomWorldObjects>.Instance;
            if (gen == null || rwo == null)
            {
                return;
            }

            foreach (var chunk in gen.worldChunks)
            {
                if (chunk == null)
                {
                    continue;
                }

                var objs = RandomObjectsOf(chunk);
                foreach (var go in objs)
                {
                    if (go != null)
                    {
                        Object.Destroy(go);
                    }
                }

                objs.Clear();
            }

            int made = 0;
            _coop.Guards.Replaying = true;
            try
            {
                foreach (var o in list)
                {
                    if (!RollRules.InRange(o.Chunk, gen.worldChunks.Count)
                        || !RollRules.InRange(o.Object, rwo.objects.Count))
                    {
                        continue;
                    }

                    var chunk = gen.worldChunks[o.Chunk];
                    RandomWorldObjects.RandomObject randomObject =
                        rwo.objects[o.Object];
                    GameObject prefab = null;
                    if (randomObject != null)
                    {
                        prefab = randomObject.prefab;
                    }

                    if (chunk == null || prefab == null)
                    {
                        continue;
                    }

                    // as WorldChunk.tryToSpawnInChunk
                    var go = Core.AddPrefab(
                        prefab, LocalOf(o), Quaternion.Euler(90f, 0f, 0f),
                        chunk.gameObject);
                    if (go == null)
                    {
                        continue;
                    }

                    Core.addToSaveable(go, isDynamic: true);
                    WorldGrid grid = Singleton<WorldGrid>.Instance;
                    if (grid != null)
                    {
                        grid.registerToNode(go);
                    }

                    RandomObjectsOf(chunk).Add(go);
                    ObjRef.RegisterSpawned(o.Id, go);
                    var inv = go.GetComponent<Inventory>();
                    if (inv != null && o.Contents.Length > 0)
                    {
                        _coop.Containers.Apply(inv, o.Contents);
                    }

                    made++;
                }
            }
            finally
            {
                _coop.Guards.Replaying = false;
            }

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] made the host's {made} of {n} random objects "
                        + "of the dawn");
            }
        }

        private static Vector3 LocalOf(DawnObject o)
        {
            return new Vector3(o.Local.X, o.Local.Y, o.Local.Z);
        }

        /// <summary>
        /// A joiner makes the host's night mushroom, unless it has made it.
        /// </summary>
        private void MakeNightObject(NightMushroom sent)
        {
            long id = sent.Id;
            string name = sent.Prefab;
            var pos = new Vector3(sent.Pos.X, sent.Pos.Y, sent.Pos.Z);
            bool known = ObjRef.Spawned.ContainsKey(id);
            if (!RollRules.TakesNightObject(NetEnemies.ClientMode, known))
            {
                return;
            }

            SaveManager saves = Singleton<SaveManager>.Instance;
            GameObject prefab = null;
            if (saves != null)
            {
                prefab = saves.getPrefab(name);
            }

            if (prefab == null)
            {
                Plugin.Log.LogWarning($"No night object {name} here");
                return;
            }

            GameObject go;
            _coop.Guards.Replaying = true;
            try
            {
                go = Core.AddPrefab(
                    prefab, pos, Quaternion.Euler(90f, 0f, 0f), null,
                    worldSpace: true);
            }
            finally
            {
                _coop.Guards.Replaying = false;
            }

            if (go == null)
            {
                return;
            }

            var chunk = WorldChunk.getChunkAtPos(pos);
            if (chunk != null)
            {
                // gone at dawn, as the host's
                chunk.nightSpawnedObjects.Add(go);
            }

            ObjRef.RegisterSpawned(id, go);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] made the host's night object {name} #{id} at "
                        + $"{pos}");
            }
        }
    }
}
