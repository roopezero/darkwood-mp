using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The host's enemies, shown on its clients. The host runs every enemy; a
    /// client runs none of its own.
    ///
    /// Host: every non-NPC Character gets a net id. Each client is told about
    /// the ones near it (within RelevantRange of its proxy) with EnemySpawn,
    /// then gets EnemyState for them twenty times a second (the host's clock,
    /// position, rotation, alive, and each sprite animator's clip and frame),
    /// EnemySound for the sounds they make, and EnemyDespawn when one goes out
    /// of its range, or a while after it died (its corpse then stays put).
    /// A corpse is sent to each client once, when it first comes within its
    /// range (one that was already dead when they joined, or that they walked
    /// away from before it settled): EnemySpawn, one EnemyState that says it's
    /// dead, and EnemyDespawn keeping the corpse, then what it holds. The
    /// client's corpse stays until the host's is gone.
    ///
    /// Client: its own non-NPC characters are frozen as they start (their AI,
    /// pathfinding and physics off, their animators' frame events cleared so
    /// they can't attack or make sounds) and hidden. A spawned enemy is the
    /// client's own copy of it from the save, found by its save id, or else
    /// made from its prefab; it's then shown and driven by the host's states,
    /// playing the same animation clips. Damage the client deals it (melee,
    /// bullets, fire...) goes to the host as EnemyHit instead of being applied,
    /// and the host applies it with the client's proxy as the attacker, so the
    /// enemy reacts to, and gains threat for, that player.
    ///
    /// NPCs (traders, quest characters) are each player's own while they're
    /// friendly. One that turns on a player is run by the host as one of these
    /// enemies until it calms down (Combat.HostileNpcs).
    /// </summary>
    public sealed class NetEnemies
    {
        public const float RelevantRange = 3000f;
        public const float StateInterval = 0.05f;
        private const float CorpseLinger = 6f;

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
            session.Register(
                MsgType.PlayerNoise, PlayerNoisePatch.OnRemoteNoise);
            session.Register(MsgType.PlayerCue, PlayerCues.OnCue);
            session.Register(MsgType.EnemyBlood, OnBlood);
            session.Register(MsgType.EnemyArmor, OnArmor);
            session.OnPeerDisconnected += id =>
            {
                _known.Remove(id);
                _corpseKnown.Remove(id);
            };

            session.Register(MsgType.NpcHit, HostileNpcs.OnNpcHit);
            session.Register(MsgType.NpcAttack, HostileNpcs.OnNpcAttack);
            session.OnSessionEnded += () =>
            {
                HostileNpcs.Clear();
                _known.Clear();
                _corpseKnown.Clear();
                _byCharHost.Clear();
                _byIdHost.Clear();
                _puppets.Clear();
                _byChar.Clear();
                EnemyMotion.Reset();
                _corpses.Clear();
                _corpseIds.Clear();
                Frozen.Clear();
                Hidden.Clear();
                _switchedOff.Clear();
            };
        }

        /// <summary>
        /// One of the characters the host runs and its clients show: every
        /// creature, and an NPC while it's hostile (Combat.HostileNpcs: the
        /// host's copy turned on a player, or a client's copy is one the host
        /// runs).
        /// </summary>
        public static bool Eligible(Character c) =>
            c != null && (c.npc == null || HostileNpcs.Running(c));

        /// <summary>
        /// Still in the fight: alive, or in a pre-death stage
        /// (Character.hasPreDeath: alive is false, but it crawls with health
        /// left until a hit finishes it or it bleeds out; getHit and the
        /// player's melee still hit it).
        /// </summary>
        public static bool InPlay(Character c) =>
            c != null && (c.alive || (c.hasPreDeath && c.health > 0f));

        /// <summary>
        /// Client: a character of an area only this player is in: a dream of
        /// its own (the shared level dream is the host's,
        /// World.Locations.SharedDream), or a separate location the host didn't
        /// place for us (World.Locations.HostRunsHere), or anything still in a
        /// scene of its own. The host doesn't run it, so it runs here, as in
        /// single player. A separate location the host placed is the host's to
        /// run, as the world is (it loads it at the same spot even when its
        /// player isn't there): our characters there are frozen and the host's
        /// are shown.
        /// </summary>
        public static bool LocalOnly(Character c) =>
            c != null && LocalOnly(c.gameObject);

        /// <summary>
        /// Client: a thing in an area only this player is in (as for a
        /// character, above).
        /// </summary>
        public static bool LocalOnly(GameObject go)
        {
            if (go == null)
            {
                return false;
            }

            if (go.scene
                != UnityEngine.SceneManagement.SceneManager.GetActiveScene())
            {
                return true;
            }

            // In the dream's own place: also while our player is still on the
            // way in (a stage's start event fires before the player stands
            // there), when the distance below doesn't tell yet. Not a shared
            // level dream's (or the epilogue's): the host runs those
            // (World.Locations.SharedDream); a prologue stage each game runs.
            var d = Singleton<Dreams>.Instance;
            if (d != null && d.dreamLocation != null
                && (d.dreaming || d.dreamPrepared || d.switchingDream)
                && go.transform.IsChildOf(d.dreamLocation.transform)
                && (World.DreamSync.Shared == null || World.Prologue.IsStage(
                    World.DreamSync.Shared)))
            {
                return true;
            }

            if (World.Locations.HostRunsHere())
            {
                return false;
            }

            var p = Player.Instance;
            return p != null
                && (go.transform.position - p.transform.position).sqrMagnitude
                    < 4000f * 4000f;
        }

        /// <summary>
        /// Client: undo Freeze, for a character that turned out to be in an
        /// area of our own.
        /// </summary>
        public static void Thaw(Character c)
        {
            if (c == null || !Frozen.Remove(c))
            {
                return;
            }

            Hidden.Remove(c);
            SetVisible(c, true);
            var rb = c.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
            }

            if (c.AIpath != null)
            {
                c.AIpath.enabled = true;
            }

            foreach (var mb in c.GetComponents<MonoBehaviour>())
            {
                if (mb is AILerp)
                {
                    mb.enabled = true;
                }
            }

            c.enabled = true; // its OnEnable hooks its animators back up
            ClientFreezeCharacterPatch.Starting = true;
            try
            {
                c.Start();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"{c.name} didn't start cleanly: {e.Message}");
            }
            finally
            {
                ClientFreezeCharacterPatch.Starting = false;
            }

            PlayIdleLoop(c);
        }

        /// <summary>
        /// Client: start a shown character's idle loop again
        /// (CharacterSounds.OnEnable's), stopped while it was hidden: its
        /// sleeping one while the host's is asleep (Character.sleeping, sent in
        /// EnemyState), as enableComponents picks.
        /// </summary>
        private static void PlayIdleLoop(Character c)
        {
            if (c == null || !c.alive || c.sounds == null)
            {
                return;
            }

            try
            {
                string loopName;
                if (c.sleeping)
                {
                    loopName = c.sounds.sleeping;
                }
                else
                {
                    loopName = c.sounds.idleLoop;
                }

                c.sounds.playIdleLoop(loopName);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't start {c.name}'s idle sound: {e.Message}");
            }
        }

        public void Update()
        {
            if (_session.IsHost && _session.Peers.Count > 0
                && WorldShare.InWorld)
            {
                HostUpdate();
            }

            if (ClientMode)
            {
                ClientSweep();
                MovePuppets();
            }
        }

        /// <summary>
        /// Client, every frame: each of the host's enemies drawn where the host
        /// has it now, by the host's clock and its last states (EnemyMotion),
        /// so they move at the host's speed whenever its states come.
        /// </summary>
        private void MovePuppets()
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            EnemyMotion.Frame(dt);
            foreach (var p in _puppets.Values)
            {
                if (p.C != null && p.DiedAt >= 0f && !p.Settled
                    && now - p.DiedAt > DeathAnimTime)
                {
                    Settle(p);
                }

                if (!p.Has || p.C == null)
                {
                    continue;
                }

                if (!p.C.gameObject.activeSelf && !p.Settled)
                {
                    Plugin.Log.LogInfo(
                        $"[enemy] the host's {p.C.name} #{p.Id} was switched "
                            + "off here: on again");
                    p.C.gameObject.SetActive(true);
                }

                if (!EnemyMotion.Where(p.Motion, now, out var pos, out var rot))
                {
                    continue;
                }

                p.C.transform.SetPositionAndRotation(pos, rot);
                EnemyMotion.Stats.Drawn(p.Motion, pos, dt);
                AlignShadow(p.C);
            }
        }

        /// <summary>
        /// Client: a copy's shadow, placed as Character.Update places it (the
        /// copy's Update doesn't run): not turned with the body unless
        /// rotateShadow, at shadow height, lowered once it's dead. A flier's
        /// shadow is offset by its altitude, which the host doesn't send, so
        /// it's left as it is.
        /// </summary>
        private static void AlignShadow(Character c)
        {
            var sh = c.shadow;
            if (sh == null || c.flier != null)
            {
                return;
            }

            if (!c.rotateShadow)
            {
                sh.eulerAngles = new Vector3(90f, 0f, 0f);
            }

            var at = c.transform.position + new Vector3(
                c.shadowOffset.x, 0f, c.shadowOffset.z);
            PosType posType;
            if (c.alive)
            {
                posType = PosType.characterShadow;
            }
            else
            {
                posType = PosType.low1;
            }

            sh.position =
                Core.getYPos(at, posType, randomize: false) + c.posSeed;
        }

        // =====================================================================
        // Host
        // =====================================================================

        private sealed class Tracked
        {
            public uint Id;
            public Character C;
            public float DiedAt = -1f;
            // an NPC: when it stopped being hostile
            public float CalmSince = -1f;
            public List<tk2dSpriteAnimator> Animators;
            public List<int> AnimatorPaths;
            // the loop it was playing at the last LoopCheck
            public string Loop = "";
            // it wore shadow armour at the last ArmorSync: what the clients
            // that know it were told
            public bool Armor;
        }

        private uint _nextId = 1;
        private readonly Dictionary<Character, Tracked> _byCharHost =
            new Dictionary<Character, Tracked>();
        private readonly Dictionary<uint, Tracked> _byIdHost =
            new Dictionary<uint, Tracked>();
        private readonly Dictionary<ulong, HashSet<uint>> _known =
            new Dictionary<ulong, HashSet<uint>>();
        // Corpses each client was told to keep (EnemyDespawn with keep corpse):
        // not sent again.
        private readonly Dictionary<ulong, HashSet<uint>> _corpseKnown =
            new Dictionary<ulong, HashSet<uint>>();
        private float _nextScan, _nextState;
        private readonly PacketWriter _batch = new PacketWriter();
        private readonly PacketWriter _one = new PacketWriter();
        /// <summary>
        /// Corpses sent to one client in one state pass at most (a joiner can
        /// have dozens near them).
        /// </summary>
        private const int CorpsesPerPass = 4;

        private void HostUpdate()
        {
            float now = Time.unscaledTime;
            if (now >= _nextScan)
            {
                _nextScan = now + 0.5f;
                foreach (var c in AllCharacters.Active())
                {
                    if (Eligible(c) && !_byCharHost.ContainsKey(c))
                    {
                        var t = new Tracked { Id = _nextId++, C = c };
                        Animators(
                            c.transform, out t.Animators, out t.AnimatorPaths);
                        _byCharHost[c] = t;
                        _byIdHost[t.Id] = t;
                        if (c.npc != null)
                        {
                            HostileNpcs.HostStarted(c, t.Id);
                        }
                    }
                }

                var gone = new List<Character>();
                foreach (var kv in _byCharHost)
                {
                    if (kv.Key == null)
                    {
                        gone.Add(kv.Key);
                        continue;
                    }

                    // An NPC that calmed down (and stayed calm a while) is each
                    // player's own again.
                    if (kv.Key.npc == null || !InPlay(kv.Key))
                    {
                        continue;
                    }

                    if (HostileNpcs.StillHostile(kv.Key))
                    {
                        kv.Value.CalmSince = -1f;
                        continue;
                    }

                    if (kv.Value.CalmSince < 0f)
                    {
                        kv.Value.CalmSince = now;
                    }
                    else if (now - kv.Value.CalmSince > HostileNpcs.CalmTime)
                    {
                        gone.Add(kv.Key);
                    }
                }

                foreach (var g in gone)
                {
                    uint id = _byCharHost[g].Id;
                    if (g != null)
                    {
                        HostileNpcs.HostStopped(g, id);
                    }

                    Forget(id);
                    _byCharHost.Remove(g);
                    _byIdHost.Remove(id);
                }
            }

            if (now >= _nextLoopCheck)
            {
                _nextLoopCheck = now + LoopCheckInterval;
                LoopCheck(now);
                foreach (var t in _byCharHost.Values)
                {
                    if (t.C != null && t.DiedAt < 0)
                    {
                        ArmorSync(t);
                    }
                }
            }

            if (now < _nextState)
            {
                return;
            }

            _nextState = now + StateInterval;

            foreach (var peer in _session.Peers)
            {
                if (!NetPlayerProxy.All.TryGetValue(peer, out var proxy)
                    || proxy == null)
                {
                    continue;
                }

                // Away (a dream, or a separate location we haven't got where
                // they have it): their own game runs the enemies there
                // (LocalOnly), so none of ours go to them; what they have of
                // ours waits for their return. In a location we run for them
                // (proxy.InLocation) they get ours there, by the same range
                // check.
                if (proxy.Away)
                {
                    continue;
                }

                if (!_known.TryGetValue(peer, out var known))
                {
                    _known[peer] = known = new HashSet<uint>();
                }

                if (!_corpseKnown.TryGetValue(peer, out var corpses))
                {
                    _corpseKnown[peer] = corpses = new HashSet<uint>();
                }

                Vector3 at = proxy.transform.position;
                int corpsesSent = 0;

                var w = _batch.Begin(MsgType.EnemyState);
                // the host's clock, which the client draws them by
                // (EnemyMotion)
                w.W.Write(now);
                long countPos = w.Length;
                w.W.Write((ushort)0);
                int count = 0;
                var drop = new List<(uint, bool)>();
                foreach (var t in _byCharHost.Values)
                {
                    var c = t.C;
                    if (c == null)
                    {
                        continue;
                    }

                    if (!InPlay(c) && t.DiedAt < 0)
                    {
                        t.DiedAt = Time.time;
                    }

                    bool dead = t.DiedAt >= 0;
                    bool corpseDone =
                        dead && Time.time - t.DiedAt > CorpseLinger;
                    bool near = c.gameObject.activeInHierarchy
                        && (c.transform.position - at).sqrMagnitude
                            < RelevantRange * RelevantRange;
                    if (dead && corpses.Contains(t.Id))
                    {
                        // it has it, settled or not (sending it again would
                        // make a second body)
                        continue;
                    }

                    if (corpseDone && !known.Contains(t.Id))
                    {
                        // A corpse it never saw settle: it gets it once it's
                        // near.
                        if (near && corpsesSent < CorpsesPerPass)
                        {
                            SendCorpse(peer, t);
                            corpses.Add(t.Id);
                            corpsesSent++;
                        }

                        continue;
                    }

                    if (corpseDone || !near)
                    {
                        // Out of range, or settled: the dead stay where they
                        // fell (the client settles it now if it hadn't).
                        if (known.Contains(t.Id))
                        {
                            drop.Add((t.Id, dead));
                        }

                        continue;
                    }

                    if (known.Add(t.Id))
                    {
                        SendSpawn(peer, t);
                    }

                    WriteState(w, t);
                    count++;
                }

                foreach (var (id, corpse) in drop)
                {
                    known.Remove(id);
                    SendDespawn(peer, id, corpse);
                    if (corpse && corpses.Add(id))
                    {
                        SendCorpseLoot(peer, id);
                    }
                }

                if (count == 0)
                {
                    continue;
                }

                var buf = w.Buffer; // patch the count in
                buf[countPos] = (byte)count;
                buf[countPos + 1] = (byte)(count >> 8);
                _session.SendTo(peer, w, Channel.State, reliable: false);
            }
        }

        /// <summary>
        /// One of the loops CharacterSounds.playIdleLoop plays for this
        /// character.
        /// </summary>
        private static bool IsLoop(CharacterSounds snd, string audio) =>
            snd != null
                && (audio == snd.idleLoop || audio == snd.idleLoopAggressive
                    || audio == snd.aggressive
                    || audio == snd.defensive || audio == snd.sleeping
                    || audio == snd.escapingLoop);

        private float _nextLoopCheck, _nextLoopResend;
        private const float LoopCheckInterval = 0.5f, LoopResendInterval = 2f;

        /// <summary>
        /// Host: the loop each living enemy plays, made sure of in the clients'
        /// games. EnemySound tells them as a loop starts or is stopped by
        /// playIdleLoop(""), but a loop also stops other ways (destroySounds,
        /// playIdleLoop underground or under water), an unreliable EnemySound
        /// can be lost, and a client's copy starts its idle loop by itself when
        /// it's switched on again (CharacterSounds.OnEnable). So a change of
        /// loop goes reliably, and every couple of seconds each client is told
        /// the loop again (the same loop is a no-op there).
        /// </summary>
        private void LoopCheck(float now)
        {
            bool resend = now >= _nextLoopResend;
            if (resend)
            {
                _nextLoopResend = now + LoopResendInterval;
            }

            foreach (var t in _byCharHost.Values)
            {
                var c = t.C;
                if (c == null || t.DiedAt >= 0 || !InPlay(c))
                {
                    continue;
                }

                AudioObject loop = null;
                if (c.sounds != null)
                {
                    loop = c.sounds.idleAudioObject;
                }

                string cur = "";
                if (loop != null && loop.IsPlaying()
                    && !string.IsNullOrEmpty(loop.audioID))
                {
                    cur = loop.audioID;
                }

                if (cur != "" && !IsLoop(c.sounds, cur))
                {
                    continue; // not one the client takes as a loop (OnSound)
                }

                bool changed = cur != t.Loop;
                if (!changed && !resend)
                {
                    continue;
                }

                t.Loop = cur;
                foreach (var peer in Knowing(t.Id))
                {
                    SendSound(peer, t.Id, cur, 1f, reliable: changed);
                }
            }
        }

        /// <summary>
        /// What kind of character it is: its prefab's name, as the save system
        /// knows it.
        /// </summary>
        internal static string KindOf(Character c)
        {
            var save = c.GetComponent<SaveableObject>();
            if (save != null && !string.IsNullOrEmpty(save.prefabName))
            {
                return save.prefabName;
            }

            return c.name.Replace("(Clone)", "").Trim();
        }

        /// <summary>
        /// Whether a character wears shadow armour now (ShadowArmor.die
        /// destroys the component).
        /// </summary>
        private static bool HasArmor(Character c)
        {
            ShadowArmor a = null;
            if (c != null)
            {
                a = c.GetComponent<ShadowArmor>();
            }

            return a != null && a.enabled;
        }

        /// <summary>
        /// Host: the clients that know this enemy are told when its shadow
        /// armour is put on (CharacterSpawner adds it to a chapter 2+ night
        /// spawn after spawning it) or dies (light, or a hit). Only the host
        /// decides that: a client's copy shows the armour and never checks
        /// light or takes damage (ShadowArmorCopyPatch).
        /// </summary>
        private void ArmorSync(Tracked t)
        {
            bool now = HasArmor(t.C);
            if (now == t.Armor)
            {
                return;
            }

            t.Armor = now;
            foreach (var peer in Knowing(t.Id))
            {
                var w = _one.Begin(MsgType.EnemyArmor);
                w.W.Write(t.Id);
                int armorFlag = 0;
                if (now)
                {
                    armorFlag = 1;
                }

                w.W.Write((byte)armorFlag);
                _session.SendTo(peer, w, Channel.Control, reliable: true);
            }

            string text = "gone";
            if (now)
            {
                text = "on";
            }

            Plugin.Log.LogInfo(
                $"[enemy] {t.Id} {t.C.name}: shadow armour {text}");
        }

        private void SendSpawn(ulong peer, Tracked t)
        {
            var c = t.C;
            // first, so that the others that know it were told what this one is
            // told
            ArmorSync(t);
            var save = c.GetComponent<SaveableObject>();
            string name = KindOf(c);
            // The prefab's path under Resources/Prefabs, as the save system
            // knows it (Core.AddPrefab's argument).
            var sm = Singleton<SaveManager>.Instance;
            string path;
            if (sm != null && sm.prefabDictionary.TryGetValue(name, out var p))
            {
                path = p;
            }
            else
            {
                path = "Characters/" + name;
            }

            var w = _session.Begin(MsgType.EnemySpawn);
            w.W.Write(t.Id);
            int savedId = 0;
            if (save != null)
            {
                savedId = save.uniqueId;
            }

            w.W.Write(savedId);
            w.WriteStr(path);
            w.WriteStr(name);
            w.Write(c.transform.position);
            WriteRot(w, c.transform.rotation);
            int armorFlag = 0;
            if (t.Armor)
            {
                armorFlag = 1;
            }

            w.W.Write((byte)armorFlag);
            _session.SendTo(peer, w, Channel.Control, reliable: true);
            // The loop it's playing now (one that started before it was sent,
            // e.g. an NPC's aggressive one as it turned hostile): after the
            // spawn, on the same channel, so it comes after it.
            AudioObject loop = null;
            if (c.sounds != null)
            {
                loop = c.sounds.idleAudioObject;
            }

            if (loop != null && loop.IsPlaying()
                && !string.IsNullOrEmpty(loop.audioID))
            {
                SendSound(peer, t.Id, loop.audioID, 1f, reliable: true);
            }
        }

        /// <summary>
        /// EnemySound: reliable on the control channel, or unreliable on the
        /// state channel.
        /// </summary>
        private void SendSound(
            ulong peer, uint id, string audio, float volume, bool reliable)
        {
            var w = _session.Begin(MsgType.EnemySound);
            w.W.Write(id);
            w.WriteStr(audio);
            w.W.Write(volume);
            int channel;
            if (reliable)
            {
                channel = Channel.Control;
            }
            else
            {
                channel = Channel.State;
            }

            _session.SendTo(peer, w, channel, reliable);
        }

        /// <summary>
        /// Host: the clients that have this enemy (it's near them).
        /// </summary>
        private IEnumerable<ulong> Knowing(uint id)
        {
            foreach (var kv in _known)
            {
                if (kv.Value.Contains(id))
                {
                    yield return kv.Key;
                }
            }
        }

        /// <summary>
        /// Host: a corpse the client doesn't have, sent as an enemy that is
        /// shown, dead and then kept (it settles at once there, on the host's
        /// last frames of its death), then what it holds. All on the reliable
        /// channel, so they come in this order.
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
            Inventory inv = null;
            if (c != null)
            {
                inv = c.GetComponent<Inventory>();
            }

            if (inv != null)
            {
                Containers instance = Containers.Instance;
                if (instance != null)
                {
                    instance.SendContentsTo(peer, inv);
                }
            }
        }

        private void SendDespawn(ulong peer, uint id, bool keepCorpse)
        {
            var w = _session.Begin(MsgType.EnemyDespawn);
            w.W.Write(id);
            int corpseFlag = 0;
            if (keepCorpse)
            {
                corpseFlag = 1;
            }

            w.W.Write((byte)corpseFlag);
            _session.SendTo(peer, w, Channel.Control, reliable: true);
        }

        private void Forget(uint id)
        {
            foreach (var kv in _known)
            {
                if (kv.Value.Remove(id))
                {
                    SendDespawn(kv.Key, id, keepCorpse: false);
                }
            }

            // the host's corpse is gone, so theirs goes too
            foreach (var kv in _corpseKnown)
            {
                if (kv.Value.Remove(id))
                {
                    SendDespawn(kv.Key, id, keepCorpse: false);
                }
            }
        }

        private static void WriteState(PacketWriter w, Tracked t)
        {
            var c = t.C;
            w.W.Write(t.Id);
            w.Write(c.transform.position);
            WriteRot(w, c.transform.rotation);
            int flags = 0;
            if (InPlay(c))
            {
                flags |= 1;
            }

            if (c.sleeping)
            {
                flags |= 2;
            }

            if (c.cuttingInHalf)
            {
                flags |= 4;
            }

            w.W.Write((byte)flags);
            float health = 1f;
            if (c.maxHealth > 0)
            {
                health = Mathf.Clamp01(c.health / c.maxHealth);
            }

            w.W.Write(health);
            int n = Mathf.Min(t.Animators.Count, 8);
            w.W.Write((byte)n);
            for (int i = 0; i < n; i++)
            {
                var a = t.Animators[i];
                tk2dSpriteAnimationClip clip = null;
                if (a != null)
                {
                    clip = a.CurrentClip;
                }

                w.W.Write(t.AnimatorPaths[i]);
                int hash = 0;
                if (clip != null)
                {
                    hash = StableHash.Of(clip.name);
                }

                w.W.Write(hash);
                int frame = 0;
                if (clip != null)
                {
                    frame = Mathf.Clamp(a.CurrentFrame, 0, 65535);
                }

                w.W.Write((ushort)frame);
                int playing = 0;
                if (a != null && a.Playing)
                {
                    playing = 1;
                }

                w.W.Write((byte)playing);
            }
        }

        /// <summary>
        /// Host: an enemy was hit and bled; the clients that see it get the
        /// splat too (their hits included).
        /// </summary>
        public void HostBlood(Character c, float yRot)
        {
            if (!_byCharHost.TryGetValue(c, out var t))
            {
                return;
            }

            foreach (var peer in Knowing(t.Id))
            {
                var w = _session.Begin(MsgType.EnemyBlood);
                w.W.Write(t.Id);
                w.W.Write(yRot);
                int waterFlag = 0;
                if (c.inWater)
                {
                    waterFlag = 1;
                }

                w.W.Write((byte)waterFlag);
                _session.SendTo(peer, w, Channel.State, reliable: false);
            }
        }

        private void OnBlood(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !ClientMode)
            {
                return;
            }

            uint id = r.R.ReadUInt32();
            float y = r.R.ReadSingle();
            bool water = r.R.ReadByte() != 0;
            // shown already
            if (_predictedBlood.TryGetValue(id, out float at)
                && Time.realtimeSinceStartup - at < 1f)
            {
                _predictedBlood.Remove(id);
                return;
            }

            if (_puppets.TryGetValue(id, out var p) && p.C != null)
            {
                string prefab = "FX/Bloodsplats/Shotsplat_stay";
                if (water)
                {
                    prefab = "FX/Bloodsplats/Shotsplat";
                }

                Core.AddPrefab(
                    prefab, p.C.transform.position,
                    Quaternion.Euler(90f, y, 0f), null);
            }
        }

        /// <summary>
        /// Host: the players that have this enemy (it's near them), for things
        /// only they need to see.
        /// </summary>
        public List<ulong> PeersSeeing(Character c)
        {
            if (c == null || !_byCharHost.TryGetValue(c, out var t))
            {
                return new List<ulong>();
            }

            return Knowing(t.Id).ToList();
        }

        /// <summary>
        /// Host: the net ID the clients know this enemy by (false if it isn't
        /// tracked).
        /// </summary>
        public bool IdOf(Character c, out uint id)
        {
            id = 0;
            if (c == null || !_byCharHost.TryGetValue(c, out var t))
            {
                return false;
            }

            id = t.Id;
            return true;
        }

        /// <summary>
        /// Client: our copy of the host's enemy with this net ID (null if we
        /// haven't got it).
        /// </summary>
        public Character PuppetOf(uint id)
        {
            if (_puppets.TryGetValue(id, out var p))
            {
                return p.C;
            }

            return null;
        }

        /// <summary>Client: our copies of the host's living enemies.</summary>
        public IEnumerable<Character> PuppetCharacters()
        {
            foreach (var p in _puppets.Values)
            {
                if (p.C != null && p.DiedAt < 0f)
                {
                    yield return p.C;
                }
            }
        }

        /// <summary>
        /// Host: an enemy made a sound; tell the clients that can see it.
        /// </summary>
        public void HostSound(Transform parent, string audioId, float volume)
        {
            var c = parent.GetComponentInParent<Character>();
            if (c == null || !_byCharHost.TryGetValue(c, out var t))
            {
                return;
            }

            foreach (var peer in Knowing(t.Id))
            {
                SendSound(peer, t.Id, audioId, volume, reliable: false);
            }
        }

        private void OnHit(ulong from, PacketReader r)
        {
            if (!_session.IsHost)
            {
                return;
            }

            uint id = r.R.ReadUInt32();
            float damage = r.R.ReadSingle();
            byte f = r.R.ReadByte();
            var effects = PlayerDamage.ReadEffects(r);
            string weaponJson = r.ReadStr();
            _byIdHost.TryGetValue(id, out var t);
            if (t == null || t.C == null || !InPlay(t.C))
            {
                World.Authority.Refuse("that enemy isn't alive here");
                return;
            }

            if (!NetPlayerProxy.All.TryGetValue(from, out var proxy)
                || proxy == null)
            {
                World.Authority.Refuse("no stand-in for that player yet");
                return;
            }

            ApplyHit(from, proxy, t.C, damage, f, effects, weaponJson);
        }

        /// <summary>
        /// Host, in a request's handler: a player's hit on one of our
        /// characters (an enemy, or an NPC that's still friendly here), checked
        /// (reach, hit rate) and capped by their weapon, applied with their
        /// stand-in as the attacker, so it reacts to them and gains threat for
        /// them.
        /// </summary>
        internal void ApplyHit(
            ulong from, NetPlayerProxy proxy, Character c, float damage, byte f,
            List<InvItemEffect> effects, string weaponJson)
        {
            var weapon = Weapon(weaponJson);
            bool ranged = weapon != null && weapon.baseClass != null
                && !weapon.baseClass.isMelee;
            float reach;
            if (ranged)
            {
                reach = RelevantRange;
            }
            else
            {
                reach = MeleeReach;
            }

            if (!World.Authority.InReach(c.transform.position, reach, c.name))
            {
                return;
            }

            if (!HitRateOk(from, weapon))
            {
                string type = null;
                if (weapon != null)
                {
                    type = weapon.type;
                }

                if (type == null)
                {
                    type = "bare hands";
                }

                World.Authority.Refuse($"more hits a second than {type} makes");
                return;
            }

            float cap = DamageCap(weapon);
            float applied = Mathf.Clamp(damage, 0f, cap);
            if (applied < damage)
            {
                var log = Plugin.Log;
                var hitter = _session.NameOf(from);
                var target = c.name;
                string weaponType = null;
                if (weapon != null)
                {
                    weaponType = weapon.type;
                }

                if (weaponType == null)
                {
                    weaponType = "bare hands";
                }

                log.LogInfo(
                    $"[authority] {hitter}'s hit on {target}: {damage:0} is "
                        + $"more than {weaponType} can do; {applied:0}");
            }

            World.Authority.Result(
                applied.ToString(
                    "0", System.Globalization.CultureInfo.InvariantCulture));
            // The bar isn't shown for another player's hit (getHit would, as
            // byPlayer); as for any hit not by our player, it shows if we have
            // that enemy selected, and an open bar is refreshed.
            if (applied > 0f)
            {
                // A death this hit causes sets off the character's onDeath as
                // that player's doing (World.ScriptedEvents.Cause). Its
                // onGetAttackedByPlayer triggers are the hitter's to set off
                // (their part, ours and the shared one, as any trigger of
                // theirs): left to them, and the result tells them
                // (ScriptedEvents.Attacked).
                var wasCause = World.ScriptedEvents.Cause;
                World.ScriptedEvents.Cause = from;
                int left;
                try
                {
                    left = ScriptedEvents.LeavingToActor(
                        ScriptedEvents.HitTypes, () =>
                        c.getHit(
                            applied, proxy.transform,
                            canCutInHalf: (f & 1) != 0, byPlayer: true,
                            canInterrupt: (f & 4) != 0, normalHit: (f & 8) != 0,
                            dontShowHealthBar: true));
                }
                finally
                {
                    World.ScriptedEvents.Cause = wasCause;
                }

                if (left > 0)
                {
                    World.Authority.Result(
                        applied.ToString(
                            "0",
                            System.Globalization.CultureInfo.InvariantCulture)
                            + ScriptedEvents.Attacked);
                }

                if (c != null)
                {
                    HealthBar(
                        c,
                        show: Player.Instance != null
                            && Player.Instance.selectedObject == c.transform);
                }
            }

            if (c != null && c.effects != null)
            {
                foreach (var e in WeaponEffects(weapon, effects))
                {
                    // bleeding, burning... from their weapon
                    c.effects.activate(e);
                }
            }
        }

        private static readonly Dictionary<InvItem, List<InvItemEffect>>
            _weaponEffects = new Dictionary<InvItem, List<InvItemEffect>>();

        /// <summary>
        /// Of the effects a hit came with, those the weapon's class can cause,
        /// with the class's own numbers rather than the sent ones: its melee
        /// sensors' effects (MeleeSensor.effects) and its flames'
        /// (Flame.effect: a firearm's shot is its `item` prefab, which
        /// Player.spawnBullet spawns, a flamethrower's flame among them; also
        /// its muzzle and particle prefabs). Bare hands cause none.
        /// </summary>
        private static List<InvItemEffect> WeaponEffects(
            InvItemClass weapon, List<InvItemEffect> sent)
        {
            var result = new List<InvItemEffect>();
            InvItem b = null;
            if (weapon != null)
            {
                b = weapon.baseClass;
            }

            if (b == null || sent == null || sent.Count == 0)
            {
                return result;
            }

            if (!_weaponEffects.TryGetValue(b, out var own))
            {
                own = new List<InvItemEffect>();
                UnityEngine.Object shot = null;
                if (b.isFirearm)
                {
                    shot = b.item;
                }

                foreach (var o in new[]
                {
                    b.meleeSensor,
                    b.meleeSensorSpecial,
                    shot,
                    b.muzzlePrefab,
                    b.muzzleParticles,
                    b._particleEmitter
                })
                {
                    GameObject go = o as GameObject;
                    if (go == null)
                    {
                        Component component = o as Component;
                        if (component != null)
                        {
                            go = component.gameObject;
                        }
                    }

                    if (go == null)
                    {
                        continue;
                    }

                    foreach (var ms in go.GetComponentsInChildren<MeleeSensor>(
                        true))
                    {
                        if (ms.effects != null)
                        {
                            foreach (var e in ms.effects)
                            {
                                if (e != null)
                                {
                                    own.Add(e);
                                }
                            }
                        }
                    }

                    foreach (var fl in go.GetComponentsInChildren<Flame>(true))
                    {
                        if (fl.effect != null)
                        {
                            own.Add(fl.effect);
                        }
                    }
                }

                _weaponEffects[b] = own;
            }

            var left = new List<InvItemEffect>(own);
            foreach (var e in sent)
            {
                int i = left.FindIndex(x => x.type == e.type);
                if (i < 0)
                {
                    Plugin.Log.LogInfo(
                        $"[authority] a hit with {weapon.type} came with "
                            + $"{e.type}, which it doesn't cause; dropped");
                    continue;
                }

                result.Add(left[i]);
                left.RemoveAt(i);
            }

            return result;
        }

        /// <summary>
        /// How far a melee hit reaches (generous: the positions the host has
        /// are a little late).
        /// </summary>
        private const float MeleeReach = 500f;
        /// <summary>
        /// Most hits a player lands in a second with a melee weapon or bare
        /// hands (or a slow gun).
        /// </summary>
        private const int MaxHitsPerSecond = 24;
        private readonly Dictionary<ulong, Queue<float>> _hitTimes =
            new Dictionary<ulong, Queue<float>>();

        /// <summary>
        /// Most hits a second this weapon lands, by the host's numbers for its
        /// type: each of a gun's shots sends a hit for every pellet that lands
        /// (InvItem.projectileAmount, Player.fireWeapon), and it fires at most
        /// once per fireRate seconds (at most every 0.05 s for automatic and
        /// burst fire, 0.15 s for one shot a click), with half again as a
        /// margin for hits that arrive bunched up. Never under
        /// MaxHitsPerSecond.
        /// </summary>
        private static int MaxHits(InvItemClass weapon)
        {
            InvItem b = null;
            if (weapon != null)
            {
                b = weapon.baseClass;
            }

            if (b == null || !b.isFirearm)
            {
                return MaxHitsPerSecond;
            }

            // A gun fired one shot per click (oneShot, semiAuto) has its
            // fireRate at 0 or near it: its pump or bolt animation, and the
            // clicks, keep it under about 7 shots a second.
            bool auto = b.fireMode == InvItem.FireMode.fullAuto
                || b.fireMode == InvItem.FireMode.burst;
            float minRate = 0.15f;
            if (auto)
            {
                minRate = 0.05f;
            }

            float shots = 1f / Mathf.Max(b.fireRate, minRate);
            return Mathf.Max(
                MaxHitsPerSecond,
                Mathf.CeilToInt(
                    Mathf.Max(1, b.projectileAmount) * shots * 1.5f));
        }

        private bool HitRateOk(ulong from, InvItemClass weapon)
        {
            if (!_hitTimes.TryGetValue(from, out var q))
            {
                _hitTimes[from] = q = new Queue<float>();
            }

            float now = Time.realtimeSinceStartup;
            while (q.Count > 0 && now - q.Peek() > 1f)
            {
                q.Dequeue();
            }

            if (q.Count >= MaxHits(weapon))
            {
                return false;
            }

            q.Enqueue(now);
            return true;
        }

        /// <summary>
        /// The weapon a hit came with (InvItemClass.SaveState as JSON; null for
        /// bare hands or one we can't read).
        /// </summary>
        private static InvItemClass Weapon(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                var st =
                    Newtonsoft.Json.JsonConvert
                        .DeserializeObject<InvItemClass.SaveState>(json);
                var it = new InvItemClass();
                st.loadValues(it);
                if (InvItemClass.isNull(it) || it.baseClass == null)
                {
                    return null;
                }

                return it;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The most damage this weapon can deal in one hit, by the game's own
        /// numbers for its class: its damage or special damage, with every
        /// damage upgrade its class offers (InvItem.upgrades; not the upgrades
        /// the hit says it has, which the client sends), doubled by the Strong
        /// skill's effect, with a margin.
        /// Bare hands: as the game's weakest weapons.
        /// </summary>
        public static float DamageCap(InvItemClass weapon)
        {
            InvItem b = null;
            if (weapon != null)
            {
                b = weapon.baseClass;
            }

            if (b == null)
            {
                return 40f;
            }

            float mod = 1f; // as getModdedDamage adds them up
            if (b.upgrades != null)
            {
                foreach (var u in b.upgrades)
                {
                    if (u != null && u.damageModifier > 1f)
                    {
                        mod += u.damageModifier - 1f;
                    }
                }
            }

            int most = Mathf.Max(b.damage, b.specialDamage);
            return Mathf.Max(10f, most * mod * 2f * 1.25f);
        }

        // =====================================================================
        // Client
        // =====================================================================

        /// <summary>
        /// A client in the host's world: its own enemies don't run, the host's
        /// are shown.
        /// </summary>
        public static bool ClientMode
        {
            get
            {
                var s = Session.Instance;
                return s != null && s.IsClient && s.Connected
                    && SaveRedirect.Active;
            }
        }

        private sealed class Puppet
        {
            public uint Id;
            public Character C;
            // made from the prefab here, rather than our copy from the save
            public bool Ours;
            public readonly Dictionary<int, tk2dSpriteAnimator> Animators =
                new Dictionary<int, tk2dSpriteAnimator>();
            public bool Has; // it has had a state
            // dead: the corpse made lootable once its death has played
            public float DiedAt = -1f;
            public bool Settled;
            // cut in half on the host (Character.cutMeInHalf): its lower half
            // is a new enemy, this part no corpse
            public bool Cut;
            // taken out of our culling while the host sends it (Uncull)
            public bool Unculled;
            public Location HomeLoc;
            public readonly EnemyMotion.Track Motion = new EnemyMotion.Track();
        }

        /// <summary>
        /// Client: take a shown copy out of our own culling. Our WorldGrid
        /// culls only around our player, so a copy 1600-3000 away (still within
        /// the host's RelevantRange) would be switched off by its node being
        /// left (Cullable.hide, tryToHideMoveable) or by its location's
        /// characters being switched off (Location.activateCharacters), and
        /// nothing would switch it on again while the host moves it: we'd be
        /// hit by an enemy we can't see. While the host sends it, its
        /// EnemyDespawn decides when it goes.
        /// </summary>
        private static void Uncull(Puppet p)
        {
            var c = p.C;
            if (c == null || p.Unculled)
            {
                return;
            }

            p.Unculled = true;
            var go = c.gameObject;
            var co = c.GetComponent<CullableObject>();
            if (co != null)
            {
                WorldGrid.Node node = co.node;
                if (node != null)
                {
                    node.removeCullable(go);
                }

                foreach (var n in co.nodes)
                {
                    if (n != null)
                    {
                        n.removeCullable(go);
                    }
                }

                co.node = null; // checkForNewNode then leaves it alone
                co.nodes.Clear();
            }

            var loc = c.baseLocation;
            if (loc != null)
            {
                if (loc.charactersList.Remove(c))
                {
                    p.HomeLoc = loc;
                }

                loc.objects.Remove(go);
            }

            // Under a location's characters holder it would go off with the
            // location's children: put it where the game puts characters that
            // left their location (a holder that's never switched off).
            CharacterSpawner instance = Singleton<CharacterSpawner>.Instance;
            GameObject holder = null;
            if (instance != null)
            {
                holder = instance.charactersEscapedFromLocationHolder;
            }

            if (holder != null && go.transform.parent != null
                && go.transform.parent != holder.transform)
            {
                c.disconnectFromLocation();
            }

            if (!go.activeSelf)
            {
                go.SetActive(true);
            }

            if (!go.activeInHierarchy)
            {
                Transform parent = go.transform.parent;
                string name = null;
                if (parent != null)
                {
                    name = parent.name;
                }

                Plugin.Log.LogWarning(
                    $"[enemy] the host's {c.name} #{p.Id} is still switched "
                        + $"off here (under {name})");
            }
        }

        /// <summary>
        /// Client: the host stopped sending a copy: our culling looks after it
        /// again, at the spot it's at now.
        /// </summary>
        private static void Recull(Puppet p)
        {
            var c = p.C;
            if (c == null || !p.Unculled)
            {
                return;
            }

            p.Unculled = false;
            if (p.HomeLoc != null && !p.HomeLoc.charactersList.Contains(c))
            {
                p.HomeLoc.charactersList.Add(c);
            }

            p.HomeLoc = null;
            var o = Singleton<OutsideLocations>.Instance;
            var grid = Singleton<WorldGrid>.Instance;
            if (grid == null || (o != null && o.playerInOutsideLocation))
            {
                return; // our current grid isn't the world's
            }

            try
            {
                grid.registerToNode(c.gameObject);
                CullableObject cullable = c.GetComponent<CullableObject>();
                WorldGrid.Node node = null;
                if (cullable != null)
                {
                    node = cullable.node;
                }

                if (node != null && !node.entered && c.gameObject.activeSelf)
                {
                    c.gameObject.SetActive(false);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't put {c.name} back into the culling: "
                        + $"{e.Message}");
            }
        }

        private readonly Dictionary<uint, Puppet> _puppets =
            new Dictionary<uint, Puppet>();
        private readonly Dictionary<Character, Puppet> _byChar =
            new Dictionary<Character, Puppet>();
        private float _nextSweep;

        // Corpses the host told us to leave where they fell: still known by id,
        // for their loot.
        private readonly Dictionary<uint, Puppet> _corpses =
            new Dictionary<uint, Puppet>();
        private readonly Dictionary<Character, uint> _corpseIds =
            new Dictionary<Character, uint>();

        /// <summary>
        /// The net id of one of the host's enemies (host: tracked; client:
        /// shown, or its corpse).
        /// </summary>
        public bool NetIdOf(Character c, out uint id)
        {
            id = 0;
            if (c == null)
            {
                return false;
            }

            if (_session.IsHost && _byCharHost.TryGetValue(c, out var t))
            {
                id = t.Id;
                return true;
            }

            if (_byChar.TryGetValue(c, out var p))
            {
                id = p.Id;
                return true;
            }

            return _corpseIds.TryGetValue(c, out id);
        }

        /// <summary>Client: this is a copy of one of the host's enemies that we
        /// made from its prefab (one spawned while playing), rather than our
        /// own copy from the save.</summary>
        public bool MadeHere(Character c) =>
            c != null && _byChar.TryGetValue(c, out var p) && p.Ours;

        /// <summary>
        /// Client: a corpse of one of the host's enemies that the host told us
        /// to keep (EnemyDespawn with keep corpse).
        /// </summary>
        internal bool KeptCorpse(Character c) =>
            c != null && _corpseIds.ContainsKey(c);

        public Character CharacterOf(uint id)
        {
            if (_session.IsHost)
            {
                if (_byIdHost.TryGetValue(id, out var t))
                {
                    return t.C;
                }

                return null;
            }

            if (_puppets.TryGetValue(id, out var p))
            {
                return p.C;
            }

            if (_corpses.TryGetValue(id, out var c))
            {
                return c.C;
            }

            return null;
        }

        /// <summary>
        /// Host: enemies the clients know of that died at least this long ago.
        /// </summary>
        public IEnumerable<Character> HostDeadSince(float seconds)
        {
            foreach (var t in _byCharHost.Values)
            {
                if (t.C != null && t.DiedAt >= 0
                    && Time.time - t.DiedAt >= seconds)
                {
                    yield return t.C;
                }
            }
        }

        public bool IsPuppet(Character c, out uint id)
        {
            id = 0;
            if (c != null && _byChar.TryGetValue(c, out var p))
            {
                id = p.Id;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Client: stop one of our own characters from acting, and hide it
        /// until the host says it's there.
        /// </summary>
        public static void Freeze(Character c, bool hide)
        {
            if (c == null)
            {
                return;
            }

            c.enabled = false;
            if (c.AIpath != null)
            {
                c.AIpath.enabled = false;
            }

            foreach (var mb in c.GetComponents<MonoBehaviour>())
            {
                if (mb is AILerp)
                {
                    mb.enabled = false;
                }
            }

            var rb = c.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.velocity = Vector3.zero;
            }

            foreach (var a in c.GetComponentsInChildren<tk2dSpriteAnimator>(
                true))
            {
                a.AnimationEventTriggered = null;
                a.AnimationCompleted = null;
            }

            if (hide && Instance != null && Instance._corpseIds.ContainsKey(c))
            {
                hide = false; // a corpse the host sent stays shown
            }

            if (hide) // no barking from a dog that isn't shown
            {
                Hide(c);
                StopSounds(c, keepDeathSound: false);
            }

            Frozen.Add(c);
        }

        public static readonly HashSet<Character> Frozen =
            new HashSet<Character>();

        /// <summary>
        /// Client: our own characters hidden while the host runs the world
        /// there (frozen ones, and our save's copies of host enemies the host
        /// stopped sending). They stay in their location's lists, so the game
        /// switches them off and on with their area, and each time
        /// CharacterSounds.OnEnable starts their idle loop again
        /// (ClientHiddenSilentPatch keeps them silent): a dead dog camp's dogs
        /// barked on in a joiner's game.
        /// </summary>
        public static readonly HashSet<Character> Hidden =
            new HashSet<Character>();

        private static void Hide(Character c)
        {
            SetVisible(c, false);
            Hidden.Add(c);
        }

        private static readonly System.Reflection.FieldInfo FovLightField =
            HarmonyLib.AccessTools.Field(typeof(Player), "FOVLight");

        /// <summary>
        /// The layers our player's view (Player.FOVLight, a Light2D) is cut by:
        /// its rays stop at colliders on these.
        /// </summary>
        internal static int FovShadowMask()
        {
            Light2D l = null;
            if (Player.Instance != null)
            {
                System.Reflection.FieldInfo fovLightField = FovLightField;
                object fovLight = null;
                if (fovLightField != null)
                {
                    fovLight = fovLightField.GetValue(Player.Instance);
                }

                l = fovLight as Light2D;
            }

            if (l != null)
            {
                return (int)l.ShadowLayer;
            }

            return 0;
        }

        /// <summary>
        /// A character's switched-on colliders that cut our player's view (on
        /// FovShadowMask's layers), by path under it.
        /// </summary>
        internal static List<string> FovBlockers(Character c, int mask)
        {
            var list = new List<string>();
            if (c == null || mask == 0)
            {
                return list;
            }

            foreach (var col in c.GetComponentsInChildren<Collider>(true))
            {
                if (col == null || !col.enabled
                    || !col.gameObject.activeInHierarchy
                    || (mask & (1 << col.gameObject.layer)) == 0)
                {
                    continue;
                }

                string path;
                if (col.transform == c.transform)
                {
                    path = ".";
                }
                else
                {
                    path = col.transform.name;
                }

                for (var t = col.transform.parent; t != null
                    && t != c.transform; t = t.parent)
                {
                    path = t.name + "/" + path;
                }

                string text = "";
                if (col.isTrigger)
                {
                    text = ":trigger";
                }

                list.Add(
                    $"{path}:{col.GetType().Name}:"
                        + $"{col.gameObject.layer}{text}");
            }

            list.Sort(System.StringComparer.Ordinal);
            return list;
        }

        private static void LogFovBlockers(Character c, string when)
        {
            int mask = FovShadowMask();
            var b = FovBlockers(c, mask);
            if (b.Count == 0)
            {
                return;
            }

            var sizes = c.GetComponentsInChildren<Collider>(true).Where(
                x => x != null && x.enabled
                    && (mask & (1 << x.gameObject.layer)) != 0)
                .Select(
                    x => $"{x.name} {x.bounds.size.x:0}x{x.bounds.size.z:0}");
            Plugin.Log.LogInfo(
                $"[enemy] {c.name} {when}: colliders on the view's shadow "
                    + $"layers (mask {mask}): {string.Join(", ", b)}; sizes "
                    + $"{string.Join(", ", sizes)}");
        }

        /// <summary>
        /// Client: the renderers and colliders SetVisible(false) switched off,
        /// per character, so showing it again switches on just those. Switching
        /// on everything under a character woke parts the game keeps off (and a
        /// corpse's parts its die2 destroys on the host): a collider on the
        /// view's shadow layers then cut the player's view to nothing beside
        /// the corpse.
        /// </summary>
        private static readonly Dictionary<Character, List<Component>>
            _switchedOff = new Dictionary<Character, List<Component>>();

        private static void SetVisible(Character c, bool on)
        {
            if (!on)
            {
                if (!_switchedOff.TryGetValue(c, out var off))
                {
                    _switchedOff[c] = off = new List<Component>();
                }

                foreach (var r in c.GetComponentsInChildren<Renderer>(true))
                {
                    if (r != null && r.enabled)
                    {
                        r.enabled = false;
                        off.Add(r);
                    }
                }

                foreach (var col in c.GetComponentsInChildren<Collider>(true))
                {
                    if (col != null && col.enabled)
                    {
                        col.enabled = false;
                        off.Add(col);
                    }
                }

                return;
            }

            if (!_switchedOff.TryGetValue(c, out var list))
            {
                return;
            }

            _switchedOff.Remove(c);
            foreach (var x in list)
            {
                if (x is Renderer r && r != null)
                {
                    r.enabled = true;
                }
                else if (x is Collider col && col != null)
                {
                    col.enabled = true;
                }
            }
        }

        /// <summary>
        /// Catch characters that started before we were in client mode (e.g.
        /// rejoining in the same world).
        /// </summary>
        private void ClientSweep()
        {
            if (Time.unscaledTime < _nextSweep)
            {
                return;
            }

            _nextSweep = Time.unscaledTime + 1f;
            foreach (var c in AllCharacters.Active())
            {
                if (!Eligible(c) || _byChar.ContainsKey(c))
                {
                    continue;
                }

                bool local = LocalOnly(c);
                if (local && Frozen.Contains(c))
                {
                    Thaw(c);
                }
                else if (!local && !Frozen.Contains(c))
                {
                    Freeze(c, hide: true);
                }
            }
        }

        private void OnSpawn(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !ClientMode)
            {
                return;
            }

            uint id = r.R.ReadUInt32();
            int uid = r.R.ReadInt32();
            string prefab = r.ReadStr();
            string name = r.ReadStr();
            Vector3 pos = r.ReadVector3();
            Quaternion rot = ReadRot(r);
            bool armor = r.R.ReadByte() != 0;
            if (_puppets.ContainsKey(id))
            {
                return;
            }

            Character c = null;
            bool ours = false;
            var sm = Singleton<SaveManager>.Instance;
            // Our copy of it from the save, when the host's save id names the
            // same character here: only ids from the save we loaded are the
            // same in both games (SharedIds), and even then it has to be the
            // same kind of creature. The host sends an NPC only while it's
            // hostile there (HostileNpcs): ours is then frozen and shown as the
            // host's.
            if (uid > 0 && uid < World.SharedIds.ResolveLimit && sm != null
                && sm.uniqueIdDict.TryGetValue(uid, out var tr) && tr != null)
            {
                c = tr.GetComponent<Character>();
                if (c != null && _byChar.ContainsKey(c))
                {
                    c = null; // already driven by another id
                }

                if (c != null && KindOf(c) != name)
                {
                    var log = Plugin.Log;
                    var kind = KindOf(c);
                    string npcNote = " (an NPC)";
                    if (c.npc == null)
                    {
                        npcNote = "";
                    }

                    log.LogInfo(
                        $"Save id {uid} is our {kind}{npcNote}, not the host's "
                            + $"{name}: making it from its prefab");
                    c = null;
                }
            }

            // An NPC made while playing (a scripted spawn runs in every game)
            // has a save id of its own in each game: ours is the one of that
            // kind near where the host's is.
            if (c == null)
            {
                c = HostileNpcs.OurCopy(
                    name, pos, x => _byChar.ContainsKey(x), KindOf);
            }

            if (c == null)
            {
                GameObject go = null;
                foreach (var candidate in new[]
                {
                    prefab,
                    "Characters/" + name
                })
                {
                    var res = Resources.Load("Prefabs/" + candidate);
                    if (res != null)
                    {
                        go = Core.AddPrefab(
                            res, pos, rot, null, worldSpace: true);
                        break;
                    }
                }

                if (go != null)
                {
                    c = go.GetComponent<Character>();
                }
                else
                {
                    c = null;
                }

                ours = c != null;
                if (c == null)
                {
                    if (go != null)
                    {
                        Object.Destroy(go);
                    }

                    Plugin.Log.LogWarning(
                        $"Couldn't make the host's enemy {name} ({prefab})");
                    return;
                }

                var save = c.GetComponent<SaveableObject>();
                if (save != null)
                {
                    // it's the host's; don't write it into this world's save
                    save.dontSave = true;
                }
            }

            var p = new Puppet { Id = id, C = c, Ours = ours };
            bool wasOff = !c.gameObject.activeInHierarchy;
            Uncull(p);
            Freeze(c, hide: false);
            SetVisible(c, true);
            // shown as the host's now: its idle loop plays again
            Hidden.Remove(c);
            PlayIdleLoop(c);
            c.transform.SetPositionAndRotation(pos, rot);
            Animators(c.transform, out var list, out var paths);
            for (int i = 0; i < list.Count; i++)
            {
                p.Animators[paths[i]] = list[i];
            }

            _puppets[id] = p;
            _byChar[c] = p;
            if (c.npc != null)
            {
                HostileNpcs.ClientStarted(c, id);
            }

            SetArmor(c, armor, fade: false);
            string text = "our copy from the save";
            if (ours)
            {
                text = "made from its prefab";
            }

            string switched = "";
            if (wasOff)
            {
                switched = ", switched on";
            }

            Plugin.Log.LogInfo(
                $"Showing the host's {name} #{id} ({text}{switched}); "
                    + $"{Frozen.Count} of our own characters are frozen");
            LogFovBlockers(c, $"#{id} shown");
        }

        /// <summary>
        /// Client: EnemyArmor, the host's enemy's shadow armour put on or gone.
        /// </summary>
        private void OnArmor(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !ClientMode)
            {
                return;
            }

            uint id = r.R.ReadUInt32();
            bool on = r.R.ReadByte() != 0;
            if (_puppets.TryGetValue(id, out var p) && p.C != null)
            {
                SetArmor(p.C, on, fade: true);
            }
        }

        /// <summary>
        /// Client: a copy wears shadow armour as the host's enemy does. The
        /// armour only shows its effect: its light check and damage do
        /// nothing on a copy (ShadowArmorCopyPatch), and it goes when the
        /// host's does (EnemyArmor), fading as the game's ShadowArmor.die makes
        /// it. A copy from our save may wear armour the host's no longer does;
        /// that goes at once.
        /// </summary>
        internal static void SetArmor(Character c, bool on, bool fade)
        {
            try
            {
                var a = c.GetComponent<ShadowArmor>();
                if (on && a == null)
                {
                    // its Start makes the effect
                    c.shadowArmor = c.gameObject.AddComponent<ShadowArmor>();
                    Plugin.Log.LogInfo(
                        $"[enemy] {c.name}: shadow armour shown");
                }
                else if (!on && a != null)
                {
                    if (a.effectGO == null)
                    {
                        Object.Destroy(a); // not started yet: no effect to stop
                    }
                    else
                    {
                        a.die(instant: !fade);
                    }

                    Plugin.Log.LogInfo($"[enemy] {c.name}: shadow armour gone");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't change {c.name}'s shadow armour: {e.Message}");
            }
        }

        /// <summary>
        /// Client: a ShadowArmor on a copy of the host's enemy, or on one of
        /// our own frozen characters.
        /// </summary>
        internal static bool IsCopy(ShadowArmor a)
        {
            var s = Session.Instance;
            if (a == null || s == null || !s.IsClient)
            {
                return false;
            }

            var c = a.GetComponent<Character>();
            return c != null
                && (Frozen.Contains(c)
                    || (Instance != null && Instance._byChar.ContainsKey(c)));
        }

        private void OnState(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !ClientMode)
            {
                return;
            }

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
                bool alive = (bits & 1) != 0,
                    asleep = (bits & 2) != 0,
                    cut = (bits & 4) != 0;
                float healthFraction = r.R.ReadSingle();
                int an = r.R.ReadByte();
                _puppets.TryGetValue(id, out var p);
                for (int k = 0; k < an; k++)
                {
                    int path = r.R.ReadInt32();
                    int clip = r.R.ReadInt32();
                    int frame = r.R.ReadUInt16();
                    bool playing = r.R.ReadByte() != 0;
                    if (p != null && !p.Settled
                        && p.Animators.TryGetValue(path, out var a)
                        && a != null)
                    {
                        ApplyAnim(a, clip, frame, playing);
                    }
                }

                if (p == null || p.C == null)
                {
                    continue;
                }

                // Drawn by the host's time (EnemyMotion), frame by frame
                // (MovePuppets).
                EnemyMotion.Add(p.Motion, sent, pos, rot);
                p.Has = true;
                if (alive)
                {
                    SetHealth(p.C, healthFraction);
                }

                if (alive && p.C.sleeping != asleep)
                {
                    // Asleep or awake as the host's is, with the idle loop to
                    // match (a mimic's sleeping one).
                    p.C.sleeping = asleep;
                    // Waking, the host's own loop (idle, or aggressive as it
                    // comes at someone) comes as an EnemySound, maybe before
                    // this: only the sleeping one is replaced.
                    AudioObject loop = null;
                    if (p.C.sounds != null)
                    {
                        loop = p.C.sounds.idleAudioObject;
                    }

                    if (asleep || loop == null || !loop.IsPlaying()
                        || loop.audioID == p.C.sounds.sleeping)
                    {
                        PlayIdleLoop(p.C);
                    }
                }

                if (!alive && cut)
                {
                    p.Cut = true;
                }

                if (!alive && p.DiedAt < 0f)
                {
                    // Out of play as on the host: a character with a pre-death
                    // stage would still count as in it here
                    // (Character.hasPreDeath with health left).
                    if (p.C.maxHealth > 0f)
                    {
                        p.C.health = Mathf.Min(
                            p.C.health,
                            Mathf.Clamp01(healthFraction) * p.C.maxHealth);
                    }

                    p.C.alive = false;
                    p.DiedAt = Time.unscaledTime;
                    StopSounds(p.C, keepDeathSound: true);
                    // What's attached to it (the human spider's sound_loop)
                    // goes as it dies, as die2 does on the host.
                    try
                    {
                        p.C.removeAttachedGameObjects();
                    }
                    catch (System.Exception e)
                    {
                        Plugin.Log.LogWarning(
                            $"Couldn't remove what's attached to {p.C.name}: "
                                + $"{e.Message}");
                    }

                    // The human spiders' sound_loop child (an Item with
                    // ItemSounds) is gone from the host's copy as it dies (seen
                    // in the map test, 0.5 s after); a copy's frozen scripts
                    // never remove it.
                    foreach (var it in p.C.GetComponentsInChildren<Item>(true))
                    {
                        if (it != null && it.gameObject != p.C.gameObject
                            && it.GetComponent<ItemSounds>() != null)
                        {
                            Object.Destroy(it.gameObject);
                        }
                    }

                    if (p.Cut)
                    {
                        CutLooks(p.C);
                    }
                    else
                    {
                        DeathLooks(p.C);
                    }

                    string text = "";
                    if (p.Cut)
                    {
                        text = " (cut in half)";
                    }

                    Plugin.Log.LogInfo(
                        $"[enemy] {p.Id} died{text}: sounds stopped");
                }
            }
        }

        /// <summary>
        /// Client: silence a copy of the host's enemy, as Character.die2 does
        /// on the host (sounds.destroySounds). The copy never runs die2, so
        /// without this its own idle loop (started by CharacterSounds.OnEnable)
        /// and the loops the host relayed (a dog's barking, which the host
        /// stops in its die2) went on playing over the corpse; and Settle's
        /// destroyComponents destroys CharacterSounds without stopping its
        /// loop, which left it playing for good. Its coroutines are stopped
        /// too, as die2 does.
        /// </summary>
        private static void StopSounds(Character c, bool keepDeathSound)
        {
            if (c == null)
            {
                return;
            }

            string death = null;
            if (c.sounds != null)
            {
                death = c.sounds.death;
            }

            if (c.sounds != null)
            {
                try
                {
                    c.sounds.destroySounds();
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't stop {c.name}'s sounds: {e.Message}");
                }
            }

            // Sounds played on it (the relayed ones and its own) are
            // AudioObjects under it.
            foreach (var ao in c.GetComponentsInChildren<AudioObject>(true))
            {
                if (ao == null
                    || (keepDeathSound && !string.IsNullOrEmpty(death)
                        && ao.audioID == death))
                {
                    continue;
                }

                ao.Stop(0.3f);
            }

            if (!keepDeathSound)
            {
                return; // only hidden, not dead
            }

            c.StopAllCoroutines();
        }

        /// <summary>
        /// Client: the host's health for a copy, and its health bar as the game
        /// does for a hit not by our player: shown if the player has it
        /// selected, and refreshed if it's the one shown.
        /// </summary>
        private static void SetHealth(Character c, float fraction)
        {
            if (c.maxHealth <= 0f)
            {
                return;
            }

            float hp = Mathf.Clamp01(fraction) * c.maxHealth;
            if (Mathf.Abs(hp - c.health) < 0.01f)
            {
                return;
            }

            bool lost = hp < c.health;
            c.health = hp;
            HealthBar(
                c,
                show: lost && Player.Instance != null
                    && Player.Instance.selectedObject == c.transform);
        }

        /// <summary>
        /// Show an enemy's health bar (as Character.getHit does for a hit by
        /// the player), or refresh it if it's showing.
        /// </summary>
        private static void HealthBar(Character c, bool show)
        {
            EnemyHealthBar bar = null;
            if (Singleton<UI>.Instance != null)
            {
                bar = Singleton<UI>.Instance.enemyHealthBar;
            }

            if (bar == null || c == null)
            {
                return;
            }

            try
            {
                if (show)
                {
                    bar.show(c.gameObject);
                }
                else if (bar.currentObj == c.gameObject)
                {
                    bar.show(c.gameObject, onlyRefresh: true);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't show {c.name}'s health bar: {e.Message}");
            }
        }

        /// <summary>
        /// Client: what Character.die2 does to how a dying character looks, for
        /// a copy (which never runs die2): its particles (a burning one's
        /// flames) stop, its shadow fades out, and so does a shadow armour's
        /// effect.
        /// </summary>
        private static void DeathLooks(Character c)
        {
            try
            {
                foreach (var ps in c.GetComponentsInChildren<ParticleSystem>())
                {
                    if (ps != null)
                    {
                        ps.Stop();
                    }
                }

                if (c.shadow != null)
                {
                    Core.tweenColor(
                        c.shadow.gameObject, new Color(1f, 1f, 1f, 0f), 1f);
                }

                // its shadow armour's effect fades, as die2 does
                var armor = c.GetComponent<ShadowArmor>();
                if (armor != null)
                {
                    armor.die();
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't play {c.name}'s death here: {e.Message}");
            }
        }

        /// <summary>
        /// Client: a separate location's characters were switched off (left) or
        /// on again (entered) here. The game does that to the corpses of the
        /// location's own characters (Location.activateCharacters: SetActive
        /// false, then true), so on the host a corpse's play-on-awake particles
        /// (a ChomperRed's) start again when its location is entered again. A
        /// copy we made from its prefab isn't one of the location's characters,
        /// so it would stay as it was: the dead ones of those standing in the
        /// location are switched off and on with it, as the host's are. Living
        /// copies stay as they are: the host runs them while a joiner is in
        /// their location.
        /// </summary>
        internal void LocationCharacters(Location loc, bool active)
        {
            if (loc == null)
            {
                return;
            }

            foreach (var p in _corpses.Values.Concat(_puppets.Values).ToList())
            {
                if (p == null || !p.Ours || p.C == null || p.C.alive
                    || p.C.gameObject.activeSelf == active)
                {
                    continue;
                }

                if (!Core.withinLocation(p.C.transform.position, 0f, loc))
                {
                    continue;
                }

                p.C.gameObject.SetActive(active);
                string again = "";
                if (active)
                {
                    again = string.Join(
                        ", ",
                        p.C.GetComponentsInChildren<ParticleSystem>()
                            .Where(ps => ps.isEmitting).Select(ps => ps.name));
                }

                string text = "off";
                if (active)
                {
                    text = "on";
                }

                string note = "";
                if (again != "")
                {
                    note = "; playing again: " + again;
                }

                Plugin.Log.LogInfo(
                    $"[enemy] the host's corpse #{p.Id} ({p.C.name}) switched "
                        + $"{text} with "
                        + $"{Core.getTrueLocationName(loc.name)}{note}");
            }
        }

        /// <summary>
        /// How long the host's death animation is shown before the corpse is
        /// settled here.
        /// </summary>
        private const float DeathAnimTime = 2f;

        private static readonly System.Reflection.MethodInfo[] CorpseSteps =
        {
            HarmonyLib.AccessTools.Method(
                typeof(Character), "destroyComponents"),
            HarmonyLib.AccessTools.Method(
                typeof(Character), "destroyComponents2"),
            HarmonyLib.AccessTools.Method(
                typeof(Character), "setDeathCollider"),
        };

        /// <summary>
        /// Client: one of the host's enemies died, and its death has played. In
        /// the game it's the end of the death animation that turns the body
        /// into a lootable corpse (Character.animationCompleted:
        /// destroyComponents, destroyComponents2, setDeathCollider); a copy's
        /// animation callbacks are off, so that's done here, once. After it the
        /// copy's frames are left on the last one rather than replayed from the
        /// host's updates.
        /// </summary>
        private static void Settle(Puppet p)
        {
            p.Settled = true;
            var c = p.C;
            if (c == null)
            {
                return;
            }

            foreach (var a in p.Animators.Values)
            {
                if (a == null || a.CurrentClip == null
                    || a.CurrentClip.frames == null
                    || a.CurrentClip.frames.Length == 0)
                {
                    continue;
                }

                a.SetFrame(
                    a.CurrentClip.frames.Length - 1, triggerEvent: false);
                a.Pause();
            }

            // destroyComponents destroys CharacterSounds without stopping its
            // loop
            StopSounds(c, keepDeathSound: true);
            // whatever a hide switched off (it's shown already; this only makes
            // sure)
            SetVisible(c, true);
            DieBody(c);
            if (p.Cut)
            {
                SettleCut(p);
                return;
            }

            if (c.animator == null)
            {
                // No sprite animation (the worms): the end of a death animation
                // never comes in the host's game either, so its corpse is never
                // made lootable there; only what dying itself does (die2) to
                // the collider.
                if (c.collider != null)
                {
                    if (!c.dontSwitchColliderTriggerOnDeath)
                    {
                        c.collider.isTrigger = true;
                    }

                    c.collider.enabled = true;
                }

                Plugin.Log.LogInfo(
                    $"The host's {c.name} #{p.Id} died: it has no death "
                        + "animation, so its body isn't searchable (as on the "
                        + "host)");
                return;
            }

            foreach (var step in CorpseSteps)
            {
                if (step == null)
                {
                    continue;
                }

                try
                {
                    step.Invoke(c, null);
                }
                catch (System.Exception e)
                {
                    if (step.Name == "setDeathCollider")
                    {
                        System.Exception why = e.InnerException;
                        if (why == null)
                        {
                            why = e;
                        }

                        DeathColliderRest(c, why);
                    }
                    else
                    {
                        System.Exception innerException = e.InnerException;
                        string message = null;
                        if (innerException != null)
                        {
                            message = innerException.Message;
                        }

                        if (message == null)
                        {
                            message = e.Message;
                        }

                        Plugin.Log.LogWarning(
                            $"Couldn't settle {c.name}'s corpse ({step.Name}): "
                                + $"{message}");
                    }
                }
            }

            string data;
            if (c.GetComponent<Item>() != null)
            {
                data =
                    $"The host's {c.name} #{p.Id} died: its corpse can be "
                        + "searched here";
            }
            else
            {
                data =
                    $"The host's {c.name} #{p.Id} died, but its corpse "
                        + "couldn't be made searchable here";
            }

            Plugin.Log.LogInfo(data);
            LogFovBlockers(c, $"#{p.Id} settled");
        }

        /// <summary>
        /// Client: what Character.die2 does to a dying character's body, for a
        /// copy (which never runs die2): its body collider is on and walked
        /// through (unless the kind keeps it solid), its hit collider goes
        /// (unless it has a pre-death, which keeps it alive a while longer),
        /// its Selectable when it isn't selectable dead, and its
        /// InSightOfPlayer. Logs the parts on the view's shadow layers that are
        /// off here, which Settle used to switch on.
        /// </summary>
        private static void DieBody(Character c)
        {
            try
            {
                int mask = FovShadowMask();
                var off = c.GetComponentsInChildren<Collider>(true)
                    .Where(
                        x => x != null && !x.enabled
                            && (mask & (1 << x.gameObject.layer)) != 0)
                    .Select(x => $"{x.name}:{x.gameObject.layer}").ToList();
                var hit = c.hitCollider;
                string text;
                if (hit == null)
                {
                    text = "none";
                }
                else
                {
                    string cuts = "";
                    if ((mask & (1 << hit.layer)) != 0)
                    {
                        cuts = " (cuts the view)";
                    }

                    text = $"{hit.name} layer {hit.layer}{cuts}";
                }

                Plugin.Log.LogInfo(
                    $"[enemy] {c.name} dies here: hit collider {text}, " +
                                   $"pre-death {c.hasPreDeath}; switched-off "
                        + $"parts on the view's shadow layers (mask {mask}): "
                        + $"[{string.Join(", ", off)}]");
                if (c.collider != null)
                {
                    if (!c.dontSwitchColliderTriggerOnDeath)
                    {
                        c.collider.isTrigger = true;
                    }

                    c.collider.enabled = true;
                }

                if (!c.hasPreDeath)
                {
                    if (hit != null)
                    {
                        Object.Destroy(hit);
                    }

                    if (c.notSelectableWhenDead)
                    {
                        var sel = c.GetComponent<Selectable>();
                        if (sel != null)
                        {
                            Object.Destroy(sel);
                        }
                    }
                }

                if (c.inSightOfPlayer != null)
                {
                    Object.Destroy(c.inSightOfPlayer);
                    c.inSightOfPlayer = null;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't settle {c.name}'s body as die2 does: "
                        + $"{e.Message}");
            }
        }

        /// <summary>
        /// Client: how a copy cut in half on the host looks as it starts: what
        /// Character.cutMeInHalf does to the upper part (its particles stop; it
        /// doesn't fade its shadow as a death does). The lower half is a new
        /// enemy on the host, shown here as any other.
        /// </summary>
        private static void CutLooks(Character c)
        {
            try
            {
                foreach (var ps in c.GetComponentsInChildren<ParticleSystem>())
                {
                    if (ps != null)
                    {
                        ps.Stop();
                    }
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't play {c.name}'s cut here: {e.Message}");
            }
        }

        // destroyComponents and destroyComponents2, without setDeathCollider.
        private static readonly System.Reflection.MethodInfo[] CutSteps =
            { CorpseSteps[0], CorpseSteps[1] };

        /// <summary>
        /// Client: the upper part of a copy cut in half on the host, once its
        /// cut has played: as on the host (cutMeInHalf, then its Cut_half
        /// animation's end), no corpse: no Item to search, its inventory went
        /// with the lower half (the host's new enemy), its selectable and
        /// capsule are gone, and it lies flat.
        /// </summary>
        private static void SettleCut(Puppet p)
        {
            var c = p.C;
            foreach (var step in CutSteps)
            {
                if (step == null)
                {
                    continue;
                }

                try
                {
                    step.Invoke(c, null);
                }
                catch (System.Exception e)
                {
                    System.Exception innerException = e.InnerException;
                    string message = null;
                    if (innerException != null)
                    {
                        message = innerException.Message;
                    }

                    if (message == null)
                    {
                        message = e.Message;
                    }

                    Plugin.Log.LogWarning(
                        $"Couldn't settle {c.name}'s cut ({step.Name}): "
                            + $"{message}");
                }
            }

            try
            {
                c.cuttingInHalf = true;
                c.cutInHalf = true;
                var item = c.GetComponent<Item>();
                if (item != null)
                {
                    Object.Destroy(item);
                }

                var inv = c.GetComponent<Inventory>();
                if (inv != null)
                {
                    World.Containers.CloseIfOpen(c.gameObject);
                    Object.Destroy(inv);
                }

                var sel = c.GetComponent<Selectable>();
                if (sel != null)
                {
                    Object.Destroy(sel);
                }

                if (c.capsuleCollider != null)
                {
                    Object.Destroy(c.capsuleCollider);
                }

                if (c.collider != null)
                {
                    c.collider.isTrigger = true;
                }

                var t = c.transform;
                t.localPosition =
                    new Vector3(t.localPosition.x, -1f, t.localPosition.z);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't settle {c.name}'s cut: {e.Message}");
            }

            Plugin.Log.LogInfo(
                $"The host's {c.name} #{p.Id} was cut in half: its lower half "
                    + "is the host's, nothing to search here");
        }

        private static readonly System.Reflection.MethodInfo YPosAfterDeath =
            HarmonyLib.AccessTools.Method(
                typeof(Character), "setYPosAfterDeath");

        /// <summary>
        /// Client: Character.setDeathCollider stopped part way on a copy (it
        /// threw). Its first step, the Item that makes the corpse searchable,
        /// is done by then; the rest is done here, each part only if what it
        /// needs is there: the body becomes a trigger (or our player bumps into
        /// a corpse the host's player walks through), takes the death collider
        /// of the clip it ended on, stops counting as active, and lies at a
        /// corpse's height. An NPC's death (its flags, its death inventory)
        /// comes from the host.
        /// </summary>
        private static void DeathColliderRest(Character c, System.Exception why)
        {
            try
            {
                if (c.GetComponent<Item>() == null)
                {
                    c.gameObject.AddComponent<Item>().name =
                        c.name.ToLower() + "_corpse";
                }

                if (!c.dontSwitchColliderTriggerOnDeath && c.collider != null)
                {
                    c.collider.isTrigger = true;
                }

                var a = c.GetComponent<tk2dSpriteAnimator>();
                string clip = null;
                if (a != null && a.CurrentClip != null)
                {
                    clip = a.CurrentClip.name;
                }

                if (clip != null && c.capsuleCollider != null
                    && c.deathColliders != null)
                {
                    foreach (var dc in c.deathColliders)
                    {
                        if (dc != null && dc.identifier == clip)
                        {
                            c.capsuleCollider.center = dc.center;
                            c.capsuleCollider.radius = dc.radius;
                            c.capsuleCollider.height = dc.height;
                        }
                    }
                }

                c.isActive = false;
                System.Reflection.MethodInfo yPosAfterDeath = YPosAfterDeath;
                if (yPosAfterDeath != null)
                {
                    yPosAfterDeath.Invoke(c, null);
                }

                string text = "none";
                if (a != null)
                {
                    text = "there";
                }

                string colliderState = "none";
                if (c.collider != null)
                {
                    colliderState = "there";
                }

                string capsuleState = "none";
                if (c.capsuleCollider != null)
                {
                    capsuleState = "there";
                }

                string shownClip = clip;
                if (shownClip == null)
                {
                    shownClip = "none";
                }

                List<ColliderInfo> deathColliders = c.deathColliders;
                int count = -1;
                if (deathColliders != null)
                {
                    count = deathColliders.Count;
                }

                string stackTrace = why.StackTrace;
                string trimmedLine = null;
                if (stackTrace != null)
                {
                    string line = stackTrace.Split('\n')[0];
                    if (line != null)
                    {
                        trimmedLine = line.Trim();
                    }
                }

                Plugin.Log.LogInfo(
                    $"{c.name}'s corpse settled here by the mod: the game's "
                        + $"setDeathCollider stopped ({why.Message}; " +
                                   $"animator {text}, clip {shownClip}, "
                        + $"collider {colliderState}, " +
                                   $"capsule {capsuleState}, death colliders "
                        + $"{count}; at {trimmedLine}); trigger now "
                        + $"{(c.collider != null && c.collider.isTrigger)}");
            }
            catch (System.Exception e)
            {
                System.Exception innerException = e.InnerException;
                string message = null;
                if (innerException != null)
                {
                    message = innerException.Message;
                }

                if (message == null)
                {
                    message = e.Message;
                }

                Plugin.Log.LogWarning(
                    $"Couldn't settle {c.name}'s corpse: {message}");
            }
        }

        private static void ApplyAnim(
            tk2dSpriteAnimator a, int clipHash, int frame, bool playing)
        {
            // never let a puppet's frames act (attack, sound) here
            a.AnimationEventTriggered = CopyFrameEvent;
            a.AnimationCompleted = null;
            if (clipHash == 0 || a.Library == null)
            {
                return;
            }

            var cur = a.CurrentClip;
            if (cur == null || StableHash.Of(cur.name) != clipHash)
            {
                foreach (var clip in a.Library.clips)
                {
                    if (clip != null && StableHash.Of(clip.name) == clipHash)
                    {
                        a.PlayFromFrame(
                            clip,
                            Mathf.Clamp(
                                frame, 0,
                                Mathf.Max(0, clip.frames.Length - 1)));
                        break;
                    }
                }
            }
            // More than 2 frames off: move the copy's clip time to the host's
            // frame. (SetFrame would change only the sprite shown; the next
            // animator update goes back to the copy's own clip time, so the
            // copy flicked to the host's frame and back on every update and
            // stayed off, as a world rabbit did 3 frames ahead.)
            else if (Mathf.Abs(a.CurrentFrame - frame) > 2
                && cur.frames != null && frame < cur.frames.Length)
            {
                a.PlayFromFrame(cur, frame);
            }

            if (!playing && a.Playing)
            {
                a.Pause();
            }
            else if (playing && a.Paused)
            {
                a.Resume();
            }
        }

        /// <summary>
        /// Client: a copy's animation events. Of what Character.OnAniEvent
        /// does, only "TweenShadows" (fading the character's extra shadows out
        /// or in with the animation) is played here: it changes how the copy
        /// looks and nothing else. Attacks, sounds and the rest are the host's.
        /// </summary>
        private static void CopyFrameEvent(
            tk2dSpriteAnimator a, tk2dSpriteAnimationClip clip, int frame)
        {
            try
            {
                tk2dSpriteAnimationFrame f = null;
                if (clip != null)
                {
                    f = clip.GetFrame(frame);
                }

                if (f == null || f.eventInfo != "TweenShadows")
                {
                    return;
                }

                Character c = null;
                if (a != null)
                {
                    c = a.GetComponentInParent<Character>();
                }

                if (c != null)
                {
                    c.tweenShadows(hide: f.eventInt == 0);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't play a copy's TweenShadows: {e.Message}");
            }
        }

        private void OnDespawn(ulong from, PacketReader r)
        {
            if (from != _session.HostId)
            {
                return;
            }

            uint id = r.R.ReadUInt32();
            bool keepCorpse = r.R.ReadByte() != 0;
            if (!_puppets.TryGetValue(id, out var p))
            {
                // The host's corpse is gone (it was destroyed there): so is
                // ours.
                if (keepCorpse || !_corpses.TryGetValue(id, out p))
                {
                    return;
                }

                _corpses.Remove(id);
                if (p.C == null)
                {
                    return;
                }

                _corpseIds.Remove(p.C);
                Plugin.Log.LogInfo(
                    $"The host's corpse #{id} ({p.C.name}) is gone there: "
                        + "removed here");
                // Destroyed, our copy from the save too: the host's is out of
                // its world for good, and a hidden copy would still be in ours
                // (its body searchable by name, saved with our world).
                World.Containers.CloseIfOpen(p.C.gameObject);
                Object.Destroy(p.C.gameObject);
                return;
            }

            _puppets.Remove(id);
            if (p.C == null)
            {
                return;
            }

            _byChar.Remove(p.C);
            if (keepCorpse) // stays where it fell
            {
                if (!p.Settled)
                {
                    Settle(p);
                }

                _corpses[id] = p;
                _corpseIds[p.C] = id;
                Recull(p);
                return;
            }

            StopSounds(p.C, keepDeathSound: false);
            // An NPC the host calmed down (or one out of our range): our copy
            // of it is ours to run again.
            if (p.C.npc != null
                && HostileNpcs.ClientStopped(p.C, id, ours: p.Ours))
            {
                Recull(p);
                return;
            }

            // its window, if our player has its corpse open: gone or hidden, it
            // is gone for us
            World.Containers.CloseIfOpen(p.C.gameObject);
            if (p.Ours)
            {
                Object.Destroy(p.C.gameObject);
            }
            else
            {
                Hide(p.C);
                Recull(p);
            }
        }

        private void OnSound(ulong from, PacketReader r)
        {
            if (from != _session.HostId)
            {
                return;
            }

            uint id = r.R.ReadUInt32();
            string audio = r.ReadStr();
            float volume = r.R.ReadSingle();
            if (!_puppets.TryGetValue(id, out var p) || p.C == null)
            {
                return;
            }

            // Once it's dead here, only its death cry still plays (it can
            // arrive just after the state that said so); a bark or a loop from
            // before the host's die2, arriving late, would go on over the
            // corpse.
            if (p.DiedAt >= 0f
                && (p.C.sounds == null || audio != p.C.sounds.death))
            {
                return;
            }

            // One of its loops (CharacterSounds.playIdleLoop: idle, aggressive
            // while it chases, defensive, sleeping, escaping) takes the place
            // of the copy's loop, as on the host. Played as a sound it would go
            // on beside the copy's idle loop, and nothing would stop it. "" is
            // the host's loop stopping (HostLoopStopPatch).
            var snd = p.C.sounds;
            if (string.IsNullOrEmpty(audio))
            {
                if (snd != null)
                {
                    try
                    {
                        snd.playIdleLoop("");
                    }
                    catch (System.Exception e)
                    {
                        Plugin.Log.LogWarning(
                            $"Couldn't stop {p.C.name}'s loop: {e.Message}");
                    }
                }

                return;
            }

            if (IsLoop(snd, audio))
            {
                try
                {
                    snd.playIdleLoop(audio);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't play {p.C.name}'s loop {audio}: "
                            + $"{e.Message}");
                }

                return;
            }

            AudioController.Play(audio, p.C.transform, volume);
        }

        /// <summary>
        /// Client: we hit a host's enemy; the host applies it.
        /// </summary>
        public void ClientHit(
            uint id, float damage, bool cut, bool byPlayer, bool interrupt,
            bool normal, List<InvItemEffect> effects)
        {
            var w = _session.Begin(MsgType.EnemyHit);
            w.W.Write(id);
            w.W.Write(damage);
            int flags = 0;
            if (cut)
            {
                flags |= 1;
            }

            if (byPlayer)
            {
                flags |= 2;
            }

            if (interrupt)
            {
                flags |= 4;
            }

            if (normal)
            {
                flags |= 8;
            }

            w.W.Write((byte)flags);
            PlayerDamage.WriteEffects(w, effects);
            InvItemClass item = null;
            if (Player.Instance != null)
            {
                item = Player.Instance.currentItem;
            }

            string s;
            if (InvItemClass.isNull(item))
            {
                s = "";
            }
            else
            {
                s = Newtonsoft.Json.JsonConvert.SerializeObject(
                    new InvItemClass.SaveState(item, 0));
            }

            w.WriteStr(s);
            _puppets.TryGetValue(id, out var p);
            Character hit = null;
            if (p != null)
            {
                hit = p.C;
            }

            World.Authority.Request(w, $"hit on enemy #{id} for {damage:0}",
                (ok, reason, result) =>
            {
                if (ok)
                {
                    ScriptedEvents.HitApplied(hit, result);
                }
            });

            // Our own hit shows its health bar, as getHit does; the host's
            // health for it follows in its next state.
            if (p == null || p.C == null)
            {
                return;
            }

            if (damage > 0f)
            {
                HealthBar(p.C, show: true);
            }

            // Predicted: the blood shows at once; the host's for this hit (a
            // moment later) isn't shown again.
            Core.AddPrefab(
                "FX/Bloodsplats/Shotsplat_stay", p.C.transform.position,
                Quaternion.Euler(90f, UnityEngine.Random.Range(0f, 360f), 0f),
                null);
            _predictedBlood[id] = Time.realtimeSinceStartup;
        }

        private readonly Dictionary<uint, float> _predictedBlood =
            new Dictionary<uint, float>();

        /// <summary>
        /// Client: the effects of the weapon hitting right now (set around a
        /// player's MeleeSensor hit).
        /// </summary>
        public static List<InvItemEffect> HitEffects;

        // ---- shared helpers -------------------------------------------------

        private static void Animators(
            Transform root, out List<tk2dSpriteAnimator> list,
            out List<int> paths)
        {
            list = new List<tk2dSpriteAnimator>();
            paths = new List<int>();
            foreach (var a in root.GetComponentsInChildren<tk2dSpriteAnimator>(
                true))
            {
                string p;
                if (a.transform == root)
                {
                    p = ".";
                }
                else
                {
                    p = a.name;
                }

                if (a.transform != root)
                {
                    for (var t = a.transform.parent; t != null
                        && t != root; t = t.parent)
                    {
                        p = t.name + "/" + p;
                    }
                }

                list.Add(a);
                paths.Add(StableHash.Of(p));
            }
        }

        private static void WriteRot(PacketWriter w, Quaternion q)
        {
            w.W.Write(q.x);
            w.W.Write(q.y);
            w.W.Write(q.z);
            w.W.Write(q.w);
        }

        private static Quaternion ReadRot(PacketReader r) => new Quaternion(
            r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle(),
            r.R.ReadSingle());
    }

    /// <summary>
    /// The characters that are active now, as
    /// Object.FindObjectsOfType&lt;Character&gt;() finds them, without
    /// searching every object in the scene (that took about 50 ms in a loaded
    /// world, twice a second on the host). Every character passes
    /// Character.OnEnable (no kind of character has its own), which lists it
    /// here; the destroyed ones drop out and the inactive ones are skipped.
    /// </summary>
    public static class AllCharacters
    {
        private static readonly HashSet<Character> _all =
            new HashSet<Character>();
        private static readonly List<Character> _now = new List<Character>();

        internal static void Add(Character c)
        {
            if (c != null)
            {
                _all.Add(c);
            }
        }

        /// <summary>
        /// A list reused on each call: use it before calling again.
        /// </summary>
        public static List<Character> Active()
        {
            _now.Clear();
            _all.RemoveWhere(c => c == null);
            foreach (var c in _all)
            {
                if (c.isActiveAndEnabled)
                {
                    _now.Add(c);
                }
            }

            return _now;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Character), "OnEnable")]
    static class AllCharactersPatch
    {
        static void Postfix(Character __instance) =>
            AllCharacters.Add(__instance);
    }
}
