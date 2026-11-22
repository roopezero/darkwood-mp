using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for NPCs across games (tools/tests/map_scenario_npcs.py):
    /// where they stand (NpcSync), the morning trader in a joiner's hideout
    /// when the host has none there (TraderSync's host copy), and his death
    /// (NpcDied by name, FlagsSync's check of a dead state).
    /// </summary>
    public static partial class MapScenarioNpcs
    {
        /// <summary>
        /// "npc-move": the NPC moved (its save id) and where it was.
        /// </summary>
        private static readonly Dictionary<string, (int id, Vector3 was)>
            _moved = new Dictionary<string, (int, Vector3)>();

        public static void Register()
        {
            Add("npc-move", NpcMove);
            Add("npc-talk", NpcTalk, timeout: 40f);
            Add("host-copy", HostCopy, timeout: 25f);
            Add("npc-died", NpcDied, timeout: 20f);
            Add("wolf-away", (a, r) => WolfFlags(true, r));
            Add("wolf-back", (a, r) => WolfFlags(false, r));
            Add("follow-go", FollowGo, timeout: 40f);
            Add("follow-recruit", FollowRecruit, timeout: 30f);
            Add("follow-near", FollowNear, timeout: 30f);
            Add("follow-check", FollowCheck, timeout: 30f);
            Add("follow-end", FollowEnd);
            Add("anim-setup", AnimSetup);
            Add("anim-fire", AnimFire);
            Add("anim-check", AnimCheck);
            Add("anim-end", (a, r) =>
            {
                AnimEnd();
                return MapScenarioIssues.Nothing();
            });
        }

        private static readonly string[] WolfFlagNames =
            { "wolf_cameToPlayerHideout", "wolf_inPlayerHideout" };
        private static bool[] _wolfWas;

        /// <summary>"wolf-away" (host): the morning brings the trader, not the
        /// wolf (Controller.startAfterNight: the wolf came to the hideout
        /// before and isn't staying in it); the story flags reach the others.
        /// "wolf-back" undoes it.</summary>
        private static IEnumerator WolfFlags(bool away, Result r)
        {
            var f = Singleton<Flags>.Instance;
            if (away)
            {
                _wolfWas = WolfFlagNames.Select(n => f.isFlagTrue(n)).ToArray();
                f.setFlag(WolfFlagNames[0], true);
                f.setFlag(WolfFlagNames[1], false);
            }
            else if (_wolfWas != null)
            {
                for (int i = 0; i < WolfFlagNames.Length; i++)
                {
                    f.setFlag(WolfFlagNames[i], _wolfWas[i]);
                }

                _wolfWas = null;
            }

            yield return new WaitForSeconds(0.5f);
            r.Info = string.Join(
                ", ", WolfFlagNames.Select(n => $"{n} {f.isFlagTrue(n)}"));
        }

        private static int SharedId(NPC n)
        {
            SaveableObject s = null;
            if (n != null)
            {
                s = n.GetComponent<SaveableObject>();
            }

            if (s != null && s.uniqueId > 0
                && s.uniqueId < SharedIds.ResolveLimit)
            {
                return s.uniqueId;
            }

            return 0;
        }

        /// <summary>
        /// The living NPC of that name (any part of it, any case) that every
        /// game has, nearest to `near`.
        /// </summary>
        private static NPC Shared(string name, Vector3 near) =>
            Nearest(
                UnityEngine.Object.FindObjectsOfType<NPC>(true).Where(
                    n => n != null
                        && n.name.IndexOf(
                            name, StringComparison.OrdinalIgnoreCase) >= 0
                        && n.GetComponent<Character>() != null
                        && n.GetComponent<Character>().alive
                        && SharedId(n) != 0), near);

        /// <summary>"npc-move NAME DX DZ" | "npc-move NAME back" (host): our
        /// NPC of that name (one every game has) nearest the centre is put at
        /// centre + (DX, DZ), as a scripted move does; "back" puts it where it
        /// was.</summary>
        private static IEnumerator NpcMove(string[] a, Result r)
        {
            NPC npc;
            Vector3 to;
            if (a.Length > 1 && a[1] == "back")
            {
                if (!_moved.TryGetValue(a[0], out var m))
                {
                    r.Fail("not moved");
                    yield break;
                }

                _moved.Remove(a[0]);
                var sm = Singleton<SaveManager>.Instance;
                if (sm.uniqueIdDict.TryGetValue(m.id, out var t) && t != null)
                {
                    npc = t.GetComponent<NPC>();
                }
                else
                {
                    npc = null;
                }

                to = m.was;
            }
            else
            {
                npc = Shared(a[0], Centre);
                if (npc != null)
                {
                    _moved[a[0]] = (SharedId(npc), npc.transform.position);
                }

                to = Centre + new Vector3(F(a[1]), 0f, F(a[2]));
            }

            if (npc == null)
            {
                r.Fail($"no {a[0]} here that every game has");
                yield break;
            }

            var from = npc.transform.position;
            to.y = from.y;
            npc.transform.position = to;
            yield return new WaitForSeconds(1f);
            r.Info =
                $"{npc.name} "
                    + $"{ObjRef.Of(npc.gameObject)}: {Fmt(from)} -> "
                    + $"{Fmt(npc.transform.position)}";
        }

        /// <summary>"npc-talk NAME DX DZ" (client): our copy of that NPC comes
        /// to centre + (DX, DZ), where the host's is (waiting up to 10 s); our
        /// player walks there and talks to him, and the host doesn't refuse
        /// it.</summary>
        private static IEnumerator NpcTalk(string[] a, Result r)
        {
            var to = Centre + new Vector3(F(a[1]), 0f, F(a[2]));
            NPC npc = null;
            float until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until)
            {
                npc = Shared(a[0], to);
                if (npc != null
                    && StateDump.Flat(npc.transform.position - to) < 60f)
                {
                    break;
                }

                yield return new WaitForSecondsRealtime(0.25f);
            }

            if (npc == null)
            {
                r.Fail($"no {a[0]} here");
                yield break;
            }

            float off = StateDump.Flat(npc.transform.position - to);
            if (off >= 60f)
            {
                r.Fail(
                    $"{npc.name} is at {Fmt(npc.transform.position)}, {off:0} "
                        + $"from the host's {Fmt(to)}");
                yield break;
            }

            yield return Walk(npc.transform.position, 150f);
            int mark = MapScenarioIssues.LogMark();
            var win = Singleton<UI>.Instance.dialogueWindow;
            AsPlayer(() => npc.talkTo());
            yield return new WaitForSecondsRealtime(2.5f);
            var refused = MapScenarioIssues.LogLines().Skip(Math.Max(0, mark))
                .Where(l => !l.Contains("[map]") && l.Contains("refused"))
                .ToList();
            bool open = win.npc == npc;
            string dialogue = "not open";
            if (open)
            {
                dialogue = "open";
            }

            r.Info =
                $"{npc.name} at {Fmt(npc.transform.position)} ({off:0} from "
                    + $"the host's): dialogue {dialogue}";
            if (open)
            {
                yield return MapScenarioGaps.Close(win, npc);
            }

            if (refused.Count > 0)
            {
                r.Fail($"refused: {refused[0].Trim()}");
            }
            else if (!open)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>Where our log stood at the last "host-copy here": npc-died
        /// reads only what came after it. The host's game is kept between map
        /// modules, so its log also has earlier modules' lines (items1's
        /// place-forged Characters/NPC/Porter is refused on purpose, and
        /// fixchecks kills a night trader too).</summary>
        private static int _copyMark;

        /// <summary>"host-copy here|gone" (host, not in a morning itself): our
        /// own morning trader in the joiner's hideout (TraderSync), with stock,
        /// or (waiting up to 20 s) gone again.</summary>
        private static IEnumerator HostCopy(string[] a, Result r)
        {
            bool want = a.Length == 0 || a[0] == "here";
            if (want)
            {
                _copyMark = MapScenarioIssues.LogMark();
            }

            var l = MapScenarioGaps.Hideout(Player.Instance);
            if (l == null)
            {
                r.Fail("no hideout");
                yield break;
            }

            float until = Time.realtimeSinceStartup + 20f;
            NPC npc = null;
            bool has = false;
            while (Time.realtimeSinceStartup < until)
            {
                if (l.trader != null)
                {
                    npc = l.trader.GetComponent<NPC>();
                }
                else
                {
                    npc = null;
                }

                Character ch = null;
                if (npc != null)
                {
                    ch = npc.GetComponent<Character>();
                }

                has = ch != null && ch.alive && npc.inventory != null
                    && npc.inventory.getAllItems().Count > 0;
                if (has == want)
                {
                    break;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            var c = Singleton<Controller>.Instance;
            string hideout = l.name;
            string trader = "no trader";
            if (npc != null)
            {
                string name = npc.name;
                int things = 0;
                if (npc.inventory != null)
                {
                    things = npc.inventory.getAllItems().Count;
                }

                trader = $"{name} with {things} things";
            }

            r.Info = $"{hideout}: {trader}, our morning {c.isAfterNight}";
            if (c.isAfterNight)
            {
                r.Fail($"we're in a morning ourselves: {r.Info}");
            }
            else if (has != want)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"npc-died" (host): the night trader in the hideout died
        /// here too, through the joiner's NpcDied (taken, not refused: our copy
        /// is where theirs was), and neither that nor their dead state was
        /// refused, since the last "host-copy here". (A dead night trader is
        /// taken away at once, Character.die's removeMe, so there's no body to
        /// look at.)</summary>
        private static IEnumerator NpcDied(string[] a, Result r)
        {
            yield return new WaitForSeconds(2f);
            var l = MapScenarioGaps.Hideout(Player.Instance);
            Vector3 spot;
            if (l != null && l.traderPosition != null)
            {
                spot = l.traderPosition.transform.position;
            }
            else
            {
                spot = Centre;
            }

            var lines = MapScenarioIssues.LogLines()
                .Skip(Math.Max(0, _copyMark)).ToList();
            var refused = lines.Where(
                x => x.Contains("[authority] refused")
                    && (x.Contains("NPC") || x.Contains("state of")
                        || x.IndexOf(
                            "trader",
                            StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            bool got = lines.Any(
                x => x.Contains("got NpcDied")
                    && x.IndexOf("trader", StringComparison.OrdinalIgnoreCase)
                        >= 0);
            var alive = UnityEngine.Object.FindObjectsOfType<Character>()
                .Where(
                    c => c.isNightTrader && c.alive
                        && StateDump.Flat(c.transform.position - spot)
                            < 600f).ToList();
            bool flag = Singleton<Flags>.Instance.isFlagTrue(
                "nightTrader_killedYesterday");
            string placeName = "the hideout";
            if (l != null)
            {
                placeName = l.name;
            }

            r.Info =
                $"NpcDied came {got}, living night traders in {placeName}: "
                    + $"{alive.Count}, nightTrader_killedYesterday {flag}";
            if (refused.Count > 0)
            {
                r.Fail($"{r.Info}; {refused[0].Trim()}");
            }
            else if (!got || alive.Count > 0 || !flag)
            {
                r.Fail(r.Info);
            }
        }
    }
}
