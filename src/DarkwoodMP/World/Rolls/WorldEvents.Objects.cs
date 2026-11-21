using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Rolls
{
    public sealed partial class WorldEvents
    {
        // ---- random objects at dawn, night mushrooms ------------------------

        private const byte DawnObjects = 1, NightObject = 2;
        private static readonly
            AccessTools.FieldRef<WorldChunk, List<GameObject>> RandomObjectsOf =
                AccessTools.FieldRefAccess<WorldChunk, List<GameObject>>(
                    "randomObjects");
        private readonly System.Random _rng = new System.Random();
        private long NewId() => ((long)_rng.Next() << 31) | (uint)_rng.Next();

        /// <summary>
        /// Host: the dawn's random objects were made; send them once their
        /// Start has filled what they hold.
        /// </summary>
        internal void RandomObjectsMade()
        {
            if (HostSharing)
            {
                _randomDue = Time.unscaledTime + 2f;
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

            var all = new List<(long id, int chunk, int obj, Vector3 local,
                string contents)>();
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

                    all.Add((id, c, oi, local, contents));
                }
            }

            var w = _session.Begin(MsgType.RandomObjects);
            w.W.Write(DawnObjects);
            w.W.Write((ushort)all.Count);
            foreach (var o in all)
            {
                w.W.Write(o.id);
                w.W.Write((ushort)o.chunk);
                w.W.Write((ushort)o.obj);
                w.Write(o.local);
                w.WriteStr(o.contents);
            }

            _session.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent {all.Count} random objects of the dawn");
            }
        }

        /// <summary>
        /// Host: a night mushroom was made (WormsSpawner.spawn).
        /// </summary>
        internal void SendNightObject(GameObject go, string prefab)
        {
            if (!HostSharing || go == null)
            {
                return;
            }

            long id = NewId();
            ObjRef.RegisterSpawned(id, go);
            var w = _session.Begin(MsgType.RandomObjects);
            w.W.Write(NightObject);
            w.W.Write(id);
            w.WriteStr(prefab);
            w.Write(go.transform.position);
            _session.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent night object {prefab} #{id} at "
                        + $"{go.transform.position}");
            }
        }

        private void OnRandomObjects(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !WorldShare.InWorld)
            {
                return;
            }

            byte kind = r.R.ReadByte();
            if (kind == DawnObjects)
            {
                int n = r.R.ReadUInt16();
                var list = new List<(long id, int chunk, int obj,
                    Vector3 local, string contents)>(n);
                for (int i = 0; i < n; i++)
                {
                    list.Add(
                        (r.R.ReadInt64(), r.R.ReadUInt16(), r.R.ReadUInt16(), r
                            .ReadVector3(), r.ReadStr()));
                }

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
                Coop.Guards.Replaying = true;
                try
                {
                    foreach (var o in list)
                    {
                        if (o.chunk >= gen.worldChunks.Count
                            || o.obj >= rwo.objects.Count)
                        {
                            continue;
                        }

                        var chunk = gen.worldChunks[o.chunk];
                        RandomWorldObjects.RandomObject randomObject =
                            rwo.objects[o.obj];
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
                            prefab, o.local, Quaternion.Euler(90f, 0f, 0f),
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
                        ObjRef.RegisterSpawned(o.id, go);
                        var inv = go.GetComponent<Inventory>();
                        if (inv != null && o.contents.Length > 0)
                        {
                            CoopSession coop = Coop.Current;
                            if (coop != null)
                            {
                                coop.Containers.Apply(inv, o.contents);
                            }
                        }

                        made++;
                    }
                }
                finally
                {
                    Coop.Guards.Replaying = false;
                }

                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] made the host's {made} of {n} random objects "
                            + "of the dawn");
                }
            }
            else if (kind == NightObject)
            {
                long id = r.R.ReadInt64();
                string name = r.ReadStr();
                var pos = r.ReadVector3();
                if (!NetEnemies.ClientMode || ObjRef.Spawned.ContainsKey(id))
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
                Coop.Guards.Replaying = true;
                try
                {
                    go = Core.AddPrefab(
                        prefab, pos, Quaternion.Euler(90f, 0f, 0f), null,
                        worldSpace: true);
                }
                finally
                {
                    Coop.Guards.Replaying = false;
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
}
