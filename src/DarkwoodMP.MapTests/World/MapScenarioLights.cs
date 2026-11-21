using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for the lights the others' puppets carry
    /// (Sync.RemotePlayer: a torch or lamp in hand, a hotbar lantern, the
    /// flashlight's beam) and for our own view: each Light2D draws into a mesh
    /// of its own, so what our player sees (its view cone, its light dot, its
    /// beam) is worked out where our player stands, not where a puppet does.
    /// Steps: "lt-hold TYPE|fists", "lt-check torch|lantern|flashlight|none".
    /// Listed in tools/tests/map_scenario_lights.py.
    /// </summary>
    public static class MapScenarioLights
    {
        private static readonly
            HarmonyLib.AccessTools.FieldRef<Player, Light2D> FovLight =
                HarmonyLib.AccessTools.FieldRefAccess<Player, Light2D>(
                    "FOVLight");
        private static readonly
            HarmonyLib.AccessTools.FieldRef<Player, Light2D> FovDot =
                HarmonyLib.AccessTools.FieldRefAccess<Player, Light2D>(
                    "FOVDot");
        private static readonly
            HarmonyLib.AccessTools.FieldRef<Player, Light2D> OwnBeam =
                HarmonyLib.AccessTools.FieldRefAccess<Player, Light2D>(
                    "Flashlight");

        public static void Register()
        {
            Add("lt-hold", Hold, timeout: 20f);
            Add("lt-check", Check, timeout: 15f);
        }

        /// <summary>
        /// "lt-hold TYPE": TYPE taken out of the hotbar into our hands;
        /// "lt-hold fists": an empty hotbar slot selected.
        /// </summary>
        private static IEnumerator Hold(string[] a, Result r)
        {
            var p = Player.Instance;
            string type = "fists";
            if (a.Length > 0)
            {
                type = a[0];
            }

            if (type != "fists")
            {
                yield return MapScenarioEnemies.TakeOut(p, type, r);
            }
            else
            {
                InvSlot slot = p.Hotbar.slots.FirstOrDefault(
                    s => InvItemClass.isNull(s.invItem));
                if (slot == null)
                {
                    slot = p.Hotbar.slots.FirstOrDefault(s => !s.selected);
                }

                if (slot == null)
                {
                    r.Fail("no hotbar slot");
                    yield break;
                }

                slot.inventory = p.Hotbar;
                if (!InvItemClass.isNull(slot.invItem))
                {
                    slot.removeItem();
                }

                foreach (var s in p.Hotbar.slots)
                {
                    s.deselect();
                }

                slot.select(noiseless: true);
                float until = Time.realtimeSinceStartup + 4f;
                while ((!InvItemClass.isNull(p.currentItem)
                            && p.currentItem != p.fists
                        || p.switchingItem)
                    && Time.realtimeSinceStartup < until)
                {
                    yield return null;
                }
            }

            yield return new WaitForSeconds(0.5f);
            var beam = OwnBeam(p);
            string heldType;
            if (InvItemClass.isNull(p.currentItem))
            {
                heldType = "nothing";
            }
            else
            {
                heldType = p.currentItem.type;
            }

            string text = "off";
            if (beam != null && beam.gameObject.activeSelf)
            {
                text = $"on, radius {beam.LightRadius:0}";
            }

            string emitter = "none";
            if (p.itemLightEmitter != null)
            {
                emitter = "on";
            }

            r.Info = $"holding {heldType}; our beam {text}, " +
                     $"held light {emitter}";
        }

        /// <summary>
        /// "lt-check WHAT" (the other side): some puppet shows WHAT (torch: the
        /// held item's light; lantern: a lantern light; flashlight: its beam
        /// on, at the radius its player's game sent), or with "none", no puppet
        /// shows any light.
        /// Always: no Light2D on our player or on a puppet draws into another
        /// light's mesh, and our view light (Player.FOVLight) is on our player
        /// and drawn from a mesh no other light writes.
        /// </summary>
        private static IEnumerator Check(string[] a, Result r)
        {
            string what = "none";
            if (a.Length > 0)
            {
                what = a[0];
            }

            float until = Time.realtimeSinceStartup + 8f;
            string seen = "", shared = "";
            bool ok = false;
            while (true)
            {
                Sync.PlayerSync instance = Plugin.Instance.Host.Players;
                List<Sync.RemotePlayer> puppets = null;
                if (instance != null)
                {
                    puppets = instance.Remotes.Values.Where(
                        x => x.Owner != Sync.PlayerSync.DummyId
                            && x.Root != null).ToList();
                }

                if (puppets == null)
                {
                    puppets = new List<Sync.RemotePlayer>();
                }

                seen = string.Join("; ", puppets.Select(Describe));
                bool any(System.Func<Sync.RemotePlayer, bool> f) =>
                    puppets.Any(f);
                switch (what)
                {
                    case "torch":
                        ok = any(x => Drawn(x.HeldLightShown));
                        break;
                    case "lantern":
                        ok = any(
                            x => x.LanternLight != null
                                && x.LanternLight.LightRadius > 130f
                                && Drawn(x.LanternLight));
                        break;
                    case "flashlight":
                        ok = any(x =>
                        {
                            Light2D l = null;
                            if (x.Beam != null && x.Beam.activeSelf)
                            {
                                l = x.Beam.GetComponent<Light2D>();
                            }

                            return l != null && x.Last != null
                                && x.Last.BeamRadius > 0f
                                && Mathf.Abs(l.LightRadius
                                    - x.Last.BeamRadius) < 1f && Drawn(l);
                        });

                        break;
                    default:
                        ok = !any(
                            x => Drawn(x.HeldLightShown)
                                || x.LanternLight != null
                                || (x.Beam != null && x.Beam.activeSelf));
                        break;
                }

                shared = SharedMeshes();
                if ((ok && shared == "") || Time.realtimeSinceStartup > until)
                {
                    break;
                }

                yield return new WaitForSeconds(0.3f);
            }

            var view = ViewSource();
            string text;
            if (seen == "")
            {
                text = "none";
            }
            else
            {
                text = seen;
            }

            r.Info = $"{what}: puppets {text}; {view}";
            if (!ok)
            {
                r.Fail($"no puppet shows {what} as wanted: {r.Info}");
            }
            else if (shared != "")
            {
                r.Fail($"lights share a mesh: {shared}; {r.Info}");
            }
            else if (!view.StartsWith("view: ours"))
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// A light that is on and has drawn something (its mesh has triangles).
        /// </summary>
        private static bool Drawn(Light2D l)
        {
            if (l == null || !l.gameObject.activeInHierarchy || l._mesh == null)
            {
                return false;
            }

            var f = l.GetComponent<MeshFilter>();
            return f != null && f.sharedMesh == l._mesh
                && l._mesh.vertexCount > 2;
        }

        private static string Describe(Sync.RemotePlayer x)
        {
            var parts = new List<string>();
            var held = x.HeldLightShown;
            if (held != null)
            {
                Sync.PlayerSnapshot snapshot = x.Last;
                string heldLight = null;
                if (snapshot != null)
                {
                    heldLight = snapshot.HeldLight;
                }

                parts.Add(
                    $"{heldLight} light r{held.LightRadius:0} drawn "
                        + $"{Drawn(held)}");
            }

            if (x.LanternLight != null)
            {
                parts.Add(
                    $"lantern r{x.LanternLight.LightRadius:0} drawn "
                        + $"{Drawn(x.LanternLight)}");
            }

            if (x.Beam != null && x.Beam.activeSelf)
            {
                var l = x.Beam.GetComponent<Light2D>();
                float lightRadius = 0f;
                if (l != null)
                {
                    lightRadius = l.LightRadius;
                }

                Sync.PlayerSnapshot last = x.Last;
                float beamRadius = 0f;
                if (last != null)
                {
                    beamRadius = last.BeamRadius;
                }

                float grey = 0f;
                if (l != null)
                {
                    grey = l.LightColor.r;
                }

                parts.Add(
                    $"beam r{lightRadius:0} (sent {beamRadius:0}) grey "
                        + $"{grey:0.00} drawn {Drawn(l)}");
            }

            string listing;
            if (parts.Count == 0)
            {
                listing = "no lights";
            }
            else
            {
                listing = string.Join(", ", parts);
            }

            return $"{x.Name}: {listing}";
        }

        /// <summary>
        /// Lights on our player or a puppet whose mesh another light also has
        /// ("" if none).
        /// </summary>
        private static string SharedMeshes()
        {
            var p = Player.Instance;
            var lights = Resources.FindObjectsOfTypeAll<Light2D>()
                .Where(l => l != null && l.gameObject.scene.IsValid()).ToList();
            var byMesh = new Dictionary<Mesh, List<Light2D>>();
            foreach (var l in lights)
            {
                Mesh mesh = l._mesh;
                MeshFilter filter = l.GetComponent<MeshFilter>();
                Mesh filterMesh = null;
                if (filter != null)
                {
                    filterMesh = filter.sharedMesh;
                }

                foreach (var m in new[] { mesh, filterMesh })
                {
                    if (m == null)
                    {
                        continue;
                    }

                    if (!byMesh.TryGetValue(m, out var list))
                    {
                        byMesh[m] = list = new List<Light2D>();
                    }

                    if (!list.Contains(l))
                    {
                        list.Add(l);
                    }
                }
            }

            bool mine(Light2D l) => (p != null && l.transform.IsChildOf(
                p.transform)) || HasPuppetParent(l.transform);
            var bad = byMesh.Where(
                kv => kv.Value.Count > 1 && kv.Value.Any(mine))
                .Select(
                    kv => string.Join(
                        " + ",
                        kv.Value.Select(
                            l => Sync.ViewWatch.Path(l.transform))));
            return string.Join("; ", bad);
        }

        private static bool HasPuppetParent(Transform t)
        {
            for (var x = t; x != null; x = x.parent)
            {
                if (x.name.StartsWith("DarkwoodMP_Remote_"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Where our view is drawn from: "view: ours ..." when Player.FOVLight,
        /// FOVDot and lightDot are on our player with meshes of their own.
        /// </summary>
        internal static string ViewSource()
        {
            var p = Player.Instance;
            if (p == null)
            {
                return "view: no player";
            }

            var parts = new List<string>();
            bool ours = true;
            foreach (var (name, l) in new[]
            {
                ("cone", FovLight(p)),
                ("dot", FovDot(p)),
                ("light dot", p.lightDot)
            })
            {
                if (l == null)
                {
                    parts.Add($"{name} missing");
                    continue;
                }

                bool onUs = l.transform.IsChildOf(p.transform);
                var f = l.GetComponent<MeshFilter>();
                bool own =
                    l._mesh == null || (f != null && f.sharedMesh == l._mesh);
                ours &= onUs && own;
                float d =
                    StateDump.Flat(l.transform.position - p.transform.position);
                string text;
                if (onUs)
                {
                    text = "on our player";
                }
                else
                {
                    text = "on " + Sync.ViewWatch.Path(l.transform);
                }

                string meshNote = "not its own";
                if (own)
                {
                    meshNote = "its own";
                }

                parts.Add($"{name} {text} {d:0} from it, mesh {meshNote}");
            }

            string whose = "NOT ours";
            if (ours)
            {
                whose = "ours";
            }

            return $"view: {whose} ({string.Join(", ", parts)})";
        }
    }
}
