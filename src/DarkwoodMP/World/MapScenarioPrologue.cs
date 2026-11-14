using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for the prologue played together (World.Prologue). The list is
    /// tools/tests/map_scenario_prologue.py; the host plays a new game (the prologue not skipped) and the client
    /// joins it in the forest after the intro film.
    /// </summary>
    public static class MapScenarioPrologue
    {
        private static Player P => Player.Instance;
        private static Dreams D => Singleton<Dreams>.Instance;

        public static void Register()
        {
            Add("prologue-in", In, timeout: 120f);
            Add("prologue-next", Next, timeout: 120f);
            Add("prologue-over", Over, timeout: 120f);
        }

        private static bool FriendSeen()
        {
            var ps = Sync.PlayerSync.Instance;
            return ps != null && ps.Remotes.Values.Any(rp => rp.Root != null && rp.Root.activeInHierarchy);
        }

        private static string Items()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var bag in new[] { P.Inventory, P.Hotbar })
                foreach (var s in bag.slots)
                    if (s != null && !InvItemClass.isNull(s.invItem)) names.Add(s.invItem.type);
            return names.Count == 0 ? "nothing" : string.Join(",", names);
        }

        /// <summary>"prologue-in STAGE" (either): we're in that stage of the prologue, dreamt together, a first play, and we see the other player.</summary>
        private static IEnumerator In(string[] a, Result r)
        {
            string stage = a.Length > 0 ? a[0] : "dream_tutorial_00";
            float until = Time.realtimeSinceStartup + 90f;
            while (!(Prologue.InStage && Prologue.Stage == stage && DreamSync.Shared == stage && WorldShare.WorldReady && FriendSeen())
                   && Time.realtimeSinceStartup < until)
                yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(1f);
            r.Info = $"stage {Prologue.Stage ?? "none"} (shared {DreamSync.Shared ?? "none"}), first play {P.firstPlay}, antagonist {P.isAntagonist}, "
                   + $"at {Locations.Current()} {Fmt(P.transform.position)}, carrying {Items()}, the other player seen {FriendSeen()}, time {Singleton<Controller>.Instance.CurrentTime}";
            if (Prologue.Stage != stage || DreamSync.Shared != stage) r.Fail($"not in the shared {stage}: {r.Info}");
            else if (!P.firstPlay) r.Fail($"not a first play: {r.Info}");
            else if (!FriendSeen()) r.Fail($"the other player isn't seen: {r.Info}");
        }

        /// <summary>"prologue-next" (either): our stage of the prologue ends as the game ends it (its outcome to the next stage, or the end); wait until we're through.</summary>
        private static IEnumerator Next(string[] a, Result r)
        {
            var d = D;
            string stage = Prologue.Stage;
            if (!Prologue.InStage) { r.Fail("not in a stage of the prologue"); yield break; }
            var outcomes = d.preset.outcomes.Where(o => o != null).ToList();
            bool last = stage == "dream_tutorial_01";
            var pick = outcomes.FirstOrDefault(o => o.name != "playerDeath"
                && o.effects.Any(e => e.type == DreamPreset.Outcome.Effect.Type.transferToDream) != last);
            string names = string.Join(",", outcomes.Select(o => o.name));
            if (pick == null) { r.Fail($"no outcome to {(last ? "the end" : "the next stage")} in {stage} ({names})"); yield break; }
            d.outcome = pick.name;
            AsPlayer(() => d.initiateEndDreaming());
            float until = Time.realtimeSinceStartup + 100f;
            while ((Prologue.Stage == stage || !WorldShare.WorldReady || (!last && !Prologue.InStage) || (last && (d.dreaming || d.switchingDream)))
                   && Time.realtimeSinceStartup < until)
                yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(2f);
            r.Info = $"ended {stage} ({pick.name} of {names}): now {Prologue.Stage ?? "out of the prologue"} at {Locations.Current()} {Fmt(P.transform.position)}";
            if (Prologue.Stage == stage) r.Fail($"still in {stage}: {r.Info}");
        }

        /// <summary>"prologue-over" (either): the prologue has ended here too: awake in the world, not a first play, the clock just after 5, beside the other player but not on them.</summary>
        private static IEnumerator Over(string[] a, Result r)
        {
            var d = D;
            float until = Time.realtimeSinceStartup + 90f;
            while ((Prologue.Active || d.dreaming || !WorldShare.WorldReady) && Time.realtimeSinceStartup < until) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(3f);
            int time = Singleton<Controller>.Instance.CurrentTime;
            float nearest = float.MaxValue;
            var ps = Sync.PlayerSync.Instance;
            if (ps != null)
                foreach (var rp in ps.Remotes.Values)
                    if (rp.Root != null && rp.Root.activeInHierarchy)
                        nearest = Mathf.Min(nearest, Vector2.Distance(new Vector2(rp.Root.transform.position.x, rp.Root.transform.position.z), new Vector2(P.transform.position.x, P.transform.position.z)));
            r.Info = $"in the prologue {Prologue.Active}, dreaming {d.dreaming}, first play {P.firstPlay}, shared dream {DreamSync.Shared ?? "none"}, at {Locations.Current()} {Fmt(P.transform.position)}, "
                   + $"time {time}, the other player {(nearest == float.MaxValue ? "not seen" : $"{nearest:0} away")}, carrying {Items()}";
            // The traders that came with the prologue (the wolf), where they are: each is in both games once.
            var wolves = StateDump.All<NPC>().Where(n => n.trader)
                .Select(n => $"{n.name} at {Fmt(n.transform.position)} under {(n.transform.parent != null ? n.transform.parent.name : "-")}/{(n.transform.parent?.parent != null ? n.transform.parent.parent.name : "-")} active {n.gameObject.activeInHierarchy} id {ObjRef.Of(n.gameObject)} ({n.inventory?.getAllItems().Count ?? -1} for sale)");
            Plugin.Log.LogInfo($"[map] traders after the prologue: {string.Join("; ", wolves)}");
            if (Prologue.Active || d.dreaming || P.firstPlay || DreamSync.Shared != null) r.Fail($"the prologue isn't over: {r.Info}");
            else if (Locations.Current() != "") r.Fail($"not in the world: {r.Info}");
            else if (time < 5 || time > 200) r.Fail($"the clock isn't just after the prologue's 5: {r.Info}");
            else if (nearest == float.MaxValue || nearest > 600f) r.Fail($"the other player isn't beside us: {r.Info}");
            else if (nearest < 20f) r.Fail($"on top of the other player: {r.Info}");
            // The host keeps our character from now on; send it now (in the prologue it isn't sent).
            if (NetEnemies.ClientMode) Characters.Instance?.SaveNow();
        }
    }
}
