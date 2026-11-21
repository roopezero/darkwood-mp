using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Npcs;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Where the NPCs stand. NPCs run in every game, so one that walks (a
    /// follower such as Doctor_follower, a scripted move, one that wanders)
    /// would end up somewhere else in each. The host's NPC is the one that
    /// counts:
    /// the host checks a joiner's talk (TalkLock) and NPC kills (NpcDied)
    /// against where its own copy is. So the host sends where its NPCs are
    /// (NpcPos): those that moved, twice a second, and all of them every few
    /// seconds (for a joiner that just came in, and for a copy a joiner's game
    /// moved by itself). Clients keep their copies there.
    ///
    /// Only NPCs with a save id shared by every game (SharedIds) are sent: the
    /// morning trader and the wolf are put in each player's hideout by each
    /// game, and are matched by name instead (TraderSync, TalkLock). NPCs that
    /// NetEnemies runs (a hostile one, if it does) go by its enemy states
    /// instead. A host's NPC in a part of the world it has switched off (its
    /// object inactive) is sent too, where it stands there, so a client that
    /// has that part on doesn't let its copy walk off by itself (a follower
    /// after the client's own player, say).
    /// </summary>
    public sealed class NpcSync
    {
        public static NpcSync Instance { get; private set; }
        private readonly Session _session;

        /// <summary>Host: where each NPC was when last sent.</summary>
        private readonly Dictionary<int, Vec2> _sent =
            new Dictionary<int, Vec2>();
        /// <summary>Client: where the host has each NPC.</summary>
        private readonly Dictionary<int, Vec2> _target =
            new Dictionary<int, Vec2>();
        private NpcTimers _timers;
        /// <summary>
        /// Host: the NPCs with a shared save id, active or not (found again
        /// every AllEvery seconds).
        /// </summary>
        private readonly List<(int id, NPC npc)> _npcs = new List<(int, NPC)>();

        public NpcSync(Session session)
        {
            Instance = this;
            _session = session;
            session.Register(MsgType.NpcPos, OnPos);
            session.OnSessionEnded += () =>
            {
                _sent.Clear();
                _target.Clear();
                _npcs.Clear();
            };
        }

        /// <summary>
        /// Whether an NPC may be placed, and its Character (null with none).
        /// </summary>
        private static NpcFacts FactsOf(NPC npc, out Character c)
        {
            c = null;
            if (npc != null)
            {
                c = npc.GetComponent<Character>();
            }

            if (c == null)
            {
                return new NpcFacts();
            }

            NetEnemies enemies = NetEnemies.Instance;
            return new NpcFacts
            {
                HasCharacter = true,
                Alive = c.alive,
                RunByEnemies = enemies != null && enemies.NetIdOf(c, out _),
            };
        }

        public void Update()
        {
            if (!_session.Connected || !WorldShare.InWorld)
            {
                return;
            }

            if (_session.IsHost)
            {
                HostUpdate();
            }
            else
            {
                ClientUpdate();
            }
        }

        private void HostUpdate()
        {
            NpcTick tick = NpcPlaceRules.Tick(
                _session.Peers.Count,
                new Seconds(Time.unscaledTime),
                _timers);
            _timers = tick.Timers;
            if (!tick.Sends)
            {
                return;
            }

            bool all = tick.All;
            if (all)
            {
                FindNpcs();
            }

            var list = new List<NpcPlace>();
            foreach (var (id, npc) in _npcs)
            {
                if (!NpcPlaceRules.Movable(FactsOf(npc, out var c)))
                {
                    continue;
                }

                var pos = c.transform.position;
                var at = new Vec2(pos.x, pos.z);
                bool sent = _sent.TryGetValue(id, out Vec2 was);
                var facts = new NpcSendFacts
                {
                    All = all,
                    Sent = sent,
                    Was = was,
                    At = at,
                };

                if (!NpcPlaceRules.Sends(facts))
                {
                    continue;
                }

                _sent[id] = at;
                list.Add(new NpcPlace { Id = id, At = at });
            }

            if (list.Count == 0)
            {
                return;
            }

            int start = 0;
            while (start < list.Count)
            {
                var w = _session.Begin(MsgType.NpcPos);
                start += NpcPosMessage.Write(w, list, start);
                _session.Broadcast(w, Channel.Control, reliable: true);
            }
        }

        /// <summary>
        /// The game's save ids list every saveable object, switched on or off
        /// (FindObjectsOfType would leave out the NPCs in parts of the world
        /// the host has switched off).
        /// </summary>
        private void FindNpcs()
        {
            _npcs.Clear();
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null)
            {
                return;
            }

            foreach (var kv in sm.uniqueIdDict)
            {
                if (!NpcPlaceRules.Shared(kv.Key, SharedIds.SendLimit)
                    || kv.Value == null)
                {
                    continue;
                }

                var npc = kv.Value.GetComponent<NPC>();
                if (npc != null)
                {
                    _npcs.Add((kv.Key, npc));
                }
            }
        }

        private void OnPos(ulong from, PacketReader r)
        {
            bool fromHost = from == _session.HostId;
            if (!NpcPlaceRules.TakesPos(fromHost, WorldShare.InWorld))
            {
                return;
            }

            foreach (NpcPlace place in NpcPosMessage.Read(r))
            {
                _target[place.Id] = place.At;
            }
        }

        private readonly List<int> _gone = new List<int>();

        /// <summary>
        /// The world grid shows and hides things by the grid node they're
        /// registered to: one that moved while hidden would stay hidden with
        /// its old node (or show far from it). Register it to the node where it
        /// is now, as the game does for a moving thing when it's hidden
        /// (CullableObject.tryToHideMoveable), and show or hide it as that node
        /// is.
        /// </summary>
        private static void Recull(GameObject go, Vector3 at)
        {
            var co = go.GetComponent<CullableObject>();
            var grid = Singleton<WorldGrid>.Instance;
            if (co == null || co.node == null || grid == null)
            {
                return;
            }

            var node = grid.getNode(at);
            if (node == null || node == co.node)
            {
                return;
            }

            var cullable = co.node.getCullable(go);
            co.node.removeCullable(go);
            co.node = node;
            if (cullable != null)
            {
                node.cullables.Add(cullable);
            }

            if (go.activeSelf != node.entered)
            {
                go.SetActive(node.entered);
            }
        }

        /// <summary>
        /// Client: our copy of an NPC the host placed at target, and its
        /// Character (null when it can't be moved). Time.deltaTime is
        /// scaled, so copies don't slide while the game is paused.
        /// </summary>
        private static NpcFollowFacts FollowFactsOf(
            SaveManager sm,
            KeyValuePair<int, Vec2> target,
            out Character c)
        {
            c = null;
            if (!sm.uniqueIdDict.TryGetValue(target.Key, out var so)
                || so == null)
            {
                return new NpcFollowFacts();
            }

            var npc = so.GetComponent<NPC>();
            if (!NpcPlaceRules.Movable(FactsOf(npc, out c)))
            {
                return new NpcFollowFacts { HasObject = true };
            }

            var pos = c.transform.position;
            return new NpcFollowFacts
            {
                HasObject = true,
                Movable = true,
                At = new Vec3(pos.x, pos.y, pos.z),
                Target = target.Value,
                Delta = new Seconds(Time.deltaTime),
            };
        }

        private void ClientUpdate()
        {
            if (_target.Count == 0)
            {
                return;
            }

            var sm = Singleton<SaveManager>.Instance;
            if (sm == null)
            {
                return;
            }

            foreach (var kv in _target)
            {
                NpcFollowFacts facts = FollowFactsOf(sm, kv, out var c);
                NpcStep step = NpcPlaceRules.Follow(facts);
                if (step.Move == NpcMove.Forget)
                {
                    _gone.Add(kv.Key);
                    continue;
                }

                if (step.Move != NpcMove.Snap && step.Move != NpcMove.Step)
                {
                    continue;
                }

                var to = new Vector3(step.To.X, step.To.Y, step.To.Z);
                c.transform.position = to;
                if (step.Move == NpcMove.Step)
                {
                    continue;
                }

                Recull(c.gameObject, to);
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {c.name} is at the host's "
                            + $"{to.x:0},{to.z:0} ({step.Distance:0} away)");
                }
            }

            foreach (var id in _gone)
            {
                _target.Remove(id);
            }

            _gone.Clear();
        }
    }
}
