using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioGaps
    {
        // ---- 62, 63, 38 the morning -----------------------------------------

        internal static Location Hideout(Player p)
        {
            p.whereAmI.checkWhereAmI();
            var here = p.whereAmI.bigLocation;
            if (here != null && here.playerBase)
            {
                return here;
            }

            var wg = Singleton<WorldGenerator>.Instance;
            if (wg != null)
            {
                return wg.locations.Where(
                    l => l != null && l.playerBase && !l.isSubLocation)
                    .OrderBy(
                        l => StateDump.Flat(
                            l.transform.position
                                - p.transform.position)).FirstOrDefault();
            }

            return null;
        }

        private static NPC MorningNpc(Location l)
        {
            if (l == null)
            {
                return null;
            }

            GameObject go;
            if (l.trader != null)
            {
                go = l.trader;
            }
            else
            {
                go = l.wolf;
            }

            if (go != null)
            {
                return go.GetComponent<NPC>();
            }

            return null;
        }

        /// <summary>"an-start [noreward] [late]": our player in their hideout
        /// at dawn: the game's morning (Controller.startAfterNight, which dawn
        /// calls in each game): the trader or the wolf comes, the clock is
        /// frozen until we walk out.</summary>
        private static IEnumerator AnStart(string[] a, Result r)
        {
            var p = Player.Instance;
            var c = Singleton<Controller>.Instance;
            var l = Hideout(p);
            if (l == null)
            {
                r.Fail("no hideout");
                yield break;
            }

            if (Location.getAtPos(p.transform.position) != l)
            {
                Vector3 to;
                if (l.traderPosition != null)
                {
                    to = l.traderPosition.transform.position;
                }
                else
                {
                    to = l.transform.position;
                }

                yield return Walk(to, 150f);
                yield return new WaitForSecondsRealtime(0.5f);
            }

            if (Location.getAtPos(p.transform.position) != l)
            {
                r.Fail(
                    $"couldn't get into {l.name} (at "
                        + $"{Fmt(p.transform.position)})");
                yield break;
            }

            bool reward = !a.Contains("noreward");
            c.startAfterNight(reward);
            // late: the day's morning is already over, so the host ends this
            // one at once; here only that it began counts.
            bool late = a.Contains("late"), began = c.isAfterNight;
            yield return new WaitForSecondsRealtime(1f);
            var npc = MorningNpc(l);
            if (late)
            {
                r.Info =
                    $"{l.name}: began {began}, afterNight now {c.isAfterNight}";
                if (!began)
                {
                    r.Fail(r.Info);
                }

                yield break;
            }

            bool frozen =
                p.effects.hasEffectType(CharacterEffectType.timeFreeze);
            string head =
                $"{l.name}: afterNight {c.isAfterNight}, frozen "
                    + $"{frozen}, ";
            string who = "no trader or wolf";
            if (npc != null)
            {
                who = npc.name + " " + ObjRef.Of(npc.gameObject);
            }

            r.Info = head + who;
            if (!c.isAfterNight)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"an-dawn" (host): a new dawn in our game while our player
        /// isn't in a hideout (no morning of our own): the day's morning isn't
        /// over yet, so a joiner's morning stays until someone walks out
        /// (AfterNight.HostDawn).</summary>
        private static IEnumerator AnDawn(string[] a, Result r)
        {
            AfterNight.HostDawn();
            r.Info = "a new morning";
            yield break;
        }

        /// <summary>"an-end": our player's morning ends, as walking out of the
        /// hideout does (Location.OnTriggerExit -> endAfterNight, and on a
        /// joiner AfterNightWalkOutPatch telling the host).</summary>
        private static IEnumerator AnEnd(string[] a, Result r)
        {
            var c = Singleton<Controller>.Instance;
            if (!c.isAfterNight)
            {
                r.Fail("not in a morning");
                yield break;
            }

            AsPlayer(() =>
            {
                c.endAfterNight();
                if (!c.isAfterNight)
                {
                    AfterNight.LocalWalkedOut();
                }
            });

            yield return new WaitForSecondsRealtime(0.5f);
            var hl = Hideout(Player.Instance);
            string text = "gone";
            if (hl != null && hl.trader != null)
            {
                text = "still here";
            }

            r.Info = $"afterNight {c.isAfterNight}, trader {text}";
            if (c.isAfterNight)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// "an-check on|off": whether our game is in its morning (waiting up to
        /// 6 s); off: the trader has gone too.
        /// </summary>
        private static IEnumerator AnCheck(string[] a, Result r)
        {
            var c = Singleton<Controller>.Instance;
            bool want = a.Length == 0 || a[0] == "on";
            float until = Time.realtimeSinceStartup + 6f;
            while (c.isAfterNight != want && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }

            yield return new WaitForSecondsRealtime(0.5f);
            var l = Hideout(Player.Instance);
            // The trader only, not the wolf: the wolf stays (each game's own).
            NPC npc = null;
            if (l != null && l.trader != null)
            {
                npc = l.trader.GetComponent<NPC>();
            }

            bool frozen = Player.Instance.effects.hasEffectType(
                CharacterEffectType.timeFreeze);
            string traderName = "gone";
            if (npc != null)
            {
                traderName = npc.name;
            }

            string text = "";
            if (l != null && l.wolf != null)
            {
                text = ", the wolf here";
            }

            r.Info =
                $"afterNight {c.isAfterNight}, frozen {frozen}, trader "
                    + $"{traderName}{text}";
            if (c.isAfterNight != want || (!want && (npc != null || frozen)))
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"an-clock running|stopped" (host): whether the world clock
        /// goes on during our morning (waiting up to 15 s for it, after the
        /// joiners' grace), checked over the next few game minutes.</summary>
        private static IEnumerator AnClock(string[] a, Result r)
        {
            var c = Singleton<Controller>.Instance;
            bool want = a.Length > 0 && a[0] == "running";
            if (!c.isAfterNight)
            {
                r.Fail("not in a morning");
                yield break;
            }

            float until = Time.realtimeSinceStartup + 15f;
            while (c.DoUpdateTime != want && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }

            int t0 = c.CurrentTime;
            float wait = Mathf.Max(3f, c.timeChangeInterval * 3f);
            yield return new WaitForSecondsRealtime(wait);
            int moved = ((c.CurrentTime - t0) % 1440 + 1440) % 1440;
            r.Info =
                $"DoUpdateTime {c.DoUpdateTime}, clock {t0} -> {c.CurrentTime} "
                    + $"in {wait:0.#} s";
            bool wrong = c.DoUpdateTime != want;
            if (!wrong)
            {
                if (want)
                {
                    wrong = moved == 0;
                }
                else
                {
                    wrong = moved != 0;
                }
            }

            if (wrong)
            {
                string text = "stopped";
                if (want)
                {
                    text = "running";
                }

                r.Fail(r.Info + $", wanted {text}");
            }
        }

        private static NPC _talkingTo;

        /// <summary>"talk [--refused]": our player talks to the morning's
        /// trader (or wolf) in our hideout (NPC.talkTo). With --refused,
        /// someone else is talking to them: the host says so and our dialogue
        /// closes again.</summary>
        private static IEnumerator Talk(string[] a, Result r)
        {
            var p = Player.Instance;
            var npc = MorningNpc(Hideout(p));
            if (npc == null)
            {
                r.Fail("no trader or wolf in our hideout");
                yield break;
            }

            if (!npc.wantsToTalk || npc.characterDialogue == null)
            {
                r.Fail(
                    $"{npc.name} doesn't want to talk (wantsToTalk "
                        + $"{npc.wantsToTalk})");
                yield break;
            }

            bool expectRefused = a.Contains("--refused");
            yield return Walk(npc.transform.position, 150f);
            int mark = MapScenarioIssues.LogMark();
            var win = Singleton<UI>.Instance.dialogueWindow;
            AsPlayer(() => npc.talkTo());
            bool host =
                Net.Session.Instance == null || Net.Session.Instance.IsHost;
            if (expectRefused)
            {
                float until = Time.realtimeSinceStartup + 12f;
                while (Time.realtimeSinceStartup < until
                    && (win.npc == npc || !LogSince(mark).Any(
                        l => !l.Contains("[map]")
                            && l.Contains("is talking to them"))))
                {
                    yield return new WaitForSecondsRealtime(0.25f);
                }

                bool told = LogSince(mark).Any(
                    l => !l.Contains("[map]")
                        && l.Contains("is talking to them"));
                string name = npc.name;
                ObjRef id = ObjRef.Of(npc.gameObject);
                string dialogue = "closed";
                if (win.npc == npc)
                {
                    dialogue = "still open";
                }

                r.Info = $"{name} {id}: told {told}, dialogue {dialogue}";
                if (!told || win.npc == npc)
                {
                    r.Fail(r.Info);
                }

                if (win.npc == npc)
                {
                    yield return Close(win, npc);
                }

                yield break;
            }

            float time = 2f;
            if (host)
            {
                time = 1.5f;
            }

            yield return new WaitForSecondsRealtime(time);
            var refused = LogSince(mark).Where(l => !l.Contains("[map]"))
                .Where(
                    l => l.Contains("is talking to them")
                        || l.Contains("just talked to them")
                        || (l.Contains("refused")
                            && l.Contains("talk"))).ToList();
            string talker = npc.name;
            ObjRef talkerId = ObjRef.Of(npc.gameObject);
            string state = "not open";
            if (win.npc == npc)
            {
                state = "open";
            }

            r.Info = $"{talker} {talkerId}: dialogue {state}";
            if (refused.Count > 0)
            {
                r.Fail($"refused: {refused[0].Trim()}");
            }
            else if (win.npc != npc)
            {
                r.Fail(r.Info);
            }

            _talkingTo = npc;
        }

        internal static IEnumerator Close(DialogueWindow win, NPC npc)
        {
            for (int i = 0; i < 40 && win.npc == npc && npc != null; i++)
            {
                if (!win.tweening && Player.Instance.inDialogue)
                {
                    // Not from the main menu, which shows the goodbye line
                    // instead (see TalkLock.CloseWhenReady). This runs nested
                    // in the step, where an exception would end it without a
                    // reply: catch it.
                    win.currentMenu = DialogueWindow.CurrentMenu.gossip;
                    try
                    {
                        win.close();
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning(
                            $"[map] closing the dialogue: {e.Message}");
                    }
                }

                yield return new WaitForSecondsRealtime(0.3f);
            }
        }

        /// <summary>
        /// "trade-open": in our dialogue (after "talk"), open the trade window
        /// (DialogueWindow.openTrade).
        /// </summary>
        private static IEnumerator TradeOpen(string[] a, Result r)
        {
            var win = Singleton<UI>.Instance.dialogueWindow;
            var p = Player.Instance;
            if (win.npc == null || !win.npc.trader)
            {
                string talkingTo = "no one";
                if (win.npc != null)
                {
                    talkingTo = win.npc.name;
                }

                r.Fail($"not talking to a trader ({talkingTo})");
                yield break;
            }

            float until = Time.realtimeSinceStartup + 8f;
            while ((win.tweening || !p.inDialogue)
                && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }

            try
            {
                win.openTrade();
            }
            catch (Exception e)
            {
                r.Fail($"openTrade: {e.Message}");
                yield break;
            }

            yield return new WaitForSecondsRealtime(1f);
            NPC npc = win.npc;
            string name = null;
            if (npc != null)
            {
                name = npc.name;
            }

            r.Info = $"{name}: inShop {p.inShop}";
            if (!p.inShop)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"talk-free": our dialogue (and trade) closed by itself, and
        /// our player has the controls back (issue #15: the host walked out of
        /// the hideout while we traded, and the trader left our game with us
        /// stuck in it).</summary>
        private static IEnumerator TalkFree(string[] a, Result r)
        {
            var win = Singleton<UI>.Instance.dialogueWindow;
            var p = Player.Instance;
            bool Free() => !p.inDialogue && !p.inShop && !p.performingAction
                && win.npc == null && !win.gameObject.activeSelf;
            float until = Time.realtimeSinceStartup + 15f;
            while (!Free() && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }

            // the fade back and Core.forbidInputsFor
            yield return new WaitForSecondsRealtime(1.5f);
            string text = "closed";
            if (win.gameObject.activeSelf)
            {
                text = "open";
            }

            r.Info =
                $"inDialogue {p.inDialogue}, inShop {p.inShop}, "
                    + $"performingAction {p.performingAction}, window " +
                     $"{text}, timeScale {Time.timeScale:0.##}, afterNight "
                    + $"{Singleton<Controller>.Instance.isAfterNight}";
            if (!Free() || Time.timeScale <= 0f)
            {
                r.Fail(r.Info);
            }

            _talkingTo = null;
        }

        /// <summary>"talk-end": our dialogue closes.</summary>
        private static IEnumerator TalkEnd(string[] a, Result r)
        {
            var win = Singleton<UI>.Instance.dialogueWindow;
            NPC npc = _talkingTo;
            if (npc == null)
            {
                npc = win.npc;
            }

            if (npc == null)
            {
                r.Fail("not talking to anyone");
                yield break;
            }

            yield return Close(win, npc);
            _talkingTo = null;
            string text = "still open";
            if (win.npc == null)
            {
                text = "closed";
            }

            r.Info = $"dialogue {text}";
            if (win.npc != null)
            {
                r.Fail(r.Info);
            }
        }
    }
}
