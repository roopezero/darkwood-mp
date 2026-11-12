using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The host's enemies, shown on its clients. The host runs every enemy; a client runs none of its own.
    ///
    /// Host: every non-NPC Character gets a net id. Each client is told about the ones near it (within
    /// RelevantRange of its proxy) with EnemySpawn, then gets EnemyState for them twenty times a second (the host's clock, position,
    /// rotation, alive, and each sprite animator's clip and frame), EnemySound for the sounds they make, and
    /// EnemyDespawn when one goes out of its range, or a while after it died (its corpse then stays put).
    /// A corpse is sent to each client once, when it first comes within its range (one that was already dead when
    /// they joined, or that they walked away from before it settled): EnemySpawn, one EnemyState that says it's
    /// dead, and EnemyDespawn keeping the corpse, then what it holds. The client's corpse stays until the host's
    /// is gone.
    ///
    /// Client: its own non-NPC characters are frozen as they start (their AI, pathfinding and physics off, their
    /// animators' frame events cleared so they can't attack or make sounds) and hidden. A spawned enemy is the
    /// client's own copy of it from the save, found by its save id, or else made from its prefab; it's then shown
    /// and driven by the host's states, playing the same animation clips. Damage the client deals it (melee,
    /// bullets, fire...) goes to the host as EnemyHit instead of being applied, and the host applies it with the
    /// client's proxy as the attacker, so the enemy reacts to, and gains threat for, that player.
    ///
    /// NPCs (traders, quest characters) are each player's own while they're friendly. One that turns on a player
    /// is run by the host as one of these enemies until it calms down (Combat.HostileNpcs).
    /// </summary>
    public sealed class NetEnemies
    {
        public const float RelevantRange = 3000f;
        public const float StateInterval = 0.05f;
        public const float CorpseLinger = 6f;

        public static NetEnemies Instance { get; private set; }
        private readonly Session _session;

        public NetEnemies(Session session)
        {
            Instance = this;
            _session = session;
            session.Register(MsgType.EnemySpawn, OnSpawn);
            session.Register(MsgType.EnemyState, OnState);
            session.Register(MsgType.EnemyDespawn, OnDespawn);
            session.Register(MsgType.EnemySound, OnSound);
            session.Register(MsgType.EnemyHit, OnHit);
            session.Register(MsgType.PlayerNoise, PlayerNoisePatch.OnRemoteNoise);
            session.Register(MsgType.PlayerCue, PlayerCues.OnCue);
            session.Register(MsgType.EnemyBlood, OnBlood);
            session.OnPeerDisconnected += id => { _known.Remove(id); _corpseKnown.Remove(id); };
            session.Register(MsgType.NpcHit, HostileNpcs.OnNpcHit);
            session.Register(MsgType.NpcAttack, HostileNpcs.OnNpcAttack);
            session.OnSessionEnded += () => { HostileNpcs.Clear(); _known.Clear(); _corpseKnown.Clear(); _byCharHost.Clear(); _puppets.Clear(); _byChar.Clear(); EnemyMotion.Reset(); _corpses.Clear(); _corpseIds.Clear(); Frozen.Clear(); };
        }

        /// <summary>
        /// One of the characters the host runs and its clients show: every creature, and an NPC while it's hostile
        /// (Combat.HostileNpcs: the host's copy turned on a player, or a client's copy is one the host runs).
        /// </summary>
        public static bool Eligible(Character c) => c != null && (c.npc == null || HostileNpcs.Running(c));

        /// <summary>
        /// Still in the fight: alive, or in a pre-death stage (Character.hasPreDeath: alive is false, but it crawls
        /// with health left until a hit finishes it or it bleeds out; getHit and the player's melee still hit it).
        /// </summary>
        public static bool InPlay(Character c) => c != null && (c.alive || (c.hasPreDeath && c.health > 0f));

        /// <summary>
        /// Client: a character of an area only this player is in: a dream (each player's own), or a separate location
        /// the host didn't place for us (World.Locations.HostRunsHere), or anything still in a scene of its own. The
        /// host doesn't run it, so it runs here, as in single player. A separate location the host placed is the
        /// host's to run, as the world is (it loads it at the same spot even when its player isn't there): our
        /// characters there are frozen and the host's are shown.
        /// </summary>
        public static bool LocalOnly(Character c) => c != null && LocalOnly(c.gameObject);

        /// <summary>Client: a thing in an area only this player is in (as for a character, above).</summary>
        public static bool LocalOnly(GameObject go)
        {
            if (go == null) return false;
            if (go.scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene()) return true;
            if (World.Locations.HostRunsHere()) return false;
            var p = Player.Instance;
            return p != null && (go.transform.position - p.transform.position).sqrMagnitude < 4000f * 4000f;
        }

        /// <summary>Client: undo Freeze, for a character that turned out to be in an area of our own.</summary>
        public static void Thaw(Character c)
        {
            if (c == null || !Frozen.Remove(c)) return;
            SetVisible(c, true);
            var rb = c.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
            if (c.AIpath != null) c.AIpath.enabled = true;
            foreach (var mb in c.GetComponents<MonoBehaviour>()) if (mb is AILerp) mb.enabled = true;
            c.enabled = true; // its OnEnable hooks its animators back up
            ClientFreezeCharacterPatch.Starting = true;
            try { c.Start(); } catch (System.Exception e) { Plugin.Log.LogWarning($"{c.name} didn't start cleanly: {e.Message}"); }
            finally { ClientFreezeCharacterPatch.Starting = false; }
            PlayIdleLoop(c);
        }

        /// <summary>
        /// Client: start a shown character's idle loop again (CharacterSounds.OnEnable's), stopped while it was hidden:
        /// its sleeping one while the host's is asleep (Character.sleeping, sent in EnemyState), as enableComponents picks.
        /// </summary>
        private static void PlayIdleLoop(Character c)
        {
            if (c == null || !c.alive || c.sounds == null) return;
            try { c.sounds.playIdleLoop(c.sleeping ? c.sounds.sleeping : c.sounds.idleLoop); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't start {c.name}'s idle sound: {e.Message}"); }
        }

        public void Update()
        {
            if (_session.IsHost && _session.Peers.Count > 0 && WorldShare.InWorld) HostUpdate();
            if (ClientMode) { ClientSweep(); MovePuppets(); }
        }

        /// <summary>
        /// Client, every frame: each of the host's enemies drawn where the host has it now, by the host's clock and its
        /// last states (EnemyMotion), so they move at the host's speed whenever its states come.
        /// </summary>
        private void MovePuppets()
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            EnemyMotion.Frame(dt);
            foreach (var p in _puppets.Values)
            {
                if (p.C != null && p.DiedAt >= 0f && !p.Settled && now - p.DiedAt > DeathAnimTime) Settle(p);
                if (!p.Has || p.C == null) continue;
                if (!p.C.gameObject.activeSelf && !p.Settled)
                {
                    Plugin.Log.LogInfo($"[enemy] the host's {p.C.name} #{p.Id} was switched off here: on again");
                    p.C.gameObject.SetActive(true);
                }
                if (!EnemyMotion.Where(p.Motion, now, out var pos, out var rot)) continue;
                p.C.transform.SetPositionAndRotation(pos, rot);
                EnemyMotion.Stats.Drawn(p.Motion, pos, dt);
                AlignShadow(p.C);
            }
        }

        /// <summary>
        /// Client: a copy's shadow, placed as Character.Update places it (the copy's Update doesn't run): not turned
        /// with the body unless rotateShadow, at shadow height, lowered once it's dead. A flier's shadow is offset by
        /// its altitude, which the host doesn't send, so it's left as it is.
        /// </summary>
        private static void AlignShadow(Character c)
        {
            var sh = c.shadow;
            if (sh == null || c.flier != null) return;
            if (!c.rotateShadow) sh.eulerAngles = new Vector3(90f, 0f, 0f);
            var at = c.transform.position + new Vector3(c.shadowOffset.x, 0f, c.shadowOffset.z);
            sh.position = Core.getYPos(at, c.alive ? PosType.characterShadow : PosType.low1, randomize: false) + c.posSeed;
        }

        // =====================================================================
        // Host
        // =====================================================================

        private sealed class Tracked
        {
            public uint Id;
            public Character C;
            public float DiedAt = -1f;
            public float CalmSince = -1f; // an NPC: when it stopped being hostile
            public List<tk2dSpriteAnimator> Animators;
            public List<int> AnimatorPaths;
            public string Loop = ""; // the loop it was playing at the last LoopCheck
        }

        private uint _nextId = 1;
        private readonly Dictionary<Character, Tracked> _byCharHost = new Dictionary<Character, Tracked>();
        private readonly Dictionary<ulong, HashSet<uint>> _known = new Dictionary<ulong, HashSet<uint>>();
        // Corpses each client was told to keep (EnemyDespawn with keep corpse): not sent again.
        private readonly Dictionary<ulong, HashSet<uint>> _corpseKnown = new Dictionary<ulong, HashSet<uint>>();
        private float _nextScan, _nextState;
        private readonly PacketWriter _batch = new PacketWriter();
        private readonly PacketWriter _one = new PacketWriter();
        /// <summary>Corpses sent to one client in one state pass at most (a joiner can have dozens near them).</summary>
        private const int CorpsesPerPass = 4;

        private void HostUpdate()
        {
            float now = Time.unscaledTime;
            if (now >= _nextScan)
            {
                _nextScan = now + 0.5f;
                foreach (var c in AllCharacters.Active())
                    if (Eligible(c) && !_byCharHost.ContainsKey(c))
                    {
                        var t = new Tracked { Id = _nextId++, C = c };
                        Animators(c.transform, out t.Animators, out t.AnimatorPaths);
                        _byCharHost[c] = t;
                        if (c.npc != null) HostileNpcs.HostStarted(c, t.Id);
                    }
                var gone = new List<Character>();
                foreach (var kv in _byCharHost)
                {
                    if (kv.Key == null) { gone.Add(kv.Key); continue; }
                    // An NPC that calmed down (and stayed calm a while) is each player's own again.
                    if (kv.Key.npc == null || !InPlay(kv.Key)) continue;
                    if (HostileNpcs.StillHostile(kv.Key)) { kv.Value.CalmSince = -1f; continue; }
                    if (kv.Value.CalmSince < 0f) kv.Value.CalmSince = now;
                    else if (now - kv.Value.CalmSince > HostileNpcs.CalmTime) gone.Add(kv.Key);
                }
                foreach (var g in gone)
                {
                    if (g != null) HostileNpcs.HostStopped(g, _byCharHost[g].Id);
                    Forget(_byCharHost[g].Id, keepCorpse: false); _byCharHost.Remove(g);
                }
            }
            if (now >= _nextLoopCheck) { _nextLoopCheck = now + LoopCheckInterval; LoopCheck(now); }
            if (now < _nextState) return;
            _nextState = now + StateInterval;

            foreach (var peer in _session.Peers)
            {
                if (!NetPlayerProxy.All.TryGetValue(peer, out var proxy) || proxy == null) continue;
                // Away (a dream, or a separate location we haven't got where they have it): their own game runs the
                // enemies there (LocalOnly), so none of ours go to them; what they have of ours waits for their return.
                // In a location we run for them (proxy.InLocation) they get ours there, by the same range check.
                if (proxy.Away) continue;
                if (!_known.TryGetValue(peer, out var known)) _known[peer] = known = new HashSet<uint>();
                if (!_corpseKnown.TryGetValue(peer, out var corpses)) _corpseKnown[peer] = corpses = new HashSet<uint>();
                Vector3 at = proxy.transform.position;
                int corpsesSent = 0;

                var w = _batch.Begin(MsgType.EnemyState);
                w.W.Write(now); // the host's clock, which the client draws them by (EnemyMotion)
                long countPos = w.Length; w.W.Write((ushort)0);
                int count = 0;
                var drop = new List<(uint, bool)>();
                foreach (var t in _byCharHost.Values)
                {
                    var c = t.C;
                    if (c == null) continue;
                    if (!InPlay(c) && t.DiedAt < 0) t.DiedAt = Time.time;
                    bool dead = t.DiedAt >= 0;
                    bool corpseDone = dead && Time.time - t.DiedAt > CorpseLinger;
                    bool near = c.gameObject.activeInHierarchy && (c.transform.position - at).sqrMagnitude < RelevantRange * RelevantRange;
                    if (corpseDone && corpses.Contains(t.Id)) continue; // it has it
                    if (corpseDone && !known.Contains(t.Id))
                    {
                        // A corpse it never saw settle: it gets it once it's near.
                        if (near && corpsesSent < CorpsesPerPass) { SendCorpse(peer, t); corpses.Add(t.Id); corpsesSent++; }
                        continue;
                    }
                    if (corpseDone || !near)
                    {
                        // Out of range, or settled: the dead stay where they fell (the client settles it now if it hadn't).
                        if (known.Contains(t.Id)) drop.Add((t.Id, dead));
                        continue;
                    }
                    if (known.Add(t.Id)) SendSpawn(peer, t);
                    WriteState(w, t);
                    count++;
                }
                foreach (var (id, corpse) in drop)
                {
                    known.Remove(id); SendDespawn(peer, id, corpse);
                    if (corpse && corpses.Add(id)) SendCorpseLoot(peer, id);
                }
                if (count == 0) continue;
                var buf = w.Buffer; // patch the count in
                buf[countPos] = (byte)count; buf[countPos + 1] = (byte)(count >> 8);
                _session.SendTo(peer, w, Channel.State, reliable: false);
            }
        }

        /// <summary>One of the loops CharacterSounds.playIdleLoop plays for this character.</summary>
        private static bool IsLoop(CharacterSounds snd, string audio) =>
            snd != null && (audio == snd.idleLoop || audio == snd.idleLoopAggressive || audio == snd.aggressive
                            || audio == snd.defensive || audio == snd.sleeping || audio == snd.escapingLoop);

        private float _nextLoopCheck, _nextLoopResend;
        private const float LoopCheckInterval = 0.5f, LoopResendInterval = 2f;

        /// <summary>
        /// Host: the loop each living enemy plays, made sure of in the clients' games. EnemySound tells them as a loop
        /// starts or is stopped by playIdleLoop(""), but a loop also stops other ways (destroySounds, playIdleLoop
        /// underground or under water), an unreliable EnemySound can be lost, and a client's copy starts its idle
        /// loop by itself when it's switched on again (CharacterSounds.OnEnable). So a change of loop goes reliably,
        /// and every couple of seconds each client is told the loop again (the same loop is a no-op there).
        /// </summary>
        private void LoopCheck(float now)
        {
            bool resend = now >= _nextLoopResend;
            if (resend) _nextLoopResend = now + LoopResendInterval;
            foreach (var t in _byCharHost.Values)
            {
                var c = t.C;
                if (c == null || t.DiedAt >= 0 || !InPlay(c)) continue;
                var loop = c.sounds != null ? c.sounds.idleAudioObject : null;
                string cur = loop != null && loop.IsPlaying() && !string.IsNullOrEmpty(loop.audioID) ? loop.audioID : "";
                if (cur != "" && !IsLoop(c.sounds, cur)) continue; // not one the client takes as a loop (OnSound)
                bool changed = cur != t.Loop;
                if (!changed && !resend) continue;
                t.Loop = cur;
                foreach (var kv in _known)
                {
                    if (!kv.Value.Contains(t.Id)) continue;
                    var w = _session.Begin(MsgType.EnemySound);
                    w.W.Write(t.Id);
                    w.WriteStr(cur);
                    w.W.Write(1f);
                    if (changed) _session.SendTo(kv.Key, w, Channel.Control, reliable: true);
                    else _session.SendTo(kv.Key, w, Channel.State, reliable: false);
                }
            }
        }

        /// <summary>What kind of character it is: its prefab's name, as the save system knows it.</summary>
        internal static string KindOf(Character c)
        {
            var save = c.GetComponent<SaveableObject>();
            return save != null && !string.IsNullOrEmpty(save.prefabName) ? save.prefabName : c.name.Replace("(Clone)", "").Trim();
        }

        private void SendSpawn(ulong peer, Tracked t)
        {
            var c = t.C;
            var save = c.GetComponent<SaveableObject>();
            string name = KindOf(c);
            // The prefab's path under Resources/Prefabs, as the save system knows it (Core.AddPrefab's argument).
            var sm = Singleton<SaveManager>.Instance;
            string path = sm != null && sm.prefabDictionary.TryGetValue(name, out var p) ? p : "Characters/" + name;
            var w = _session.Begin(MsgType.EnemySpawn);
            w.W.Write(t.Id);
            w.W.Write(save != null ? save.uniqueId : 0);
            w.WriteStr(path);
            w.WriteStr(name);
            w.Write(c.transform.position);
            WriteRot(w, c.transform.rotation);
            _session.SendTo(peer, w, Channel.Control, reliable: true);
            // The loop it's playing now (one that started before it was sent, e.g. an NPC's aggressive one as it
            // turned hostile): after the spawn, on the same channel, so it comes after it.
            var loop = c.sounds != null ? c.sounds.idleAudioObject : null;
            if (loop != null && loop.IsPlaying() && !string.IsNullOrEmpty(loop.audioID))
            {
                var s = _session.Begin(MsgType.EnemySound);
                s.W.Write(t.Id);
                s.WriteStr(loop.audioID);
                s.W.Write(1f);
                _session.SendTo(peer, s, Channel.Control, reliable: true);
            }
        }

        /// <summary>
        /// Host: a corpse the client doesn't have, sent as an enemy that is shown, dead and then kept (it settles
        /// at once there, on the host's last frames of its death), then what it holds. All on the reliable channel,
        /// so they come in this order.
        /// </summary>
        private void SendCorpse(ulong peer, Tracked t)
        {
            SendSpawn(peer, t);
            var w = _one.Begin(MsgType.EnemyState);
            w.W.Write(Time.unscaledTime);
            w.W.Write((ushort)1);
            WriteState(w, t);
            _session.SendTo(peer, w, Channel.Control, reliable: true);
            SendDespawn(peer, t.Id, keepCorpse: true);
            SendCorpseLoot(peer, t.Id);
        }

        private void SendCorpseLoot(ulong peer, uint id)
        {
            var c = CharacterOf(id);
            var inv = c != null ? c.GetComponent<Inventory>() : null;
            if (inv != null) Containers.Instance?.SendContentsTo(peer, inv);
        }

        private void SendDespawn(ulong peer, uint id, bool keepCorpse)
        {
            var w = _session.Begin(MsgType.EnemyDespawn);
            w.W.Write(id);
            w.W.Write((byte)(keepCorpse ? 1 : 0));
            _session.SendTo(peer, w, Channel.Control, reliable: true);
        }

        private void Forget(uint id, bool keepCorpse)
        {
            foreach (var kv in _known)
                if (kv.Value.Remove(id)) SendDespawn(kv.Key, id, keepCorpse);
            foreach (var kv in _corpseKnown) // the host's corpse is gone, so theirs goes too
                if (kv.Value.Remove(id) && !keepCorpse) SendDespawn(kv.Key, id, keepCorpse: false);
        }

        private static void WriteState(PacketWriter w, Tracked t)
        {
            var c = t.C;
            w.W.Write(t.Id);
            w.Write(c.transform.position);
            WriteRot(w, c.transform.rotation);
            w.W.Write((byte)((InPlay(c) ? 1 : 0) | (c.sleeping ? 2 : 0) | (c.cuttingInHalf ? 4 : 0)));
            w.W.Write(c.maxHealth > 0 ? Mathf.Clamp01(c.health / c.maxHealth) : 1f);
            int n = Mathf.Min(t.Animators.Count, 8);
            w.W.Write((byte)n);
            for (int i = 0; i < n; i++)
            {
                var a = t.Animators[i];
                var clip = a != null ? a.CurrentClip : null;
                w.W.Write(t.AnimatorPaths[i]);
                w.W.Write(clip != null ? StableHash.Of(clip.name) : 0);
                w.W.Write((ushort)(clip != null ? Mathf.Clamp(a.CurrentFrame, 0, 65535) : 0));
                w.W.Write((byte)(a != null && a.Playing ? 1 : 0));
            }
        }

        /// <summary>Host: an enemy was hit and bled; the clients that see it get the splat too (their hits included).</summary>
        public void HostBlood(Character c, float yRot)
        {
            if (!_byCharHost.TryGetValue(c, out var t)) return;
            foreach (var kv in _known)
            {
                if (!kv.Value.Contains(t.Id)) continue;
                var w = _session.Begin(MsgType.EnemyBlood);
                w.W.Write(t.Id); w.W.Write(yRot); w.W.Write((byte)(c.inWater ? 1 : 0));
                _session.SendTo(kv.Key, w, Channel.State, reliable: false);
            }
        }

        private void OnBlood(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !ClientMode) return;
            uint id = r.R.ReadUInt32(); float y = r.R.ReadSingle(); bool water = r.R.ReadByte() != 0;
            if (_predictedBlood.TryGetValue(id, out float at) && Time.realtimeSinceStartup - at < 1f) { _predictedBlood.Remove(id); return; } // shown already
            if (_puppets.TryGetValue(id, out var p) && p.C != null)
                Core.AddPrefab(water ? "FX/Bloodsplats/Shotsplat" : "FX/Bloodsplats/Shotsplat_stay", p.C.transform.position, Quaternion.Euler(90f, y, 0f), null);
        }

        /// <summary>Host: the players that have this enemy (it's near them), for things only they need to see.</summary>
        public List<ulong> PeersSeeing(Character c)
        {
            var list = new List<ulong>();
            if (c == null || !_byCharHost.TryGetValue(c, out var t)) return list;
            foreach (var kv in _known) if (kv.Value.Contains(t.Id)) list.Add(kv.Key);
            return list;
        }

        /// <summary>Host: the net ID the clients know this enemy by (false if it isn't tracked).</summary>
        public bool IdOf(Character c, out uint id)
        {
            id = 0;
            if (c == null || !_byCharHost.TryGetValue(c, out var t)) return false;
            id = t.Id;
            return true;
        }

        /// <summary>Client: our copy of the host's enemy with this net ID (null if we haven't got it).</summary>
        public Character PuppetOf(uint id) => _puppets.TryGetValue(id, out var p) ? p.C : null;

        /// <summary>Client: our copies of the host's living enemies.</summary>
        public IEnumerable<Character> PuppetCharacters()
        {
            foreach (var p in _puppets.Values) if (p.C != null && p.DiedAt < 0f) yield return p.C;
        }

        /// <summary>Host: an enemy made a sound; tell the clients that can see it.</summary>
        public void HostSound(Transform parent, string audioId, float volume)
        {
            var c = parent.GetComponentInParent<Character>();
            if (c == null || !_byCharHost.TryGetValue(c, out var t)) return;
            foreach (var kv in _known)
            {
                if (!kv.Value.Contains(t.Id)) continue;
                var w = _session.Begin(MsgType.EnemySound);
                w.W.Write(t.Id);
                w.WriteStr(audioId);
                w.W.Write(volume);
                _session.SendTo(kv.Key, w, Channel.State, reliable: false);
            }
        }

        private void OnHit(ulong from, PacketReader r)
        {
            if (!_session.IsHost) return;
            uint id = r.R.ReadUInt32();
            float damage = r.R.ReadSingle();
            byte f = r.R.ReadByte();
            var effects = PlayerDamage.ReadEffects(r);
            string weaponJson = r.ReadStr();
            Tracked t = null;
            foreach (var x in _byCharHost.Values) if (x.Id == id) { t = x; break; }
            if (t == null || t.C == null || !InPlay(t.C)) { World.Authority.Refuse("that enemy isn't alive here"); return; }
            if (!NetPlayerProxy.All.TryGetValue(from, out var proxy) || proxy == null) { World.Authority.Refuse("no stand-in for that player yet"); return; }
            ApplyHit(from, proxy, t.C, damage, f, effects, weaponJson);
        }

        /// <summary>
        /// Host, in a request's handler: a player's hit on one of our characters (an enemy, or an NPC that's still
        /// friendly here), checked (reach, hit rate) and capped by their weapon, applied with their stand-in as the
        /// attacker, so it reacts to them and gains threat for them.
        /// </summary>
        internal void ApplyHit(ulong from, NetPlayerProxy proxy, Character c, float damage, byte f, List<InvItemEffect> effects, string weaponJson)
        {
            var weapon = Weapon(weaponJson);
            bool ranged = weapon != null && weapon.baseClass != null && !weapon.baseClass.isMelee;
            if (!World.Authority.InReach(c.transform.position, ranged ? RelevantRange : MeleeReach, c.name)) return;
            if (!HitRateOk(from, weapon)) { World.Authority.Refuse($"more hits a second than {weapon?.type ?? "bare hands"} makes"); return; }
            float cap = DamageCap(weapon);
            float applied = Mathf.Clamp(damage, 0f, cap);
            if (applied < damage) Plugin.Log.LogInfo($"[authority] {_session.NameOf(from)}'s hit on {c.name}: {damage:0} is more than {weapon?.type ?? "bare hands"} can do; {applied:0}");
            World.Authority.Result(applied.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            // The bar isn't shown for another player's hit (getHit would, as byPlayer); as for any hit not by our
            // player, it shows if we have that enemy selected, and an open bar is refreshed.
            if (applied > 0f)
            {
                c.getHit(applied, proxy.transform, canCutInHalf: (f & 1) != 0, byPlayer: true, canInterrupt: (f & 4) != 0, normalHit: (f & 8) != 0, dontShowHealthBar: true);
                if (c != null) HealthBar(c, show: Player.Instance != null && Player.Instance.selectedObject == c.transform);
            }
            if (c != null && c.effects != null) foreach (var e in effects) c.effects.activate(e); // bleeding, burning... from their weapon
        }

        /// <summary>How far a melee hit reaches (generous: the positions the host has are a little late).</summary>
        public const float MeleeReach = 500f;
        /// <summary>Most hits a player lands in a second with a melee weapon or bare hands (or a slow gun).</summary>
        public const int MaxHitsPerSecond = 24;
        private readonly Dictionary<ulong, Queue<float>> _hitTimes = new Dictionary<ulong, Queue<float>>();

        /// <summary>
        /// Most hits a second this weapon lands, by the host's numbers for its type: each of a gun's shots sends a hit
        /// for every pellet that lands (InvItem.projectileAmount, Player.fireWeapon), and it fires at most once per
        /// fireRate seconds (at most every 0.05 s for automatic and burst fire, 0.15 s for one shot a click), with half again as a margin for hits
        /// that arrive bunched up. Never under MaxHitsPerSecond.
        /// </summary>
        public static int MaxHits(InvItemClass weapon)
        {
            var b = weapon?.baseClass;
            if (b == null || !b.isFirearm) return MaxHitsPerSecond;
            // A gun fired one shot per click (oneShot, semiAuto) has its fireRate at 0 or near it: its pump or
            // bolt animation, and the clicks, keep it under about 7 shots a second.
            bool auto = b.fireMode == InvItem.FireMode.fullAuto || b.fireMode == InvItem.FireMode.burst;
            float shots = 1f / Mathf.Max(b.fireRate, auto ? 0.05f : 0.15f);
            return Mathf.Max(MaxHitsPerSecond, Mathf.CeilToInt(Mathf.Max(1, b.projectileAmount) * shots * 1.5f));
        }

        private bool HitRateOk(ulong from, InvItemClass weapon)
        {
            if (!_hitTimes.TryGetValue(from, out var q)) _hitTimes[from] = q = new Queue<float>();
            float now = Time.realtimeSinceStartup;
            while (q.Count > 0 && now - q.Peek() > 1f) q.Dequeue();
            if (q.Count >= MaxHits(weapon)) return false;
            q.Enqueue(now);
            return true;
        }

        /// <summary>The weapon a hit came with (InvItemClass.SaveState as JSON; null for bare hands or one we can't read).</summary>
        private static InvItemClass Weapon(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var st = Newtonsoft.Json.JsonConvert.DeserializeObject<InvItemClass.SaveState>(json);
                var it = new InvItemClass();
                st.loadValues(it);
                return InvItemClass.isNull(it) || it.baseClass == null ? null : it;
            }
            catch { return null; }
        }

        /// <summary>
        /// The most damage this weapon can deal in one hit, by the game's own numbers: its damage or special
        /// damage, with its upgrades (getModdedDamage), doubled by the Strong skill's effect, with a margin.
        /// Bare hands: as the game's weakest weapons.
        /// </summary>
        public static float DamageCap(InvItemClass weapon)
        {
            if (weapon == null || weapon.baseClass == null) return 40f;
            int most = Mathf.Max(weapon.getModdedDamage(weapon.baseClass.damage), weapon.getModdedDamage(weapon.baseClass.specialDamage));
            return Mathf.Max(10f, most * 2f * 1.25f);
        }

        // =====================================================================
        // Client
        // =====================================================================

        /// <summary>A client in the host's world: its own enemies don't run, the host's are shown.</summary>
        public static bool ClientMode
        {
            get
            {
                var s = Session.Instance;
                return s != null && s.IsClient && s.Connected && SaveRedirect.Active;
            }
        }

        private sealed class Puppet
        {
            public uint Id;
            public Character C;
            public bool Ours; // made from the prefab here, rather than our copy from the save
            public readonly Dictionary<int, tk2dSpriteAnimator> Animators = new Dictionary<int, tk2dSpriteAnimator>();
            public bool Has; // it has had a state
            public float DiedAt = -1f; public bool Settled; // dead: the corpse made lootable once its death has played
            public bool Cut; // cut in half on the host (Character.cutMeInHalf): its lower half is a new enemy, this part no corpse
            public bool Unculled; public Location HomeLoc; // taken out of our culling while the host sends it (Uncull)
            public readonly EnemyMotion.Track Motion = new EnemyMotion.Track();
        }

        /// <summary>
        /// Client: take a shown copy out of our own culling. Our WorldGrid culls only around our player, so a copy
        /// 1600-3000 away (still within the host's RelevantRange) would be switched off by its node being left
        /// (Cullable.hide, tryToHideMoveable) or by its location's characters being switched off
        /// (Location.activateCharacters), and nothing would switch it on again while the host moves it: we'd be hit
        /// by an enemy we can't see. While the host sends it, its EnemyDespawn decides when it goes.
        /// </summary>
        private static void Uncull(Puppet p)
        {
            var c = p.C;
            if (c == null || p.Unculled) return;
            p.Unculled = true;
            var go = c.gameObject;
            var co = c.GetComponent<CullableObject>();
            if (co != null)
            {
                co.node?.removeCullable(go);
                foreach (var n in co.nodes) n?.removeCullable(go);
                co.node = null; // checkForNewNode then leaves it alone
                co.nodes.Clear();
            }
            var loc = c.baseLocation;
            if (loc != null)
            {
                if (loc.charactersList.Remove(c)) p.HomeLoc = loc;
                loc.objects.Remove(go);
            }
            // Under a location's characters holder it would go off with the location's children: put it where the
            // game puts characters that left their location (a holder that's never switched off).
            var holder = Singleton<CharacterSpawner>.Instance?.charactersEscapedFromLocationHolder;
            if (holder != null && go.transform.parent != null && go.transform.parent != holder.transform) c.disconnectFromLocation();
            if (!go.activeSelf) go.SetActive(true);
            if (!go.activeInHierarchy) Plugin.Log.LogWarning($"[enemy] the host's {c.name} #{p.Id} is still switched off here (under {go.transform.parent?.name})");
        }

        /// <summary>Client: the host stopped sending a copy: our culling looks after it again, at the spot it's at now.</summary>
        private static void Recull(Puppet p)
        {
            var c = p.C;
            if (c == null || !p.Unculled) return;
            p.Unculled = false;
            if (p.HomeLoc != null && !p.HomeLoc.charactersList.Contains(c)) p.HomeLoc.charactersList.Add(c);
            p.HomeLoc = null;
            var o = Singleton<OutsideLocations>.Instance;
            var grid = Singleton<WorldGrid>.Instance;
            if (grid == null || (o != null && o.playerInOutsideLocation)) return; // our current grid isn't the world's
            try
            {
                grid.registerToNode(c.gameObject);
                var node = c.GetComponent<CullableObject>()?.node;
                if (node != null && !node.entered && c.gameObject.activeSelf) c.gameObject.SetActive(false);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't put {c.name} back into the culling: {e.Message}"); }
        }

        private readonly Dictionary<uint, Puppet> _puppets = new Dictionary<uint, Puppet>();
        private readonly Dictionary<Character, Puppet> _byChar = new Dictionary<Character, Puppet>();
        private float _nextSweep;

        // Corpses the host told us to leave where they fell: still known by id, for their loot.
        private readonly Dictionary<uint, Puppet> _corpses = new Dictionary<uint, Puppet>();
        private readonly Dictionary<Character, uint> _corpseIds = new Dictionary<Character, uint>();

        /// <summary>The net id of one of the host's enemies (host: tracked; client: shown, or its corpse).</summary>
        public bool NetIdOf(Character c, out uint id)
        {
            id = 0;
            if (c == null) return false;
            if (_session.IsHost && _byCharHost.TryGetValue(c, out var t)) { id = t.Id; return true; }
            if (_byChar.TryGetValue(c, out var p)) { id = p.Id; return true; }
            return _corpseIds.TryGetValue(c, out id);
        }

        /// <summary>Client: this is a copy of one of the host's enemies that we made from its prefab (one spawned
        /// while playing), rather than our own copy from the save.</summary>
        public bool MadeHere(Character c) => c != null && _byChar.TryGetValue(c, out var p) && p.Ours;

        public Character CharacterOf(uint id)
        {
            if (_session.IsHost) { foreach (var t in _byCharHost.Values) if (t.Id == id) return t.C; return null; }
            if (_puppets.TryGetValue(id, out var p)) return p.C;
            return _corpses.TryGetValue(id, out var c) ? c.C : null;
        }

        /// <summary>Host: enemies the clients know of that died at least this long ago.</summary>
        public IEnumerable<Character> HostDeadSince(float seconds)
        {
            foreach (var t in _byCharHost.Values)
                if (t.C != null && t.DiedAt >= 0 && Time.time - t.DiedAt >= seconds) yield return t.C;
        }

        public bool IsPuppet(Character c, out uint id)
        {
            id = 0;
            if (c != null && _byChar.TryGetValue(c, out var p)) { id = p.Id; return true; }
            return false;
        }

        /// <summary>Client: stop one of our own characters from acting, and hide it until the host says it's there.</summary>
        public static void Freeze(Character c, bool hide)
        {
            if (c == null) return;
            c.enabled = false;
            if (c.AIpath != null) c.AIpath.enabled = false;
            foreach (var mb in c.GetComponents<MonoBehaviour>())
                if (mb is AILerp) mb.enabled = false;
            var rb = c.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = true; rb.velocity = Vector3.zero; }
            foreach (var a in c.GetComponentsInChildren<tk2dSpriteAnimator>(true)) { a.AnimationEventTriggered = null; a.AnimationCompleted = null; }
            if (hide && Instance != null && Instance._corpseIds.ContainsKey(c)) hide = false; // a corpse the host sent stays shown
            if (hide) { SetVisible(c, false); StopSounds(c, keepDeathSound: false); } // no barking from a dog that isn't shown
            Frozen.Add(c);
        }

        public static readonly HashSet<Character> Frozen = new HashSet<Character>();

        private static void SetVisible(Character c, bool on)
        {
            foreach (var r in c.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
            foreach (var col in c.GetComponentsInChildren<Collider>(true)) col.enabled = on;
        }

        /// <summary>Catch characters that started before we were in client mode (e.g. rejoining in the same world).</summary>
        private void ClientSweep()
        {
            if (Time.unscaledTime < _nextSweep) return;
            _nextSweep = Time.unscaledTime + 1f;
            foreach (var c in AllCharacters.Active())
            {
                if (!Eligible(c) || _byChar.ContainsKey(c)) continue;
                bool local = LocalOnly(c);
                if (local && Frozen.Contains(c)) Thaw(c);
                else if (!local && !Frozen.Contains(c)) Freeze(c, hide: true);
            }
        }

        private void OnSpawn(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !ClientMode) return;
            uint id = r.R.ReadUInt32();
            int uid = r.R.ReadInt32();
            string prefab = r.ReadStr();
            string name = r.ReadStr();
            Vector3 pos = r.ReadVector3();
            Quaternion rot = ReadRot(r);
            if (_puppets.ContainsKey(id)) return;

            Character c = null; bool ours = false;
            var sm = Singleton<SaveManager>.Instance;
            // Our copy of it from the save, when the host's save id names the same character here: only ids from
            // the save we loaded are the same in both games (SharedIds), and even then it has to be the same kind
            // of creature. The host sends an NPC only while it's hostile there (HostileNpcs): ours is then frozen and
            // shown as the host's.
            if (uid > 0 && uid < World.SharedIds.ResolveLimit && sm != null && sm.uniqueIdDict.TryGetValue(uid, out var tr) && tr != null)
            {
                c = tr.GetComponent<Character>();
                if (c != null && _byChar.ContainsKey(c)) c = null; // already driven by another id
                if (c != null && KindOf(c) != name)
                {
                    Plugin.Log.LogInfo($"Save id {uid} is our {KindOf(c)}{(c.npc == null ? "" : " (an NPC)")}, not the host's {name}: making it from its prefab");
                    c = null;
                }
            }
            // An NPC made while playing (a scripted spawn runs in every game) has a save id of its own in each game:
            // ours is the one of that kind near where the host's is.
            if (c == null) c = HostileNpcs.OurCopy(name, pos, x => _byChar.ContainsKey(x), KindOf);
            if (c == null)
            {
                GameObject go = null;
                foreach (var candidate in new[] { prefab, "Characters/" + name })
                {
                    var res = Resources.Load("Prefabs/" + candidate);
                    if (res != null) { go = Core.AddPrefab(res, pos, rot, null, worldSpace: true); break; }
                }
                c = go != null ? go.GetComponent<Character>() : null;
                ours = c != null;
                if (c == null) { if (go != null) Object.Destroy(go); Plugin.Log.LogWarning($"Couldn't make the host's enemy {name} ({prefab})"); return; }
                var save = c.GetComponent<SaveableObject>();
                if (save != null) save.dontSave = true; // it's the host's; don't write it into this world's save
            }
            var p = new Puppet { Id = id, C = c, Ours = ours };
            bool wasOff = !c.gameObject.activeInHierarchy;
            Uncull(p);
            Freeze(c, hide: false);
            SetVisible(c, true);
            PlayIdleLoop(c);
            c.transform.SetPositionAndRotation(pos, rot);
            Animators(c.transform, out var list, out var paths);
            for (int i = 0; i < list.Count; i++) p.Animators[paths[i]] = list[i];
            _puppets[id] = p;
            _byChar[c] = p;
            if (c.npc != null) HostileNpcs.ClientStarted(c, id);
            Plugin.Log.LogInfo($"Showing the host's {name} #{id} ({(ours ? "made from its prefab" : "our copy from the save")}{(wasOff ? ", switched on" : "")}); {Frozen.Count} of our own characters are frozen");
        }

        private void OnState(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !ClientMode) return;
            float sent = r.R.ReadSingle();
            float now = Time.unscaledTime;
            EnemyMotion.Arrived(sent, now);
            int n = r.R.ReadUInt16();
            for (int i = 0; i < n; i++)
            {
                uint id = r.R.ReadUInt32();
                Vector3 pos = r.ReadVector3();
                Quaternion rot = ReadRot(r);
                int bits = r.R.ReadByte();
                bool alive = (bits & 1) != 0, asleep = (bits & 2) != 0, cut = (bits & 4) != 0;
                float healthFraction = r.R.ReadSingle();
                int an = r.R.ReadByte();
                _puppets.TryGetValue(id, out var p);
                for (int k = 0; k < an; k++)
                {
                    int path = r.R.ReadInt32(); int clip = r.R.ReadInt32(); int frame = r.R.ReadUInt16(); bool playing = r.R.ReadByte() != 0;
                    if (p != null && !p.Settled && p.Animators.TryGetValue(path, out var a) && a != null) ApplyAnim(a, clip, frame, playing);
                }
                if (p == null || p.C == null) continue;
                // Drawn by the host's time (EnemyMotion), frame by frame (MovePuppets).
                EnemyMotion.Add(p.Motion, sent, pos, rot);
                p.Has = true;
                if (alive) SetHealth(p.C, healthFraction);
                if (alive && p.C.sleeping != asleep)
                {
                    // Asleep or awake as the host's is, with the idle loop to match (a mimic's sleeping one).
                    p.C.sleeping = asleep;
                    // Waking, the host's own loop (idle, or aggressive as it comes at someone) comes as an EnemySound,
                    // maybe before this: only the sleeping one is replaced.
                    var loop = p.C.sounds != null ? p.C.sounds.idleAudioObject : null;
                    if (asleep || loop == null || !loop.IsPlaying() || loop.audioID == p.C.sounds.sleeping) PlayIdleLoop(p.C);
                }
                if (!alive && cut) p.Cut = true;
                if (!alive && p.DiedAt < 0f)
                {
                    // Out of play as on the host: a character with a pre-death stage would still count as in it here
                    // (Character.hasPreDeath with health left).
                    if (p.C.maxHealth > 0f) p.C.health = Mathf.Min(p.C.health, Mathf.Clamp01(healthFraction) * p.C.maxHealth);
                    p.C.alive = false; p.DiedAt = Time.unscaledTime;
                    StopSounds(p.C, keepDeathSound: true);
                    // What's attached to it (the human spider's sound_loop) goes as it dies, as die2 does on the host.
                    try { p.C.removeAttachedGameObjects(); } catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't remove what's attached to {p.C.name}: {e.Message}"); }
                    // The human spiders' sound_loop child (an Item with ItemSounds) is gone from the host's copy as it
                    // dies (seen in the map test, 0.5 s after); a copy's frozen scripts never remove it.
                    foreach (var it in p.C.GetComponentsInChildren<Item>(true))
                        if (it != null && it.gameObject != p.C.gameObject && it.GetComponent<ItemSounds>() != null) Object.Destroy(it.gameObject);
                    if (p.Cut) CutLooks(p.C); else DeathLooks(p.C);
                    Plugin.Log.LogInfo($"[enemy] {p.Id} died{(p.Cut ? " (cut in half)" : "")}: sounds stopped");
                }
            }
        }

        /// <summary>
        /// Client: silence a copy of the host's enemy, as Character.die2 does on the host (sounds.destroySounds).
        /// The copy never runs die2, so without this its own idle loop (started by CharacterSounds.OnEnable) and
        /// the loops the host relayed (a dog's barking, which the host stops in its die2) went on playing over the
        /// corpse; and Settle's destroyComponents destroys CharacterSounds without stopping its loop, which left it
        /// playing for good. Its coroutines are stopped too, as die2 does.
        /// </summary>
        private static void StopSounds(Character c, bool keepDeathSound)
        {
            if (c == null) return;
            string death = c.sounds != null ? c.sounds.death : null;
            if (c.sounds != null) { try { c.sounds.destroySounds(); } catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't stop {c.name}'s sounds: {e.Message}"); } }
            // Sounds played on it (the relayed ones and its own) are AudioObjects under it.
            foreach (var ao in c.GetComponentsInChildren<AudioObject>(true))
            {
                if (ao == null || (keepDeathSound && !string.IsNullOrEmpty(death) && ao.audioID == death)) continue;
                ao.Stop(0.3f);
            }
            if (!keepDeathSound) return; // only hidden, not dead
            c.StopAllCoroutines();
        }

        /// <summary>
        /// Client: the host's health for a copy, and its health bar as the game does for a hit not by our player:
        /// shown if the player has it selected, and refreshed if it's the one shown.
        /// </summary>
        private static void SetHealth(Character c, float fraction)
        {
            if (c.maxHealth <= 0f) return;
            float hp = Mathf.Clamp01(fraction) * c.maxHealth;
            if (Mathf.Abs(hp - c.health) < 0.01f) return;
            bool lost = hp < c.health;
            c.health = hp;
            HealthBar(c, show: lost && Player.Instance != null && Player.Instance.selectedObject == c.transform);
        }

        /// <summary>Show an enemy's health bar (as Character.getHit does for a hit by the player), or refresh it if it's showing.</summary>
        private static void HealthBar(Character c, bool show)
        {
            var bar = Singleton<UI>.Instance != null ? Singleton<UI>.Instance.enemyHealthBar : null;
            if (bar == null || c == null) return;
            try
            {
                if (show) bar.show(c.gameObject);
                else if (bar.currentObj == c.gameObject) bar.show(c.gameObject, onlyRefresh: true);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't show {c.name}'s health bar: {e.Message}"); }
        }

        /// <summary>
        /// Client: what Character.die2 does to how a dying character looks, for a copy (which never runs die2): its
        /// particles (a burning one's flames) stop, its shadow fades out, and so does a shadow armour's effect.
        /// </summary>
        private static void DeathLooks(Character c)
        {
            try
            {
                foreach (var ps in c.GetComponentsInChildren<ParticleSystem>()) if (ps != null) ps.Stop();
                if (c.shadow != null) Core.tweenColor(c.shadow.gameObject, new Color(1f, 1f, 1f, 0f), 1f);
                var armor = c.GetComponent<ShadowArmor>(); // its shadow armour's effect fades, as die2 does
                if (armor != null) armor.die();
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't play {c.name}'s death here: {e.Message}"); }
        }

        /// <summary>
        /// Client: a separate location's characters were switched off (left) or on again (entered) here. The game does
        /// that to the corpses of the location's own characters (Location.activateCharacters: SetActive false, then
        /// true), so on the host a corpse's play-on-awake particles (a ChomperRed's) start again when its location is
        /// entered again. A copy we made from its prefab isn't one of the location's characters, so it would stay as it
        /// was: the dead ones of those standing in the location are switched off and on with it, as the host's are.
        /// Living copies stay as they are: the host runs them while a joiner is in their location.
        /// </summary>
        internal void LocationCharacters(Location loc, bool active)
        {
            if (loc == null) return;
            foreach (var p in _corpses.Values.Concat(_puppets.Values).ToList())
            {
                if (p == null || !p.Ours || p.C == null || p.C.alive || p.C.gameObject.activeSelf == active) continue;
                if (!Core.withinLocation(p.C.transform.position, 0f, loc)) continue;
                p.C.gameObject.SetActive(active);
                string again = active ? string.Join(", ", p.C.GetComponentsInChildren<ParticleSystem>().Where(ps => ps.isEmitting).Select(ps => ps.name)) : "";
                Plugin.Log.LogInfo($"[enemy] the host's corpse #{p.Id} ({p.C.name}) switched {(active ? "on" : "off")} with {Core.getTrueLocationName(loc.name)}{(again != "" ? "; playing again: " + again : "")}");
            }
        }

        /// <summary>How long the host's death animation is shown before the corpse is settled here.</summary>
        private const float DeathAnimTime = 2f;

        private static readonly System.Reflection.MethodInfo[] CorpseSteps =
        {
            HarmonyLib.AccessTools.Method(typeof(Character), "destroyComponents"),
            HarmonyLib.AccessTools.Method(typeof(Character), "destroyComponents2"),
            HarmonyLib.AccessTools.Method(typeof(Character), "setDeathCollider"),
        };

        /// <summary>
        /// Client: one of the host's enemies died, and its death has played. In the game it's the end of the death
        /// animation that turns the body into a lootable corpse (Character.animationCompleted: destroyComponents,
        /// destroyComponents2, setDeathCollider); a copy's animation callbacks are off, so that's done here, once.
        /// After it the copy's frames are left on the last one rather than replayed from the host's updates.
        /// </summary>
        private static void Settle(Puppet p)
        {
            p.Settled = true;
            var c = p.C;
            if (c == null) return;
            foreach (var a in p.Animators.Values)
            {
                if (a == null || a.CurrentClip == null || a.CurrentClip.frames == null || a.CurrentClip.frames.Length == 0) continue;
                a.SetFrame(a.CurrentClip.frames.Length - 1, triggerEvent: false);
                a.Pause();
            }
            StopSounds(c, keepDeathSound: true); // destroyComponents destroys CharacterSounds without stopping its loop
            if (p.Cut) { SettleCut(p); return; }
            if (c.animator == null)
            {
                // No sprite animation (the worms): the end of a death animation never comes in the host's game either,
                // so its corpse is never made lootable there; only what dying itself does (die2) to the collider.
                if (c.collider != null) { if (!c.dontSwitchColliderTriggerOnDeath) c.collider.isTrigger = true; c.collider.enabled = true; }
                Plugin.Log.LogInfo($"The host's {c.name} #{p.Id} died: it has no death animation, so its body isn't searchable (as on the host)");
                return;
            }
            foreach (var step in CorpseSteps)
            {
                if (step == null) continue;
                try { step.Invoke(c, null); }
                catch (System.Exception e)
                {
                    if (step.Name == "setDeathCollider") DeathColliderRest(c, e.InnerException ?? e);
                    else Plugin.Log.LogWarning($"Couldn't settle {c.name}'s corpse ({step.Name}): {e.InnerException?.Message ?? e.Message}");
                }
            }
            foreach (var col in c.GetComponentsInChildren<Collider>(true)) if (col != null) col.enabled = true;
            Plugin.Log.LogInfo(c.GetComponent<Item>() != null ? $"The host's {c.name} #{p.Id} died: its corpse can be searched here" : $"The host's {c.name} #{p.Id} died, but its corpse couldn't be made searchable here");
        }

        /// <summary>
        /// Client: how a copy cut in half on the host looks as it starts: what Character.cutMeInHalf does to the
        /// upper part (its particles stop; it doesn't fade its shadow as a death does). The lower half is a new
        /// enemy on the host, shown here as any other.
        /// </summary>
        private static void CutLooks(Character c)
        {
            try { foreach (var ps in c.GetComponentsInChildren<ParticleSystem>()) if (ps != null) ps.Stop(); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't play {c.name}'s cut here: {e.Message}"); }
        }

        private static readonly System.Reflection.MethodInfo[] CutSteps =
        {
            HarmonyLib.AccessTools.Method(typeof(Character), "destroyComponents"),
            HarmonyLib.AccessTools.Method(typeof(Character), "destroyComponents2"),
        };

        /// <summary>
        /// Client: the upper part of a copy cut in half on the host, once its cut has played: as on the host
        /// (cutMeInHalf, then its Cut_half animation's end), no corpse: no Item to search, its inventory went with the
        /// lower half (the host's new enemy), its selectable and capsule are gone, and it lies flat.
        /// </summary>
        private static void SettleCut(Puppet p)
        {
            var c = p.C;
            foreach (var step in CutSteps)
            {
                if (step == null) continue;
                try { step.Invoke(c, null); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't settle {c.name}'s cut ({step.Name}): {e.InnerException?.Message ?? e.Message}"); }
            }
            try
            {
                c.cuttingInHalf = true; c.cutInHalf = true;
                var item = c.GetComponent<Item>(); if (item != null) Object.Destroy(item);
                var inv = c.GetComponent<Inventory>(); if (inv != null) Object.Destroy(inv);
                var sel = c.GetComponent<Selectable>(); if (sel != null) Object.Destroy(sel);
                if (c.capsuleCollider != null) Object.Destroy(c.capsuleCollider);
                if (c.collider != null) c.collider.isTrigger = true;
                var t = c.transform;
                t.localPosition = new Vector3(t.localPosition.x, -1f, t.localPosition.z);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't settle {c.name}'s cut: {e.Message}"); }
            Plugin.Log.LogInfo($"The host's {c.name} #{p.Id} was cut in half: its lower half is the host's, nothing to search here");
        }

        private static readonly System.Reflection.MethodInfo YPosAfterDeath = HarmonyLib.AccessTools.Method(typeof(Character), "setYPosAfterDeath");

        /// <summary>
        /// Client: Character.setDeathCollider stopped part way on a copy (it threw). Its first step, the Item that
        /// makes the corpse searchable, is done by then; the rest is done here, each part only if what it needs is
        /// there: the body becomes a trigger (or our player bumps into a corpse the host's player walks through),
        /// takes the death collider of the clip it ended on, stops counting as active, and lies at a corpse's height.
        /// An NPC's death (its flags, its death inventory) comes from the host.
        /// </summary>
        private static void DeathColliderRest(Character c, System.Exception why)
        {
            try
            {
                if (c.GetComponent<Item>() == null) c.gameObject.AddComponent<Item>().name = c.name.ToLower() + "_corpse";
                if (!c.dontSwitchColliderTriggerOnDeath && c.collider != null) c.collider.isTrigger = true;
                var a = c.GetComponent<tk2dSpriteAnimator>();
                string clip = a != null && a.CurrentClip != null ? a.CurrentClip.name : null;
                if (clip != null && c.capsuleCollider != null && c.deathColliders != null)
                    foreach (var dc in c.deathColliders)
                        if (dc != null && dc.identifier == clip)
                        {
                            c.capsuleCollider.center = dc.center; c.capsuleCollider.radius = dc.radius; c.capsuleCollider.height = dc.height;
                        }
                c.isActive = false;
                YPosAfterDeath?.Invoke(c, null);
                Plugin.Log.LogInfo($"{c.name}'s corpse settled here by the mod: the game's setDeathCollider stopped ({why.Message}; " +
                                   $"animator {(a != null ? "there" : "none")}, clip {clip ?? "none"}, collider {(c.collider != null ? "there" : "none")}, " +
                                   $"capsule {(c.capsuleCollider != null ? "there" : "none")}, death colliders {c.deathColliders?.Count ?? -1}; at {why.StackTrace?.Split('\n')[0]?.Trim()}); trigger now {(c.collider != null && c.collider.isTrigger)}");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't settle {c.name}'s corpse: {e.InnerException?.Message ?? e.Message}"); }
        }

        private static void ApplyAnim(tk2dSpriteAnimator a, int clipHash, int frame, bool playing)
        {
            a.AnimationEventTriggered = CopyFrameEvent; // never let a puppet's frames act (attack, sound) here
            a.AnimationCompleted = null;
            if (clipHash == 0 || a.Library == null) return;
            var cur = a.CurrentClip;
            if (cur == null || StableHash.Of(cur.name) != clipHash)
            {
                foreach (var clip in a.Library.clips)
                    if (clip != null && StableHash.Of(clip.name) == clipHash) { a.PlayFromFrame(clip, Mathf.Clamp(frame, 0, Mathf.Max(0, clip.frames.Length - 1))); break; }
            }
            // More than 2 frames off: move the copy's clip time to the host's frame. (SetFrame would change only the
            // sprite shown; the next animator update goes back to the copy's own clip time, so the copy flicked to
            // the host's frame and back on every update and stayed off, as a world rabbit did 3 frames ahead.)
            else if (Mathf.Abs(a.CurrentFrame - frame) > 2 && cur.frames != null && frame < cur.frames.Length) a.PlayFromFrame(cur, frame);
            if (!playing && a.Playing) a.Pause(); else if (playing && a.Paused) a.Resume();
        }

        /// <summary>
        /// Client: a copy's animation events. Of what Character.OnAniEvent does, only "TweenShadows" (fading the
        /// character's extra shadows out or in with the animation) is played here: it changes how the copy looks and
        /// nothing else. Attacks, sounds and the rest are the host's.
        /// </summary>
        private static void CopyFrameEvent(tk2dSpriteAnimator a, tk2dSpriteAnimationClip clip, int frame)
        {
            try
            {
                var f = clip != null ? clip.GetFrame(frame) : null;
                if (f == null || f.eventInfo != "TweenShadows") return;
                var c = a != null ? a.GetComponentInParent<Character>() : null;
                if (c != null) c.tweenShadows(hide: f.eventInt == 0);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't play a copy's TweenShadows: {e.Message}"); }
        }

        private void OnDespawn(ulong from, PacketReader r)
        {
            if (from != _session.HostId) return;
            uint id = r.R.ReadUInt32();
            bool keepCorpse = r.R.ReadByte() != 0;
            if (!_puppets.TryGetValue(id, out var p))
            {
                // The host's corpse is gone (it was destroyed there): so is ours.
                if (keepCorpse || !_corpses.TryGetValue(id, out p)) return;
                _corpses.Remove(id);
                if (p.C == null) return;
                _corpseIds.Remove(p.C);
                Plugin.Log.LogInfo($"The host's corpse #{id} ({p.C.name}) is gone there: removed here");
                // Destroyed, our copy from the save too: the host's is out of its world for good, and a hidden copy
                // would still be in ours (its body searchable by name, saved with our world).
                Object.Destroy(p.C.gameObject);
                return;
            }
            _puppets.Remove(id);
            if (p.C == null) return;
            _byChar.Remove(p.C);
            if (keepCorpse) { if (!p.Settled) Settle(p); _corpses[id] = p; _corpseIds[p.C] = id; Recull(p); return; } // stays where it fell
            StopSounds(p.C, keepDeathSound: false);
            // An NPC the host calmed down (or one out of our range): our copy of it is ours to run again.
            if (p.C.npc != null && HostileNpcs.ClientStopped(p.C, id, ours: p.Ours)) { Recull(p); return; }
            if (p.Ours) Object.Destroy(p.C.gameObject); else { SetVisible(p.C, false); Recull(p); }
        }

        private void OnSound(ulong from, PacketReader r)
        {
            if (from != _session.HostId) return;
            uint id = r.R.ReadUInt32();
            string audio = r.ReadStr();
            float volume = r.R.ReadSingle();
            if (!_puppets.TryGetValue(id, out var p) || p.C == null) return;
            // Once it's dead here, only its death cry still plays (it can arrive just after the state that said so);
            // a bark or a loop from before the host's die2, arriving late, would go on over the corpse.
            if (p.DiedAt >= 0f && (p.C.sounds == null || audio != p.C.sounds.death)) return;
            // One of its loops (CharacterSounds.playIdleLoop: idle, aggressive while it chases, defensive, sleeping,
            // escaping) takes the place of the copy's loop, as on the host. Played as a sound it would go on beside
            // the copy's idle loop, and nothing would stop it. "" is the host's loop stopping (HostLoopStopPatch).
            var snd = p.C.sounds;
            if (string.IsNullOrEmpty(audio))
            {
                if (snd != null) { try { snd.playIdleLoop(""); } catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't stop {p.C.name}'s loop: {e.Message}"); } }
                return;
            }
            if (IsLoop(snd, audio))
            {
                try { snd.playIdleLoop(audio); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't play {p.C.name}'s loop {audio}: {e.Message}"); }
                return;
            }
            AudioController.Play(audio, p.C.transform, volume);
        }

        /// <summary>Client: we hit a host's enemy; the host applies it.</summary>
        public void ClientHit(uint id, float damage, bool cut, bool byPlayer, bool interrupt, bool normal, List<InvItemEffect> effects)
        {
            var w = _session.Begin(MsgType.EnemyHit);
            w.W.Write(id);
            w.W.Write(damage);
            w.W.Write((byte)((cut ? 1 : 0) | (byPlayer ? 2 : 0) | (interrupt ? 4 : 0) | (normal ? 8 : 0)));
            PlayerDamage.WriteEffects(w, effects);
            var item = Player.Instance != null ? Player.Instance.currentItem : null;
            w.WriteStr(InvItemClass.isNull(item) ? "" : Newtonsoft.Json.JsonConvert.SerializeObject(new InvItemClass.SaveState(item, 0)));
            World.Authority.Request(w, $"hit on enemy #{id} for {damage:0}");
            // Our own hit shows its health bar, as getHit does; the host's health for it follows in its next state.
            if (damage > 0f && _puppets.TryGetValue(id, out var hit) && hit.C != null) HealthBar(hit.C, show: true);
            // Predicted: the blood shows at once; the host's for this hit (a moment later) isn't shown again.
            if (_puppets.TryGetValue(id, out var p) && p.C != null)
            {
                Core.AddPrefab("FX/Bloodsplats/Shotsplat_stay", p.C.transform.position, Quaternion.Euler(90f, UnityEngine.Random.Range(0f, 360f), 0f), null);
                _predictedBlood[id] = Time.realtimeSinceStartup;
            }
        }

        private readonly Dictionary<uint, float> _predictedBlood = new Dictionary<uint, float>();

        /// <summary>Client: the effects of the weapon hitting right now (set around a player's MeleeSensor hit).</summary>
        public static List<InvItemEffect> HitEffects;

        // ---- shared helpers ------------------------------------------------------

        private static void Animators(Transform root, out List<tk2dSpriteAnimator> list, out List<int> paths)
        {
            list = new List<tk2dSpriteAnimator>();
            paths = new List<int>();
            foreach (var a in root.GetComponentsInChildren<tk2dSpriteAnimator>(true))
            {
                string p = a.transform == root ? "." : a.name;
                for (var t = a.transform.parent; t != null && t != root && a.transform != root; t = t.parent) p = t.name + "/" + p;
                list.Add(a);
                paths.Add(StableHash.Of(p));
            }
        }

        private static void WriteRot(PacketWriter w, Quaternion q) { w.W.Write(q.x); w.W.Write(q.y); w.W.Write(q.z); w.W.Write(q.w); }
        private static Quaternion ReadRot(PacketReader r) => new Quaternion(r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle());
    }

    /// <summary>
    /// The characters that are active now, as Object.FindObjectsOfType&lt;Character&gt;() finds them, without
    /// searching every object in the scene (that took about 50 ms in a loaded world, twice a second on the host).
    /// Every character passes Character.OnEnable (no kind of character has its own), which lists it here; the
    /// destroyed ones drop out and the inactive ones are skipped.
    /// </summary>
    public static class AllCharacters
    {
        private static readonly HashSet<Character> _all = new HashSet<Character>();
        private static readonly List<Character> _now = new List<Character>();

        internal static void Add(Character c) { if (c != null) _all.Add(c); }

        /// <summary>A list reused on each call: use it before calling again.</summary>
        public static List<Character> Active()
        {
            _now.Clear();
            _all.RemoveWhere(c => c == null);
            foreach (var c in _all) if (c.isActiveAndEnabled) _now.Add(c);
            return _now;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Character), "OnEnable")]
    static class AllCharactersPatch
    {
        static void Postfix(Character __instance) => AllCharacters.Add(__instance);
    }
}
