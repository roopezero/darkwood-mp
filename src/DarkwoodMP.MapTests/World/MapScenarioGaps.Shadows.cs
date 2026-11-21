using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioGaps
    {
        // ---- 66 shadow-covered items ----------------------------------------

        /// <summary>"shadow-ready" (both games): the crate's Item points at its
        /// shadow. The game finds it in Item.Awake, before a save's ShadowArmor
        /// is added to a prefab that has none, which ours (made for the test)
        /// hasn't.</summary>
        private static IEnumerator ShadowReady(string[] a, Result r)
        {
            var crate = ShadowCrate();
            if (crate == null)
            {
                r.Fail("no crate at the shadow spot");
                yield break;
            }

            var sa = crate.GetComponent<ShadowArmor>();
            if (sa == null)
            {
                r.Fail(
                    $"{crate.name} {ObjRef.Of(crate.gameObject)} has no "
                        + "shadow");
                yield break;
            }

            crate.shadowArmor = sa;
            r.Info =
                $"{crate.name} "
                    + $"{ObjRef.Of(crate.gameObject)}: "
                    + $"shadow {BreakSync.ShadowHealth(crate):0.#} "
                    + $"of {sa.maxHealth:0}, item health {crate.health}";
        }

        /// <summary>
        /// "shadow-hit DAMAGE": our player hits the crate (Item.getHit), which
        /// only the shadow takes.
        /// </summary>
        private static IEnumerator ShadowHit(string[] a, Result r)
        {
            var p = Player.Instance;
            var crate = ShadowCrate();
            if (crate == null || crate.shadowArmor == null)
            {
                r.Fail("no shadow-covered crate");
                yield break;
            }

            int dmg = 10;
            if (a.Length > 0)
            {
                dmg = int.Parse(a[0]);
            }

            yield return Walk(crate.transform.position, 150f);
            int hp = crate.health;
            int mark = MapScenarioIssues.LogMark();
            AsPlayer(() => crate.getHit(dmg, p.transform));
            yield return new WaitForSecondsRealtime(1.5f);
            var refused = Refused(mark);
            r.Info =
                $"shadow {BreakSync.ShadowHealth(crate):0.#}, item health {hp} "
                    + $"-> {crate.health}";
            if (refused.Count > 0)
            {
                r.Fail($"refused: {refused[0].Trim()}");
            }
            else if (crate.health != hp)
            {
                r.Fail($"the item took the hit, not the shadow: {r.Info}");
            }
        }

        /// <summary>"shadow-check alive MAXHEALTH|dead": the crate's shadow
        /// here (waiting up to 15 s): alive and going down to at most
        /// MAXHEALTH, or gone; the crate itself unhurt.</summary>
        private static IEnumerator ShadowCheck(string[] a, Result r)
        {
            var crate = ShadowCrate();
            if (crate == null)
            {
                r.Fail("no crate at the shadow spot");
                yield break;
            }

            bool alive = a.Length == 0 || a[0] == "alive";
            float at = 1e9f;
            if (a.Length > 1)
            {
                at = F(a[1]);
            }

            bool Ok()
            {
                var sa = crate.GetComponent<ShadowArmor>();
                if (alive)
                {
                    return sa != null
                        && BreakSync.ShadowHealth(crate) <= at + 0.01f;
                }

                return sa == null;
            }

            float until = Time.realtimeSinceStartup + 15f;
            while (!Ok() && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }

            var s = crate.GetComponent<ShadowArmor>();
            string text = "gone";
            if (s != null)
            {
                text = $"alive, going to {BreakSync.ShadowHealth(crate):0.#}";
            }

            r.Info =
                $"shadow {text}, item health {crate.health}, destroyed "
                    + $"{crate.destroyed}";
            if (!Ok() || crate.destroyed)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"shadow-reload": the crate is loaded again from a save that
        /// says its shadow is dead, as a joiner who joins after it died loads
        /// it: the prefab's shadow is back, then Item.SaveState.loadValues
        /// (with no shadow state) kills it with die(instant: true). The shadow
        /// isn't checked here; shadow-check dead does that.</summary>
        private static IEnumerator ShadowReload(string[] a, Result r)
        {
            var crate = ShadowCrate();
            if (crate == null)
            {
                r.Fail("no crate at the shadow spot");
                yield break;
            }

            if (crate.GetComponent<ShadowArmor>() != null)
            {
                r.Fail("the crate's shadow is still alive before the reload");
                yield break;
            }

            var saved = new Item.SaveState(crate); // no shadow, so no saS
            crate.shadowArmor = crate.gameObject.AddComponent<ShadowArmor>();
            yield return null; // its Start makes the effect
            yield return null;
            saved.loadValues(crate);
            yield return null;
            string text = "gone";
            if (crate.GetComponent<ShadowArmor>() != null)
            {
                text = "alive";
            }

            r.Info = $"after loadValues: shadow {text}";
        }
    }
}
