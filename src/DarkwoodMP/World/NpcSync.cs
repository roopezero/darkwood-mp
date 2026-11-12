using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Where the NPCs stand. NPCs run in every game, so one that walks (a follower such as Doctor_follower, a
    /// scripted move, one that wanders) would end up somewhere else in each. The host's NPC is the one that counts:
    /// the host checks a joiner's talk (TalkLock) and NPC kills (NpcDied) against where its own copy is. So the host
    /// sends where its NPCs are (NpcPos): those that moved, twice a second, and all of them every few seconds (for a
    /// joiner that just came in, and for a copy a joiner's game moved by itself). Clients keep their copies there.
    ///
    /// Only NPCs with a save id shared by every game (SharedIds) are sent: the morning trader and the wolf are put in
    /// each player's hideout by each game, and are matched by name instead (TraderSync, TalkLock). NPCs that NetEnemies
    /// runs (a hostile one, if it does) go by its enemy states instead. A host's NPC in a part of the world it has
    /// switched off (its object inactive) is sent too, where it stands there, so a client that has that part on
    /// doesn't let its copy walk off by itself (a follower after the client's own player, say).
    /// </summary>
    public sealed class NpcSync
    {
        public static NpcSync Instance { get; private set; }
        private readonly Session _session;

        /// <summary>Host: where each NPC was when last sent.</summary>
        private readonly Dictionary<int, Vector2> _sent = new Dictionary<int, Vector2>();
        /// <summary>Client: where the host has each NPC.</summary>
        private readonly Dictionary<int, Vector2> _target = new Dictionary<int, Vector2>();
        private float _nextMoved, _nextAll;
        /// <summary>Host: the NPCs with a shared save id, active or not (found again every AllEvery seconds).</summary>
        private readonly List<(int id, NPC npc)> _npcs = new List<(int, NPC)>();

        private const float Moved = 30f;        // sent when it moved this far
        private const float MovedEvery = 0.5f, AllEvery = 4f;
        private const float Snap = 600f;        // a client's copy this far off is put there at once
        private const float Close = 15f;        // and this close is left alone

        public NpcSync(Session session)
        {
            Instance = this;
            _session = session;
            session.Register(MsgType.NpcPos, OnPos);
            session.OnSessionEnded += () => { _sent.Clear(); _target.Clear(); _npcs.Clear(); };
        }

        private static bool Movable(NPC npc, out Character c)
        {
            c = npc != null ? npc.GetComponent<Character>() : null;
            if (c == null || !c.alive) return false;
            return NetEnemies.Instance == null || !NetEnemies.Instance.NetIdOf(c, out _);
        }

        public void Update()
        {
            if (!_session.Connected || !WorldShare.InWorld) return;
            if (_session.IsHost) HostUpdate(); else ClientUpdate();
        }

        private void HostUpdate()
        {
            if (_session.Peers.Count == 0 || Time.unscaledTime < _nextMoved) return;
            _nextMoved = Time.unscaledTime + MovedEvery;
            bool all = Time.unscaledTime >= _nextAll;
            if (all) { _nextAll = Time.unscaledTime + AllEvery; FindNpcs(); }
            var list = new List<(int id, Vector2 p)>();
            foreach (var (id, npc) in _npcs)
            {
                if (!Movable(npc, out var c)) continue;
                var pos = c.transform.position;
                var p = new Vector2(pos.x, pos.z);
                bool moved = !_sent.TryGetValue(id, out var was) || (p - was).sqrMagnitude > Moved * Moved;
                if (!moved && !all) continue;
                _sent[id] = p;
                list.Add((id, p));
            }
            if (list.Count == 0) return;
            for (int start = 0; start < list.Count; start += 200)
            {
                int n = Mathf.Min(200, list.Count - start);
                var w = _session.Begin(MsgType.NpcPos);
                w.W.Write((ushort)n);
                for (int i = start; i < start + n; i++) { w.W.Write(list[i].id); w.W.Write(list[i].p.x); w.W.Write(list[i].p.y); }
                _session.Broadcast(w, Channel.Control, reliable: true);
            }
        }

        /// <summary>
        /// The game's save ids list every saveable object, switched on or off (FindObjectsOfType would leave out
        /// the NPCs in parts of the world the host has switched off).
        /// </summary>
        private void FindNpcs()
        {
            _npcs.Clear();
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null) return;
            foreach (var kv in sm.uniqueIdDict)
            {
                if (kv.Key <= 0 || kv.Key >= SharedIds.SendLimit || kv.Value == null) continue;
                var npc = kv.Value.GetComponent<NPC>();
                if (npc != null) _npcs.Add((kv.Key, npc));
            }
        }

        private void OnPos(ulong from, PacketReader r)
        {
            if (from != _session.HostId || !WorldShare.InWorld) return;
            int n = r.R.ReadUInt16();
            for (int i = 0; i < n; i++)
            {
                int id = r.R.ReadInt32();
                float x = r.R.ReadSingle(), z = r.R.ReadSingle();
                _target[id] = new Vector2(x, z);
            }
        }

        private readonly List<int> _gone = new List<int>();

        /// <summary>
        /// The world grid shows and hides things by the grid node they're registered to: one that moved while hidden
        /// would stay hidden with its old node (or show far from it). Register it to the node where it is now, as the
        /// game does for a moving thing when it's hidden (CullableObject.tryToHideMoveable), and show or hide it as
        /// that node is.
        /// </summary>
        private static void Recull(GameObject go, Vector3 at)
        {
            var co = go.GetComponent<CullableObject>();
            var grid = Singleton<WorldGrid>.Instance;
            if (co == null || co.node == null || grid == null) return;
            var node = grid.getNode(at);
            if (node == null || node == co.node) return;
            var cullable = co.node.getCullable(go);
            co.node.removeCullable(go);
            co.node = node;
            if (cullable != null) node.cullables.Add(cullable);
            if (go.activeSelf != node.entered) go.SetActive(node.entered);
        }

        private void ClientUpdate()
        {
            if (_target.Count == 0) return;
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null) return;
            float dt = Time.deltaTime;
            foreach (var kv in _target)
            {
                if (!sm.uniqueIdDict.TryGetValue(kv.Key, out var so) || so == null) continue;
                var npc = so.GetComponent<NPC>();
                if (!Movable(npc, out var c)) { _gone.Add(kv.Key); continue; }
                var t = c.transform;
                var pos = t.position;
                var to = new Vector3(kv.Value.x, pos.y, kv.Value.y);
                float d = StateDump.Flat(to - pos);
                if (d <= Close) continue;
                if (d > Snap)
                {
                    t.position = to;
                    Recull(c.gameObject, to);
                    if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] {npc.name} is at the host's {to.x:0},{to.z:0} ({d:0} away)");
                    continue;
                }
                t.position = Vector3.MoveTowards(pos, to, Mathf.Max(250f, d * 4f) * dt);
            }
            foreach (var id in _gone) _target.Remove(id);
            _gone.Clear();
        }
    }
}
