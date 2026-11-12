using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// What the host's enemies do that a joiner sees or hears on its own screen, beyond their sounds and blood
    /// (NetEnemies) and explosions (EnemyAreaHits):
    ///
    /// - Combat music (CombatMusic). The game plays it while `Player.charactersAttackingMe` has someone in it, which
    ///   `Character.addToPlayerAttackers` fills only for the host's player. The host checks twice a second, for each
    ///   joiner's proxy, whether one of its enemies is chasing that proxy (behaviour chasingTarget with the proxy as
    ///   target, as `addToPlayerAttackers` counts it for the host's player), and tells that joiner when it changes.
    /// - Effect prefabs (EnemyEffect): the banshee's scream (`Character.bansheeScream` spawns FX/BansheeScream, whose
    ///   collider shakes the player's camera and switches off lights it reaches), the forest spirit's teleport and
    ///   spawn effect (ForestSpirit_fastSpawnEff from the FX pool), and an enemy's particle emitter switched by its
    ///   animation (`Character.modifyEmitter`), which frozen copies on a joiner never run. A joiner makes the same
    ///   prefab in its game, so the scream there shakes its own camera and switches off its own copies of the lights.
    /// - The banshee's sight (no message): what a player sees is their own screen, so a joiner checks itself, twice a
    ///   second, whether its player sees one of its copies of the host's banshees (`Player.isInSight`, as the
    ///   banshee's `checkIfInSightOfPlayer` does), and shows the overlay, the agitated sound, the shake and the
    ///   banshee's sight light as `onBansheeSeePlayer` / `bansheeAgitated` do. Where the banshee goes is the host's.
    /// </summary>
    public static class EnemyFx
    {
        public static void Register(Session s)
        {
            s.Register(MsgType.CombatMusic, OnCombatMusic);
            s.Register(MsgType.EnemyEffect, OnEffect);
            s.OnPeerDisconnected += id => _inCombat.Remove(id);
            s.OnSessionEnded += Clear;
        }

        public static void Clear()
        {
            _inCombat.Clear();
            HostSaysInCombat = false;
            _seen.Clear();
        }

        private static bool Hosting
        {
            get { var s = Session.Instance; return s != null && s.IsHost && s.Peers.Count > 0 && WorldShare.InWorld; }
        }

        private static float _next;

        public static void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            if (Hosting) HostCombat();
            if (NetEnemies.ClientMode && WorldShare.InWorld) BansheeSight();
        }

        // ---- combat music ------------------------------------------------------------------------------

        private static readonly Dictionary<ulong, bool> _inCombat = new Dictionary<ulong, bool>();

        /// <summary>Client: the host says one of its enemies is after our player.</summary>
        public static bool HostSaysInCombat;

        private static void HostCombat()
        {
            var s = Session.Instance;
            var chars = AllCharacters.Active();
            foreach (var proxy in NetPlayerProxy.All.Values)
            {
                if (proxy == null) continue;
                bool fight = false;
                if (proxy.alive)
                    foreach (var c in chars)
                        if (c != null && c.alive && NetEnemies.Eligible(c) && c.behaviour == Character.Behaviour.chasingTarget && c.target == proxy.transform) { fight = true; break; }
                _inCombat.TryGetValue(proxy.Owner, out bool was);
                if (fight == was) continue;
                _inCombat[proxy.Owner] = fight;
                if (Threat.Debug || WorldActions.Debug) Plugin.Log.LogInfo($"[enemyfx] {proxy.OwnerName} {(fight ? "is" : "is no longer")} in combat");
                if (proxy.Owner == Sync.PlayerSync.DummyId) continue;
                var w = s.Begin(MsgType.CombatMusic);
                w.W.Write((byte)(fight ? 1 : 0));
                s.SendTo(proxy.Owner, w, Channel.Control, reliable: true);
            }
        }

        private static void OnCombatMusic(ulong from, PacketReader r)
        {
            var s = Session.Instance;
            if (s == null || !s.IsClient || from != s.HostId) return;
            HostSaysInCombat = r.R.ReadByte() != 0;
            var me = Player.Instance;
            if (me == null || !WorldShare.InWorld) return;
            bool want = HostSaysInCombat || (me.charactersAttackingMe != null && me.charactersAttackingMe.Count > 0);
            if (want != me.InCombat) me.InCombat = want; // the setter starts or fades out the combat track
            Plugin.Log.LogInfo($"[enemyfx] combat music {(want ? "on" : "off")} (the host says {(HostSaysInCombat ? "an enemy is after us" : "none is")})");
        }

        // ---- effect prefabs -----------------------------------------------------------------------------

        /// <summary>Host: one of its enemies made a prefab (kind 1) or a pooled FX effect (kind 2) here.</summary>
        internal static void SendPrefab(byte kind, string name, Vector3 pos, float rotY)
        {
            if (!Hosting || WorldActions.Replaying) return;
            var s = Session.Instance;
            var w = s.Begin(MsgType.EnemyEffect);
            w.W.Write(kind);
            w.WriteStr(name);
            w.Write(pos);
            if (kind == 1) w.W.Write(rotY);
            s.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[enemyfx] sent {name} at {pos.x:0},{pos.z:0}");
        }

        /// <summary>Host: one of its enemies switched its particle emitter; the players that have it see it too.</summary>
        internal static void SendEmitter(Character c, int toggle)
        {
            if (!Hosting || WorldActions.Replaying) return;
            var e = NetEnemies.Instance;
            if (e == null || !e.IdOf(c, out uint id)) return;
            var s = Session.Instance;
            foreach (var peer in e.PeersSeeing(c))
            {
                var w = s.Begin(MsgType.EnemyEffect);
                w.W.Write((byte)3);
                w.W.Write(id);
                w.W.Write(toggle);
                s.SendTo(peer, w, Channel.Control, reliable: true);
            }
        }

        private static void OnEffect(ulong from, PacketReader r)
        {
            var s = Session.Instance;
            if (s == null || !s.IsClient || from != s.HostId || !WorldShare.InWorld) return;
            byte kind = r.R.ReadByte();
            if (kind == 3)
            {
                uint id = r.R.ReadUInt32(); int toggle = r.R.ReadInt32();
                var c = NetEnemies.Instance?.PuppetOf(id);
                if (c == null) return;
                Show(() => c.modifyEmitter(toggle));
                Plugin.Log.LogInfo($"[enemyfx] {c.name}'s emitter {(toggle == 0 ? "off" : "on")}");
                return;
            }
            string name = r.ReadStr();
            var pos = r.ReadVector3();
            float rotY = kind == 1 ? r.R.ReadSingle() : 0f;
            var me = Player.Instance;
            if (me == null || EnemyAreaHits.PlayerAway() || Core.trueDistance(me.transform.position, pos) > 4000f)
            {
                Plugin.Log.LogInfo($"[enemyfx] the host's enemy made {name}, not shown: our player is {(me != null && !EnemyAreaHits.PlayerAway() ? $"too far (at {me.transform.position.x:0},{me.transform.position.z:0})" : "away")}");
                return;
            }
            if (Show(() =>
                {
                    if (kind == 1) Core.AddPrefab(name, pos, Quaternion.Euler(90f, rotY, 0f), null);
                    else Core.AddPooledPrefab("FX", name, pos, Quaternion.identity);
                }))
                Plugin.Log.LogInfo($"[enemyfx] the host's enemy made {name} at {pos.x:0},{pos.z:0}");
        }

        /// <summary>Makes an effect the host's enemy made, as a replay (nothing in it is sent back).</summary>
        private static bool Show(System.Action make)
        {
            WorldActions.Replaying = true;
            try { make(); return true; }
            catch (System.Exception e) { Plugin.Log.LogInfo($"[enemyfx] couldn't show the host's effect here: {e.Message}"); return false; }
            finally { WorldActions.Replaying = false; }
        }

        // ---- the banshee's sight (client) -----------------------------------------------------------------

        private static readonly HashSet<Character> _seen = new HashSet<Character>();

        private static void BansheeSight()
        {
            var me = Player.Instance;
            var e = NetEnemies.Instance;
            if (me == null || e == null) return;
            bool away = EnemyAreaHits.PlayerAway();
            Character nearest = null; float nearestDist = float.MaxValue;
            var now = new HashSet<Character>();
            foreach (var c in e.PuppetCharacters())
            {
                if (!c.banshee || away || !me.alive) continue;
                bool far = c.inSightOfPlayer != null && c.inSightOfPlayer.playerCanBeFarAway;
                if (!me.isInSight(c.transform, far)) continue;
                now.Add(c);
                float d = Mathf.Max(1f, Core.trueDistance(c.transform.position, me.transform.position));
                if (d < nearestDist) { nearestDist = d; nearest = c; }
            }
            // Out of sight now: as onBansheeOutOfSightOfPlayer does for its look.
            foreach (var c in _seen)
                if (c != null && !now.Contains(c)) SightLight(c, false);
            if (now.Count == 0)
            {
                if (_seen.Count > 0) Unseen(me);
                _seen.Clear();
                return;
            }
            if (_seen.Count == 0) Plugin.Log.LogInfo($"[enemyfx] our player sees the host's banshee {nearest.name} ({nearestDist:0} away)");
            _seen.Clear();
            foreach (var c in now) { _seen.Add(c); SightLight(c, true); }
            // As onBansheeSeePlayer and bansheeAgitated do, for the nearest one.
            if (me.bansheeAgitatedSoundAO == null) me.bansheeAgitatedSoundAO = AudioController.Play("banshee_agitated_player", me._transform);
            Singleton<CamMain>.Instance?.shake(0.5f, 1200f / nearestDist);
            var ui = Singleton<UI>.Instance;
            if (ui != null)
            {
                ui.initBansheeOverlay();
                if (ui.bansheeOverlay != null) Core.tweenAlpha(ui.bansheeOverlay.gameObject, Mathf.Clamp(70f / nearestDist, 0f, 0.5f), 0.5f, timeScaleDependent: true);
            }
        }

        private static void Unseen(Player me)
        {
            if (me.bansheeAgitatedSoundAO != null) { me.bansheeAgitatedSoundAO.Stop(1f); me.bansheeAgitatedSoundAO = null; }
            var ui = Singleton<UI>.Instance;
            if (ui != null)
            {
                ui.initBansheeOverlay();
                if (ui.bansheeOverlay != null) Core.tweenAlpha(ui.bansheeOverlay.gameObject, 0f, 0.5f, timeScaleDependent: true);
                ui.wantToRemoveBansheeOverlay();
            }
            Plugin.Log.LogInfo("[enemyfx] the host's banshees are out of our player's sight");
        }

        private static void SightLight(Character c, bool on)
        {
            var t = c.transform.Find("SightLight");
            if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }
    }

    /// <summary>
    /// "--dwmp-test-blast" (host), after AreaHitsTest: the nearest enemy to a joined player screams as a banshee does
    /// (FX/BansheeScream, sent as EnemyEffect), the forest spirit's effect is made beside the player (pooled, sent as
    /// EnemyEffect), and the host's after-night is ended (AfterNightEnded).
    /// </summary>
    static class EnemyFxTest
    {
        internal static System.Collections.IEnumerator Run()
        {
            NetPlayerProxy p = null;
            while (p == null)
            {
                foreach (var x in NetPlayerProxy.All.Values) if (x != null && x.Owner != Sync.PlayerSync.DummyId) p = x;
                yield return null;
            }
            yield return new WaitForSeconds(8f);
            if (p == null) yield break;
            var at = p.transform.position;
            Character c = null;
            foreach (var x in AllCharacters.Active())
                if (x != null && x.alive && NetEnemies.Eligible(x) && (c == null || (x.transform.position - at).sqrMagnitude < (c.transform.position - at).sqrMagnitude)) c = x;
            if (c != null)
            {
                Plugin.Log.LogInfo($"--dwmp-test-blast: {c.name} screams as a banshee");
                try { c.bansheeScream(); } catch (System.Exception e) { Plugin.Log.LogInfo($"--dwmp-test-blast: the scream failed here: {e.Message}"); }
            }
            Plugin.Log.LogInfo($"--dwmp-test-blast: the forest spirit's effect beside {p.OwnerName}");
            try { Core.AddPooledPrefab("FX", "ForestSpirit_fastSpawnEff", at + new Vector3(100f, 0f, 0f), Quaternion.identity); }
            catch (System.Exception e)
            {
                // Chapter 1's FX pool hasn't got it: send it anyway, for the joiner's side of the message.
                Plugin.Log.LogInfo($"--dwmp-test-blast: no such pooled effect here ({e.Message}): sent without the patch");
                EnemyFx.SendPrefab(2, "ForestSpirit_fastSpawnEff", at + new Vector3(100f, 0f, 0f), 0f);
            }
            yield return new WaitForSeconds(1f);
            var ctl = Singleton<Controller>.Instance;
            if (ctl != null)
            {
                Plugin.Log.LogInfo("--dwmp-test-blast: the host's after-night ends");
                ctl.isAfterNight = true;
                ctl.endAfterNight();
            }
            Plugin.Log.LogInfo("--dwmp-test-blast: effects done");
        }
    }

    /// <summary>Client: the game's own count of attackers (in an area of our own) mustn't turn off the host's combat music.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.checkInCombatChars))]
    static class CombatMusicKeepPatch
    {
        static bool Prefix(Player __instance)
        {
            if (!EnemyFx.HostSaysInCombat || !NetEnemies.ClientMode) return true;
            if (!__instance.InCombat) __instance.InCombat = true; // (setting it off and on again would fade the music out)
            return false;
        }
    }

    /// <summary>Host: the banshee's scream (FX/BansheeScream where it stands) goes to the joiners too.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.bansheeScream))]
    static class BansheeScreamPatch
    {
        static void Postfix(Character __instance)
        {
            if (NetEnemies.Eligible(__instance)) EnemyFx.SendPrefab(1, "FX/BansheeScream", __instance.transform.position, 0f);
        }
    }

    /// <summary>Host: pooled effects of enemies (the forest spirit's teleport and spawn) go to the joiners too.</summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPooledPrefab), new[] { typeof(string), typeof(string), typeof(Vector3), typeof(Quaternion) })]
    static class EnemyPooledFxPatch
    {
        // A postfix: it isn't sent when the pool hasn't got it here (the call throws).
        static void Postfix(string pool, string prefab, Vector3 position, Transform __result)
        {
            if (__result != null && pool == "FX" && prefab != null && EnemyFxNames.Pooled.Contains(prefab)) EnemyFx.SendPrefab(2, prefab, position, 0f);
        }
    }

    /// <summary>Pooled FX effects enemies make (the forest spirit's teleport and spawn).</summary>
    static class EnemyFxNames
    {
        internal static readonly HashSet<string> Pooled = new HashSet<string> { "ForestSpirit_fastSpawnEff" };
    }

    /// <summary>Host: an enemy's animation switched its particle emitter (Character.modifyEmitter).</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.modifyEmitter))]
    static class EnemyEmitterPatch
    {
        static void Postfix(Character __instance, int _toggle)
        {
            if (NetEnemies.Eligible(__instance) && (__instance._particleLight != null || __instance._particleEmitter != null))
                EnemyFx.SendEmitter(__instance, _toggle);
        }
    }
}
