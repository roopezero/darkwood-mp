using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat.Enemies;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The host side: which enemies are tracked and sent to whom. See
    /// NetEnemies.cs.
    /// </summary>
    internal sealed partial class NetEnemies
    {
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
        private readonly PacketWriter _batch = new PacketWriter(new BepInLog());
        private readonly PacketWriter _one = new PacketWriter(new BepInLog());
        /// <summary>
        /// Corpses sent to one client in one state pass at most (a joiner can
        /// have dozens near them).
        /// </summary>
        private void HostUpdate()
        {
            float now = Time.unscaledTime;
            if (EnemyTrackRules.Due(new Seconds(now), new Seconds(_nextScan)))
            {
                _nextScan = now + EnemyTrackRules.ScanInterval;
                foreach (var c in Plugin.Instance.Host.Characters.Active())
                {
                    if (EnemyTrackRules.Tracks(
                        Eligible(c), _byCharHost.ContainsKey(c)))
                    {
                        var t = new Tracked { Id = _nextId++, C = c };
                        Animators(
                            c.transform, out t.Animators, out t.AnimatorPaths);
                        _byCharHost[c] = t;
                        _byIdHost[t.Id] = t;
                        if (c.npc != null)
                        {
                            _coop.Hostiles.HostStarted(c, t.Id);
                        }
                    }
                }

                var gone = new List<Character>();
                foreach (var kv in _byCharHost)
                {
                    // An NPC that calmed down (and stayed calm a while) is each
                    // player's own again.
                    var step = EnemyTrackRules.Calm(CalmOf(kv.Value, now));
                    if (step == CalmStep.Gone)
                    {
                        gone.Add(kv.Key);
                    }
                    else if (step == CalmStep.Hostile)
                    {
                        kv.Value.CalmSince = -1f;
                    }
                    else if (step == CalmStep.StartCalm)
                    {
                        kv.Value.CalmSince = now;
                    }
                }

                foreach (var g in gone)
                {
                    uint id = _byCharHost[g].Id;
                    if (g != null)
                    {
                        _coop.Hostiles.HostStopped(g, id);
                    }

                    Forget(id);
                    _byCharHost.Remove(g);
                    _byIdHost.Remove(id);
                }
            }

            if (EnemyTrackRules.Due(
                new Seconds(now), new Seconds(_nextLoopCheck)))
            {
                _nextLoopCheck = now + EnemyLoopRules.LoopCheckInterval;
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
                NetPlayerProxy.All.TryGetValue(peer, out var proxy);
                bool standIn = proxy != null;
                bool away = standIn && proxy.Away;
                // Away (a dream, or a separate location we haven't got where
                // they have it): their own game runs the enemies there
                // (LocalOnly), so none of ours go to them; what they have of
                // ours waits for their return. In a location we run for them
                // (proxy.InLocation) they get ours there, by the same range
                // check.
                if (!EnemyRelayRules.Serves(standIn, away))
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
                int countAt = EnemyStateMessage.WriteHead(w, now);
                int count = 0;
                var drop = new List<(uint, bool)>();
                foreach (var t in _byCharHost.Values)
                {
                    var c = t.C;
                    if (c != null
                        && EnemyRelayRules.Dies(InPlay(c), t.DiedAt >= 0))
                    {
                        t.DiedAt = Time.time;
                    }

                    // A corpse the client has isn't sent again (it would make
                    // a second body); one it never saw settle goes once it's
                    // near; out of range, or settled, the dead stay where they
                    // fell (the client settles it now if it hadn't).
                    var facts = RelayOf(t, at) with
                    {
                        Known = known.Contains(t.Id),
                        CorpseKept = corpses.Contains(t.Id),
                        CorpsesSent = corpsesSent,
                    };

                    var step = EnemyRelayRules.Choose(facts);
                    if (step == RelayStep.SendCorpse)
                    {
                        SendCorpse(peer, t);
                        corpses.Add(t.Id);
                        corpsesSent++;
                    }
                    else if (step == RelayStep.Drop)
                    {
                        drop.Add((t.Id, false));
                    }
                    else if (step == RelayStep.DropCorpse)
                    {
                        drop.Add((t.Id, true));
                    }
                    else if (step == RelayStep.Spawn)
                    {
                        known.Add(t.Id);
                        SendSpawn(peer, t);
                        WriteState(w, t);
                        count++;
                    }
                    else if (step == RelayStep.State)
                    {
                        WriteState(w, t);
                        count++;
                    }
                }

                foreach (var (id, corpse) in drop)
                {
                    known.Remove(id);
                    SendDespawn(peer, id, corpse);
                    if (corpse && EnemyRelayRules.SendsLoot(
                        corpse, corpses.Add(id)))
                    {
                        SendCorpseLoot(peer, id);
                    }
                }

                if (!EnemyRelayRules.SendsBatch(count))
                {
                    continue;
                }

                EnemyStateMessage.SetCount(w, countAt, count);
                _session.SendTo(peer, w, Channel.State, reliable: false);
            }
        }

        /// <summary>
        /// One of the loops CharacterSounds.playIdleLoop plays for this
        /// character.
        /// </summary>
        private static bool IsLoop(CharacterSounds snd, string audio) =>
            EnemyLoopRules.IsLoop(LoopsOf(snd), audio);

        /// <summary>
        /// The loops playIdleLoop plays with these sounds.
        /// </summary>
        private static LoopNames LoopsOf(CharacterSounds snd)
        {
            if (snd == null)
            {
                return new LoopNames { Exists = false };
            }

            return new LoopNames
            {
                Exists = true,
                Idle = snd.idleLoop,
                IdleAggressive = snd.idleLoopAggressive,
                Aggressive = snd.aggressive,
                Defensive = snd.defensive,
                Sleeping = snd.sleeping,
                Escaping = snd.escapingLoop,
            };
        }

        private float _nextLoopCheck, _nextLoopResend;

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
            bool resend = EnemyTrackRules.Due(
                new Seconds(now), new Seconds(_nextLoopResend));
            if (resend)
            {
                _nextLoopResend = now + EnemyLoopRules.LoopResendInterval;
            }

            foreach (var t in _byCharHost.Values)
            {
                // A sound that isn't one the client takes as a loop (OnSound)
                // sends nothing.
                var facts = LoopOf(t) with { Resend = resend };
                var send = EnemyLoopRules.Check(facts);
                if (send == LoopSend.None)
                {
                    continue;
                }

                t.Loop = facts.Current;
                foreach (var peer in Knowing(t.Id))
                {
                    SendSound(
                        peer, t.Id, facts.Current, 1f,
                        reliable: send == LoopSend.Changed);
                }
            }
        }

        /// <summary>
        /// The loop check's facts for one tracked enemy, but Resend.
        /// </summary>
        private static LoopFacts LoopOf(Tracked t)
        {
            var c = t.C;
            if (c == null)
            {
                return new LoopFacts { Exists = false };
            }

            AudioObject loop = null;
            if (c.sounds != null)
            {
                loop = c.sounds.idleAudioObject;
            }

            string audio = null;
            bool playing = loop != null && loop.IsPlaying();
            if (playing)
            {
                audio = loop.audioID;
            }

            string current = EnemyLoopRules.CurrentLoop(playing, audio);
            return new LoopFacts
            {
                Exists = true,
                Dead = t.DiedAt >= 0,
                InPlay = InPlay(c),
                Current = current,
                IsLoop = IsLoop(c.sounds, current),
                Told = t.Loop,
            };
        }

        /// <summary>
        /// The state pass's facts for one tracked enemy and a client's
        /// stand-in, without what the client has.
        /// </summary>
        private static RelayFacts RelayOf(Tracked t, Vector3 standIn)
        {
            var c = t.C;
            if (c == null)
            {
                return new RelayFacts { Exists = false };
            }

            bool dead = t.DiedAt >= 0;
            return new RelayFacts
            {
                Exists = true,
                Dead = dead,
                SinceDeath = new Seconds(Time.time - t.DiedAt),
                Active = c.gameObject.activeInHierarchy,
                Enemy = VecOf(c.transform.position),
                StandIn = VecOf(standIn),
            };
        }

        /// <summary>
        /// What kind of character it is: its prefab's name, as the save system
        /// knows it.
        /// </summary>
        internal static string KindOf(Character c)
        {
            var save = c.GetComponent<SaveableObject>();
            string prefab = null;
            if (save != null)
            {
                prefab = save.prefabName;
            }

            return EnemyTrackRules.KindOf(prefab, c.name);
        }

        /// <summary>
        /// The scan's facts for one tracked character.
        /// </summary>
        private static CalmFacts CalmOf(Tracked t, float now)
        {
            var c = t.C;
            bool exists = c != null;
            bool npc = exists && c.npc != null;
            bool inPlay = npc && InPlay(c);
            return new CalmFacts
            {
                Exists = exists,
                Npc = npc,
                InPlay = inPlay,
                Hostile = inPlay && HostileNpcs.StillHostile(c),
                Calming = t.CalmSince >= 0f,
                CalmSince = new Seconds(t.CalmSince),
                Now = new Seconds(now),
            };
        }
    }
}
