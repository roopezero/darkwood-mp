using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for the move from chapter 1 to chapter 2 with every player in (tools/tests/map_scenario_chapter.py,
    /// run in map4 with two or more client games): each client marks its hotbar with things of its own, the host's
    /// game starts chapter 2 as a chapter's GameEvent step does, and each client must come into the host's chapter 2
    /// (its scene, its world, on the ground beside the host) with its own hotbar, not the host's.
    /// </summary>
    public static class MapScenarioChapter
    {
        private static Player P => Player.Instance;

        /// <summary>The hotbar this game's player had before the chapter ("chapter-mark"), kept across the world reload.</summary>
        private static string _mark;

        public static void Register()
        {
            // "chapter-mark N" (client): empty our hotbar and put things in it that only this client has (N: 0, 1, 2...);
            // "chapter-mark keep": note our hotbar as it is.
            Add("chapter-mark", Mark);
            // "chapter-go [wait]" (host): start the next chapter (with "wait": someone else sets it off); wait until our
            // game is in it and ready.
            Add("chapter-go", Go, timeout: 300f);
            // "chapter-fire NAME": fire the GameEvents NAME in our game, as our player's doing (as its trigger does).
            Add("chapter-fire", Fire);
            // "chapter-in" (client): wait until we're in the host's new chapter with our own character.
            Add("chapter-in", In, timeout: 560f);
            // "chapter-offmap" (client): join again with our kept place off the map; we must be beside the host.
            Add("chapter-offmap", OffMap, timeout: 400f);
            // "chapter-all N" (host): N players in our new chapter, each beside us.
            Add("chapter-all", All, timeout: 240f);
        }

        internal static string Hotbar(Player p) =>
            string.Join(",", p.Hotbar.slots.Select(s => InvItemClass.isNull(s.invItem) ? "-" : $"{s.invItem.type}x{s.invItem.amount}"));

        private static string Types(string hotbar) => string.Join(",", hotbar.Split(',').Select(s => s.Split('x')[0]));

        private static int Chapter => Chapters.HostChapter;

        private static IEnumerator Mark(string[] a, Result r)
        {
            var p = P;
            // A second argument: that many hotbar slots, taken as the game's hotbar upgrade item gives one.
            int want = a.Length > 1 ? int.Parse(a[1]) : 0;
            while (want > 0 && p.Hotbar.slots.Count < want) p.upgradeHotbar();
            if (a.Length > 0 && a[0] == "keep") { _mark = Hotbar(p); r.Info = $"hotbar {_mark} in chapter {Chapter}, {HotbarUi(p)}"; yield break; }
            int n = a.Length > 0 ? int.Parse(a[0]) : 0;
            var slots = p.Hotbar.slots;
            foreach (var s in slots) if (!InvItemClass.isNull(s.invItem)) s.removeItem();
            p.switchToItem(p.fists);
            int at = n % slots.Count;
            slots[at].inventory = p.Hotbar; slots[at].createItem("bandages", n + 2);
            int at2 = (n + 1) % slots.Count;
            slots[at2].inventory = p.Hotbar; slots[at2].createItem("rope", 1);
            yield return new WaitForSeconds(0.5f);
            _mark = Hotbar(p);
            Characters.Instance?.SaveNow();
            r.Info = $"hotbar {_mark} in chapter {Chapter}, {HotbarUi(p)}";
        }

        private static IEnumerator Go(string[] a, Result r)
        {
            // With "wait" the chapter may be on its way already: wait for chapter 2's scene, ready.
            if (a.Length == 0 || a[0] != "wait")
            {
                _mark = Hotbar(P);
                var step = new GameEvent { type = GameEvent.Type.transportPlayerToObject, activeModifier = true, intValue = Chapter + 1, eventAmount = 1 };
                Singleton<Controller>.Instance.StartCoroutine(step.fire(P.gameObject));
                yield return new WaitForSecondsRealtime(2f);
            }
            while (Chapter != 2 || SceneManager.GetActiveScene().name != "chapter2" || !WorldShare.WorldReady || Chapters.Generating)
                yield return new WaitForSecondsRealtime(0.5f);
            r.Info = $"in chapter {Chapter} ({SceneManager.GetActiveScene().name}) at {Pos(P)}, hotbar {Hotbar(P)}";
        }

        private static IEnumerator Fire(string[] a, Result r)
        {
            // The trigger that runs it (the exit's EventTriggers), set off by our player beside it.
            EventTriggers et = null; global::EventTrigger t = null;
            foreach (var e in Object.FindObjectsOfType<EventTriggers>(true))
            {
                if (e == null || !e.gameObject.scene.IsValid()) continue;
                t = e.eventTriggers.FirstOrDefault(x => x?.gameEvents != null && x.gameEvents.name == a[0]);
                if (t != null) { et = e; break; }
            }
            if (et == null) { r.Fail($"no trigger for {a[0]} here (in {Locations.Current() ?? "the world"})"); yield break; }
            // Its area is off while we land in it and the host sees us there: the host checks the exit is within
            // reach of where it last saw us, and the area set off in the frame we landed sent it before our new
            // place (refused: "5995 away", at the spot loc-go left us).
            var cols = et.GetComponents<Collider>().Where(c => c.enabled).ToList();
            foreach (var c in cols) c.enabled = false;
            Place(et.transform.position + new Vector3(40f, 0f, 0f));
            yield return new WaitForSeconds(2f);
            r.Info = $"set off {et.name} ({t.type}) for {a[0]} at {Pos(et.transform)} in {Locations.Current() ?? "the world"}, chapter {Chapter}";
            var type = t.type;
            AsPlayer(() => et.fireEventTrigger(type));
            yield return new WaitForSeconds(1f);
            foreach (var c in cols) if (c != null) c.enabled = true;
        }

        /// <summary>
        /// "chapter-offmap" (client): our player goes far off the map (where a separate location's place is), and we
        /// join again from there: the kept place isn't on the map, so we must come back beside the host.
        /// </summary>
        private static IEnumerator OffMap(string[] a, Result r)
        {
            Place(new Vector3(-90000f, P.transform.position.y, -90000f));
            yield return new WaitForSeconds(1f);
            Net.Session.Instance.Backend.Rejoin(); // sends our character (from off the map) first
            while (Characters.Instance.HasCharacter) yield return new WaitForSecondsRealtime(0.2f);
            yield return In(a, r);
        }

        private static IEnumerator In(string[] a, Result r)
        {
            float giveUp = Time.realtimeSinceStartup + 300f;
            while (!(Chapter == 2 && SceneManager.GetActiveScene().name == "chapter2" && WorldShare.WorldReady && Combat.NetEnemies.ClientMode
                     && Characters.Instance != null && Characters.Instance.HasCharacter))
            {
                if (Time.realtimeSinceStartup > giveUp)
                {
                    r.Fail($"not in with our character after 300 s: chapter {Chapter}, scene {SceneManager.GetActiveScene().name}, in world {WorldShare.InWorld}, " +
                           $"gen done {Core.worldGenFinished()}, loading {Core.loadingGame}, inputs off {Core.forbidInputs}, client mode {Combat.NetEnemies.ClientMode}, " +
                           $"have {Characters.Instance?.HasCharacter}, at {Pos(P)}");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            yield return new WaitForSecondsRealtime(3f); // its place and its things settle
            var p = P;
            string hot = Hotbar(p);
            var host = Host();
            float d = host.HasValue ? StateDump.Flat(p.transform.position - host.Value) : -1f;
            string ui = HotbarUi(p);
            r.Info = $"chapter {Chapter} ({SceneManager.GetActiveScene().name}) at {Pos(p)}, host {(host.HasValue ? $"{d:0} away" : "not seen")}, ground {OnGround(p)} (nearest chunk {Characters.NearestChunk(p.transform.position):0}, chunk size {Singleton<WorldGenerator>.Instance?.chunkSize:0}, {Singleton<WorldGenerator>.Instance?.worldChunks?.Count} chunks), hotbar {hot}, {ui}";
            if (_mark != null && hot != _mark) r.Fail($"our hotbar isn't ours: {hot}, had {_mark}; {r.Info}");
            else if (!ui.StartsWith("ui ok")) r.Fail($"the hotbar shown isn't ours: {r.Info}");
            else if (!OnGround(p)) r.Fail($"no ground under our player: {r.Info}");
            else if (!host.HasValue) r.Fail($"the host isn't shown: {r.Info}");
            else if (d > 800f) r.Fail($"not beside the host: {r.Info}");
        }

        private static IEnumerator All(string[] a, Result r)
        {
            int want = int.Parse(a[0]);
            var ps = Sync.PlayerSync.Instance;
            float until = Time.realtimeSinceStartup + 200f;
            System.Collections.Generic.List<string> seen = null;
            while (Time.realtimeSinceStartup < until)
            {
                seen = ps == null ? new System.Collections.Generic.List<string>() :
                    ps.Remotes.Values.Where(rp => rp.Root != null && rp.Root.activeInHierarchy && StateDump.Flat(rp.Root.transform.position - P.transform.position) < 800f)
                        .Select(rp => Pos(rp.Root.transform)).ToList();
                if (seen.Count >= want) break;
                yield return new WaitForSecondsRealtime(1f);
            }
            r.Info = $"chapter {Chapter}, hotbar {Hotbar(P)}, players beside us: {string.Join(" ", seen)}";
            if (seen.Count < want) r.Fail($"{seen.Count} of {want} players beside us: {r.Info}");
            // The host's own hotbar is the game's to carry over; only what's in which slot is checked here (the amounts of a
            // stack can change as the chapter's save is loaded).
            else if (_mark != null && !Keeps(Hotbar(P), _mark)) r.Fail($"our hotbar changed: {r.Info}, had {_mark}");
            else if (!HotbarUi(P).StartsWith("ui ok")) r.Fail($"the hotbar shown isn't ours: {HotbarUi(P)}");
        }

        /// <summary>
        /// Whether the hotbar on screen is the hotbar's slots: one slot picture (UIInvSlot) under the hotbar's UI for each
        /// slot, each slot with its own. "ui ok N" or what's wrong.
        /// </summary>
        internal static string HotbarUi(Player p)
        {
            var bar = p.Hotbar;
            if (bar == null || bar.thisUI == null) return "ui none";
            int shown = bar.thisUI.GetComponentsInChildren<UIInvSlot>(false).Length;
            int without = bar.slots.Count(s => s.UIInvSlot == null || !s.UIInvSlot.gameObject.activeInHierarchy);
            return shown == bar.slots.Count && without == 0 ? $"ui ok {shown}" : $"ui {shown} shown for {bar.slots.Count} slots, {without} slots not shown";
        }

        /// <summary>Where the host's player is shown in our game (the remote player nearest us).</summary>
        private static Vector3? Host()
        {
            var ps = Sync.PlayerSync.Instance;
            var s = Net.Session.Instance;
            if (ps == null || s == null) return null;
            return ps.Remotes.TryGetValue(s.HostId, out var rp) && rp.Root != null && rp.Root.activeInHierarchy ? rp.Root.transform.position : (Vector3?)null;
        }

        /// <summary>The ground under the player: a world chunk there (WorldChunk.getChunkAtPos), or on the map's chunks by their places.</summary>
        private static bool OnGround(Player p) =>
            WorldChunk.getChunkAtPos(p.transform.position) != null || Characters.OnMap(p.transform.position);

        /// <summary>The hotbar has the same slots as `had`, with the same kind of thing in each slot that had one.</summary>
        private static bool Keeps(string now, string had)
        {
            var n = Types(now).Split(','); var h = Types(had).Split(',');
            return n.Length == h.Length && h.Select((t, i) => t == "-" || t == n[i]).All(ok => ok);
        }

        private static string Pos(Player p) => Pos(p.transform);
        private static string Pos(Transform t) => $"({t.position.x:0}, {t.position.z:0})";
    }
}
