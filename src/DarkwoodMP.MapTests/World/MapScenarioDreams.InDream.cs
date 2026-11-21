using System.Collections;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioDreams
    {
        /// <summary>
        /// "dream-chars" (either): the dream location's characters and events,
        /// logged (for reading a run).
        /// </summary>
        private static IEnumerator DreamChars(string[] a, Result r)
        {
            float seconds = 1f;
            if (a.Length > 0)
            {
                seconds = float.Parse(
                    a[0], System.Globalization.CultureInfo.InvariantCulture);
            }

            yield return new WaitForSeconds(seconds);
            var loc = D.dreamLocation;
            if (loc == null)
            {
                r.Fail("no dream location");
                yield break;
            }

            var ne = Combat.NetEnemies.Instance;
            var sb = new System.Text.StringBuilder();
            sb.Append(
                $"dream {loc.name} at {Fmt(loc.transform.position)}, us at "
                    + $"{Fmt(P.transform.position)}, hostRunsHere "
                    + $"{Locations.HostRunsHere()}; ");
            foreach (var c in Object.FindObjectsOfType<Character>(true))
            {
                if (StateDump.Flat(
                    c.transform.position - loc.transform.position) > 15000f)
                {
                    continue;
                }

                uint id = 0;
                bool net = ne != null
                    && (ne.NetIdOf(c, out id) || ne.IsPuppet(c, out id));
                string line =
                    $"\n  {c.name} npc {(c.npc != null)} dummy {c.dummy} elig "
                        + $"{Combat.NetEnemies.Eligible(c)} act "
                        + $"{c.gameObject.activeInHierarchy} en {c.enabled} "
                        + $"alive {c.alive} ";
                string spot = Fmt(c.transform.position);
                string target = "-";
                if (c.target != null)
                {
                    target = c.target.name;
                }

                string state =
                    $"at {spot} tgt {target} net {net}#{id} aggr "
                        + $"{c.aggressive} sleep {c.sleeping} ";
                var scripts = c.GetComponents<MonoBehaviour>().Select(m =>
                {
                    if (m == null)
                    {
                        return "?";
                    }

                    return m.GetType().Name;
                });

                sb.Append(line + state + $"comps {string.Join(",", scripts)}");
            }

            foreach (var ge in loc.GetComponentsInChildren<GameEvents>(true))
            {
                sb.Append(
                    $"\n  events {ge.name} act "
                        + $"{ge.gameObject.activeInHierarchy}");
            }

            r.Info = sb.ToString();
            Plugin.Log.LogInfo("[dream-mob] " + r.Info);
        }

        /// <summary>
        /// The dream location's characters (within a location's reach of its
        /// marker), active or not.
        /// </summary>
        private static System.Collections.Generic.IEnumerable<Character>
            DreamCharacters() =>
            Object.FindObjectsOfType<Character>(true).Where(
                c => D.dreamLocation != null && Combat.NetEnemies.Eligible(c)
                    && StateDump.Flat(
                        c.transform.position
                            - D.dreamLocation.transform.position) < 15000f);

        /// <summary>
        /// "dream-wardrobe" (client): in the church dream, our player closes
        /// the burnt wardrobe, which lets its chomper out
        /// (spawnChomper_dream_church_ruins_01). The host runs the event and
        /// the chomper; we wait until the host's chomper is shown here.
        /// </summary>
        private static IEnumerator DreamWardrobe(string[] a, Result r)
        {
            var loc = D.dreamLocation;
            EventTriggers et = null;
            if (loc != null)
            {
                et = loc.GetComponentsInChildren<EventTriggers>(true)
                    .FirstOrDefault(
                        x => x.name.StartsWith("Wardrobe_Big_1_Burned"));
            }

            if (et == null)
            {
                string name = null;
                if (loc != null)
                {
                    name = loc.name;
                }

                r.Fail($"no burnt wardrobe in {name}");
                yield break;
            }

            var p = P;
            // Near it, on a spot with no loose thing (a bowl, the draggable
            // wardrobe) our player would land on and push.
            var away = p.transform.position - et.transform.position;
            away.y = 0f;
            Vector3 dir;
            if (away.sqrMagnitude > 1f)
            {
                dir = away.normalized;
            }
            else
            {
                dir = Vector3.right;
            }

            var at = et.transform.position + dir * 250f;
            for (int i = 0; i < 16; i++)
            {
                var spot = et.transform.position + Quaternion.Euler(
                    0f, 22.5f * i, 0f) * dir * 250f;
                spot.y = p.transform.position.y;
                if (Physics.OverlapSphere(spot, 100f).Any(
                    col => col.attachedRigidbody != null
                        && !col.attachedRigidbody.isKinematic
                        && col.GetComponentInParent<Player>() == null))
                {
                    continue;
                }

                at = spot;
                break;
            }

            p.transform.position =
                new Vector3(at.x, p.transform.position.y, at.z);
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = p.transform.position;
            }

            yield return new WaitForSeconds(0.5f);
            AsPlayer(
                () => et.fireEventTrigger(
                    global::EventTrigger.Type.onCloseContainer));
            var ne = Combat.NetEnemies.Instance;
            float until = Time.realtimeSinceStartup + 15f;
            Character shown = null;
            while (shown == null && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.5f);
                shown = DreamCharacters().FirstOrDefault(
                    c => c.name.StartsWith("ChomperBlack")
                        && c.gameObject.activeInHierarchy
                        && ne.IsPuppet(c, out _));
            }

            string wardrobe = et.name;
            string there = Fmt(et.transform.position);
            string here = Fmt(p.transform.position);
            string seen = "no";
            if (shown != null)
            {
                seen = Fmt(shown.transform.position);
            }

            r.Info =
                $"closed {wardrobe} at {there} from {here}; the host's chomper "
                    + $"shown: {seen}";
            if (shown == null)
            {
                r.Fail($"the host's chomper isn't shown here: {r.Info}");
            }
        }

        /// <summary>
        /// "dream-mob" (host): the dream's chomper is out and running in our
        /// game, sent to the client (a net id), and goes for someone by threat;
        /// it bites for 1, so the comparison isn't cut short by a death.
        /// </summary>
        private static IEnumerator DreamMobHost(string[] a, Result r)
        {
            var ne = Combat.NetEnemies.Instance;
            Character c = null;
            float until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until)
            {
                c = DreamCharacters().FirstOrDefault(
                    x => x.name.StartsWith("ChomperBlack") && x.alive
                        && x.gameObject.activeInHierarchy
                        && ne.NetIdOf(x, out _));
                if (c != null && c.target != null)
                {
                    break;
                }

                yield return new WaitForSeconds(0.25f);
            }

            if (c == null)
            {
                r.Fail("no chomper of the dream running here");
                yield break;
            }

            foreach (var s in c.sensorTypes)
            {
                if (s != null)
                {
                    s.damage = 1;
                }
            }

            var proxy = Combat.NetPlayerProxy.All.Values.FirstOrDefault(
                x => x != null && x.Owner != Sync.PlayerSync.DummyId);
            ne.NetIdOf(c, out uint id);
            string chomper = c.name;
            string spot = Fmt(c.transform.position);
            string target = "none";
            if (c.target != null)
            {
                target = c.target.name;
            }

            bool? away = null;
            if (proxy != null)
            {
                away = proxy.Away;
            }

            string inside = "-";
            if (proxy != null && proxy.InLocation != null)
            {
                inside = proxy.InLocation.name;
            }

            string threat = "";
            if (proxy != null)
            {
                threat = $", threat {Combat.Threat.Of(c).Get(proxy):0.#}";
            }

            r.Info = $"{chomper} #{id} at {spot}, target {target}; " +
                     $"the client's stand-in: away {away}, in {inside}" +
                     threat;
            if (proxy == null || proxy.Away
                || proxy.InLocation != D.dreamLocation)
            {
                r.Fail($"the client isn't in the dream we run: {r.Info}");
            }
            else if (c.target == null)
            {
                r.Fail($"the chomper goes for nobody: {r.Info}");
            }
        }

        /// <summary>
        /// "dream-mob-copies" (client): none of our own dream characters runs;
        /// the host's are the ones shown.
        /// </summary>
        private static IEnumerator DreamMobCopies(string[] a, Result r)
        {
            yield return new WaitForSeconds(0.5f);
            var ne = Combat.NetEnemies.Instance;
            var all = DreamCharacters().ToList();
            int puppets = all.Count(
                c => ne.IsPuppet(c, out _) && c.gameObject.activeInHierarchy);
            var running = all.Where(
                c => !ne.IsPuppet(c, out _) && c.gameObject.activeInHierarchy
                    && !Combat.NetEnemies.Frozen.Contains(c)).ToList();
            string line =
                $"host runs it here {Locations.HostRunsHere()}; the host's "
                    + $"shown {puppets}; ours running {running.Count}";
            string names = "";
            if (running.Count > 0)
            {
                names = $" ({string.Join(", ", running.Select(c => c.name))})";
            }

            r.Info = line + names;
            if (!Locations.HostRunsHere() || puppets == 0 || running.Count > 0)
            {
                r.Fail($"the dream's characters aren't the host's: {r.Info}");
            }
        }

        /// <summary>
        /// "dream-entered" (any game, run in all at once with
        /// "host+client..."): once we're dreaming, when our game went into the
        /// dream ("entered T", Unix seconds); the driver's "max_spread" checks
        /// the games went in together.
        /// </summary>
        private static IEnumerator DreamEntered(string[] a, Result r)
        {
            while (!D.dreaming)
            {
                yield return new WaitForSeconds(0.25f);
            }

            r.Info = System.FormattableString.Invariant(
                $"entered {DreamSync.EnteredAt:0.000}");
        }
    }
}
