using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Traders;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// TraderSync: trades, and what the patches hand over.
    /// </summary>
    internal sealed partial class TraderSync
    {
        /// <summary>
        /// Stock applied from the host, per trader: a refused trade the host
        /// sent no stock for is undone here.
        /// </summary>
        // by the NPC's instance id
        private readonly Dictionary<int, int> _applied =
            new Dictionary<int, int>();

        /// <summary>
        /// Our player's trade with this trader was accepted (the stock in
        /// npc.inventory is the result): share it. took/gave: item type ->
        /// count; repChange: what the trade did to the reputation (undone if
        /// refused).
        /// </summary>
        internal void LocalTrade(
            NPC npc, Dictionary<string, int> took, Dictionary<string, int> gave,
            int repChange)
        {
            if (npc == null)
            {
                return;
            }

            var send = TradeRules.Share(new TradeShareFacts
            {
                Connected = _session.Connected,
                HasInventory = npc.inventory != null,
                IsHost = _session.IsHost,
                Peers = _session.Peers.Count,
            });

            if (send == TradeSend.None)
            {
                return;
            }

            string name = npc.name, stock = StockOf(npc);
            // it goes with the trade
            FlagsSync instance = FlagsSync.Instance;
            if (instance != null)
            {
                instance.Absorb(name, repChange);
            }

            // Reputation our player earned just before (in the same dialogue,
            // or since the last two-second FlagsDelta) goes first, on the same
            // reliable Control channel, so the host's copy has it when it
            // checks the trade's (CheckReputation). The trade's own change was
            // absorbed above, so it isn't sent twice. Before WriteTrade: the
            // flush builds its message with Session.Begin, in the same writer
            // the trade's is built in.
            if (!_session.IsHost)
            {
                FlagsSync flags = FlagsSync.Instance;
                if (flags != null)
                {
                    flags.FlushNow();
                }
            }

            var at = npc.transform.position;
            var who = ObjRef.Of(npc.gameObject);
            var w = WriteTrade(new Trade
            {
                Trader = WhoOf(name, who, at),
                Took = took,
                Gave = gave,
                Stock = stock,
                Change = repChange,
            });

            Plugin.Log.LogInfo(
                $"Traded with {name}: took {Text(took)}, gave {Text(gave)}");
            if (send == TradeSend.ToAll)
            {
                _session.Broadcast(w, Channel.Control, reliable: true);
                return;
            }

            _applied.TryGetValue(npc.GetInstanceID(), out int seen);
            // for InventoryWatch.Undo: + what the player got, - what they gave
            var change = TradeRules.Undo(took, gave);

            // The session may have ended by the time the answer comes (it
            // then runs as refused), so keep this session's Authority.
            Authority authority = _coop.Authority;
            authority.Request(
                w, $"trade with {name}", (ok, reason, result) =>
            {
                if (ok)
                {
                    return;
                }

                authority.Inventory.Undo(change);
                NPC t;
                if (npc != null)
                {
                    t = npc;
                }
                else
                {
                    t = Find(name, who, at);
                }

                if (t == null)
                {
                    return;
                }

                if (repChange != 0)
                {
                    if (FlagsSync.Instance != null)
                    {
                        FlagsSync.Instance.AddReputation(name, -repChange);
                    }
                    else
                    {
                        t.reputation -= repChange;
                    }
                }

                _applied.TryGetValue(t.GetInstanceID(), out int now);
                if (!TradeRules.UndoesStock(seen, now, t.inventory != null))
                {
                    return; // the host's stock came: that's what he has
                }

                Coop.Guards.Replaying = true;
                try
                {
                    foreach (var kv in gave)
                    {
                        t.inventory.removeItemAmount(kv.Key, kv.Value);
                    }

                    foreach (var kv in took)
                    {
                        t.inventory.addItemType(kv.Key, kv.Value);
                    }
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't undo the trade with {name}: {e.Message}");
                }
                finally
                {
                    Coop.Guards.Replaying = false;
                }
            }, inventory: false);
        }

        private PacketWriter WriteTrade(in Trade trade)
        {
            var w = _session.Begin(MsgType.TraderTrade);
            TraderTrade.Write(w, trade);
            return w;
        }

        private void OnTrade(ulong from, PacketReader r)
        {
            var trade = TraderTrade.Read(r);
            var (name, who, at) = Unpack(trade.Trader);
            var took = trade.Took;
            var gave = trade.Gave;
            string stock = trade.Stock;
            int repChange = trade.Change;
            if (!_session.IsHost)
            {
                if (from != _session.HostId)
                {
                    return;
                }

                // the reputation is in the NPC's state, here whether he is or
                // not
                FlagsSync instance = FlagsSync.Instance;
                if (instance != null)
                {
                    instance.AddReputation(name, repChange);
                }

                if (!ApplyTo(name, who, at, stock))
                {
                    _waiting[Key(name, who)] = stock;
                }

                return;
            }

            // Host: a client's trade, as a request, checked against our copy of
            // that trader.
            var npc = Find(name, who, at);
            var verdict = TradeRules.MayTrade(FactsOf(npc, trade, at));
            if (TradeRules.SendsStock(verdict))
            {
                Send(npc, from);
            }

            if (verdict.Refused)
            {
                _coop.Authority.Refuse(verdict);
                return;
            }

            Apply(npc, stock);
            FlagsSync flags = FlagsSync.Instance;
            if (flags != null)
            {
                flags.AddReputation(name, repChange);
            }

            Plugin.Log.LogInfo(
                $"{_session.NameOf(from)} traded with {name}: took "
                    + $"{Text(took)}, gave {Text(gave)}, reputation "
                    + $"{repChange:+0;-0;0}");
            _session.Broadcast(
                WriteTrade(trade with { Trader = WhoOf(npc) }),
                Channel.Control, reliable: true, except: from);
        }

        /// <summary>
        /// Host: what the trade rule knows of a client's trade and our copy
        /// of the trader (null: not here): the only code that reads the NPC,
        /// his stock and his reputation for it.
        /// </summary>
        private TradeFacts FactsOf(NPC npc, in Trade trade, Vector3 at)
        {
            string name = trade.Trader.Name;
            var facts = new TradeFacts
            {
                Name = name,
                Found = npc != null,
                Took = trade.Took,
                Gave = trade.Gave,
                Change = trade.Change,
            };

            if (npc == null)
            {
                return facts;
            }

            Flags instance = Singleton<Flags>.Instance;
            Flags.NPCState st = null;
            if (instance != null)
            {
                st = instance.getNPCState(name);
            }

            int rep = 0;
            if (st != null)
            {
                rep = st.reputation;
            }

            facts = facts with
            {
                Reach = _coop.Authority.ReachOf(at),
                Has = Stacks(Inv(npc)),
                Reputation = rep,
            };

            try
            {
                var state = JsonConvert.DeserializeObject<Inventory.SaveState>(
                    trade.Stock);
                if (state != null && state.invItems != null)
                {
                    var stacks = new List<ItemStack>();
                    foreach (var it in state.invItems)
                    {
                        if (it != null)
                        {
                            stacks.Add(new ItemStack
                            {
                                Type = it.type,
                                Amount = it.amount,
                            });
                        }
                    }

                    facts = facts with { Stock = stacks };
                }
            }
            catch (System.Exception e)
            {
                facts = facts with { StockError = e.Message };
            }

            return facts;
        }

        private static string Text(IReadOnlyDictionary<string, int> d)
        {
            if (d.Count == 0)
            {
                return "nothing";
            }

            var parts = new List<string>();
            foreach (var kv in d)
            {
                parts.Add($"{kv.Value} {kv.Key}");
            }

            return string.Join(", ", parts);
        }

        // ---- what the patches hand over -------------------------------------

        /// <summary>
        /// NPC.randomizeTraderInv is about to roll this trader's stock:
        /// whether it may (the host's game; a client's asks the host). Rolls
        /// with no session.
        /// </summary>
        internal static bool BeforeStocking(NPC npc)
        {
            CoopSession coop = Coop.Current;
            if (coop == null
                || TraderRules.RollsHere(NetEnemies.ClientMode))
            {
                return true;
            }

            coop.Traders.ClientStocking(npc);
            return false;
        }

        /// <summary>
        /// NPC.randomizeTraderInv ran: the host sends the new stock.
        /// </summary>
        internal static void AfterStocking(NPC npc)
        {
            CoopSession coop = Coop.Current;
            if (coop != null && TraderRules.RollsHere(NetEnemies.ClientMode))
            {
                coop.Traders.HostStocked(npc);
            }
        }

        /// <summary>
        /// DialogueWindow.acceptTrade is about to move what's in the two
        /// exchange windows: what they hold, and the trader's reputation.
        /// Nothing (null took) with no session, for a replay or no trader.
        /// </summary>
        internal static TradeWindows BeforeTrade(DialogueWindow window)
        {
            CoopSession coop = Coop.Current;
            if (coop == null || window == null || window.npc == null
                || coop.Guards.Replaying)
            {
                return default;
            }

            return new TradeWindows
            {
                Took = Count(window.exchangeTrader),
                Gave = Count(window.exchangePlayer),
                Reputation = window.npc.reputation,
            };
        }

        /// <summary>
        /// DialogueWindow.acceptTrade ran: an accepted trade is shared. What
        /// was in the windows before is what was traded.
        /// </summary>
        internal static void AfterTrade(
            DialogueWindow window, TradeWindows before)
        {
            CoopSession coop = Coop.Current;
            if (coop == null || before.Took == null || window.npc == null)
            {
                return;
            }

            // Accepted: both windows were emptied. Not accepted (not enough
            // reputation, no room): the things are still in them.
            var accept = new AcceptFacts
            {
                Counted = true,
                Took = before.Took.Count,
                Gave = before.Gave.Count,
                TraderLeft = Count(window.exchangeTrader).Count,
                PlayerLeft = Count(window.exchangePlayer).Count,
            };

            if (!TradeRules.Accepted(accept))
            {
                return;
            }

            coop.Traders.LocalTrade(
                window.npc, before.Took, before.Gave,
                window.npc.reputation - before.Reputation);
        }

        /// <summary>Item type -> how many, in this inventory.</summary>
        internal static Dictionary<string, int> Count(Inventory inv)
        {
            return TradeRules.Count(Stacks(inv));
        }

        /// <summary>
        /// The stacks in this inventory, for the trade rules: the only code
        /// that reads an inventory's items for them. None for no inventory.
        /// </summary>
        internal static List<ItemStack> Stacks(Inventory inv)
        {
            var stacks = new List<ItemStack>();
            if (inv == null)
            {
                return stacks;
            }

            foreach (var it in inv.getAllItems())
            {
                if (!InvItemClass.isNull(it))
                {
                    stacks.Add(
                        new ItemStack { Type = it.type, Amount = it.amount });
                }
            }

            return stacks;
        }
    }
}
