using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Named steps for the two-game test (tools/run-test.sh map):
    /// tools/tests/mapdriver.py sends "do STEP ARGS..." to the host's or the
    /// client's test control, and the game runs the step's coroutine here and
    /// answers "done [INFO]" or "fail REASON" (also "fail timeout" when it runs
    /// past its limit).
    ///
    /// To add a step:
    /// <c>Add("name", (args, r) => MyStep(args, r), timeout: 20f)</c> with
    /// <c>IEnumerator MyStep(string[] args, Result r)</c>. Call
    /// <c>r.Fail("why")</c> and <c>yield break</c> when it can't be done; set
    /// <c>r.Info</c> to say what it did (it's printed by the driver). Do what
    /// the local player does inside <c>AsPlayer(() => ...)</c>, so a client's
    /// game treats it as its player's action and asks the host. Walk to a thing
    /// first with <c>Walk(pos)</c>: the host refuses actions from out of reach,
    /// and a jump would be corrected by its movement check. Things are picked
    /// nearest to <see cref="Centre"/>, the test area's centre, which is the
    /// same spot in both games (the driver sends the host's to the client with
    /// "do centre X Z"), so both pick the same door or container.
    /// </summary>
    public static partial class MapScenario
    {
        public sealed class Result
        {
            public string Failure, Info;
            public void Fail(string why)
            {
                if (Failure == null)
                {
                    Failure = why;
                }
            }
        }

        public delegate IEnumerator Step(string[] args, Result r);
        private static readonly Dictionary<string, (Step run, float timeout)>
            _steps = new Dictionary<string, (Step, float)>();

        public static void Add(string name, Step step, float timeout = 20f) =>
            _steps[name] = (step, timeout);

        /// <summary>
        /// The test area's centre: set by "centre X Z", else TestWorld's, else
        /// where our player stands.
        /// </summary>
        public static Vector3 Centre
        {
            get
            {
                Vector3? centre = _centre;
                if (centre.HasValue)
                {
                    return centre.Value;
                }

                return StateDump.DefaultCentre();
            }
        }

        private static Vector3? _centre;
        public static void SetCentre(Vector3 at) =>
            _centre = new Vector3(at.x, 0f, at.z);
        public static void ResetCentre()
        {
            _centre = null;
            Home = null;
        }

        /// <summary>
        /// The first centre the driver gave this game (the host's test area),
        /// which mt-home returns to.
        /// </summary>
        public static Vector3? Home { get; private set; }

        /// <summary>
        /// The levels of the step running now (the step's enumerator and the
        /// ones it yielded), and its result.
        /// </summary>
        private static Stack<IEnumerator> _running;
        private static Result _runningResult;

        /// <summary>
        /// Run a step to its end or its time limit, then reply "done [info]" /
        /// "fail reason". A step's nested enumerators (Walk, TakeOut...) are
        /// stepped here rather than handed to Unity, so the time limit, the
        /// exception catch and r.Fail apply inside them too; anything else a
        /// level yields (null, WaitForSeconds, a Coroutine...) goes up to Unity
        /// as before. Every level is disposed at the end, however the step
        /// ends, so the steps' finally blocks run.
        /// </summary>
        public static IEnumerator Run(
            string name, string[] args, Action<string> reply)
        {
            if (!_steps.TryGetValue(name, out var s))
            {
                reply(
                    $"fail unknown step {name} (known: "
                        + $"{string.Join(", ", _steps.Keys.OrderBy(k => k))})");
                yield break;
            }

            var r = new Result();
            float until = Time.realtimeSinceStartup + s.timeout;
            IEnumerator e;
            try
            {
                e = s.run(args, r);
            }
            catch (Exception ex)
            {
                reply($"fail {ex.GetType().Name}: {ex.Message}");
                yield break;
            }

            var stack = new Stack<IEnumerator>();
            stack.Push(e);
            _running = stack;
            _runningResult = r;
            try
            {
                while (r.Failure == null)
                {
                    var top = stack.Peek();
                    bool more;
                    try
                    {
                        more = top.MoveNext();
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[map] {name}: {ex}");
                        r.Fail($"{ex.GetType().Name}: {ex.Message}");
                        break;
                    }

                    if (r.Failure != null)
                    {
                        break;
                    }

                    if (!more)
                    {
                        DisposeLevel(stack.Pop(), name);
                        if (stack.Count == 0)
                        {
                            break;
                        }

                        continue;
                    }

                    if (Time.realtimeSinceStartup > until)
                    {
                        r.Fail("timeout");
                        break;
                    }

                    if (top.Current is IEnumerator sub)
                    {
                        stack.Push(sub);
                        continue;
                    }

                    yield return top.Current;
                }
            }
            finally
            {
                while (stack.Count > 0)
                {
                    DisposeLevel(stack.Pop(), name);
                }

                if (_running == stack)
                {
                    _running = null;
                    _runningResult = null;
                }
            }

            Plugin.Instance.Host.TestAsPlayer = false;
            string line = "done";
            if (r.Failure != null)
            {
                line = $"fail {r.Failure}";
            }
            else if (r.Info != null)
            {
                line = $"done {r.Info}";
            }

            // One line: the driver reads one reply per step, and an exception's
            // message can hold a line break (ArgumentNullException's
            // "Parameter name: ..."), which put every later reply one step
            // behind.
            line = line.Replace("\r", " ").Replace("\n", " ");
            Plugin.Log.LogInfo(
                $"[map] {name} {string.Join(" ", args)}: {line}");
            reply(line);
        }

        private static void DisposeLevel(IEnumerator level, string name)
        {
            try
            {
                IDisposable disposable = level as IDisposable;
                if (disposable != null)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[map] {name}: while ending it: {ex}");
            }
        }

        /// <summary>
        /// Plugin.StopTests: end the running step now (its finally blocks run);
        /// it replies "fail stopped".
        /// </summary>
        internal static void Stop()
        {
            var stack = _running;
            if (stack == null)
            {
                return;
            }

            Result runningResult = _runningResult;
            if (runningResult != null)
            {
                runningResult.Fail("stopped");
            }

            while (stack.Count > 0)
            {
                DisposeLevel(stack.Pop(), "stop");
            }

            _running = null;
            _runningResult = null;
        }

        // ---- helpers --------------------------------------------------------

        /// <summary>
        /// Do this as our player's own action (a client asks the host for it).
        /// </summary>
        public static void AsPlayer(Action a)
        {
            Plugin.Instance.Host.TestAsPlayer = true;
            try
            {
                a();
            }
            finally
            {
                Plugin.Instance.Host.TestAsPlayer = false;
            }
        }

        // under MoveCheck.MaxSpeed, so the host doesn't correct it
        public const float WalkSpeed = 600f;

        /// <summary>
        /// The time a scripted walk moves our player for in one frame: the
        /// frame's, but at most a fortieth of a second. The walks put the
        /// player at the next spot each frame (Place), so a slow frame (a
        /// place's things coming into view) was one long jump, and the physics
        /// saw the player only after it, already well inside an area trigger.
        /// EventTriggers.OnTriggerEnter skips an entry when the player has been
        /// in the area before and is already standing on it (its check against
        /// counting one entry twice), so walking back into the Markings
        /// sometimes didn't darken. A player moved by the game's physics
        /// touches the edge first, and isn't skipped.
        /// </summary>
        public static float FrameTime => Mathf.Min(Time.deltaTime, 1f / 40f);

        /// <summary>
        /// Move our player in a straight line (by a spot beside any armed trap
        /// on it) until within `within` of the spot (x, z).
        /// </summary>
        public static IEnumerator Walk(
            Vector3 to, float within = 120f, int detours = 2)
        {
            var p = Player.Instance;
            if (p == null)
            {
                yield break;
            }

            // Round the armed traps on the way (not one by where we're going):
            // walking through the station's chain trap caught the host, which
            // then couldn't get anywhere, and sprang a trap a later step was to
            // use.
            Vector3? detour = null;
            if (detours > 0)
            {
                detour = Detour(p.transform.position, to, within);
            }

            if (detour.HasValue)
            {
                yield return Walk(detour.Value, 20f, detours - 1);
            }

            while (true)
            {
                var at = p.transform.position;
                var d = new Vector3(to.x - at.x, 0f, to.z - at.z);
                if (d.magnitude <= within)
                {
                    yield break;
                }

                Place(
                    at + Vector3.ClampMagnitude(
                        d,
                        Mathf.Min(
                            WalkSpeed * FrameTime,
                            d.magnitude - within * 0.5f)));
                yield return null;
            }
        }

        /// <summary>
        /// Put our player at (x, z) at once, at the height it stands at, its
        /// rigidbody too.
        /// </summary>
        internal static void Place(Vector3 to)
        {
            var p = Player.Instance;
            to.y = p.transform.position.y;
            p.transform.position = to;
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = to;
            }
        }

        private const float TrapClearance = 110f;

        /// <summary>A spot to walk by first so the straight line from `from` to
        /// `to` stays TrapClearance from the nearest armed trap on it (null:
        /// none in the way). Traps within reach of where we're going are left
        /// alone.</summary>
        private static Vector3? Detour(Vector3 from, Vector3 to, float within)
        {
            var seg = new Vector3(to.x - from.x, 0f, to.z - from.z);
            float len = seg.magnitude;
            if (len < 1f)
            {
                return null;
            }

            var dir = seg / len;
            Trigger worst = null;
            float worstT = float.MaxValue;
            Vector3 side = Vector3.zero;
            foreach (var t in Object.FindObjectsOfType<Trigger>())
            {
                if (t == null || !t.active || t.triggered)
                {
                    continue;
                }

                var tp = t.transform.position;
                if (StateDump.Flat(tp - to) < within + TrapClearance
                    || StateDump.Flat(tp - from) < TrapClearance)
                {
                    continue;
                }

                var rel = new Vector3(tp.x - from.x, 0f, tp.z - from.z);
                float along = Vector3.Dot(rel, dir);
                if (along <= 0f || along >= len)
                {
                    continue;
                }

                var perp = rel - dir * along;
                if (perp.magnitude >= TrapClearance || along >= worstT)
                {
                    continue;
                }

                worst = t;
                worstT = along;
                // to the side away from it
                if (perp.sqrMagnitude < 1f)
                {
                    side = new Vector3(-dir.z, 0f, dir.x);
                }
                else
                {
                    side = -perp.normalized;
                }
            }

            if (worst == null)
            {
                return null;
            }

            var w = worst.transform.position + side * (TrapClearance * 1.6f);
            w.y = from.y;
            return w;
        }

        public static T Nearest<T>(IEnumerable<T> things, Vector3 to)
            where T : Component =>
            things.Where(t => t != null)
                .OrderBy(t => StateDump.Flat(t.transform.position - to))
                .FirstOrDefault();

        internal static float F(string s) =>
            float.Parse(s, CultureInfo.InvariantCulture);
        internal static string Fmt(Vector3 p) =>
            FormattableString.Invariant($"{p.x:0} {p.z:0}");

        /// <summary>
        /// Everything our player carries: the bags and the hotbar.
        /// </summary>
        internal static IEnumerable<InvItemClass> Carried(Player p) =>
            p.Inventory.getAllItems()
                .Concat(
                    p.Hotbar.slots.Where(sl =>
                        !InvItemClass.isNull(sl.invItem)).Select(
                            sl => sl.invItem));

        /// <summary>
        /// Our player back to full health, flames put out, effects and bleeding
        /// stopped, out of a bear trap.
        /// </summary>
        internal static void HealNow()
        {
            var p = Player.Instance;
            if (p == null || !p.alive)
            {
                return;
            }

            foreach (var b in p.GetComponentsInChildren<Burn>())
            {
                if (b != null)
                {
                    b.stop();
                }
            }

            if (p.effects != null)
            {
                p.effects.removeAllEffects();
            }

            p.bleeding = false;
            if (p.inBearTrap)
            {
                p.stopAllPerformingActionAnims();
            }

            p.setHealth(p.maxHealth);
        }

        // ---- the starter steps ----------------------------------------------

        static MapScenario()
        {
            // "where": the test area's centre here (the host's is sent to the
            // client with "centre").
            Add("where", (a, r) => Info(r, Fmt(Centre)));
            // "centre X Z": pick things nearest to this spot from now on.
            Add("centre", (a, r) =>
            {
                if (a.Length < 2)
                {
                    r.Fail("usage: centre X Z");
                    return None();
                }

                _centre = new Vector3(F(a[0]), 0f, F(a[1]));
                if (Home == null)
                {
                    Home = _centre;
                }

                return Info(r, Fmt(_centre.Value));
            });

            // "wait SECS": just wait.
            Add("wait", (a, r) =>
            {
                float seconds = 1f;
                if (a.Length > 0)
                {
                    seconds = F(a[0]);
                }

                return Wait(seconds);
            }, timeout: 120f);

            // "goto X Z": walk there.
            Add(
                "goto", (a, r) => Walk(new Vector3(F(a[0]), 0f, F(a[1])), 60f),
                timeout: 60f);
            Add("toggle-door", ToggleDoor);
            Add("take-item", TakeItem);
            Add("drop-item", DropItem);
            Add("time", Time_);
            Add("heal", Heal);
            Add("alive", Alive);
            // The rest are in the MapScenario*.cs files.
            MapScenarioWorld.Register();
            MapScenarioIssues.Register();
            MapScenarioStatus.Register();
            MapScenarioFixChecks.Register();
            MapScenarioWhispers.Register();
            MapScenarioGaps.Register();
            MapScenarioLocations.Register();
            MapScenarioDreams.Register();
            MapScenarioPrologue.Register();
            MapScenarioEpilogue.Register();
            MapScenarioDice.Register();
            MapScenarioSkills.Register();
            MapScenarioEnemies.Register();
            MapScenarioItems1.Register();
            MapScenarioNpcs.Register();
            MapScenarioFarWorld.Register();
            MapScenarioFar.Register();
            MapScenarioHostileNpcs.Register();
            MapScenarioItems2.Register();
            MapScenarioFarEvents.Register();
            MapScenarioSweep.Register();
            MapScenarioMulti.Register();
            MapScenarioNight.Register();
            MapScenarioDogWatch.Register();
            MapScenarioUi.Register();
            MapScenarioSavage.Register();
            MapScenarioKnock.Register();
            MapScenarioPush.Register();
            MapScenarioRepair.Register();
            MapScenarioWbRows.Register();
            MapScenarioLights.Register();
            MapScenarioShortcut.Register();
            MapScenarioView.Register();
            MapScenarioCorpse.Register();
            MapScenarioChapter.Register();
            MapScenarioLater.Register();
            MapScenarioGenerator.Register();
            MapScenarioDive.Register();
            MapScenarioBasement.Register();
            MapScenarioStory.Register();
            MapScenarioStory2.Register();
            MapScenarioClips.Register();
        }

        internal static IEnumerator None()
        {
            yield break;
        }

        internal static IEnumerator Info(Result r, string info)
        {
            r.Info = info;
            yield break;
        }

        private static IEnumerator Wait(float s)
        {
            yield return new WaitForSeconds(s);
        }
    }
}
