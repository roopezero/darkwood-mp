using System.Collections.Generic;
using System.Reflection;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Sync
{
    /// <summary>
    /// What the local player sounds and looks like to the others, beyond the sprites PlayerSync sends: the sounds
    /// the game plays on our player (steps, shots, reloads, hammering, pain), a gun's muzzle flash, the blood when
    /// we're hurt and the flames while we burn. Each player's game sends its own; the host passes a client's on to
    /// the others (except the sender), and every game shows it on that player's puppet. It's presentation only:
    /// nothing in it changes the world, so the host has nothing to check.
    /// </summary>
    public sealed class PlayerFx
    {
        public static PlayerFx Instance { get; private set; }
        /// <summary>How many of the other players' shots this game has shown on their puppets (for the map test).</summary>
        public static int ShotsSeen;

        /// <summary>Sounds are batched: one PlayerSounds at most this often, with at most MaxSounds in it.</summary>
        public const float SoundInterval = 0.1f;
        public const int MaxSounds = 16;

        private readonly Session _s;
        private readonly PacketWriter _w = new PacketWriter(); // our own: these are sent from inside game calls
        private readonly List<KeyValuePair<string, float>> _sounds = new List<KeyValuePair<string, float>>();
        private float _nextSounds;
        private bool _burningSent;
        private readonly HashSet<string> _logged = new HashSet<string>();

        /// <summary>Above this many units from the listener, a puppet's sound isn't played (it couldn't be heard).</summary>
        public const float HearingRange = 3000f;

        public PlayerFx(Session session)
        {
            Instance = this;
            _s = session;
            session.Register(MsgType.PlayerSounds, OnSounds);
            session.Register(MsgType.PlayerShot, OnShot);
            session.Register(MsgType.PlayerHurt, OnHurt);
            session.OnPeerConnected += _ => _burningSent = false; // a newcomer hears whether we burn
            session.OnSessionEnded += () => { _sounds.Clear(); _burningSent = false; _logged.Clear(); };
        }

        /// <summary>We're in a world with someone to tell.</summary>
        public bool Sharing => _s.Active && _s.Connected && (_s.IsClient || _s.Peers.Count > 0) && WorldShare.InWorld;

        public void Update()
        {
            if (!Sharing) { _sounds.Clear(); return; }
            float now = Time.unscaledTime;
            if (_sounds.Count > 0 && now >= _nextSounds)
            {
                _nextSounds = now + SoundInterval;
                var w = Begin(MsgType.PlayerSounds);
                int n = Mathf.Min(_sounds.Count, MaxSounds);
                w.W.Write((byte)n);
                for (int i = 0; i < n; i++) { w.WriteStr(_sounds[i].Key); w.W.Write(_sounds[i].Value); }
                Send(w, reliable: false);
                LogOnce("sent sounds", $"[fx] sending our player's sounds ({_sounds[0].Key}...)");
                _sounds.Clear();
            }
            var p = Player.Instance;
            var burn = p != null && p.alive ? p.GetComponent<Burn>() : null;
            if ((burn != null) != _burningSent)
            {
                _burningSent = burn != null;
                var w = Begin(MsgType.PlayerHurt);
                if (burn != null)
                {
                    w.W.Write((byte)2);
                    w.WriteStr(burn.noParticle ? "" : p.burnPrefab != null ? p.burnPrefab.name : "fire_flamesV2_burningEnemy_01");
                    w.WriteStr(burn.noSound ? "" : string.IsNullOrEmpty(burn.burnSoundOverride) ? "fire_loop_01" : burn.burnSoundOverride);
                }
                else w.W.Write((byte)3);
                Send(w, reliable: true);
                Plugin.Log.LogInfo($"[fx] sent: our player {(burn != null ? "started" : "stopped")} burning");
            }
        }

        // ---- what our player does (called from the patches below) ----------------------------------------

        public void OurSound(string audioId, float volume)
        {
            if (_sounds.Count < MaxSounds) _sounds.Add(new KeyValuePair<string, float>(audioId, volume));
        }

        public void OurShot(InvItem gun)
        {
            var w = Begin(MsgType.PlayerShot);
            w.WriteStr(gun.muzzlePrefab != null ? gun.muzzlePrefab.name : "");
            w.WriteStr(gun.muzzleParticles != null ? gun.muzzleParticles.name : "");
            w.W.Write((byte)(gun.noMuzzleFlash ? 0 : 1));
            w.Write(gun.muzzleOffset);
            Send(w, reliable: false);
            LogOnce("sent shot", "[fx] sending our player's shots");
        }

        public void OurBlood(string prefab, GameObject splat)
        {
            var p = Player.Instance;
            var w = Begin(MsgType.PlayerHurt);
            w.W.Write((byte)1);
            w.WriteStr(prefab);
            w.W.Write(splat.transform.position.y - p.transform.position.y);
            w.W.Write(splat.transform.eulerAngles.y);
            Send(w, reliable: false);
            Plugin.Log.LogInfo($"[fx] sent: our player bled ({prefab})");
        }

        private PacketWriter Begin(MsgType t)
        {
            _w.Begin(t);
            _w.W.Write(_s.LocalId); // a client's is replaced by the host anyway
            return _w;
        }

        private void Send(PacketWriter w, bool reliable)
        {
            if (_s.IsHost) _s.Broadcast(w, Channel.State, reliable);
            else _s.SendToHost(w, Channel.State, reliable);
        }

        // ---- what the others do -------------------------------------------------------------------------

        /// <summary>
        /// Who a message is about: on the host, always the client that sent it (whatever it says); on a client,
        /// what the host says, if it came from the host. 0 if it's not to be shown.
        /// </summary>
        private ulong Owner(ulong from, PacketReader r)
        {
            ulong said = r.R.ReadUInt64();
            if (_s.IsHost) return from;
            return from == _s.HostId && said != _s.LocalId ? said : 0;
        }

        /// <summary>The host passes a client's message on to the other clients, with the sender's id in it.</summary>
        private PacketWriter Relay(MsgType t, ulong owner)
        {
            if (!_s.IsHost) return null;
            _w.Begin(t);
            _w.W.Write(owner);
            return _w;
        }

        private void Relayed(PacketWriter w, ulong except, bool reliable)
        {
            if (w != null) _s.Broadcast(w, Channel.State, reliable, except: except);
        }

        private static Transform Puppet(ulong owner)
        {
            var ps = PlayerSync.Instance;
            if (ps == null || !ps.Remotes.TryGetValue(owner, out var rp) || rp.Root == null || !rp.Root.activeInHierarchy) return null;
            return rp.Root.transform;
        }

        private void OnSounds(ulong from, PacketReader r)
        {
            ulong owner = Owner(from, r);
            int n = r.R.ReadByte();
            if (owner == 0 || n > MaxSounds) return;
            var relay = Relay(MsgType.PlayerSounds, owner);
            relay?.W.Write((byte)n);
            var puppet = Puppet(owner);
            bool near = puppet != null && WorldShare.InWorld && Player.Instance != null
                && (Player.Instance.transform.position - puppet.position).sqrMagnitude < HearingRange * HearingRange;
            if (puppet != null && !near) LogOnce($"far {owner}", $"[fx] {_s.NameOf(owner)}'s sounds: too far away to hear (puppet at {puppet.position}, we're at {(Player.Instance != null ? Player.Instance.transform.position.ToString() : "nowhere")})");
            for (int i = 0; i < n; i++)
            {
                string id = r.ReadStr();
                float volume = Mathf.Clamp(r.R.ReadSingle(), 0f, 2f);
                if (relay != null) { relay.WriteStr(id); relay.W.Write(volume); }
                if (near) AudioController.Play(id, puppet, volume);
                if (near && i == 0) LogOnce($"sounds {owner}", $"[fx] {_s.NameOf(owner)}'s sounds play on their puppet ({id})");
            }
            Relayed(relay, from, reliable: false);
        }

        private void OnShot(ulong from, PacketReader r)
        {
            ulong owner = Owner(from, r);
            string muzzle = r.ReadStr(), particles = r.ReadStr();
            byte flash = r.R.ReadByte();
            Vector3 offset = r.ReadVector3();
            if (owner == 0) return;
            var relay = Relay(MsgType.PlayerShot, owner);
            if (relay != null) { relay.WriteStr(muzzle); relay.WriteStr(particles); relay.W.Write(flash); relay.Write(offset); }
            Relayed(relay, from, reliable: false);

            var t = Puppet(owner);
            if (t == null) return;
            ShotsSeen++;
            // As Player.fireWeapon places them on our own player.
            Vector3 at = t.position + t.up * offset.y + t.right * offset.x;
            var rot = Quaternion.Euler(90f, t.eulerAngles.y, 0f);
            if (muzzle != "") Pooled(muzzle, at, rot);
            if (particles != "") Pooled(particles, at, rot);
            if (flash != 0) Core.AddPrefab("FX/Muzzle/PistolFlash", t.position + t.up, rot, null, worldSpace: true);
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[fx] {_s.NameOf(owner)} fired ({muzzle})");
            else LogOnce($"shot {owner}", $"[fx] {_s.NameOf(owner)}'s shots flash on their puppet");
        }

        private void OnHurt(ulong from, PacketReader r)
        {
            ulong owner = Owner(from, r);
            byte what = r.R.ReadByte();
            string a = "", b = ""; float dy = 0f, yaw = 0f;
            if (what == 1) { a = r.ReadStr(); dy = r.R.ReadSingle(); yaw = r.R.ReadSingle(); }
            else if (what == 2) { a = r.ReadStr(); b = r.ReadStr(); }
            else if (what != 3) return;
            if (owner == 0) return;
            var relay = Relay(MsgType.PlayerHurt, owner);
            if (relay != null)
            {
                relay.W.Write(what);
                if (what == 1) { relay.WriteStr(a); relay.W.Write(dy); relay.W.Write(yaw); }
                else if (what == 2) { relay.WriteStr(a); relay.WriteStr(b); }
            }
            Relayed(relay, from, reliable: what != 1);

            string who = _s.NameOf(owner);
            var ps = PlayerSync.Instance;
            RemotePlayer rp = null;
            if (ps == null || !ps.Remotes.TryGetValue(owner, out rp)) return;
            if (what == 1)
            {
                var t = Puppet(owner);
                // Only the game's blood splats: a peer doesn't get to spawn any prefab it names.
                if (t == null || !a.StartsWith("FX/Bloodsplats/")) return;
                Core.AddPrefab(a, t.position + Vector3.up * Mathf.Clamp(dy, -50f, 50f), Quaternion.Euler(90f, yaw, 0f), null);
                Plugin.Log.LogInfo($"[fx] {who} bled ({a}): shown on their puppet");
            }
            else if (what == 2) { rp.ShowBurning(a, b); Plugin.Log.LogInfo($"[fx] {who} is burning: flames on their puppet ({a})"); }
            else { rp.StopBurning(); Plugin.Log.LogInfo($"[fx] {who} stopped burning"); }
        }

        private static void Pooled(string name, Vector3 at, Quaternion rot)
        {
            try { Core.AddPooledPrefab("FX", name, at, rot); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[fx] no \"{name}\" in the FX pool here: {e.Message}"); }
        }

        private void LogOnce(string key, string line)
        {
            if (_logged.Add(key)) Plugin.Log.LogInfo(line);
        }

        // ---- telling which sounds are our player's -------------------------------------------------------

        /// <summary>Inside one of Player's own methods that play its sounds without a place (at the listener).</summary>
        internal static int InPlayerCode;
        /// <summary>Inside Player.getHit: the blood splats it makes are ours.</summary>
        internal static int InGetHit;

        internal static bool IsOurs(Transform parent)
        {
            var p = Player.Instance;
            if (p == null || WorldActions.Replaying) return false;
            if (parent != null) return parent == p.transform || parent.IsChildOf(p.transform);
            // A sound without a place, played by our player's own code (a gunshot, a reload, a swing, pain). Not
            // while a shared world action runs inside it: that thing's own sounds come from its replay elsewhere.
            return InPlayerCode > 0 && WorldActions.Depth == 0;
        }
    }

    /// <summary>A sound the game played: if it's on our player (or by our player's own code), the others hear it too.</summary>
    [HarmonyPatch(typeof(AudioController), "_PlayAsSound")]
    public static class PlayerSoundPatch
    {
        private static void Postfix(string audioID, float volume, Transform parentObj, AudioObject __result)
        {
            var fx = PlayerFx.Instance;
            if (__result == null || fx == null || !fx.Sharing || !PlayerFx.IsOurs(parentObj)) return;
            // A loop (tiredness, low health, fire) is stopped by our game later; a copy would never stop.
            var item = __result.audioItem;
            if (item == null || item.Loop != AudioItem.LoopMode.DoNotLoop) return;
            fx.OurSound(audioID, volume);
        }
    }

    /// <summary>Player's methods that play its sounds at the listener rather than on the player (see PlayerFx.IsOurs).</summary>
    [HarmonyPatch]
    public static class PlayerCodePatch
    {
        private static readonly string[] Names =
        {
            "onReleaseAim", "reload", "OnAniFinish", "initiateAttack", "InitiateEquipmentUsage", "attack",
            "getHitByShadow", "aim", "onActivateItem", "switchToItem",
        };

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var n in Names)
            {
                var m = AccessTools.DeclaredMethod(typeof(Player), n);
                if (m != null) yield return m;
                else Plugin.Log.LogWarning($"[fx] Player.{n} isn't in this build: its sounds won't be heard by the others");
            }
            // getHit has two: the one that does it.
            yield return AccessTools.DeclaredMethod(typeof(Player), nameof(Player.getHit),
                new[] { typeof(float), typeof(Transform), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
            // The coroutine that fires a gun again (bursts, and the click of an empty one).
            foreach (var t in typeof(Player).GetNestedTypes(BindingFlags.NonPublic))
                if (t.Name.Contains("waitToFireWeapon")) { var m = AccessTools.Method(t, "MoveNext"); if (m != null) yield return m; }
        }

        private static void Prefix() => PlayerFx.InPlayerCode++;
        private static void Finalizer() { if (PlayerFx.InPlayerCode > 0) PlayerFx.InPlayerCode--; }
    }

    /// <summary>Our player fired a gun: the others see the muzzle flash on our puppet.</summary>
    [HarmonyPatch(typeof(Player), "fireWeapon")]
    public static class PlayerShotPatch
    {
        private static void Postfix(Player __instance)
        {
            var fx = PlayerFx.Instance;
            if (fx == null || !fx.Sharing || __instance != Player.Instance) return;
            var gun = __instance.currentItem?.baseClass;
            if (gun != null) fx.OurShot(gun);
        }
    }

    /// <summary>The blood splats of our player's getHit: shown on our puppet too.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.getHit),
        new[] { typeof(float), typeof(Transform), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class PlayerGetHitFxPatch
    {
        private static void Prefix() => PlayerFx.InGetHit++;
        private static void Finalizer() { if (PlayerFx.InGetHit > 0) PlayerFx.InGetHit--; }
    }

    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[] { typeof(string), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool) })]
    public static class PlayerBloodPatch
    {
        private static void Postfix(string prefab, GameObject __result)
        {
            if (PlayerFx.InGetHit == 0 || __result == null || prefab == null || !prefab.StartsWith("FX/Bloodsplats/")) return;
            var fx = PlayerFx.Instance;
            if (fx != null && fx.Sharing && !WorldActions.Replaying) fx.OurBlood(prefab, __result);
        }
    }
}
