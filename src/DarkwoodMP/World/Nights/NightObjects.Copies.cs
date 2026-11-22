using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Rolls;
using UnityEngine;

namespace DarkwoodMP.World.Nights
{
    /// <summary>
    /// NightObjects in a joiner's game: the host's night objects made, armed
    /// and removed here.
    /// </summary>
    internal sealed partial class NightObjects
    {
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
}
