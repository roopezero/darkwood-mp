using System.Collections;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Things on fire (the game's Burn: flames and the fire sound on an enemy, a door, a window, furniture),
    /// shared from the host. The host's world is the one that burns: there, fire hurts enemies and breaks doors
    /// and furniture, and those hits reach the others as before (enemy health, DoorHit...). When something starts
    /// or stops burning there, the clients light or put out their copy of it, for the look and sound.
    ///
    /// On a client, fire only shows: Burn's damage over time (Burn.waitToGetHit) doesn't run there, except on
    /// the client's own player, whose health is the client's. Fire a client's own throw starts reaches the host
    /// through the host's copy of the throw (ThrowSync). Fire a client's own flamethrower starts on a door, a window or
    /// furniture is sent to the host as a Burning request: the host checks the player is within reach and the thing
    /// can burn, and lights its copy, which then burns for everyone; a refused one is put out again.
    ///
    /// Burning: ObjRef, u8 what (1 enemy, 2 door, 3 window, 4 item), f32 burn time, u8 on.
    /// </summary>
    public sealed class FireSync
    {
        public static FireSync Instance { get; private set; }
        private readonly Session _session;

        public enum What : byte { None = 0, Enemy = 1, Door = 2, Window = 3, Item = 4 }

        public FireSync(Session session)
        {
            Instance = this;
            _session = session;
            session.Register(MsgType.Burning, OnBurning);
        }

        private static What Kind(GameObject go)
        {
            if (go.GetComponent<Player>() != null || go.GetComponent<NetPlayerProxy>() != null) return What.None;
            if (go.GetComponent<Character>() != null) return What.Enemy;
            if (go.GetComponent<Door>() != null) return What.Door;
            if (go.GetComponent<Window>() != null) return What.Window;
            if (go.GetComponent<Item>() != null) return What.Item;
            return What.None;
        }

        /// <summary>Host: something started (on) or stopped burning here.</summary>
        internal void Changed(Burn b, bool on)
        {
            if (b == null || WorldActions.Replaying || !_session.IsHost || _session.Peers.Count == 0 || !WorldShare.InWorld) return;
            var what = Kind(b.gameObject);
            if (what == What.None) return;
            var r = ObjRef.Of(b.gameObject);
            if (!r.Valid) return;
            var w = _session.Begin(MsgType.Burning);
            r.Write(w);
            w.W.Write((byte)what);
            w.W.Write(b.burnTime);
            w.W.Write((byte)(on ? 1 : 0));
            _session.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] {b.name} {(on ? "caught fire" : "stopped burning")} ({r})");
        }

        /// <summary>
        /// What a flame touching this would set alight, found as Flame.onCollideWith finds it: furniture that can
        /// break, a door, a window. Null for a character (its burn is an effect on the hit) or anything else.
        /// </summary>
        internal static GameObject Flammable(Transform t, Collider col)
        {
            if (col == null) return null;
            if (col.transform.GetComponent<CharBase>() != null || (t != null && t.GetComponent<CharBase>() != null)) return null;
            var item = col.transform.GetComponent<Item>();
            if (item == null && t != null) item = t.GetComponent<Item>();
            if (item != null && item.destructible) return item.gameObject;
            var door = Door.getDoorScript(col.transform);
            if (door != null) return door.gameObject;
            var window = col.GetComponent<Window>();
            return window != null ? window.gameObject : null;
        }

        /// <summary>Client: our player's flame set this alight here; ask the host to light its copy (put ours out if it says no).</summary>
        internal void ClientIgnited(GameObject go)
        {
            if (go == null || !NetEnemies.ClientMode || WorldActions.Replaying) return;
            var what = Kind(go);
            if (what == What.None || what == What.Enemy) return;
            var r = ObjRef.Of(go);
            if (!r.Valid) return;
            var burn = go.GetComponent<Burn>();
            var w = _session.Begin(MsgType.Burning);
            r.Write(w);
            w.W.Write((byte)what);
            w.W.Write(burn != null ? burn.burnTime : 20f);
            w.W.Write((byte)1);
            Authority.Request(w, $"set {go.name} alight", (ok, reason, result) =>
            {
                if (ok || go == null) return;
                var b = go.GetComponent<Burn>();
                if (b == null) return;
                WorldActions.Replaying = true;
                try { b.stop(); }
                finally { WorldActions.Replaying = false; }
            });
        }

        /// <summary>Host: a client's player set this alight (a Burning request).</summary>
        private void OnClientIgnited(ObjRef obj, What what, float time, bool on)
        {
            if (!Authority.Handling) return; // only as a request
            if (!on) { Authority.Refuse("players only light things"); return; }
            Component c = what == What.Enemy ? null : Resolve(obj, what);
            if (c == null) { Authority.Refuse("there's no such thing here to burn"); return; }
            if (!Authority.InReach(c.transform.position, FlameReach, c.name)) return;
            if (c is Item item && (!item.destructible || item.destroyed)) { Authority.Refuse($"{c.name} can't burn"); return; }
            if (c is Door door && door.destroyed) { Authority.Refuse($"{c.name} is broken"); return; }
            if (c.GetComponent<Burn>() != null) return; // burning already: nothing to do
            c.gameObject.AddComponent<Burn>().burnTime = Mathf.Clamp(time, 1f, 20f);
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] {_session.NameOf(Authority.From)} set {c.name} alight ({obj})");
        }

        /// <summary>How far a player's flames reach, generously (a flamethrower's flame flies 700 a second for 1 to 1.5 s).</summary>
        public const float FlameReach = 1500f;

        private static Component Resolve(ObjRef obj, What what)
        {
            switch (what)
            {
                case What.Enemy: return ObjRef.Resolve<Character>(obj);
                case What.Door: return ObjRef.Resolve<Door>(obj);
                case What.Window: return ObjRef.Resolve<Window>(obj);
                case What.Item: return ObjRef.Resolve<Item>(obj);
                default: return null;
            }
        }

        private void OnBurning(ulong from, PacketReader r)
        {
            var s = _session;
            if (!WorldShare.InWorld) return;
            var obj = ObjRef.Read(r);
            var what = (What)r.R.ReadByte();
            float time = r.R.ReadSingle();
            bool on = r.R.ReadByte() != 0;
            if (s.IsHost) { OnClientIgnited(obj, what, time, on); return; }
            if (!s.IsClient || from != s.HostId) return;
            Component c = Resolve(obj, what);
            if (what == What.None || (int)what > 4) return;
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] got Burning {obj} {(on ? "on" : "off")}: " + (c != null ? c.name : "not found here"));
            if (c == null) return;
            var burn = c.GetComponent<Burn>();
            WorldActions.Replaying = true;
            try
            {
                if (on && burn == null) c.gameObject.AddComponent<Burn>().burnTime = time;
                else if (!on && burn != null) burn.stop();
            }
            finally { WorldActions.Replaying = false; }
        }
    }

    [HarmonyPatch(typeof(Burn), "Start")]
    static class BurnStartPatch
    {
        static void Postfix(Burn __instance) => FireSync.Instance?.Changed(__instance, on: true);
    }

    [HarmonyPatch(typeof(Burn), nameof(Burn.stop))]
    static class BurnStopPatch
    {
        static void Prefix(Burn __instance) => FireSync.Instance?.Changed(__instance, on: false);
    }

    /// <summary>Client: fire hurts only our own player here; the rest burns on the host.</summary>
    [HarmonyPatch(typeof(Burn), nameof(Burn.waitToGetHit))]
    static class ClientBurnDamagePatch
    {
        static bool Prefix(Burn __instance, ref IEnumerator __result)
        {
            if (!NetEnemies.ClientMode || __instance.GetComponent<Player>() != null) return true;
            __result = Nothing();
            return false;
        }

        private static IEnumerator Nothing() { yield break; }
    }
}
