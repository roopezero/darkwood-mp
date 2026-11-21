using System.Collections;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>The trader's steps (ui-trader-*).</summary>
    public static partial class MapScenarioUi
    {
        // ---- the trader -----------------------------------------------------

        private static NPC _trader;

        private static int _traderMark;

        /// <summary>"ui-trader-show": stand at the trader with his stock's
        /// window shown, as the trade window shows it (DialogueWindow: npc's
        /// Inventory.show, Player.inShop).</summary>
        private static IEnumerator TraderShow(string[] a, Result r)
        {
            var npc = MapScenarioWorld.Trader();
            if (npc == null)
            {
                r.Fail("no trader here");
                yield break;
            }

            yield return Walk(npc.transform.position, 150f);
            P.inShop = true;
            P.talkedToNPC = npc;
            npc.inventory.show();
            _trader = npc;
            yield return new WaitForSeconds(1f);
            _traderMark = MapScenarioIssues.LogMark();
            string drawn = Drawn(npc.inventory);
            r.Info =
                $"{npc.name}: {npc.inventory.slots.Count} slots, "
                    + $"{npc.inventory.getAllItems().Count} items shown";
            if (drawn != null)
            {
                r.Fail($"drawn wrong as shown: {drawn}");
            }
        }

        /// <summary>
        /// "ui-trader-check": the host's new stock reached the shown window
        /// (still open) and it draws it.
        /// </summary>
        private static IEnumerator TraderCheck(string[] a, Result r)
        {
            var npc = _trader;
            if (npc == null || npc.inventory == null)
            {
                r.Fail("no trader shown");
                yield break;
            }

            float until = Time.realtimeSinceStartup + 6f;
            string line = null;
            while (line == null && Time.realtimeSinceStartup < until)
            {
                line = MapScenarioIssues.LogLines().Skip(_traderMark)
                    .FirstOrDefault(l => l.Contains("has the host's stock"));
                if (line == null)
                {
                    yield return new WaitForSecondsRealtime(0.5f);
                }
            }

            yield return null;
            yield return null;
            string drawn = Drawn(npc.inventory);
            r.Info =
                $"{npc.name}: {npc.inventory.getAllItems().Count} items, "
                    + $"open={npc.inventory.open}; {line}";
            if (line == null)
            {
                r.Fail("no new stock from the host");
            }
            else if (!npc.inventory.open)
            {
                r.Fail($"{r.Info}: the new stock closed his window");
            }
            else if (drawn != null)
            {
                r.Fail($"the window shows something else: {drawn}");
            }
        }

        private static IEnumerator TraderHide(string[] a, Result r)
        {
            if (_trader != null && _trader.inventory != null
                && _trader.inventory.open)
            {
                _trader.inventory.hide();
            }

            P.inShop = false;
            P.talkedToNPC = null;
            _trader = null;
            yield return new WaitForSeconds(0.5f);
            r.Info = "hidden";
        }
    }
}
