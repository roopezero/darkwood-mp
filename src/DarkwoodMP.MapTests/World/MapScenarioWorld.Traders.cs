using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Steps for traders: restocks and trades.</summary>
    public static partial class MapScenarioWorld
    {
        internal static NPC Trader() => Nearest(
            Object.FindObjectsOfType<NPC>().Where(
                n => n.trader && n.inventory != null),
            Centre);

        /// <summary>
        /// "trader-restock": the trader stocks up, as on a new day (the host's
        /// to do).
        /// </summary>
        private static IEnumerator TraderRestock(string[] a, Result r)
        {
            var npc = Trader();
            if (npc == null)
            {
                r.Fail("no trader here");
                yield break;
            }

            string was = Contents(npc.inventory);
            AccessTools.Method(typeof(NPC), "randomizeTraderInv").Invoke(
                npc, null);
            yield return new WaitForSeconds(1f);
            r.Info = $"{npc.name}: [{was}] -> [{Contents(npc.inventory)}]";
        }

        /// <summary>"trade": with the trade window open, give the trader things
        /// from our bags worth at least his cheapest thing and take that (as
        /// DialogueWindow.acceptTrade moves them), the difference in value onto
        /// the reputation as acceptTrade does. A client's trade goes to the
        /// host, which checks it against his stock; a refusal (rolled back
        /// here) fails the step. The dumps then compare his stock in both
        /// games.
        /// "trade --race AT": the same, at epoch second AT (players trading
        /// with him at the same moment); a refusal doesn't fail it, the reply
        /// says "got ..." or "refused ...".
        /// "trade --earn": his reputation is brought down to 0 (and that
        /// reaches the host), then our player earns as much as his cheapest
        /// thing is worth and, in the same frame, takes it for nothing, paid
        /// with that reputation: the host must have the earned reputation when
        /// it checks the trade (accepted).</summary>
        private static IEnumerator TradeSeen(string[] a, Result r)
        {
            yield return new WaitForSeconds(1f);
            var lines = MapScenarioIssues.LogLines();
            string took = lines.LastOrDefault(
                l => l.Contains(" traded with ") && l.Contains(": took "));
            string refused = lines.LastOrDefault(
                l => l.Contains("[authority] refused TraderTrade"));
            string answer = took;
            if (answer == null)
            {
                answer = refused;
            }

            if (answer == null)
            {
                answer = "no client's trade here";
            }

            r.Info = answer.Trim();
            if (refused != null)
            {
                r.Fail($"refused: {refused.Trim()}");
            }
            else if (took == null)
            {
                r.Fail(r.Info);
            }
        }

        private static IEnumerator Trade(string[] a, Result r)
        {
            bool earn = Array.IndexOf(a, "--earn") >= 0;
            int ri = Array.IndexOf(a, "--race");
            double raceAt = 0;
            if (ri >= 0 && a.Length > ri + 1)
            {
                raceAt = double.Parse(a[ri + 1], CultureInfo.InvariantCulture);
            }

            var npc = Trader();
            if (npc == null)
            {
                r.Fail("no trader here");
                yield break;
            }

            yield return Walk(npc.transform.position, 150f);
            // His cheapest thing, paid for with things from our bags (not the
            // hotbar: later steps use what we hold), the most valuable first,
            // until they're worth as much.
            var slot = npc.inventory.slots.Where(
                s => s != null && !InvItemClass.isNull(s.invItem)
                    && (!earn || s.invItem.getSellValue() > 0))
                .OrderBy(s => s.invItem.getSellValue()).FirstOrDefault();
            if (slot == null)
            {
                r.Fail($"{npc.name} has nothing");
                yield break;
            }

            string boughtType = slot.invItem.type;
            int boughtValue = slot.invItem.getSellValue();
            var gave = new Dictionary<string, int>();
            int giveValue = 0;
            var ours = P.Inventory.getAllItems()
                .Where(
                    i => !InvItemClass.isNull(i)
                        && !string.IsNullOrEmpty(i.type) && i.amount > 0
                        && i.type != boughtType
                        && i.getSellValue() > 0).ToList();
            // One thing that pays for it, the cheapest such; else several, the
            // most valuable first.
            var one = ours.Where(i => i.getSellValue() >= boughtValue)
                .OrderBy(i => i.getSellValue()).FirstOrDefault();
            if (earn)
            {
                ours.Clear(); // nothing given: the reputation pays
            }

            List<InvItemClass> items;
            if (earn)
            {
                items = new List<InvItemClass>();
            }
            else if (one != null)
            {
                items = new List<InvItemClass> { one };
            }
            else
            {
                items = ours.OrderByDescending(i => i.getSellValue()).ToList();
            }

            foreach (var it in items)
            {
                for (int n = 0; n < Math.Max(1, it.amount)
                    && giveValue < boughtValue; n++)
                {
                    gave.TryGetValue(it.type, out int had);
                    gave[it.type] = had + 1;
                    giveValue += it.getSellValue();
                }
            }

            int repChange = giveValue - boughtValue;
            string giveText =
                string.Join(",", gave.Select(kv => $"{kv.Key}x{kv.Value}"));
            if (earn)
            {
                // Down to 0 first, shared as any reputation change is, so the
                // host's copy can't pay for the trade by itself.
                npc.reputation = 0;
                CoopSession coop = Coop.Current;
                if (coop != null)
                {
                    coop.Story.FlushNow();
                }

                yield return new WaitForSeconds(3f);
            }

            if (!earn && npc.reputation + repChange < 0)
            {
                r.Fail(
                    $"our bags ({giveText}: {giveValue}) don't pay for "
                        + $"{boughtType} ({boughtValue}) at reputation "
                        + $"{npc.reputation}");
                yield break;
            }

            P.inShop = true;
            P.talkedToNPC = npc;
            yield return new WaitForSeconds(0.8f);
            if (ri >= 0)
            {
                while (MapScenarioSavage.Now < raceAt)
                {
                    yield return null;
                }

                // What's cheapest may have changed meanwhile; the same slot as
                // the others, as it is now.
                if (InvItemClass.isNull(slot.invItem)
                    || slot.invItem.type != boughtType)
                {
                    P.inShop = false;
                    P.talkedToNPC = null;
                    r.Fail($"{boughtType} gone from his slot before the race");
                    yield break;
                }
            }

            string was = Contents(npc.inventory);
            int mark = MapScenarioIssues.LogMark();
            AsPlayer(() =>
            {
                if (earn)
                {
                    // earned just now (as a dialogue's reward): only our game
                    // has it yet
                    npc.reputation += boughtValue;
                }

                // His thing to us, ours to him
                // (exchangeTrader.transferAllItemsToPlayer,
                // exchangePlayer.transferAllItemsTo).
                if (slot.invItem.amount > 1)
                {
                    slot.invItem.amount -= 1;
                }
                else
                {
                    slot.removeItem();
                }

                P.Inventory.addItemTypeToPlayer(
                    boughtType, 1, dropIfNoRoom: true);
                foreach (var kv in gave)
                {
                    P.Inventory.removeItemAmountFromPlayer(kv.Key, kv.Value);
                    npc.inventory.addItemType(kv.Key, kv.Value);
                }

                npc.reputation += repChange;
                // What the AcceptTrade patch does after acceptTrade.
                CoopSession coop = Coop.Current;
                if (coop != null)
                {
                    coop.Traders.LocalTrade(npc, new Dictionary<string, int>
                    {
                        [boughtType] = 1
                    }, gave, repChange);
                }
            });

            yield return new WaitForSeconds(1.5f);
            P.inShop = false;
            P.talkedToNPC = null;
            yield return new WaitForSeconds(1f);
            string refused = MapScenarioIssues.LogLines().Skip(mark)
                .FirstOrDefault(l => l.Contains("refused trade with"));
            string text;
            if (earn)
            {
                text = $"nothing, earned {boughtValue} reputation first";
            }
            else
            {
                text = giveText;
            }

            r.Info =
                $"{npc.name}: bought {boughtType} ({boughtValue}), gave {text} "
                    + $"({giveValue}): [{was}] -> [{Contents(npc.inventory)}], "
                    + $"reputation {npc.reputation}";
            if (ri >= 0)
            {
                string outcome = "got ";
                if (refused != null)
                {
                    outcome = "refused ";
                }

                r.Info = outcome + r.Info;
                yield break;
            }

            if (refused != null)
            {
                r.Fail($"{r.Info}: {refused.Trim()}");
            }
            else if (Contents(npc.inventory) == was)
            {
                r.Fail($"{r.Info}: his stock didn't change");
            }
        }
    }
}
