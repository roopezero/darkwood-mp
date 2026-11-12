using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// What a night event puts into the world: its GameEvent steps of type gameObject / spawn make a prefab at a
    /// target (a waypoint, a door, the location's root): the night mushrooms (expObj_nightMushroom_01), the lights
    /// (NightEvent_new_lights, Light_deadly_01_spawn), cockroaches, gore, cracks, messages on the floor, smoke,
    /// vortexes, and the holders of the event's later steps. A joiner plays the host's night event for its sounds
    /// and effects only (WorldEvents.PresentationOnly), so none of these were made there.
    ///
    /// Host: a night event fires (CustomEvent.fire); the copy of each of its GameEvents that RandomEvent.fire makes
    /// under the hideout is noted with the event's name, and each object one of its spawn steps makes is sent as
    /// NightObject (made): an id, the event, which GameEvents and step (so the joiner takes the prefab from its own
    /// copy of the event), where, and what it was made under. A joiner makes it there and names it by that id. When
    /// the host's object is gone (picked up, removed by a later step, gone at dawn), the joiners' copies go too.
    /// A player let in later gets the live ones as "made before": its copy in the save it loaded (a saveable one,
    /// such as a mushroom) is tied to the id by name and place, and one the save didn't keep is made.
    /// Characters a step makes are left to NetEnemies; the objects' own scripts run in each game as the scripted
    /// events' rules have them (ScriptedEvents: triggers the world sets off are the host's, and the id lets the host
    /// name the object in EventFired).
    /// </summary>
    public static class NightObjects
    {
        private const byte Made = 1, Gone = 2, MadeBefore = 3;
        private sealed class Tag { public string Scenario, Event; public int Group, Step; public byte Armed = 255; }
        private static readonly ConditionalWeakTable<GameEvent, Tag> _tags = new ConditionalWeakTable<GameEvent, Tag>();
        private sealed class Live { public long Id; public GameObject Go; public string Name; public Tag Tag; public Vector3 Pos; public Quaternion Rot; public ObjRef Parent; public byte Armed = 255; }
        private static readonly List<Live> _live = new List<Live>();
        private static readonly List<(byte kind, long id, Tag tag, Vector3 pos, Quaternion rot, ObjRef parent)> _incoming = new List<(byte, long, Tag, Vector3, Quaternion, ObjRef)>();
        private static Session _session;
        private static float _nextCheck;
        private static readonly System.Random _rng = new System.Random();

        internal static CustomEvent Firing;
        internal static readonly Stack<GameEvent> Steps = new Stack<GameEvent>();

        /// <summary>For the map test: what the host sent and the joiner made.</summary>
        public static int SentCount, MadeCount;

        public static void Register(Session s)
        {
            _session = s;
            s.Register(MsgType.NightObject, OnMessage);
            s.OnPeerConnected += SendLiveTo;
            s.OnSessionEnded += () => { _live.Clear(); _incoming.Clear(); _rearm.Clear(); Firing = null; Steps.Clear(); };
        }

        private static bool HostSharing => _session != null && _session.IsHost && WorldActions.Sharing;
        /// <summary>Hosting, with players in or not yet: what's made before a player comes is sent when they come.</summary>
        private static bool Hosting => _session != null && _session.IsHost && !WorldActions.Replaying && WorldShare.InWorld;

        // ---- host --------------------------------------------------------------------------------------------

        /// <summary>Core.AddPrefab made <paramref name="go"/> from <paramref name="prefab"/>, under <paramref name="parent"/>.</summary>
        internal static void Added(Object prefab, GameObject go, GameObject parent)
        {
            if (go == null || prefab == null || !Hosting) return;
            // RandomEvent.fire's copy of one of the firing night event's GameEvents: tag its steps.
            var re = Firing != null ? Firing.theEvent : null;
            if (re != null && re.type == RandomEvent.Type.locationEvent)
            {
                int g = re.gameEvents.FindIndex(x => x != null && (Object)x.gameObject == prefab);
                var ge = g >= 0 ? go.GetComponent<GameEvents>() : null;
                if (ge != null)
                {
                    var ns = Singleton<NightScenarios>.Instance;
                    string sc = ns != null && ns.currentScenario != null ? ns.currentScenario.name : "";
                    for (int s = 0; s < ge.events.Count; s++)
                        if (ge.events[s] != null) { _tags.Remove(ge.events[s]); _tags.Add(ge.events[s], new Tag { Scenario = sc, Event = Firing.gameObject.name, Group = g, Step = s }); }
                    return;
                }
            }
            if (Steps.Count == 0) return;
            var step = Steps.Peek();
            if (step == null || step.type != GameEvent.Type.gameObject || step.gameObjectModifyType != GameEvent.GameObjectModify.spawn) return;
            if (step.targetTransform == null || (Object)step.targetTransform.gameObject != prefab || !_tags.TryGetValue(step, out var tag)) return;
            var c = go.GetComponent<Character>();
            if (c != null && c.npc == null) return; // an enemy: NetEnemies
            long id = ((long)_rng.Next() << 31) | (uint)_rng.Next();
            ObjRef.RegisterSpawned(id, go);
            var live = new Live { Id = id, Go = go, Name = go.name, Tag = tag, Pos = go.transform.position, Rot = go.transform.rotation, Parent = parent != null ? ObjRef.Of(parent) : default };
            _live.Add(live);
            if (HostSharing) Send(Made, live, 0);
            SentCount++;
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] sent night object {go.name} #{id} ({tag.Event} step {tag.Group}/{tag.Step}) at {live.Pos}");
        }

        private static void Send(byte kind, Live l, ulong to)
        {
            var w = _session.Begin(MsgType.NightObject);
            w.W.Write(kind);
            w.W.Write(l.Id);
            if (kind != Gone)
            {
                w.WriteStr(l.Tag.Scenario); w.WriteStr(l.Tag.Event);
                w.W.Write((byte)l.Tag.Group); w.W.Write((byte)l.Tag.Step);
                w.Write(l.Pos);
                w.W.Write(l.Rot.x); w.W.Write(l.Rot.y); w.W.Write(l.Rot.z); w.W.Write(l.Rot.w);
                l.Parent.Write(w);
                l.Armed = ArmedOf(l.Go);
                w.W.Write(l.Armed);
            }
            if (to == 0) _session.Broadcast(w, Channel.Control, reliable: true);
            else _session.SendTo(to, w, Channel.Control, reliable: true);
        }

        /// <summary>Host: a player came in; it gets the night's objects that are still there.</summary>
        private static void SendLiveTo(ulong peer)
        {
            if (!Hosting) return;
            _live.RemoveAll(l => l.Go == null);
            foreach (var l in _live) Send(MadeBefore, l, peer);
            if (WorldActions.Debug && _live.Count > 0) Plugin.Log.LogInfo($"[world] sent {_live.Count} night objects to #{peer} (made before it came)");
        }

        public static void Update()
        {
            if (_session == null) return;
            if (_session.IsHost)
            {
                if (_live.Count == 0 || Time.unscaledTime < _nextCheck) return;
                _nextCheck = Time.unscaledTime + 1f;
                for (int i = _live.Count - 1; i >= 0; i--)
                {
                    if (_live[i].Go != null)
                    {
                        // Its trap armed later (a mushroom in the hideout is, a while after it's made): sent again.
                        if (HostSharing && _live[i].Armed != 255 && ArmedOf(_live[i].Go) != _live[i].Armed)
                        {
                            Send(Made, _live[i], 0);
                            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] night object {_live[i].Name} #{_live[i].Id}: its trap armed {_live[i].Armed == 1} now, sent");
                        }
                        continue;
                    }
                    if (HostSharing) Send(Gone, _live[i], 0);
                    if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] night object {_live[i].Name} #{_live[i].Id} is gone");
                    _live.RemoveAt(i);
                }
            }
            for (int i = _rearm.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < _rearm[i].at) continue;
                var trap = _rearm[i].go != null ? _rearm[i].go.GetComponent<Trigger>() : null;
                if (trap != null && trap.active != (_rearm[i].armed == 1))
                {
                    if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] night object {trap.name}: its trap armed {_rearm[i].armed == 1} again, as the host's");
                    trap.active = _rearm[i].armed == 1;
                }
                _rearm.RemoveAt(i);
            }
            if (_incoming.Count > 0 && WorldShare.WorldReady && NetEnemies.ClientMode)
            {
                var list = _incoming.ToList();
                _incoming.Clear();
                foreach (var m in list) Apply(m.kind, m.id, m.tag, m.pos, m.rot, m.parent);
            }
        }

        // ---- client ------------------------------------------------------------------------------------------

        private static void OnMessage(ulong from, PacketReader r)
        {
            if (_session.IsHost || from != _session.HostId) return;
            byte kind = r.R.ReadByte();
            long id = r.R.ReadInt64();
            Tag tag = null; Vector3 pos = default; Quaternion rot = Quaternion.identity; ObjRef parent = default;
            if (kind != Gone)
            {
                tag = new Tag { Scenario = r.ReadStr(), Event = r.ReadStr(), Group = r.R.ReadByte(), Step = r.R.ReadByte() };
                pos = r.ReadVector3();
                rot = new Quaternion(r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle(), r.R.ReadSingle());
                parent = ObjRef.Read(r);
                tag.Armed = r.R.ReadByte();
            }
            // While our world loads (a player let in), they wait for it.
            if (WorldShare.WorldReady && NetEnemies.ClientMode) Apply(kind, id, tag, pos, rot, parent);
            else _incoming.Add((kind, id, tag, pos, rot, parent));
        }

        private static void Apply(byte kind, long id, Tag tag, Vector3 pos, Quaternion rot, ObjRef parent)
        {
            ObjRef.Spawned.TryGetValue(id, out var have);
            if (kind == Gone)
            {
                if (have != null)
                {
                    WorldActions.Replaying = true;
                    try { Object.Destroy(have); }
                    finally { WorldActions.Replaying = false; }
                }
                if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] night object #{id} gone (the host's)");
                return;
            }
            if (have != null) { Arm(have, tag.Armed); return; } // made already: only its trap's state
            var prefab = Prefab(tag);
            if (prefab == null) { Plugin.Log.LogWarning($"No night event step {tag.Scenario}/{tag.Event} {tag.Group}/{tag.Step} here to make its object"); return; }
            if (kind == MadeBefore)
            {
                // Saved with the world we loaded: ours from the save, by name and place.
                var sm = Singleton<SaveManager>.Instance;
                GameObject best = null; float bestD = ObjRef.PlacedSlack * ObjRef.PlacedSlack;
                if (sm != null)
                    foreach (var t in sm.dynamicObjects)
                    {
                        if (t == null || t.name != prefab.name || ObjRef.Of(t.gameObject).K == ObjRef.Kind.Spawned) continue;
                        float d = (t.position - pos).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = t.gameObject; }
                    }
                if (best != null)
                {
                    ObjRef.RegisterSpawned(id, best);
                    Arm(best, tag.Armed);
                    if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] night object {best.name} #{id}: ours from the save");
                    return;
                }
            }
            var under = parent.K != ObjRef.Kind.None ? ObjRef.Resolve<Transform>(parent) : null;
            GameObject go;
            WorldActions.Replaying = true;
            try
            {
                go = Core.AddPrefab(prefab, pos, rot, under != null ? under.gameObject : null, worldSpace: true);
                if (go != null)
                {
                    ObjRef.RegisterSpawned(id, go);
                    Arm(go, tag.Armed);
                    var loc = under != null ? go.transform.GetLocation() : null;
                    if (loc != null) loc.addToObjects(go); // as GameEvent.fire does
                    else Singleton<WorldGrid>.Instance?.registerToNode(go);
                }
            }
            finally { WorldActions.Replaying = false; }
            if (go == null) return;
            MadeCount++;
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] made the host's night object {go.name} #{id} ({tag.Event}) at {pos}{(kind == MadeBefore ? " (made before we came)" : "")}");
        }

        /// <summary>Whether its Trigger (a mushroom is one) is armed: 1 yes, 0 no, 255 it has none.</summary>
        private static byte ArmedOf(GameObject go)
        {
            var trap = go != null ? go.GetComponent<Trigger>() : null;
            return (byte)(trap == null ? 255 : trap.active ? 1 : 0);
        }

        private static void Arm(GameObject go, byte armed)
        {
            var trap = armed != 255 ? go.GetComponent<Trigger>() : null;
            if (trap == null) return;
            trap.active = armed == 1;
            // And again once its own Start has run. Only the newest state is kept: a "made before" (not armed yet)
            // and the host's "armed now" can come within the same second, and the older one must not win.
            _rearm.RemoveAll(x => x.go == go);
            _rearm.Add((go, armed, Time.unscaledTime + 1f));
        }
        private static readonly List<(GameObject go, byte armed, float at)> _rearm = new List<(GameObject, byte, float)>();

        private static GameObject Prefab(Tag tag)
        {
            var ns = Singleton<NightScenarios>.Instance;
            var sc = ns != null ? ns.getScenario(tag.Scenario) : null;
            var ev = sc != null ? WorldEvents.FindEvent(sc, tag.Event) : null;
            if (ev == null && ns != null)
                foreach (var other in ns.scenarios) { ev = other != null ? WorldEvents.FindEvent(other, tag.Event) : null; if (ev != null) break; }
            var re = ev != null ? ev.theEvent : null;
            if (re == null || tag.Group >= re.gameEvents.Count || re.gameEvents[tag.Group] == null) return null;
            var steps = re.gameEvents[tag.Group].events;
            if (tag.Step >= steps.Count || steps[tag.Step] == null || steps[tag.Step].targetTransform == null) return null;
            return steps[tag.Step].targetTransform.gameObject;
        }
    }

    /// <summary>Host: the night event firing now, so the copies of its GameEvents are known.</summary>
    [HarmonyPatch(typeof(CustomEvent), nameof(CustomEvent.fire))]
    static class NightObjectsFiringPatch
    {
        static void Prefix(CustomEvent __instance, out CustomEvent __state) { __state = NightObjects.Firing; NightObjects.Firing = __instance; }
        static void Finalizer(CustomEvent __state) => NightObjects.Firing = __state;
    }

    /// <summary>The GameEvent step whose coroutine runs now (its spawns are made inside it).</summary>
    [HarmonyPatch]
    static class NightObjectsStepPatch
    {
        private static readonly Dictionary<System.Type, FieldInfo> _this = new Dictionary<System.Type, FieldInfo>();

        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var t in typeof(GameEvent).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
                if (t.Name.StartsWith("<fire>") && typeof(System.Collections.IEnumerator).IsAssignableFrom(t))
                {
                    var m = AccessTools.Method(t, "MoveNext");
                    var f = AccessTools.Field(t, "<>4__this");
                    if (m != null && f != null) { _this[t] = f; yield return m; }
                }
        }

        static void Prefix(object __instance, out bool __state)
        {
            __state = _this.TryGetValue(__instance.GetType(), out var f);
            if (__state) NightObjects.Steps.Push(f.GetValue(__instance) as GameEvent);
        }

        static System.Exception Finalizer(bool __state, System.Exception __exception)
        {
            if (__state && NightObjects.Steps.Count > 0) NightObjects.Steps.Pop();
            return __exception;
        }
    }

    /// <summary>Host: what a night event's step made.</summary>
    [HarmonyPatch(typeof(Core), nameof(Core.AddPrefab), new[] { typeof(Object), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(bool) })]
    static class NightObjectsAddPatch
    {
        static void Postfix(Object prefab, GameObject parentGO, GameObject __result) => NightObjects.Added(prefab, __result, parentGO);
    }
}
