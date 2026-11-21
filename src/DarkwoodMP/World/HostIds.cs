using System.Collections.Generic;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Ids for what the world makes by itself while players are joined: a
    /// location's characters, a delayed event's spawn, the morning trader,
    /// other runtime spawns. Each game gives those its own save ids (or none
    /// until the next save), so without this ObjRef could only name them by
    /// name and position.
    ///
    ///   Host:   every saveable object that starts while it shares its world
    ///           (SaveableObject.init) and, a moment later, still has no shared
    ///           name (not a spawn of ours, not an enemy, no save id every
    ///           joiner has) gets a random id. The ids made in a frame go out
    ///           together as HostIds: id, name, position. A joiner let in later
    ///           gets all the live ones (flagged as made before it came, so it
    ///           doesn't look for them by time).
    ///   Client: ties the id to its own copy: an object of that name, made
    ///           within Window seconds before or after the message, the nearest
    ///           within ObjRef.PlacedSlack of the host's position (of those
    ///           equally near, the one in the same place in its hierarchy,
    ///           PlaceOf), not tied already. Until one is tied (or when none
    ///           ever is), a ref to the id is looked up by that name and
    ///           position, as Placed. An id not tied within Window (the host
    ///           loaded a location this joiner only goes to minutes later), or
    ///           one made before it came, is kept by name: an object of that
    ///           name that starts here later, still unnamed after Settle, is
    ///           tied to the nearest such id within PlacedSlack that isn't tied
    ///           to a live one.
    /// Objects with a save id every game shares keep being named by it; this
    /// only changes what was Placed.
    /// </summary>
    internal sealed class HostIds
    {
        private readonly Session _session;
        private readonly ObjNames _names;
        // how far apart in time the host's object and a client's copy may start
        public const float Window = HostIdRules.Window;
        private const int MaxPerMessage = HostIdsMessage.MaxPerMessage;

        private readonly List<(GameObject go, float at)> _started =
            new List<(GameObject, float)>();
        private readonly List<(long id, string name, Vector3 pos,
            int place, float until)> _pending =
                new List<(long, string, Vector3, int, float)>();
        // Client: ids the host gave out that weren't tied in time (or were made
        // before we came), by name.
        private readonly
            Dictionary<string, List<(long id, Vector3 pos, int place)>> _late =
                new Dictionary<string, List<(long, Vector3, int)>>();
        private readonly HashSet<GameObject> _lateChecked =
            new HashSet<GameObject>();
        // still in _started: a fresh id may take over
        private readonly HashSet<GameObject> _lateTied =
            new HashSet<GameObject>();
        private readonly
            List<(long id, string name, Vector3 pos, int place)> _outgoing =
                new List<(long, string, Vector3, int)>();
        // Client: where the host last had each announced object (as announced,
        // or where an ObjMove for it that came before we tied it put it), for
        // our copy to go there once it's tied (Place).
        private readonly Dictionary<long, Vector3> _hostAt =
            new Dictionary<long, Vector3>();
        private float _nextPrune;

        /// <summary>
        /// A random 62-bit id for a Spawned or shared object; one generator, so
        /// ids made in the same millisecond differ.
        /// </summary>
        internal static long NewId()
        {
            System.Random rng = Plugin.Instance.Host.IdRandom;
            return HostIdRules.NewId(rng.Next(), rng.Next());
        }

        /// <summary>
        /// How many ids this game gave out (host) or tied (client); for the
        /// tests.
        /// </summary>
        public int Given { get; private set; }

        public int Tied { get; private set; }

        /// <summary>
        /// For one session (CoopSession.HostIds), after its ObjNames.
        /// </summary>
        public HostIds(CoopSession coop)
        {
            _session = coop.Net;
            _names = coop.Names;
            coop.Handle(MsgType.HostIds, OnHostIds);
            _session.OnPeerConnected += SendAllTo;
            coop.Hook(() => _session.OnPeerConnected -= SendAllTo);
        }

        private bool Hosting => _session.IsHost && Coop.Guards.Sharing;
        private bool Joined => Combat.NetEnemies.ClientMode
            && WorldShare.InWorld && !WorldShare.Loading;

        private static readonly
            AccessTools.FieldRef<SaveableObject, bool> InitializedRef =
                AccessTools.FieldRefAccess<SaveableObject, bool>("initialized");

        /// <summary>
        /// SaveableObject.init is about to run (HostIdPatches): whether it is
        /// to be watched, as it isn't initialized yet. False with no session.
        /// </summary>
        internal static bool BeforeInit(SaveableObject s)
        {
            return Coop.Current != null && !InitializedRef(s);
        }

        /// <summary>
        /// SaveableObject.init ran: if it initialized s now (watched, from
        /// BeforeInit), s started.
        /// </summary>
        internal static void AfterInit(SaveableObject s, bool watched)
        {
            CoopSession coop = Coop.Current;
            if (coop != null && watched && InitializedRef(s))
            {
                coop.HostIds.Started(s.gameObject);
            }
        }

        /// <summary>
        /// A saveable object started (SaveableObject.init ran for it).
        /// </summary>
        internal void Started(GameObject go)
        {
            if (go == null || Coop.Guards.Replaying || Core.loadingGame)
            {
                return;
            }

            if (Hosting || Joined)
            {
                _started.Add((go, Time.unscaledTime));
            }
        }

        public void Update()
        {
            float now = Time.unscaledTime;
            if (now >= _nextPrune)
            {
                _nextPrune = now + HostIdRules.PruneEvery;
                _names.PruneShared();
            }

            if (_started.Count == 0 && _pending.Count == 0)
            {
                return;
            }

            if (Hosting)
            {
                for (int i = 0; i < _started.Count; i++)
                {
                    var (go, at) = _started[i];
                    if (!HostIdRules.Settled(new Seconds(now - at)))
                    {
                        continue;
                    }

                    _started.RemoveAt(i--);
                    if (!HostIdRules.NeedsId(go != null, _names.Unnamed(go)))
                    {
                        continue;
                    }

                    long id = NewId();
                    _names.BindShared(id, go);
                    // where it settles from here on is sent (as announced: it
                    // may not have yet)
                    CoopSession coop = Coop.Current;
                    if (coop != null)
                    {
                        coop.Drags.Watch(go);
                    }

                    _outgoing.Add(
                        (id, go.name, go.transform.position, PlaceOf(go)));
                    Given++;
                }

                if (_outgoing.Count > 0)
                {
                    Send(_outgoing, 0, 0);
                    _outgoing.Clear();
                }

                return;
            }

            if (!Joined)
            {
                _started.Clear();
                return;
            }

            // Client: tie what the host announced to objects that started here
            // after the message.
            for (int i = 0; i < _pending.Count; i++)
            {
                var p = _pending[i];
                if (TryBind(p.id, p.name, p.pos, p.place))
                {
                    _pending.RemoveAt(i--);
                }
                else if (HostIdRules.Expired(
                    new Seconds(now),
                    new Seconds(p.until)))
                {
                    AddLate(p.id, p.name, p.pos, p.place);
                    _pending.RemoveAt(i--);
                }
            }

            // What started a while ago and is still unnamed: maybe the copy of
            // an id the host gave out long before.
            if (_late.Count > 0)
            {
                foreach (var (go, at) in _started)
                {
                    if (go != null
                        && HostIdRules.Settled(new Seconds(now - at))
                        && _lateChecked.Add(go))
                    {
                        TryBindLate(go);
                    }
                }
            }

            // Forget what started too long ago to be anyone's copy.
            _started.RemoveAll(
                s => s.go == null
                    || HostIdRules.Forgotten(new Seconds(now - s.at)));
            if (_started.Count == 0)
            {
                _lateChecked.Clear();
                _lateTied.Clear();
            }
        }

        private void AddLate(
            long id, string name, Vector3 pos, int place)
        {
            if (!_late.TryGetValue(name, out var list))
            {
                _late[name] = list = new List<(long, Vector3, int)>();
            }

            list.Add((id, pos, place));
        }

        /// <summary>
        /// Where an object stands in its hierarchy (each level's name and
        /// sibling index, up to its root): a hash that tells apart objects of
        /// the same name on the same spot (the home dream's bedroom door,
        /// Door1, has a second Door1 with its events at the same spot). A
        /// prefab spawned in every game gives the same one in each; elsewhere
        /// it may differ, so it only picks between objects that are equally
        /// near.
        /// </summary>
        internal static int PlaceOf(GameObject go)
        {
            var levels = new List<PlaceLevel>();
            for (var t = go.transform; t != null; t = t.parent)
            {
                levels.Add(
                    new PlaceLevel
                    {
                        Name = t.name,
                        Index = t.GetSiblingIndex(),
                    });
            }

            return StableHash.Of(HostIdRules.PlacePath(levels));
        }

        private static Vec2 GroundOf(Vector3 p)
        {
            return new Vec2(p.x, p.z);
        }

        private void TryBindLate(GameObject go)
        {
            if (!_late.TryGetValue(go.name, out var list)
                || !_names.Unnamed(go))
            {
                return;
            }

            var q = GroundOf(go.transform.position);
            var bestOne = default(Candidate);
            int best = -1, place = PlaceOf(go);
            for (int i = 0; i < list.Count; i++)
            {
                var (id, pos, p) = list[i];
                float d = HostIdRules.SquaredDistance(q, GroundOf(pos));
                if (!HostIdRules.IsLateCandidate(_names.IsBound(id), d))
                {
                    continue;
                }

                var one = new Candidate { Squared = d, SamePlace = p == place };
                if (best < 0 || HostIdRules.Better(one, bestOne))
                {
                    bestOne = one;
                    best = i;
                }
            }

            if (best < 0 || !_names.BindShared(list[best].id, go))
            {
                return;
            }

            _lateTied.Add(go);
            Tied++;
            Place(list[best].id, go);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[ids] tied {go.name} at {go.transform.position} to the "
                        + $"host's #{list[best].id} (given out a while ago)");
            }
        }

        /// <summary>
        /// What TryBind looks at in an object that started at at: whether it
        /// may be the copy of the id announced for name.
        /// </summary>
        private CopyFacts CopyOf(GameObject go, float at, string name)
        {
            if (go == null)
            {
                return default;
            }

            return new CopyFacts
            {
                Alive = true,
                Age = new Seconds(Time.unscaledTime - at),
                SameName = go.name == name,
                Unnamed = _names.Unnamed(go),
                LateTied = _lateTied.Contains(go),
            };
        }

        private bool TryBind(
            long id, string name, Vector3 pos, int place)
        {
            GameObject best = null;
            var bestOne = default(Candidate);
            var spot = GroundOf(pos);
            foreach (var (go, at) in _started)
            {
                if (!HostIdRules.MayBeCopy(CopyOf(go, at, name)))
                {
                    continue;
                }

                float d = HostIdRules.SquaredDistance(
                    GroundOf(go.transform.position),
                    spot);
                if (!HostIdRules.InSlack(d))
                {
                    continue;
                }

                var one = new Candidate
                {
                    Squared = d,
                    SamePlace = PlaceOf(go) == place,
                };

                if (best == null || HostIdRules.Better(one, bestOne))
                {
                    bestOne = one;
                    best = go;
                }
            }

            if (best == null)
            {
                return false;
            }

            // Tied a moment ago to an id from long before: the id the host
            // gives out now is the one for it.
            if (_lateTied.Remove(best))
            {
                _names.Unbind(best);
            }

            if (!_names.BindShared(id, best))
            {
                return false;
            }

            Tied++;
            Place(id, best);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[ids] tied {name} at {best.transform.position} to the "
                        + $"host's #{id}");
            }

            return true;
        }

        /// <summary>
        /// Client: our object was just tied to the host's id. The host decides
        /// where things stand, so a loose one (furniture, a box: physics
        /// settles each game's copy on its own, a few units apart) goes to
        /// where the host has it (DragSync.PutAtHost: not while it's dragged,
        /// carried or being moved by another game). Called by ObjRef too, when
        /// it ties an id it is asked for by the announced name and position.
        /// </summary>
        internal void Place(long id, GameObject go)
        {
            if (go == null || _session.IsHost
                || !_hostAt.TryGetValue(id, out var pos))
            {
                return;
            }

            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Drags.PutAtHost(go, pos);
            }
        }

        /// <summary>
        /// Client: the host sent where an object we haven't tied yet came to
        /// rest (ObjMove, last).
        /// </summary>
        internal void HostMoved(long id, Vector3 pos)
        {
            if (_hostAt.ContainsKey(id))
            {
                _hostAt[id] = pos;
            }
        }

        private void Send(
            List<(long id, string name, Vector3 pos, int place)> list,
            byte flags, ulong to)
        {
            var ids = new List<HostIdEntry>(list.Count);
            foreach (var e in list)
            {
                ids.Add(
                    new HostIdEntry
                    {
                        Id = e.id,
                        Name = e.name,
                        Pos = new Vec3(e.pos.x, e.pos.y, e.pos.z),
                        Place = e.place,
                    });
            }

            for (int start = 0; start < ids.Count; start += MaxPerMessage)
            {
                int n = Mathf.Min(MaxPerMessage, ids.Count - start);
                var w = _session.Begin(MsgType.HostIds);
                HostIdsMessage.Write(
                    w,
                    flags,
                    new HostIdSlice { List = ids, Start = start, Count = n });
                if (to == 0)
                {
                    _session.Broadcast(w, Channel.Control, reliable: true);
                }
                else
                {
                    _session.SendTo(to, w, Channel.Control, reliable: true);
                }
            }

            if (WorldActions.Debug)
            {
                string text;
                if (to == 0)
                {
                    text = "";
                }
                else
                {
                    text = $" to #{to} (made before it joined)";
                }

                Plugin.Log.LogInfo($"[ids] gave out {list.Count} ids{text}");
            }
        }

        /// <summary>
        /// Host: a joiner came in; its world has what was made before, so it
        /// gets every id given out.
        /// </summary>
        private void SendAllTo(ulong peer)
        {
            if (!_session.IsHost)
            {
                return;
            }

            var all = new List<(long, string, Vector3, int)>();
            foreach (var kv in _names.SharedObjects)
            {
                all.Add((kv.Key, kv.Value.name, kv.Value.transform.position,
                    PlaceOf(kv.Value)));
            }

            if (all.Count > 0)
            {
                Send(all, HostIdRules.MadeBefore, peer);
            }
        }

        private void OnHostIds(ulong from, PacketReader r)
        {
            if (_session.IsHost || from != _session.HostId)
            {
                return;
            }

            var ids = HostIdsMessage.Read(r, out byte flags);
            float until = Time.unscaledTime + Window;
            foreach (var e in ids)
            {
                long id = e.Id;
                string name = e.Name;
                var pos = new Vector3(e.Pos.X, e.Pos.Y, e.Pos.Z);
                int place = e.Place;
                _names.Announced(id, name, pos);
                _hostAt[id] = pos;
                // Made before this joiner came: its copy is in the save it
                // loaded, found by name and position when named.
                if (HostIdRules.GoesLate(flags))
                {
                    AddLate(id, name, pos, place);
                    continue;
                }

                if (!Joined || !TryBind(id, name, pos, place))
                {
                    _pending.Add((id, name, pos, place, until));
                }
            }
        }
    }
}
