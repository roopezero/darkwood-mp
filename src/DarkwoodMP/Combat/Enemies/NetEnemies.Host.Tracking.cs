using System.Collections.Generic;
using System.Linq;
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
        private const int CorpsesPerPass = 4;

        private void HostUpdate()
        {
            float now = Time.unscaledTime;
            if (now >= _nextScan)
            {
                _nextScan = now + 0.5f;
                foreach (var c in Plugin.Instance.Host.Characters.Active())
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
    }
}
