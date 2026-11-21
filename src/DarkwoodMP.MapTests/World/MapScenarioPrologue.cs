using DarkwoodMP.Combat;
using DarkwoodMP.World.Dreaming;
using System.Collections;
using System.Linq;
using UnityEngine;
using Prologue = DarkwoodMP.World.Story.Prologue;
using DarkwoodMP.World.Places;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for the prologue played together (World.Prologue). The
    /// list is tools/tests/map_scenario_prologue.py; the host plays a new game
    /// (the prologue not skipped) and the client joins it in the forest after
    /// the intro film.
    /// </summary>
    public static partial class MapScenarioPrologue
    {
        private static Player P => Player.Instance;
        private static Dreams D => Singleton<Dreams>.Instance;

        public static void Register()
        {
            Add("prologue-in", In, timeout: 120f);
            Add("prologue-next", Next, timeout: 120f);
            Add("prologue-over", Over, timeout: 120f);
            Add("prologue-key-look", KeyLook);
            Add("prologue-key-take", KeyTake, timeout: 60f);
            Add("prologue-key-has", KeyHas);
            Add("prologue-key-door", KeyDoor, timeout: 60f);
            Add("prologue-key-door-is", KeyDoorIs);
            Add("prologue-key-reset", KeyReset, timeout: 60f);
        }

        private static bool FriendSeen()
        {
            var ps = Plugin.Instance.Host.Players;
            return ps != null
                && ps.Remotes.Values.Any(
                    rp => rp.Root != null && rp.Root.activeInHierarchy);
        }

        private static string Items()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var bag in new[] { P.Inventory, P.Hotbar })
            {
                foreach (var s in bag.slots)
                {
                    if (s != null && !InvItemClass.isNull(s.invItem))
                    {
                        names.Add(s.invItem.type);
                    }
                }
            }

            if (names.Count == 0)
            {
                return "nothing";
            }

            return string.Join(",", names);
        }

        /// <summary>
        /// "prologue-in STAGE" (either): we're in that stage of the prologue,
        /// dreamt together, a first play, and we see the other player.
        /// </summary>
        private static IEnumerator In(string[] a, Result r)
        {
            string stage = "dream_tutorial_00";
            if (a.Length > 0)
            {
                stage = a[0];
            }

            float until = Time.realtimeSinceStartup + 90f;
            while (!(Prologue.InStage && Prologue.Stage == stage
                    && DreamSync.SharedNow == stage && WorldShare.WorldReady
                    && FriendSeen())
                && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.5f);
            }

            yield return new WaitForSeconds(1f);
            string stageName = Prologue.Stage;
            if (stageName == null)
            {
                stageName = "none";
            }

            string shared = DreamSync.SharedNow;
            if (shared == null)
            {
                shared = "none";
            }

            r.Info =
                $"stage {stageName} (shared {shared}), first play "
                    + $"{P.firstPlay}, antagonist {P.isAntagonist}, "
                    + $"at {Whereabouts.Current()} "
                    + $"{Fmt(P.transform.position)}, "
                    + $"carrying {Items()}, the other player seen "
                    + $"{FriendSeen()}, time "
                    + $"{Singleton<Controller>.Instance.CurrentTime}";
            if (Prologue.Stage != stage || DreamSync.SharedNow != stage)
            {
                r.Fail($"not in the shared {stage}: {r.Info}");
            }
            else if (!P.firstPlay)
            {
                r.Fail($"not a first play: {r.Info}");
            }
            else if (!FriendSeen())
            {
                r.Fail($"the other player isn't seen: {r.Info}");
            }
        }

        /// <summary>
        /// "prologue-next" (either): our stage of the prologue ends as the game
        /// ends it (its outcome to the next stage, or the end); wait until
        /// we're through.
        /// </summary>
        private static IEnumerator Next(string[] a, Result r)
        {
            var d = D;
            string stage = Prologue.Stage;
            if (!Prologue.InStage)
            {
                r.Fail("not in a stage of the prologue");
                yield break;
            }

            var outcomes = d.preset.outcomes.Where(o => o != null).ToList();
            bool last = stage == "dream_tutorial_01";
            var pick = outcomes.FirstOrDefault(o => o.name != "playerDeath"
                    && o.effects.Any(
                        e => e.type
                            == DreamPreset.Outcome.Effect.Type.transferToDream)
                        != last);
            string names = string.Join(",", outcomes.Select(o => o.name));
            if (pick == null)
            {
                string text = "the next stage";
                if (last)
                {
                    text = "the end";
                }

                r.Fail($"no outcome to {text} in {stage} ({names})");
                yield break;
            }

            d.outcome = pick.name;
            AsPlayer(() => d.initiateEndDreaming());
            float until = Time.realtimeSinceStartup + 100f;
            while ((Prologue.Stage == stage || !WorldShare.WorldReady
                    || (!last && !Prologue.InStage)
                    || (last && (d.dreaming || d.switchingDream)))
                && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.5f);
            }

            yield return new WaitForSeconds(2f);
            string picked = pick.name;
            string now = Prologue.Stage;
            if (now == null)
            {
                now = "out of the prologue";
            }

            r.Info =
                $"ended {stage} ({picked} of {names}): now {now} at "
                    + $"{Whereabouts.Current()} {Fmt(P.transform.position)}";
            if (Prologue.Stage == stage)
            {
                r.Fail($"still in {stage}: {r.Info}");
            }
        }

        /// <summary>
        /// "prologue-over" (either): the prologue has ended here too: awake in
        /// the world, not a first play, the clock just after 5, beside the
        /// other player but not on them.
        /// </summary>
        private static IEnumerator Over(string[] a, Result r)
        {
            var d = D;
            float until = Time.realtimeSinceStartup + 90f;
            while ((Prologue.Active || d.dreaming || !WorldShare.WorldReady)
                && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.5f);
            }

            yield return new WaitForSeconds(3f);
            int time = Singleton<Controller>.Instance.CurrentTime;
            float nearest = float.MaxValue;
            var ps = Plugin.Instance.Host.Players;
            if (ps != null)
            {
                foreach (var rp in ps.Remotes.Values)
                {
                    if (rp.Root != null && rp.Root.activeInHierarchy)
                    {
                        nearest = Mathf.Min(
                            nearest,
                            Vector2.Distance(
                                new Vector2(
                                    rp.Root.transform.position.x,
                                    rp.Root.transform.position.z),
                                new Vector2(
                                    P.transform.position.x,
                                    P.transform.position.z)));
                    }
                }
            }

            bool active = Prologue.Active;
            bool dreaming = d.dreaming;
            bool first = P.firstPlay;
            string shared = DreamSync.SharedNow;
            if (shared == null)
            {
                shared = "none";
            }

            string line =
                $"in the prologue {active}, dreaming {dreaming}, first play "
                    + $"{first}, shared dream {shared}, at "
                    + $"{Whereabouts.Current()} {Fmt(P.transform.position)}, ";
            string other;
            if (nearest == float.MaxValue)
            {
                other = "not seen";
            }
            else
            {
                other = $"{nearest:0} away";
            }

            r.Info = line
                + $"time {time}, the other player {other}, carrying {Items()}";
            // The traders that came with the prologue (the wolf), where they
            // are: each is in both games once.
            var wolves = StateDump.All<NPC>().Where(n => n.trader)
                .Select(n =>
                {
                    string trader = n.name;
                    string spot = Fmt(n.transform.position);
                    string parent = "-";
                    if (n.transform.parent != null)
                    {
                        parent = n.transform.parent.name;
                    }

                    Transform above = n.transform.parent;
                    string grand = "-";
                    if (above != null && above.parent != null)
                    {
                        grand = n.transform.parent.parent.name;
                    }

                    bool shown = n.gameObject.activeInHierarchy;
                    ObjRef id = ObjRef.Of(n.gameObject);
                    Inventory stock = n.inventory;
                    int count = -1;
                    if (stock != null)
                    {
                        count = stock.getAllItems().Count;
                    }

                    return $"{trader} at {spot} under {parent}/{grand} active "
                        + $"{shown} id {id} ({count} for sale)";
                });

            Plugin.Log.LogInfo(
                "[map] traders after the prologue: "
                    + $"{string.Join("; ", wolves)}");
            if (Prologue.Active || d.dreaming || P.firstPlay
                || DreamSync.SharedNow != null)
            {
                r.Fail($"the prologue isn't over: {r.Info}");
            }
            else if (Whereabouts.Current() != "")
            {
                r.Fail($"not in the world: {r.Info}");
            }
            else if (time < 5 || time > 200)
            {
                r.Fail(
                    $"the clock isn't just after the prologue's 5: {r.Info}");
            }
            else if (nearest == float.MaxValue || nearest > 600f)
            {
                r.Fail($"the other player isn't beside us: {r.Info}");
            }
            else if (nearest < 20f)
            {
                r.Fail($"on top of the other player: {r.Info}");
            }

            // The host keeps our character from now on; send it now (in the
            // prologue it isn't sent).
            if (NetEnemies.ClientMode)
            {
                CoopSession coop = Coop.Current;
                if (coop != null)
                {
                    coop.Characters.SaveNow();
                }
            }
        }
    }
}
