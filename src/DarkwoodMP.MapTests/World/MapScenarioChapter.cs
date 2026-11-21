using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Chapters = DarkwoodMP.World.Story.Chapters;
using DarkwoodMP.World.Places;
using Object = UnityEngine.Object;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for the move from chapter 1 to chapter 2 with every
    /// player in (tools/tests/map_scenario_chapter.py, run in map4 with two or
    /// more client games): each client marks its hotbar with things of its own,
    /// the host's game starts chapter 2 as a chapter's GameEvent step does, and
    /// each client must come into the host's chapter 2 (its scene, its world,
    /// on the ground beside the host) with its own hotbar, not the host's.
    /// </summary>
    public static partial class MapScenarioChapter
    {
        private static Player P => Player.Instance;

        /// <summary>
        /// The hotbar this game's player had before the chapter
        /// ("chapter-mark"), kept across the world reload.
        /// </summary>
        private static string _mark;

        /// <summary>Our log's line count at "chapter-mark". The game keeps
        /// running between the tests of a full run, so the log also holds
        /// earlier tests' lines (host-chapter starts chapter 2 too); the checks
        /// here read only this run's.</summary>
        private static int _logFrom;

        /// <summary>
        /// Our log's lines since "chapter-mark", without the test's own "[map]"
        /// result lines (they quote other lines).
        /// </summary>
        private static System.Collections.Generic.List<string> Lines() =>
            MapScenarioIssues.LogLines().Skip(_logFrom)
                .Where(l => !l.Contains("[map]")).ToList();

        public static void Register()
        {
            // "chapter-mark N" (client): empty our hotbar and put things in it
            // that only this client has (N: 0, 1, 2...);
            // "chapter-mark keep": note our hotbar as it is.
            Add("chapter-mark", Mark);
            // "chapter-go [wait]" (host): start the next chapter (with "wait":
            // someone else sets it off); wait until our game is in it and
            // ready.
            Add("chapter-go", Go, timeout: 300f);
            // "chapter-fire NAME": fire the GameEvents NAME in our game, as our
            // player's doing (as its trigger does).
            Add("chapter-fire", Fire);
            // "chapter-in" (client): wait until we're in the host's new chapter
            // with our own character.
            Add("chapter-in", In, timeout: 560f);
            // "chapter-offmap" (client): join again with our kept place off the
            // map; we must be beside the host.
            Add("chapter-offmap", OffMap, timeout: 400f);
            // "chapter-all N" (host): N players in our new chapter, each beside
            // us, and chapter 2 started once.
            Add("chapter-all", All, timeout: 240f);
            // "chapter-own-dream [NAME]" (host): our player goes into a dream
            // of its own (NAME, not shared).
            Add("chapter-own-dream", OwnDream, timeout: 60f);
            // "chapter-pending" (client): the host told us the chapter we set
            // off waits for it (a notice), not refused.
            Add("chapter-pending", Pending, timeout: 30f);
            // "chapter-held" (host): a joiner's chapter is held while we dream:
            // no chapter starts yet.
            Add("chapter-held", HeldStep, timeout: 30f);
        }

        internal static string Hotbar(Player p) =>
            string.Join(",", p.Hotbar.slots.Select(s =>
            {
                if (InvItemClass.isNull(s.invItem))
                {
                    return "-";
                }

                return $"{s.invItem.type}x{s.invItem.amount}";
            }));

        private static string Types(string hotbar) =>
            string.Join(",", hotbar.Split(',').Select(s => s.Split('x')[0]));

        private static int Chapter => Chapters.HostChapter;

        /// The host's next chapter is being built (false with no session).
        private static bool Generating
        {
            get
            {
                CoopSession coop = Coop.Current;
                return coop != null && coop.Chapters.Generating;
            }
        }

        /// Our character in the host's world has come; false with no session.
        private static bool HasCharacter()
        {
            CoopSession coop = Coop.Current;
            return coop != null && coop.Characters.HasCharacter;
        }

        private static IEnumerator Mark(string[] a, Result r)
        {
            _logFrom = MapScenarioIssues.LogMark();
            var p = P;
            // A second argument: that many hotbar slots, taken as the game's
            // hotbar upgrade item gives one.
            int want = 0;
            if (a.Length > 1)
            {
                want = int.Parse(a[1]);
            }

            while (want > 0 && p.Hotbar.slots.Count < want)
            {
                p.upgradeHotbar();
            }

            if (a.Length > 0 && a[0] == "keep")
            {
                _mark = Hotbar(p);
                r.Info = $"hotbar {_mark} in chapter {Chapter}, {HotbarUi(p)}";
                yield break;
            }

            int n = 0;
            if (a.Length > 0)
            {
                n = int.Parse(a[0]);
            }

            var slots = p.Hotbar.slots;
            foreach (var s in slots)
            {
                if (!InvItemClass.isNull(s.invItem))
                {
                    s.removeItem();
                }
            }

            p.switchToItem(p.fists);
            int at = n % slots.Count;
            slots[at].inventory = p.Hotbar;
            slots[at].createItem("bandages", n + 2);
            int at2 = (n + 1) % slots.Count;
            slots[at2].inventory = p.Hotbar;
            slots[at2].createItem("rope", 1);
            yield return new WaitForSeconds(0.5f);
            _mark = Hotbar(p);
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Characters.SaveNow();
            }

            r.Info = $"hotbar {_mark} in chapter {Chapter}, {HotbarUi(p)}";
        }

        private static IEnumerator Go(string[] a, Result r)
        {
            // With "wait" the chapter may be on its way already: wait for
            // chapter 2's scene, ready.
            if (a.Length == 0 || a[0] != "wait")
            {
                _mark = Hotbar(P);
                var step = new GameEvent
                {
                    type = GameEvent.Type.transportPlayerToObject,
                    activeModifier = true,
                    intValue = Chapter + 1,
                    eventAmount = 1
                };

                Singleton<Controller>.Instance.StartCoroutine(
                    step.fire(P.gameObject));
                yield return new WaitForSecondsRealtime(2f);
            }

            while (Chapter != 2
                || SceneManager.GetActiveScene().name != "chapter2"
                || !WorldShare.WorldReady || Generating)
            {
                yield return new WaitForSecondsRealtime(0.5f);
            }

            r.Info =
                $"in chapter {Chapter} ({SceneManager.GetActiveScene().name}) "
                    + $"at {Pos(P)}, hotbar {Hotbar(P)}";
        }

        private static IEnumerator Fire(string[] a, Result r)
        {
            // The trigger that runs it (the exit's EventTriggers), set off by
            // our player beside it.
            EventTriggers et = null;
            global::EventTrigger t = null;
            foreach (var e in Object.FindObjectsOfType<EventTriggers>(true))
            {
                if (e == null || !e.gameObject.scene.IsValid())
                {
                    continue;
                }

                t = e.eventTriggers.FirstOrDefault(
                    x => x != null && x.gameEvents != null
                        && x.gameEvents.name == a[0]);
                if (t != null)
                {
                    et = e;
                    break;
                }
            }

            if (et == null)
            {
                string wanted = a[0];
                string location = Whereabouts.Current();
                if (location == null)
                {
                    location = "the world";
                }

                r.Fail($"no trigger for {wanted} here (in {location})");
                yield break;
            }

            // Its area is off while we land in it and the host sees us there:
            // the host checks the exit is within reach of where it last saw us,
            // and the area set off in the frame we landed sent it before our
            // new place (refused: "5995 away", at the spot loc-go left us).
            var cols =
                et.GetComponents<Collider>().Where(c => c.enabled).ToList();
            foreach (var c in cols)
            {
                c.enabled = false;
            }

            Place(et.transform.position + new Vector3(40f, 0f, 0f));
            yield return new WaitForSeconds(2f);
            string area = et.name;
            global::EventTrigger.Type kind = t.type;
            string eventName = a[0];
            string spot = Pos(et.transform);
            string place = Whereabouts.Current();
            if (place == null)
            {
                place = "the world";
            }

            r.Info =
                $"set off {area} ({kind}) for {eventName} at {spot} in "
                    + $"{place}, chapter {Chapter}";
            var type = t.type;
            AsPlayer(() => et.fireEventTrigger(type));
            yield return new WaitForSeconds(1f);
            foreach (var c in cols)
            {
                if (c != null)
                {
                    c.enabled = true;
                }
            }
        }

        /// <summary>
        /// "chapter-offmap" (client): our player goes far off the map (where a
        /// separate location's place is), and we join again from there: the
        /// kept place isn't on the map, so we must come back beside the host.
        /// </summary>
        private static IEnumerator OffMap(string[] a, Result r)
        {
            Place(new Vector3(-90000f, P.transform.position.y, -90000f));
            yield return new WaitForSeconds(1f);
            // sends our character (from off the map) first
            Net.Session.Instance.Backend.Rejoin();
            while (HasCharacter())
            {
                yield return new WaitForSecondsRealtime(0.2f);
            }

            yield return In(a, r);
        }

        private static IEnumerator In(string[] a, Result r)
        {
            float giveUp = Time.realtimeSinceStartup + 300f;
            while (!(Chapter == 2
                    && SceneManager.GetActiveScene().name == "chapter2"
                    && WorldShare.WorldReady && Combat.NetEnemies.ClientMode
                    && HasCharacter()))
            {
                if (Time.realtimeSinceStartup > giveUp)
                {
                    string line =
                        "not in with our character after 300 s: chapter "
                            + $"{Chapter}, scene "
                            + $"{SceneManager.GetActiveScene().name}, in world "
                            + $"{WorldShare.InWorld}, " +
                                  $"gen done {Core.worldGenFinished()}, "
                            + $"loading {Core.loadingGame}, inputs off "
                            + $"{Core.forbidInputs}, client mode "
                            + $"{Combat.NetEnemies.ClientMode}, ";
                    bool? have = null;
                    CoopSession coop = Coop.Current;
                    if (coop != null)
                    {
                        have = coop.Characters.HasCharacter;
                    }

                    r.Fail(line + $"have {have}, at {Pos(P)}");
                    yield break;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            // its place and its things settle
            yield return new WaitForSecondsRealtime(3f);
            var p = P;
            string hot = Hotbar(p);
            var host = Host();
            float d = -1f;
            if (host.HasValue)
            {
                d = StateDump.Flat(p.transform.position - host.Value);
            }

            string ui = HotbarUi(p);
            int told = Count("[notice] The host is starting chapter 2");
            int chapter = Chapter;
            string scene = SceneManager.GetActiveScene().name;
            string spot = Pos(p);
            string away = "not seen";
            if (host.HasValue)
            {
                away = $"{d:0} away";
            }

            bool ground = OnGround(p);
            float nearest = KeptCharacters.Characters.NearestChunk(
                p.transform.position);
            WorldGenerator generator = Singleton<WorldGenerator>.Instance;
            float? size = null;
            if (generator != null)
            {
                size = generator.chunkSize;
            }

            WorldGenerator world = Singleton<WorldGenerator>.Instance;
            int? chunks = null;
            if (world != null)
            {
                var list = world.worldChunks;
                if (list != null)
                {
                    chunks = list.Count;
                }
            }

            r.Info =
                $"told {told}x; chapter {chapter} ({scene}) at {spot}, host "
                    + $"{away}, ground {ground} (nearest chunk {nearest:0}, "
                    + $"chunk size {size:0}, {chunks} chunks), hotbar {hot}, "
                    + $"{ui}";
            if (told != 1)
            {
                r.Fail($"the host's ChapterStart came {told} times: {r.Info}");
            }
            else if (_mark != null && hot != _mark)
            {
                r.Fail($"our hotbar isn't ours: {hot}, had {_mark}; {r.Info}");
            }
            else if (!ui.StartsWith("ui ok"))
            {
                r.Fail($"the hotbar shown isn't ours: {r.Info}");
            }
            else if (!OnGround(p))
            {
                r.Fail($"no ground under our player: {r.Info}");
            }
            else if (!host.HasValue)
            {
                r.Fail($"the host isn't shown: {r.Info}");
            }
            else if (d > 800f)
            {
                r.Fail($"not beside the host: {r.Info}");
            }
        }

        /// <summary>
        /// How many lines of our log since "chapter-mark" have this text.
        /// </summary>
        private static int Count(string text) =>
            Lines().Count(l => l.Contains(text));

        private static IEnumerator All(string[] a, Result r)
        {
            int want = int.Parse(a[0]);
            var ps = Plugin.Instance.Host.Players;
            float until = Time.realtimeSinceStartup + 200f;
            System.Collections.Generic.List<string> seen = null;
            while (Time.realtimeSinceStartup < until)
            {
                if (ps == null)
                {
                    seen = new System.Collections.Generic.List<string>();
                }
                else
                {
                    seen = ps.Remotes.Values.Where(
                        rp => rp.Root != null && rp.Root.activeInHierarchy
                            && StateDump.Flat(
                                rp.Root.transform.position
                                    - P.transform.position) < 800f)
                        .Select(
                            rp => Pos(rp.Root.transform)).ToList();
                }

                if (seen.Count >= want)
                {
                    break;
                }

                yield return new WaitForSecondsRealtime(1f);
            }

            int starts = Count("Starting chapter 2:"),
                runs = Count("set off chapter 2") - Count("can't run here yet");
            r.Info =
                $"chapter {Chapter}, hotbar {Hotbar(P)}, players beside us: "
                    + $"{string.Join(" ", seen)}; started {starts}x, a "
                    + $"joiner's run {runs}x";
            if (seen.Count < want)
            {
                r.Fail($"{seen.Count} of {want} players beside us: {r.Info}");
            }
            else if (starts != 1 || runs > 1)
            {
                r.Fail($"chapter 2 didn't start once: {r.Info}");
            }
            // The host's own hotbar is the game's to carry over; only what's in
            // which slot is checked here (the amounts of a stack can change as
            // the chapter's save is loaded).
            else if (_mark != null && !Keeps(Hotbar(P), _mark))
            {
                r.Fail($"our hotbar changed: {r.Info}, had {_mark}");
            }
            else if (!HotbarUi(P).StartsWith("ui ok"))
            {
                r.Fail($"the hotbar shown isn't ours: {HotbarUi(P)}");
            }
        }

        /// <summary>
        /// Whether the hotbar on screen is the hotbar's slots: one slot picture
        /// (UIInvSlot) under the hotbar's UI for each slot, each slot with its
        /// own. "ui ok N" or what's wrong.
        /// </summary>
        internal static string HotbarUi(Player p)
        {
            var bar = p.Hotbar;
            if (bar == null || bar.thisUI == null)
            {
                return "ui none";
            }

            int shown =
                bar.thisUI.GetComponentsInChildren<UIInvSlot>(false).Length;
            int without = bar.slots.Count(
                s => s.UIInvSlot == null
                    || !s.UIInvSlot.gameObject.activeInHierarchy);
            if (shown == bar.slots.Count && without == 0)
            {
                return $"ui ok {shown}";
            }

            return $"ui {shown} shown for {bar.slots.Count} slots, {without} "
                + "slots not shown";
        }

        /// <summary>
        /// Where the host's player is shown in our game (the remote player
        /// nearest us).
        /// </summary>
        private static Vector3? Host()
        {
            var ps = Plugin.Instance.Host.Players;
            var s = Net.Session.Instance;
            if (ps == null || s == null)
            {
                return null;
            }

            if (ps.Remotes.TryGetValue(s.HostId, out var rp)
                && rp.Root != null && rp.Root.activeInHierarchy)
            {
                return rp.Root.transform.position;
            }

            return (Vector3?)null;
        }

        /// <summary>
        /// The ground under the player: a world chunk there
        /// (WorldChunk.getChunkAtPos), or on the map's chunks by their places.
        /// </summary>
        private static bool OnGround(Player p) =>
            WorldChunk.getChunkAtPos(p.transform.position) != null
                || KeptCharacters.Characters.OnMap(p.transform.position);

        /// <summary>
        /// The hotbar has the same slots as `had`, with the same kind of thing
        /// in each slot that had one.
        /// </summary>
        private static bool Keeps(string now, string had)
        {
            var n = Types(now).Split(',');
            var h = Types(had).Split(',');
            return n.Length == h.Length
                && h.Select((t, i) => t == "-" || t == n[i]).All(ok => ok);
        }

        private static string Pos(Player p)
        {
            if (p == null)
            {
                return "(no player)";
            }

            return Pos(p.transform);
        }

        private static string Pos(Transform t) =>
            $"({t.position.x:0}, {t.position.z:0})";
    }
}
