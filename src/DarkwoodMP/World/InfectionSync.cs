using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Infection, shared: the patches of black growth on the ground (Traps/infection_splat: an Infection with a
    /// Ground of GroundType.infection that hurts 8 a second and stops running) that fire clears, as at the border
    /// between the Dry Meadow and the Silent Forest (border_gate_06_meadowForest_01 has nine).
    ///
    /// Cleared: Infection.disappear (a burning melee weapon touching it, MeleeSensor; a Burn on one clears every
    /// patch within 40; burning gasoline sets Burn on patches next to it) removes its Ground at once and fades it
    /// out. Every patch cleared in a frame goes in one InfectionCleared. The host sends all of its own (its world's
    /// fire, its player); a client sends only what its own player's burning weapon touched, as a request: the host
    /// clears those within reach of the player and passes them on. The others clear theirs with Replaying set, so
    /// a cleared patch is never sent back, and a patch already fading isn't cleared twice.
    ///
    /// Spread: a patch made by Infection.init spreads into up to 8 new ones around it after 25-30 s
    /// (spawnInfection), and spiders lay new ones (Character.waitToSpawnEgg). That's the world doing it by itself,
    /// so it's the host's: each new patch on the host is InfectionSpawned, made in the clients' games with the same
    /// id (ObjRef Spawned), and a client's infection never spreads by itself.
    /// </summary>
    public static class InfectionSync
    {
        private static Session S => Session.Instance;
        private static readonly System.Random _rng = new System.Random();
        private static long NewId() => ((long)_rng.Next() << 31) | (uint)_rng.Next();

        private struct Patch { public ObjRef Ref; public float X, Z; }
        private static readonly List<Patch> _cleared = new List<Patch>();

        /// <summary>Host: inside spawnInfection or a spider laying: a new patch made now is sent.</summary>
        internal static int Growing;

        public static void Register(Session s)
        {
            s.Register(MsgType.InfectionCleared, OnCleared);
            s.Register(MsgType.InfectionSpawned, OnSpawned);
            s.OnSessionEnded += () => { _cleared.Clear(); Growing = 0; };
        }

        /// <summary>A patch was cleared in this game, by something that's ours to send.</summary>
        internal static void Cleared(Infection inf)
        {
            var r = ObjRef.Of(inf.gameObject);
            if (!r.Valid) return;
            var p = inf.transform.position;
            _cleared.Add(new Patch { Ref = r, X = p.x, Z = p.z });
        }

        /// <summary>Each frame: send what was cleared this frame, in one message.</summary>
        public static void Update()
        {
            if (_cleared.Count == 0) return;
            if (!WorldActions.Sharing) { _cleared.Clear(); return; }
            var w = S.Begin(MsgType.InfectionCleared);
            Write(w, _cleared);
            string what = $"infection cleared: {_cleared.Count}";
            _cleared.Clear();
            Plugin.Log.LogInfo($"[world] sent {what}");
            if (S.IsHost) S.Broadcast(w, Channel.Control, reliable: true);
            else Authority.Request(w, what, inventory: false);
        }

        private static void Write(PacketWriter w, List<Patch> patches)
        {
            w.W.Write((ushort)patches.Count);
            foreach (var p in patches) { p.Ref.Write(w); w.W.Write(p.X); w.W.Write(p.Z); }
        }

        private static void OnCleared(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !WorldShare.InWorld) return;
            if (s.IsClient && from != s.HostId) return;
            int n = r.R.ReadUInt16();
            var done = new List<Patch>();
            for (int i = 0; i < n; i++)
            {
                var p = new Patch { Ref = ObjRef.Read(r), X = r.R.ReadSingle(), Z = r.R.ReadSingle() };
                var inf = Find(p);
                if (inf == null || inf.disappearing) continue;
                // A client's player clears only what its burning weapon reached.
                if (s.IsHost && Authority.Handling && !Authority.Near(from, inf.transform.position, WorldActions.Reach)) continue;
                WorldActions.Replaying = true;
                try { Clear(inf); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't clear infection {p.Ref}: {e.Message}"); }
                finally { WorldActions.Replaying = false; }
                done.Add(p);
            }
            Plugin.Log.LogInfo($"[world] infection cleared: {done.Count} (from {s.NameOf(from)})" + (done.Count < n ? $", {n - done.Count} not here, already gone or out of reach" : ""));
            if (s.IsHost && Authority.Handling && done.Count == 0 && n > 0) { Authority.Refuse("none of that infection is within their reach"); return; }
            if (s.IsHost && done.Count > 0)
            {
                var w = s.Begin(MsgType.InfectionCleared);
                Write(w, done);
                s.Broadcast(w, Channel.Control, reliable: true, except: from);
            }
        }

        /// <summary>Clear it as the game does; one culled (inactive) here can't play its fade, so it's just removed.</summary>
        private static void Clear(Infection inf)
        {
            if (inf.gameObject.activeInHierarchy) inf.disappear();
            else { inf.disappearing = true; Object.Destroy(inf.gameObject); }
        }

        /// <summary>The patch by its name across games, or else the one nearest where it was (patches are 70 apart).</summary>
        private static Infection Find(Patch p)
        {
            var inf = ObjRef.Resolve<Infection>(p.Ref);
            if (inf != null) return inf;
            Infection best = null; float bestD = 20f * 20f;
            foreach (var i in Resources.FindObjectsOfTypeAll<Infection>())
            {
                if (i == null || !i.gameObject.scene.IsValid() || i.disappearing) continue;
                var pos = i.transform.position;
                float d = (pos.x - p.X) * (pos.x - p.X) + (pos.z - p.Z) * (pos.z - p.Z);
                if (d <= bestD) { bestD = d; best = i; }
            }
            if (best == null && WorldActions.Debug) Plugin.Log.LogInfo($"[world] infection {p.Ref} not found here");
            return best;
        }

        // ---- spread (the host's) ---------------------------------------------------------------------

        /// <summary>Host: a new patch was made by the world (spread or a spider): send it.</summary>
        internal static void Spawned(Infection inf)
        {
            long id = NewId();
            ObjRef.RegisterSpawned(id, inf.gameObject);
            var parent = inf.transform.parent != null ? ObjRef.Of(inf.transform.parent.gameObject) : default;
            var p = inf.transform.position;
            var w = S.Begin(MsgType.InfectionSpawned);
            w.W.Write(id);
            w.W.Write(p.x); w.W.Write(p.y); w.W.Write(p.z);
            parent.Write(w);
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] sent infection spawned Spawned:{id} at {p}");
            S.Broadcast(w, Channel.Control, reliable: true);
        }

        private static void OnSpawned(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !WorldShare.InWorld || !s.IsClient || from != s.HostId) return;
            long id = r.R.ReadInt64();
            var pos = new Vector3(r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle());
            var parentRef = ObjRef.Read(r);
            if (ObjRef.Spawned.TryGetValue(id, out var had) && had != null) return;
            var parent = parentRef.Valid ? ObjRef.Resolve<Transform>(parentRef) : null;
            WorldActions.Replaying = true;
            try
            {
                var go = Core.AddPrefab("Traps/infection_splat", pos, Quaternion.Euler(90f, 0f, 0f), parent != null ? parent.gameObject : Core.ItemContainer, worldSpace: true);
                var inf = go != null ? go.GetComponent<Infection>() : null;
                if (inf == null) return;
                ObjRef.RegisterSpawned(id, go);
                inf.timesToSpread = 0; // the host's spreads; its new patches come as messages
                inf.init();
                if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] got infection spawned Spawned:{id} at {pos}");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't make the host's infection: {e.Message}"); }
            finally { WorldActions.Replaying = false; }
        }
    }

    [HarmonyPatch(typeof(Infection), nameof(Infection.disappear))]
    static class InfectionDisappearPatch
    {
        static void Prefix(Infection __instance, out bool __state) => __state = __instance != null && !__instance.disappearing;
        static void Postfix(Infection __instance, bool __state)
        {
            if (!__state || __instance == null || !__instance.disappearing || !WorldActions.Sharing) return;
            // A client's own player's burning weapon only; its copy of the host's fire clears what the host's does,
            // and the host says so.
            if (NetEnemies.ClientMode && !LiquidLitPatch.OwnSwing) return;
            InfectionSync.Cleared(__instance);
        }
    }

    /// <summary>A patch spreading: the host's, sent; a client's copy never spreads by itself.</summary>
    [HarmonyPatch(typeof(Infection), "spawnInfection")]
    static class InfectionSpreadPatch
    {
        static bool Prefix()
        {
            if (NetEnemies.ClientMode) return false;
            InfectionSync.Growing++;
            return true;
        }
        static System.Exception Finalizer(System.Exception __exception)
        {
            if (!NetEnemies.ClientMode && InfectionSync.Growing > 0) InfectionSync.Growing--;
            return __exception;
        }
    }

    /// <summary>A spider laying its eggs: a patch of infection where it stands (the host's spiders only).</summary>
    [HarmonyPatch(typeof(Character), "waitToSpawnEgg")]
    static class SpiderLaysInfectionPatch
    {
        static void Prefix() => InfectionSync.Growing++;
        static System.Exception Finalizer(System.Exception __exception)
        {
            if (InfectionSync.Growing > 0) InfectionSync.Growing--;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Infection), nameof(Infection.init))]
    static class InfectionInitPatch
    {
        static void Postfix(Infection __instance)
        {
            var s = Session.Instance;
            if (InfectionSync.Growing <= 0 || __instance == null || s == null || !s.IsHost || !WorldActions.Sharing) return;
            InfectionSync.Spawned(__instance);
        }
    }
}
