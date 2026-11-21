using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Steps for one trader, two players.</summary>
    public static partial class MapScenarioMulti
    {
        // ---- 3. one trader, two players -------------------------------------

        /// <summary>"mt-talk [--refused]": talk to the trader nearest the
        /// centre (NPC.talkTo) and stay in the dialogue (talk-end closes it).
        /// With --refused, someone else is talking to him: we're told so and
        /// ours closes.</summary>
        private static IEnumerator Talk(string[] a, Result r)
        {
            var npc = MapScenarioWorld.Trader();
            if (npc == null)
            {
                r.Fail("no trader here");
                yield break;
            }

            bool expectRefused = a.Contains("--refused");
            yield return Walk(npc.transform.position, 150f);
            int mark = MapScenarioIssues.LogMark();
            var win = Singleton<UI>.Instance.dialogueWindow;
            AsPlayer(() => npc.talkTo());
            float wait = 2.5f;
            if (expectRefused)
            {
                wait = 8f;
            }

            float until = Time.realtimeSinceStartup + wait;
            bool Told() => MapScenarioIssues.LogLines().Skip(mark)
                .Any(
                    l => !l.Contains("[map]")
                        && (l.Contains("is talking to them") || l.Contains(
                            "just talked to them")));
            while (Time.realtimeSinceStartup < until
                && !(expectRefused && Told() && win.npc != npc))
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }

            bool told = Told();
            string name = npc.name;
            ObjRef id = ObjRef.Of(npc.gameObject);
            string dialogue = "closed";
            if (win.npc == npc)
            {
                dialogue = "open";
            }

            r.Info =
                $"{name} {id}: told someone's talking {told}, dialogue "
                    + $"{dialogue}";
            if (expectRefused)
            {
                if (!told || win.npc == npc)
                {
                    r.Fail(r.Info);
                }

                if (win.npc == npc)
                {
                    yield return MapScenarioGaps.Close(win, npc);
                }
            }
            else if (told || win.npc != npc)
            {
                r.Fail(r.Info);
            }
        }
    }
}
