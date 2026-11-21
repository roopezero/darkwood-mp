using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Nights;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// What a night event puts into the world: its GameEvent steps of type
    /// gameObject / spawn make a prefab at a target (a waypoint, a door, the
    /// location's root): the night mushrooms (expObj_nightMushroom_01), the
    /// lights (NightEvent_new_lights, Light_deadly_01_spawn), cockroaches,
    /// gore, cracks, messages on the floor, smoke, vortexes, and the holders of
    /// the event's later steps. A joiner plays the host's night event for its
    /// sounds and effects only (WorldEvents.PresentationOnly), so none of these
    /// were made there.
    ///
    /// Host: a night event fires (CustomEvent.fire); the copy of each of its
    /// GameEvents that RandomEvent.fire makes under the hideout is noted with
    /// the event's name, and each object one of its spawn steps makes is sent
    /// as NightObject (made): an id, the event, which GameEvents and step (so
    /// the joiner takes the prefab from its own copy of the event), where, and
    /// what it was made under. A joiner makes it there and names it by that id.
    /// When the host's object is gone (picked up, removed by a later step, gone
    /// at dawn), the joiners' copies go too.
    /// A player let in later gets the live ones as "made before": its copy in
    /// the save it loaded (a saveable one, such as a mushroom) is tied to the
    /// id by name and place, and one the save didn't keep is made.
    /// Characters a step makes are left to NetEnemies; the objects' own scripts
    /// run in each game as the scripted events' rules have them
    /// (ScriptedEvents: triggers the world sets off are the host's, and the id
    /// lets the host name the object in EventFired).
    /// </summary>
    internal sealed class NightObjects
    {
        private const byte Made = NightObjectRules.Made;
        private const byte Gone = NightObjectRules.Gone;
        private const byte MadeBefore = NightObjectRules.MadeBefore;
        private const byte NoTrap = NightObjectRules.NoTrap;
        private sealed class Tag
        {
            public string Scenario, Event;
            public int Group, Step;
            public byte Armed = NoTrap;
        }

        private readonly ConditionalWeakTable<GameEvent, Tag> _tags =
            new ConditionalWeakTable<GameEvent, Tag>();
        private sealed class Live
        {
            public long Id;
            public GameObject Go;
            public string Name;
            public Tag Tag;
            public Vector3 Pos;
            public Quaternion Rot;
            public ObjRef Parent;
            public byte Armed = NoTrap;
        }

        private readonly List<Live> _live = new List<Live>();
        private readonly List<(byte kind, long id, Tag tag,
            Vector3 pos, Quaternion rot, ObjRef parent)> _incoming =
                new List<(byte, long, Tag, Vector3, Quaternion, ObjRef)>();
        private readonly List<(GameObject go, byte armed, Seconds at)>
            _rearm = new List<(GameObject, byte, Seconds)>();
        private readonly CoopSession _coop;
        private Seconds _nextCheck;
        private readonly System.Random _rng = new System.Random();

        /// <summary>
        /// The night events firing now (CustomEvent.fire), the newest on top.
        /// </summary>
        private readonly Stack<CustomEvent> _firing = new Stack<CustomEvent>();

        /// <summary>
        /// The GameEvent steps whose coroutines run now, the newest on top.
        /// </summary>
        private readonly Stack<GameEvent> _steps = new Stack<GameEvent>();

        public NightObjects(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.NightObject, OnMessage);
            coop.Net.OnPeerConnected += SendLiveTo;
            coop.Hook(() => coop.Net.OnPeerConnected -= SendLiveTo);
        }

        /// <summary>
        /// For the map test: how many objects the host sent this session.
        /// </summary>
        public int SentCount { get; private set; }

        private Session S => _coop.Net;

        /// <summary>The night event firing now, or null.</summary>
        private CustomEvent Firing
        {
            get
            {
                if (_firing.Count == 0)
                {
                    return null;
                }

                return _firing.Peek();
            }
        }

        /// <summary>
        /// CustomEvent.fire starts: the copies of its GameEvents are known
        /// while it runs.
        /// </summary>
        internal void Fires(CustomEvent ev)
        {
            _firing.Push(ev);
        }

        /// <summary>
        /// The CustomEvent.fire that Fires was told of ended.
        /// </summary>
        internal void Fired()
        {
            if (_firing.Count > 0)
            {
                _firing.Pop();
            }
        }

        /// <summary>
        /// A GameEvent step's coroutine runs (its spawns are made inside it).
        /// </summary>
        internal void StepRuns(GameEvent step)
        {
            _steps.Push(step);
        }

        /// <summary>
        /// The step that StepRuns was told of stopped running.
        /// </summary>
        internal void StepDone()
        {
            if (_steps.Count > 0)
            {
                _steps.Pop();
            }
        }

        private bool HostSharing =>
            NightObjectRules.Sends(HostOf(S));
        /// <summary>
        /// Hosting, with players in or not yet: what's made before a player
        /// comes is sent when they come.
        /// </summary>
        private bool Hosting =>
            NightObjectRules.Records(HostOf(S));

        private Seconds Now => new Seconds(Time.unscaledTime);

        /// <summary>
        /// What NightObjectRules knows of the session and the world.
        /// </summary>
        private NightHostFacts HostOf(Session s)
        {
            if (s == null)
            {
                return default;
            }

            return new NightHostFacts
            {
                HasSession = true,
                IsHost = s.IsHost,
                Replaying = Coop.Guards.Replaying,
                InWorld = WorldShare.InWorld,
                Sharing = Coop.Guards.Sharing,
            };
        }

        // ---- host -----------------------------------------------------------

        /// <summary>
        /// Core.AddPrefab made <paramref name="go"/> from
        /// <paramref name="prefab"/>, under <paramref name="parent"/>.
        /// </summary>
        internal void Added(
            Object prefab, GameObject go, GameObject parent)
        {
            if (go == null || prefab == null)
            {
                return;
            }

            var step = NightObjectRules.Added(FactsOf(prefab, go));
            if (step == AddedStep.Tag)
            {
                // RandomEvent.fire's copy of one of the firing night event's
                // GameEvents: tag its steps.
                TagSteps(prefab, go.GetComponent<GameEvents>());
                return;
            }

            // An enemy is left to NetEnemies.
            if (step != AddedStep.Record)
            {
                return;
            }

            _tags.TryGetValue(_steps.Peek(), out var tag);
            long id = ((long)_rng.Next() << 31) | (uint)_rng.Next();
            ObjRef.RegisterSpawned(id, go);
            var live = new Live
            {
                Id = id,
                Go = go,
                Name = go.name,
                Tag = tag,
                Pos = go.transform.position,
                Rot = go.transform.rotation
            };

            if (parent != null)
            {
                live.Parent = ObjRef.Of(parent);
            }

            _live.Add(live);
            if (HostSharing)
            {
                Send(Made, live, 0);
            }

            SentCount++;
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent night object {go.name} #{id} ({tag.Event} "
                        + $"step {tag.Group}/{tag.Step}) at {live.Pos}");
            }
        }

        /// <summary>
        /// What NightObjectRules knows of what Core.AddPrefab made: the only
        /// code that reads the firing event, the running step and the made
        /// object for it. Nothing else is read while we don't record.
        /// </summary>
        private AddedFacts FactsOf(Object prefab, GameObject go)
        {
            var facts = new AddedFacts { Made = true, Records = Hosting };
            if (!facts.Records)
            {
                return facts;
            }

            RandomEvent re = null;
            if (Firing != null)
            {
                re = Firing.theEvent;
            }

            if (re != null && re.type == RandomEvent.Type.locationEvent)
            {
                int g = GroupOf(re, prefab);
                facts = facts with
                {
                    LocationEvent = true,
                    InGroup = g >= 0,
                    CopyHasEvents = g >= 0
                        && go.GetComponent<GameEvents>() != null,
                };
            }

            GameEvent step = null;
            if (_steps.Count > 0)
            {
                step = _steps.Peek();
            }

            if (step == null)
            {
                return facts;
            }

            var c = go.GetComponent<Character>();
            return facts with
            {
                HasStep = true,
                SpawnStep = step.type == GameEvent.Type.gameObject
                    && step.gameObjectModifyType
                        == GameEvent.GameObjectModify.spawn,
                TargetIsPrefab = step.targetTransform != null
                    && (Object)step.targetTransform.gameObject == prefab,
                Tagged = _tags.TryGetValue(step, out _),
                Character = c != null,
                HasNpc = c != null && c.npc != null,
            };
        }

        /// <summary>
        /// Which of the night event's GameEvents prefab is; -1 for none.
        /// </summary>
        private int GroupOf(RandomEvent re, Object prefab)
        {
            return re.gameEvents.FindIndex(
                x => x != null && (Object)x.gameObject == prefab);
        }

        /// <summary>
        /// Tag the steps of RandomEvent.fire's copy of one of the firing
        /// night event's GameEvents, so what they make is known.
        /// </summary>
        private void TagSteps(Object prefab, GameEvents ge)
        {
            int g = GroupOf(Firing.theEvent, prefab);
            var ns = Singleton<NightScenarios>.Instance;
            string sc = "";
            if (ns != null && ns.currentScenario != null)
            {
                sc = ns.currentScenario.name;
            }

            for (int s = 0; s < ge.events.Count; s++)
            {
                if (ge.events[s] != null)
                {
                    _tags.Remove(ge.events[s]);
                    _tags.Add(ge.events[s], new Tag
                    {
                        Scenario = sc,
                        Event = Firing.gameObject.name,
                        Group = g,
                        Step = s
                    });
                }
            }
        }

        private void Send(byte kind, Live l, ulong to)
        {
            var w = S.Begin(MsgType.NightObject);
            var night = new NightObjectBody { Kind = kind, Id = l.Id };
            if (kind != Gone)
            {
                l.Armed = ArmedOf(l.Go);
                night = night with
                {
                    Scenario = l.Tag.Scenario,
                    Event = l.Tag.Event,
                    Group = l.Tag.Group,
                    Step = l.Tag.Step,
                    Pos = new Vec3(l.Pos.x, l.Pos.y, l.Pos.z),
                    Rot = new Quat(l.Rot.x, l.Rot.y, l.Rot.z, l.Rot.w),
                    Parent = ObjRef.NameOf(l.Parent),
                    Armed = l.Armed,
                };
            }

            NightObjectMessage.Write(w, night);

            if (to == 0)
            {
                S.Broadcast(w, Channel.Control, reliable: true);
            }
            else
            {
                S.SendTo(to, w, Channel.Control, reliable: true);
            }
        }

        /// <summary>
        /// Host: a player came in; it gets the night's objects that are still
        /// there.
        /// </summary>
        private void SendLiveTo(ulong peer)
        {
            if (!Hosting)
            {
                return;
            }

            _live.RemoveAll(l => l.Go == null);
            foreach (var l in _live)
            {
                Send(MadeBefore, l, peer);
            }

            if (WorldActions.Debug && _live.Count > 0)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent {_live.Count} night objects to #{peer} "
                        + "(made before it came)");
            }
        }

        public void Update()
        {
            if (S.IsHost)
            {
                if (!NightObjectRules.CheckDue(_live.Count, Now, _nextCheck))
                {
                    return;
                }

                _nextCheck = NightObjectRules.NextCheck(Now);
                for (int i = _live.Count - 1; i >= 0; i--)
                {
                    var check = NightObjectRules.Check(FactsOf(_live[i]));
                    if (check == LiveStep.Keep)
                    {
                        continue;
                    }

                    // Its trap armed later (a mushroom in the hideout is, a
                    // while after it's made): sent again.
                    if (check == LiveStep.Resend)
                    {
                        Send(Made, _live[i], 0);
                        if (WorldActions.Debug)
                        {
                            Plugin.Log.LogInfo(
                                $"[world] night object {_live[i].Name} "
                                    + $"#{_live[i].Id}: its trap armed "
                                    + $"{_live[i].Armed == 1} now, sent");
                        }

                        continue;
                    }

                    if (check == LiveStep.SendGone)
                    {
                        Send(Gone, _live[i], 0);
                    }

                    if (WorldActions.Debug)
                    {
                        Plugin.Log.LogInfo(
                            $"[world] night object {_live[i].Name} "
                                + $"#{_live[i].Id} is gone");
                    }

                    _live.RemoveAt(i);
                }
            }

            for (int i = _rearm.Count - 1; i >= 0; i--)
            {
                Trigger trap = null;
                if (_rearm[i].go != null)
                {
                    trap = _rearm[i].go.GetComponent<Trigger>();
                }

                var rearm = new RearmFacts
                {
                    Now = Now,
                    At = _rearm[i].at,
                    HasTrap = trap != null,
                    Active = trap != null && trap.active,
                    Armed = _rearm[i].armed,
                };

                var step = NightObjectRules.Rearm(rearm);
                if (step == RearmStep.Wait)
                {
                    continue;
                }

                if (step == RearmStep.Set)
                {
                    if (WorldActions.Debug)
                    {
                        Plugin.Log.LogInfo(
                            $"[world] night object {trap.name}: its trap armed "
                                + $"{_rearm[i].armed == 1} again, as the "
                                + "host's");
                    }

                    trap.active = _rearm[i].armed == 1;
                }

                _rearm.RemoveAt(i);
            }

            bool now = NightObjectRules.AppliesNow(
                WorldShare.WorldReady, NetEnemies.ClientMode);
            if (_incoming.Count > 0 && now)
            {
                var list = _incoming.ToList();
                _incoming.Clear();
                foreach (var m in list)
                {
                    Apply(m.kind, m.id, m.tag, m.pos, m.rot, m.parent);
                }
            }
        }

        // ---- client ---------------------------------------------------------

        private void OnMessage(ulong from, PacketReader r)
        {
            if (S.IsHost || from != S.HostId)
            {
                return;
            }

            NightObjectBody night = NightObjectMessage.Read(r);
            byte kind = night.Kind;
            long id = night.Id;
            Tag tag = null;
            Vector3 pos = default;
            Quaternion rot = Quaternion.identity;
            ObjRef parent = default;
            if (kind != Gone)
            {
                tag = new Tag
                {
                    Scenario = night.Scenario,
                    Event = night.Event,
                    Group = night.Group,
                    Step = night.Step,
                    Armed = night.Armed,
                };

                pos = new Vector3(night.Pos.X, night.Pos.Y, night.Pos.Z);
                rot = new Quaternion(
                    night.Rot.X, night.Rot.Y, night.Rot.Z, night.Rot.W);
                parent = ObjRef.RefOf(night.Parent);
            }

            // While our world loads (a player let in), they wait for it.
            if (NightObjectRules.AppliesNow(
                WorldShare.WorldReady, NetEnemies.ClientMode))
            {
                Apply(kind, id, tag, pos, rot, parent);
            }
            else
            {
                _incoming.Add((kind, id, tag, pos, rot, parent));
            }
        }

        private void Apply(
            byte kind, long id, Tag tag, Vector3 pos, Quaternion rot,
            ObjRef parent)
        {
            ObjRef.Spawned.TryGetValue(id, out var have);
            GameObject prefab = null;
            if (kind != Gone && have == null)
            {
                prefab = Prefab(tag);
            }

            var step =
                NightObjectRules.Apply(kind, have != null, prefab != null);
            if (step == ApplyStep.Destroy || step == ApplyStep.Forget)
            {
                if (step == ApplyStep.Destroy)
                {
                    Coop.Guards.Replaying = true;
                    try
                    {
                        Object.Destroy(have);
                    }
                    finally
                    {
                        Coop.Guards.Replaying = false;
                    }
                }

                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] night object #{id} gone (the host's)");
                }

                return;
            }

            // Made already: only its trap's state.
            if (step == ApplyStep.ArmOnly)
            {
                Arm(have, tag.Armed);
                return;
            }

            if (step == ApplyStep.NoPrefab)
            {
                Plugin.Log.LogWarning(
                    $"No night event step {tag.Scenario}/{tag.Event} "
                        + $"{tag.Group}/{tag.Step} here to make its object");
                return;
            }

            if (step == ApplyStep.MatchSaved)
            {
                // Saved with the world we loaded: ours from the save, by name
                // and place.
                var best = SavedAt(prefab.name, pos);
                if (best != null)
                {
                    ObjRef.RegisterSpawned(id, best);
                    Arm(best, tag.Armed);
                    if (WorldActions.Debug)
                    {
                        Plugin.Log.LogInfo(
                            $"[world] night object {best.name} #{id}: ours "
                                + "from the save");
                    }

                    return;
                }
            }

            Transform under = null;
            if (parent.K != ObjRef.Kind.None)
            {
                under = ObjRef.Resolve<Transform>(parent);
            }

            GameObject go;
            Coop.Guards.Replaying = true;
            try
            {
                GameObject parentGO = null;
                if (under != null)
                {
                    parentGO = under.gameObject;
                }

                go = Core.AddPrefab(
                    prefab, pos, rot, parentGO, worldSpace: true);
                if (go != null)
                {
                    ObjRef.RegisterSpawned(id, go);
                    Arm(go, tag.Armed);
                    Location loc = null;
                    if (under != null)
                    {
                        loc = go.transform.GetLocation();
                    }

                    if (loc != null)
                    {
                        loc.addToObjects(go); // as GameEvent.fire does
                    }
                    else
                    {
                        WorldGrid instance = Singleton<WorldGrid>.Instance;
                        if (instance != null)
                        {
                            instance.registerToNode(go);
                        }
                    }
                }
            }
            finally
            {
                Coop.Guards.Replaying = false;
            }

            if (go == null)
            {
                return;
            }

            if (WorldActions.Debug)
            {
                string text = "";
                if (kind == MadeBefore)
                {
                    text = " (made before we came)";
                }

                Plugin.Log.LogInfo(
                    $"[world] made the host's night object {go.name} #{id} "
                        + $"({tag.Event}) at {pos}{text}");
            }
        }

        /// <summary>
        /// The object saved with the world we loaded, named so, nearest pos
        /// (within ObjRef.PlacedSlack) and not tied to a host's id yet; null
        /// for none.
        /// </summary>
        private GameObject SavedAt(string name, Vector3 pos)
        {
            var sm = Singleton<SaveManager>.Instance;
            if (sm == null)
            {
                return null;
            }

            var found = new List<GameObject>();
            var saved = new List<SavedObject>();
            foreach (var t in sm.dynamicObjects)
            {
                if (t == null || t.name != name)
                {
                    continue;
                }

                found.Add(t.gameObject);
                saved.Add(new SavedObject
                {
                    Name = t.name,
                    Spawned =
                        ObjRef.Of(t.gameObject).K == ObjRef.Kind.Spawned,
                    Pos = VecOf(t.position),
                });
            }

            int best = NightObjectRules.NearestSaved(saved, name, VecOf(pos));
            if (best < 0)
            {
                return null;
            }

            return found[best];
        }

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        /// <summary>
        /// What NightObjectRules knows of a live object: the only code that
        /// reads it for Check.
        /// </summary>
        private LiveFacts FactsOf(Live l)
        {
            return new LiveFacts
            {
                Alive = l.Go != null,
                Sending = HostSharing,
                Sent = l.Armed,
                Now = ArmedOf(l.Go),
            };
        }

        /// <summary>
        /// Whether its Trigger (a mushroom is one) is armed: 1 yes, 0 no, 255
        /// it has none.
        /// </summary>
        private byte ArmedOf(GameObject go)
        {
            Trigger trap = null;
            if (go != null)
            {
                trap = go.GetComponent<Trigger>();
            }

            bool active = trap != null && trap.active;
            return NightObjectRules.ArmedOf(trap != null, active);
        }

        private void Arm(GameObject go, byte armed)
        {
            Trigger trap = null;
            if (NightObjectRules.Arms(armed))
            {
                trap = go.GetComponent<Trigger>();
            }

            if (trap == null)
            {
                return;
            }

            trap.active = armed == NightObjectRules.Armed;
            // And again once its own Start has run. Only the newest state is
            // kept: a "made before" (not armed yet) and the host's "armed now"
            // can come within the same second, and the older one must not win.
            _rearm.RemoveAll(x => x.go == go);
            _rearm.Add((go, armed, NightObjectRules.RearmAt(Now)));
        }

        private GameObject Prefab(Tag tag)
        {
            var ns = Singleton<NightScenarios>.Instance;
            NightScenario sc = null;
            if (ns != null)
            {
                sc = ns.getScenario(tag.Scenario);
            }

            CustomEvent ev = null;
            if (sc != null)
            {
                ev = WorldEvents.FindEvent(sc, tag.Event);
            }

            if (ev == null && ns != null)
            {
                foreach (var other in ns.scenarios)
                {
                    if (other != null)
                    {
                        ev = WorldEvents.FindEvent(other, tag.Event);
                    }
                    else
                    {
                        ev = null;
                    }

                    if (ev != null)
                    {
                        break;
                    }
                }
            }

            RandomEvent re = null;
            if (ev != null)
            {
                re = ev.theEvent;
            }

            if (re == null || tag.Group >= re.gameEvents.Count
                || re.gameEvents[tag.Group] == null)
            {
                return null;
            }

            var steps = re.gameEvents[tag.Group].events;
            if (tag.Step >= steps.Count || steps[tag.Step] == null
                || steps[tag.Step].targetTransform == null)
            {
                return null;
            }

            return steps[tag.Step].targetTransform.gameObject;
        }
    }

    /// <summary>
    /// Host: the night event firing now, so the copies of its GameEvents are
    /// known.
    /// </summary>
    [HarmonyPatch(typeof(CustomEvent), nameof(CustomEvent.fire))]
    static class NightObjectsFiringPatch
    {
        static void Prefix(CustomEvent __instance, out NightObjects __state)
        {
            __state = null;
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                __state = coop.Nights;
                __state.Fires(__instance);
            }
        }

        static void Finalizer(NightObjects __state)
        {
            if (__state != null)
            {
                __state.Fired();
            }
        }
    }

    /// <summary>
    /// The GameEvent step whose coroutine runs now (its spawns are made inside
    /// it).
    /// </summary>
    [HarmonyPatch]
    static class NightObjectsStepPatch
    {
        private static readonly Dictionary<System.Type, FieldInfo> _this =
            new Dictionary<System.Type, FieldInfo>();

        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var t in typeof(GameEvent).GetNestedTypes(
                BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (t.Name.StartsWith("<fire>")
                    && typeof(System.Collections.IEnumerator).IsAssignableFrom(
                        t))
                {
                    var m = AccessTools.Method(t, "MoveNext");
                    var f = AccessTools.Field(t, "<>4__this");
                    if (m != null && f != null)
                    {
                        _this[t] = f;
                        yield return m;
                    }
                }
            }
        }

        static void Prefix(object __instance, out NightObjects __state)
        {
            __state = null;
            CoopSession coop = Coop.Current;
            if (coop == null
                || !_this.TryGetValue(__instance.GetType(), out var f))
            {
                return;
            }

            __state = coop.Nights;
            __state.StepRuns(f.GetValue(__instance) as GameEvent);
        }

        static System.Exception Finalizer(
            NightObjects __state, System.Exception __exception)
        {
            if (__state != null)
            {
                __state.StepDone();
            }

            return __exception;
        }
    }

    /// <summary>Host: what a night event's step made.</summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[]
    {
        typeof(Object),
        typeof(Vector3),
        typeof(Quaternion),
        typeof(GameObject),
        typeof(bool)
    })]
    static class NightObjectsAddPatch
    {
        static void Postfix(
            Object prefab, GameObject parentGO,
            GameObject __result)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Nights.Added(prefab, __result, parentGO);
            }
        }
    }
}
