using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for NPCs across games (tools/tests/map_scenario_npcs.py): where they stand (NpcSync), the
    /// morning trader in a joiner's hideout when the host has none there (TraderSync's host copy), and his death
    /// (NpcDied by name, FlagsSync's check of a dead state).
    /// </summary>
    public static class MapScenarioNpcs
    {
        /// <summary>"npc-move": the NPC moved (its save id) and where it was.</summary>
        private static readonly Dictionary<string, (int id, Vector3 was)> _moved = new Dictionary<string, (int, Vector3)>();

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
        }

        /// <summary>"follow-go DX DZ" (client): our player walks to centre + (DX, DZ).</summary>
        private static IEnumerator FollowGo(string[] a, Result r)
        {
            yield return Walk(Centre + new Vector3(F(a[0]), 0f, F(a[1])), 80f);
            r.Info = $"at {Fmt(Player.Instance.transform.position)}";
        }

        /// <summary>"follow-recruit": the NPC made a follower, its aggressiveness and behaviour before.</summary>
        private static (Character c, Aggressiveness ag, Character.Behaviour be, Vector3 was) _follower;

        /// <summary>"follow-recruit NAME" (host): our NPC of that name nearest the centre is made a follower by a
        /// scripted step (GameEvent modifyCharacter, aggressiveness follower) run as the joiner's event (as an
        /// EventFired from them runs it: ScriptedEvents.By). It is recorded as theirs and goes after the joiner's
        /// stand-in as its target, not us (waiting up to 10 s). Whether it walks after them is follow-check's: the test
        /// world's night trader never starts walking (it stays idle), so that step needs an NPC that walks.</summary>
        private static IEnumerator FollowRecruit(string[] a, Result r)
        {
            var npc = Shared(a[0], Centre);
            var c = npc != null ? npc.GetComponent<Character>() : null;
            var proxy = Combat.NetPlayerProxy.All.Values.FirstOrDefault(x => x != null && x.Owner != Sync.PlayerSync.DummyId);
            if (c == null) { r.Fail($"no {a[0]} here that every game has"); yield break; }
            if (proxy == null) { r.Fail("no joiner"); yield break; }
            _follower = (c, c.aggressiveness, c.behaviour, c.transform.position);
            var ge = new GameEvent
            {
                type = GameEvent.Type.modifyCharacter,
                characterModifyType = GameEvent.CharacterModify.aggressiveness,
                destAggressiveness = Aggressiveness.follower,
                targetGameObjects = new List<GameObject> { npc.gameObject },
            };
            ScriptedEvents.RunAs(ScriptedEvents.Part.HostAndShared, replaying: true, () =>
            {
                var was = ScriptedEvents.By;
                ScriptedEvents.By = proxy.Owner;
                try { Plugin.Instance.StartCoroutine(ge.fire(npc.gameObject)); }
                finally { ScriptedEvents.By = was; }
            });
            float until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until && c.target != proxy.transform)
                yield return new WaitForSecondsRealtime(0.5f);
            r.Info = FollowInfo(c, proxy);
            if (NpcFollowers.LeaderOf(c) != proxy) r.Fail($"not recorded as {proxy.OwnerName}'s follower: {r.Info}");
            else if (c.target != proxy.transform) r.Fail(r.Info);
        }

        private static string FollowInfo(Character c, Combat.NetPlayerProxy proxy) =>
            $"{c.name}: {c.aggressiveness}, {c.behaviour}, target {(c.target == null ? "none" : c.target == proxy.transform ? proxy.OwnerName : c.target.name)}, " +
            $"{StateDump.Flat(c.transform.position - proxy.transform.position):0} from {proxy.OwnerName}, {StateDump.Flat(c.transform.position - Player.Instance.transform.position):0} from us";

        /// <summary>"follow-check" (host, after the joiner walked off): the follower went after them, not us (within
        /// 500 of their stand-in, waiting up to 20 s).</summary>
        private static IEnumerator FollowCheck(string[] a, Result r)
        {
            var c = _follower.c;
            var proxy = Combat.NetPlayerProxy.All.Values.FirstOrDefault(x => x != null && x.Owner != Sync.PlayerSync.DummyId);
            if (c == null || proxy == null) { r.Fail("no follower or no joiner"); yield break; }
            float until = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < until && StateDump.Flat(c.transform.position - proxy.transform.position) >= 500f)
                yield return new WaitForSecondsRealtime(0.5f);
            r.Info = FollowInfo(c, proxy);
            if (StateDump.Flat(c.transform.position - proxy.transform.position) >= 500f || c.target != proxy.transform) r.Fail(r.Info);
        }

        /// <summary>"follow-near NAME" (client): our copy of that NPC comes to us (within 500), as the host's follows
        /// our player there (NpcSync; waiting up to 20 s).</summary>
        private static IEnumerator FollowNear(string[] a, Result r)
        {
            float until = Time.realtimeSinceStartup + 20f, d = -1f;
            NPC npc = null;
            while (Time.realtimeSinceStartup < until)
            {
                npc = Shared(a[0], Player.Instance.transform.position);
                d = npc != null ? StateDump.Flat(npc.transform.position - Player.Instance.transform.position) : -1f;
                if (npc != null && d < 500f) break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            r.Info = npc != null ? $"{npc.name} at {Fmt(npc.transform.position)}, {d:0} from us at {Fmt(Player.Instance.transform.position)}" : $"no {a[0]} here";
            if (npc == null || d >= 500f) r.Fail(r.Info);
        }

        /// <summary>"follow-end" (host): the follower is as it was, where it was.</summary>
        private static IEnumerator FollowEnd(string[] a, Result r)
        {
            var c = _follower.c;
            if (c == null) { r.Fail("no follower"); yield break; }
            c.aggressiveness = _follower.ag;
            c.target = null;
            c.setBehaviour(_follower.be, force: true);
            c.transform.position = _follower.was;
            _follower = default;
            yield return new WaitForSeconds(1f);
            r.Info = $"{c.name} back at {Fmt(c.transform.position)}, {c.aggressiveness}, {c.behaviour}";
        }

        private static readonly string[] WolfFlagNames = { "wolf_cameToPlayerHideout", "wolf_inPlayerHideout" };
        private static bool[] _wolfWas;

        /// <summary>"wolf-away" (host): the morning brings the trader, not the wolf (Controller.startAfterNight: the wolf
        /// came to the hideout before and isn't staying in it); the story flags reach the others. "wolf-back" undoes it.</summary>
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
                for (int i = 0; i < WolfFlagNames.Length; i++) f.setFlag(WolfFlagNames[i], _wolfWas[i]);
                _wolfWas = null;
            }
            yield return new WaitForSeconds(0.5f);
            r.Info = string.Join(", ", WolfFlagNames.Select(n => $"{n} {f.isFlagTrue(n)}"));
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        private static string Fmt(Vector3 p) => FormattableString.Invariant($"{p.x:0} {p.z:0}");

        private static int SharedId(NPC n)
        {
            var s = n != null ? n.GetComponent<SaveableObject>() : null;
            return s != null && s.uniqueId > 0 && s.uniqueId < SharedIds.ResolveLimit ? s.uniqueId : 0;
        }

        /// <summary>The living NPC of that name (any part of it, any case) that every game has, nearest to `near`.</summary>
        private static NPC Shared(string name, Vector3 near) =>
            Nearest(UnityEngine.Object.FindObjectsOfType<NPC>(true).Where(n => n != null && n.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                && n.GetComponent<Character>() != null && n.GetComponent<Character>().alive && SharedId(n) != 0), near);

        /// <summary>"npc-move NAME DX DZ" | "npc-move NAME back" (host): our NPC of that name (one every game has) nearest
        /// the centre is put at centre + (DX, DZ), as a scripted move does; "back" puts it where it was.</summary>
        private static IEnumerator NpcMove(string[] a, Result r)
        {
            NPC npc;
            Vector3 to;
            if (a.Length > 1 && a[1] == "back")
            {
                if (!_moved.TryGetValue(a[0], out var m)) { r.Fail("not moved"); yield break; }
                _moved.Remove(a[0]);
                var sm = Singleton<SaveManager>.Instance;
                npc = sm.uniqueIdDict.TryGetValue(m.id, out var t) && t != null ? t.GetComponent<NPC>() : null;
                to = m.was;
            }
            else
            {
                npc = Shared(a[0], Centre);
                if (npc != null) _moved[a[0]] = (SharedId(npc), npc.transform.position);
                to = Centre + new Vector3(F(a[1]), 0f, F(a[2]));
            }
            if (npc == null) { r.Fail($"no {a[0]} here that every game has"); yield break; }
            var from = npc.transform.position;
            to.y = from.y;
            npc.transform.position = to;
            yield return new WaitForSeconds(1f);
            r.Info = $"{npc.name} {ObjRef.Of(npc.gameObject)}: {Fmt(from)} -> {Fmt(npc.transform.position)}";
        }

        /// <summary>"npc-talk NAME DX DZ" (client): our copy of that NPC comes to centre + (DX, DZ), where the host's
        /// is (waiting up to 10 s); our player walks there and talks to him, and the host doesn't refuse it.</summary>
        private static IEnumerator NpcTalk(string[] a, Result r)
        {
            var to = Centre + new Vector3(F(a[1]), 0f, F(a[2]));
            NPC npc = null;
            float until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until)
            {
                npc = Shared(a[0], to);
                if (npc != null && StateDump.Flat(npc.transform.position - to) < 60f) break;
                yield return new WaitForSecondsRealtime(0.25f);
            }
            if (npc == null) { r.Fail($"no {a[0]} here"); yield break; }
            float off = StateDump.Flat(npc.transform.position - to);
            if (off >= 60f) { r.Fail($"{npc.name} is at {Fmt(npc.transform.position)}, {off:0} from the host's {Fmt(to)}"); yield break; }
            yield return Walk(npc.transform.position, 150f);
            int mark = MapScenarioIssues.LogMark();
            var win = Singleton<UI>.Instance.dialogueWindow;
            AsPlayer(() => npc.talkTo());
            yield return new WaitForSecondsRealtime(2.5f);
            var refused = MapScenarioIssues.LogLines().Skip(Math.Max(0, mark)).Where(l => !l.Contains("[map]") && l.Contains("refused")).ToList();
            bool open = win.npc == npc;
            r.Info = $"{npc.name} at {Fmt(npc.transform.position)} ({off:0} from the host's): dialogue {(open ? "open" : "not open")}";
            if (open) yield return MapScenarioGaps.Close(win, npc);
            if (refused.Count > 0) r.Fail($"refused: {refused[0].Trim()}");
            else if (!open) r.Fail(r.Info);
        }

        /// <summary>Where our log stood at the last "host-copy here": npc-died reads only what came after it. The host's
        /// game is kept between map modules, so its log also has earlier modules' lines (items1's place-forged
        /// Characters/NPC/Porter is refused on purpose, and fixchecks kills a night trader too).</summary>
        private static int _copyMark;

        /// <summary>"host-copy here|gone" (host, not in a morning itself): our own morning trader in the joiner's
        /// hideout (TraderSync), with stock, or (waiting up to 20 s) gone again.</summary>
        private static IEnumerator HostCopy(string[] a, Result r)
        {
            bool want = a.Length == 0 || a[0] == "here";
            if (want) _copyMark = MapScenarioIssues.LogMark();
            var l = MapScenarioGaps.Hideout(Player.Instance);
            if (l == null) { r.Fail("no hideout"); yield break; }
            float until = Time.realtimeSinceStartup + 20f;
            NPC npc = null;
            bool has = false;
            while (Time.realtimeSinceStartup < until)
            {
                npc = l.trader != null ? l.trader.GetComponent<NPC>() : null;
                var ch = npc != null ? npc.GetComponent<Character>() : null;
                has = ch != null && ch.alive && npc.inventory != null && npc.inventory.getAllItems().Count > 0;
                if (has == want) break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            var c = Singleton<Controller>.Instance;
            r.Info = $"{l.name}: {(npc != null ? $"{npc.name} with {(npc.inventory != null ? npc.inventory.getAllItems().Count : 0)} things" : "no trader")}, our morning {c.isAfterNight}";
            if (c.isAfterNight) r.Fail($"we're in a morning ourselves: {r.Info}");
            else if (has != want) r.Fail(r.Info);
        }

        /// <summary>"npc-died" (host): the night trader in the hideout died here too, through the joiner's NpcDied
        /// (taken, not refused: our copy is where theirs was), and neither that nor their dead state was refused, since the
        /// last "host-copy here". (A
        /// dead night trader is taken away at once, Character.die's removeMe, so there's no body to look at.)</summary>
        private static IEnumerator NpcDied(string[] a, Result r)
        {
            yield return new WaitForSeconds(2f);
            var l = MapScenarioGaps.Hideout(Player.Instance);
            var spot = l != null && l.traderPosition != null ? l.traderPosition.transform.position : Centre;
            var lines = MapScenarioIssues.LogLines().Skip(Math.Max(0, _copyMark)).ToList();
            var refused = lines.Where(x => x.Contains("[authority] refused") && (x.Contains("NPC") || x.Contains("state of") || x.IndexOf("trader", StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            bool got = lines.Any(x => x.Contains("got NpcDied") && x.IndexOf("trader", StringComparison.OrdinalIgnoreCase) >= 0);
            var alive = UnityEngine.Object.FindObjectsOfType<Character>().Where(c => c.isNightTrader && c.alive && StateDump.Flat(c.transform.position - spot) < 600f).ToList();
            bool flag = Singleton<Flags>.Instance.isFlagTrue("nightTrader_killedYesterday");
            r.Info = $"NpcDied came {got}, living night traders in {(l != null ? l.name : "the hideout")}: {alive.Count}, nightTrader_killedYesterday {flag}";
            if (refused.Count > 0) r.Fail($"{r.Info}; {refused[0].Trim()}");
            else if (!got || alive.Count > 0 || !flag) r.Fail(r.Info);
        }
    }
}
