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
    public static class MapScenario
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
        private static readonly Dictionary<string, (Step run, float timeout)> _steps =
            new Dictionary<string, (Step, float)>();

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
                    $"fail unknown step {name} (known: {string.Join(", ", _steps.Keys.OrderBy(k => k))})");
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
            WorldActions.TestAsPlayer = false;
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
            WorldActions.TestAsPlayer = true;
            try
            {
                a();
            }
            finally
            {
                WorldActions.TestAsPlayer = false;
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

        public static T Nearest<T>(IEnumerable<T> things, Vector3 to) where T : Component =>
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
            MapScenarioDive.Register();
            MapScenarioStory.Register();
            MapScenarioStory2.Register();
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

        /// <summary>
        /// "toggle-door [open|close]": the door nearest the centre, opened or
        /// closed (toggled by default).
        /// </summary>
        private static IEnumerator ToggleDoor(string[] a, Result r)
        {
            var p = Player.Instance;
            var door = Nearest(
                Object.FindObjectsOfType<Door>().Where(
                    d => !d.destroyed && ObjRef.Of(d.gameObject).Valid),
                Centre);
            if (door == null)
            {
                r.Fail("no door");
                yield break;
            }
            yield return Walk(door.transform.position);
            bool open;
            if (a.Length > 0)
            {
                open = a[0] == "open";
            }
            else
            {
                open = !door.opened;
            }
            if (open != door.opened)
            {
                AsPlayer(() =>
                {
                    if (open)
                    {
                        door.open(p.transform.position, p.transform, 0f);
                    }
                    else
                    {
                        door.close(p.transform);
                    }
                });
            }
            yield return new WaitForSeconds(0.5f);
            r.Info =
                $"{door.name} {ObjRef.Of(door.gameObject)} open={door.opened}";
            if (door.opened != open)
            {
                r.Fail($"{door.name} is still open={door.opened}");
            }
        }

        /// <summary>"take-item [TYPE] [--locked]": the first item (of TYPE)
        /// from the unlocked container nearest the centre (or a locked one too,
        /// with --locked), into our bags.</summary>
        private static IEnumerator TakeItem(string[] a, Result r)
        {
            var p = Player.Instance;
            // a locked one too (the host should refuse it)
            bool locked = a.Contains("--locked");
            a = a.Where(x => x != "--locked").ToArray();
            string type = null;
            if (a.Length > 0)
            {
                type = a[0];
            }
            bool Has(InvSlot s) => !InvItemClass.isNull(s.invItem)
                && (type == null || s.invItem.type == type);
            var inv = Nearest(
                Object.FindObjectsOfType<Inventory>().Where(
                    i => i.invType == Inventory.InvType.itemInv
                        && !i.isWorkbench
                        && !i.name.StartsWith("DroppedItem") && i.slots.Any(Has)
                        && (locked
                            || !(i.GetComponentInParent<Locked>() is Locked l
                                && l.locked)) && i != p.Inventory
                        && i != p.Hotbar
                        && !i.transform.IsChildOf(p.transform)), Centre);
            if (inv == null)
            {
                string wanted = type;
                if (wanted == null)
                {
                    wanted = "anything";
                }
                r.Fail($"no container with {wanted} in it");
                yield break;
            }
            yield return Walk(inv.transform.position);
            var slot = inv.slots.First(Has);
            var item = new InvItemClass(slot.invItem);
            p.openedItemInventory = inv; // as if its window were open
            yield return new WaitForSeconds(0.6f);
            int Count() =>
                Carried(p).Where(i => i.type == item.type).Sum(i => i.amount);
            int before = Count();
            AsPlayer(() =>
            {
                slot.removeItem();
                p.Inventory.addItemTypeToPlayer(
                    item.type, item.amount, dropIfNoRoom: true);
            });
            if (Count() <= before)
            {
                r.Fail(
                    $"took {item.type} x{item.amount} from {inv.name} {ObjRef.Of(inv.gameObject)} but it isn't in our bags (full?)");
            }
            yield return new WaitForSeconds(0.6f);
            p.openedItemInventory = null;
            yield return new WaitForSeconds(0.5f);
            _lastTaken = item.type;
            r.Info =
                $"{item.type} x{item.amount} from {inv.name} {ObjRef.Of(inv.gameObject)}";
        }
        private static string _lastTaken;

        /// <summary>
        /// "drop-item [TYPE] [AMOUNT]": drop one of TYPE (the last taken by
        /// default) from our bags.
        /// </summary>
        private static IEnumerator DropItem(string[] a, Result r)
        {
            var p = Player.Instance;
            string type;
            if (a.Length > 0)
            {
                type = a[0];
            }
            else
            {
                type = _lastTaken;
            }
            int amount = 1;
            if (a.Length > 1)
            {
                amount = int.Parse(a[1]);
            }
            if (type == null)
            {
                r.Fail("nothing to drop (take-item first, or give a type)");
                yield break;
            }
            var carried = Carried(p).FirstOrDefault(i => i.type == type);
            if (carried == null)
            {
                r.Fail($"not carrying {type}");
                yield break;
            }
            var item = new InvItemClass(carried)
            {
                amount = Math.Min(amount, carried.amount)
            };
            Transform dropped = null;
            AsPlayer(() =>
            {
                p.Inventory.removeItemAmountFromPlayer(type, item.amount);
                dropped = p.spawnDroppedInvItem(item);
            });
            // dropped things are shared once they've come to rest
            yield return new WaitForSeconds(2.5f);
            string spot = "?";
            if (dropped != null)
            {
                spot = Fmt(dropped.position);
            }
            string id = "";
            if (dropped != null)
            {
                id = ObjRef.Of(dropped.gameObject).ToString();
            }
            r.Info = $"{type} x{amount} at {spot} {id}";
        }

        /// <summary>"heal": our player back to full health, flames put out,
        /// bleeding and other effects (infection's damage, poison) stopped, out
        /// of a bear trap. The scenario does this after steps that hurt, since
        /// a death changes the story flags and resets things the dumps
        /// compare.</summary>
        private static IEnumerator Heal(string[] a, Result r)
        {
            var p = Player.Instance;
            if (p == null)
            {
                r.Fail("no player");
                yield break;
            }
            if (!p.alive || p.health <= 0f)
            {
                r.Fail($"our player is dead (health {p.health:0})");
                yield break;
            }
            float was = p.health;
            HealNow();
            yield return new WaitForSeconds(0.3f);
            r.Info = $"health {was:0} -> {p.health:0} of {p.maxHealth:0}";
        }

        /// <summary>"alive": our player and every other player are alive, and
        /// nobody died in this game's session (the log's "X died" notices, and
        /// the host's "A death didn't ..." lines).</summary>
        private static IEnumerator Alive(string[] a, Result r)
        {
            var p = Player.Instance;
            float health = 0f;
            if (p != null)
            {
                health = p.health;
            }
            var parts = new List<string> { $"we: health {health:0}" };
            if (p == null || !p.alive || p.health <= 0f)
            {
                float playerHealth = 0f;
                if (p != null)
                {
                    playerHealth = p.health;
                }
                r.Fail($"our player is dead (health {playerHealth:0})");
            }
            var ps = Sync.PlayerSync.Instance;
            if (ps != null)
            {
                foreach (var rp in ps.Remotes.Values)
                {
                    if (rp.Owner == Sync.PlayerSync.DummyId)
                    {
                        continue;
                    }
                    var last = rp.Last;
                    string percent = "?";
                    if (last != null)
                    {
                        percent = last.Health.ToString();
                    }
                    parts.Add(
                        $"{rp.Name}: alive {rp.Alive}, health {percent}%");
                    if (!rp.Alive || (last != null && last.Health <= 0))
                    {
                        r.Fail($"{rp.Name} is dead");
                    }
                }
            }
            var lines = MapScenarioIssues.LogLines();
            int from = lines.FindLastIndex(l => l.Contains("World ready"));
            // (Not our own earlier "[map] alive" failures, which quote those
            // lines.)
            var deaths = lines.Skip(Math.Max(0, from))
                .Where(
                    l => !l.Contains("[map]")
                        && ((l.Contains("[notice]") && l.TrimEnd()
                                .EndsWith(" died"))
                            || l.Contains("A death didn't"))).ToList();
            if (deaths.Count > 0)
            {
                r.Fail(
                    $"deaths in the log: {string.Join(" | ", deaths.Select(l => l.Trim()))}");
            }
            r.Info = string.Join("; ", parts);
            if (r.Failure != null)
            {
                r.Failure += "; " + r.Info;
            }
            yield break;
        }

        /// <summary>
        /// "time +H": move the clock on H hours (the host's; everyone follows).
        /// </summary>
        private static IEnumerator Time_(string[] a, Result r)
        {
            var s = Net.Session.Instance;
            if (s != null && s.IsClient)
            {
                r.Fail("the clock is the host's");
                yield break;
            }
            var ctl = Singleton<Controller>.Instance;
            if (ctl == null || a.Length < 1)
            {
                r.Fail("usage: time +H");
                yield break;
            }
            float h = F(a[0].TrimStart('+'));
            TestWorld.SetTime(Mathf.Repeat(ctl.CurrentTime + h * 60f, 1440f));
            yield return new WaitForSeconds(1f);
            r.Info = $"day {ctl.day} {ctl.CurrentTime / 60f:0.00} h";
        }
    }
}
