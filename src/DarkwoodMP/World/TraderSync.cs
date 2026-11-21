using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Traders;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// What traders have for sale, the same for everyone. A trader stocks up at
    /// random when they appear and on each new day (NPC.randomizeTraderInv).
    /// That's the host's game's to do: a client's doesn't, and asks the host
    /// instead (TraderAsk); the host sends what its trader has (TraderStock)
    /// when asked and whenever it restocks.
    ///
    /// Traders are named by the NPC's name and ObjRef, not as containers: NPCs
    /// live in each player's game separately. One from the shared save (a Saved
    /// ref) is that one only: a new chapter-1 world has three "wolfman" traders
    /// (the two camps and the wolf's hideout), each with his own stock. Any
    /// other (the morning trader, the wolf, which each game spawns at its own
    /// player's hideout, with another id in each) is the nearest of that name
    /// to where the sender's stands that isn't one from the shared save. Stock
    /// for a trader that hasn't appeared in a client's game yet is kept until
    /// they do.
    ///
    /// Trades: the game moves the things traded through the trade window's two
    /// exchange inventories, straight into the trader's inventory
    /// (DialogueWindow.acceptTrade), so the container sharing never sees them.
    /// After an accepted trade the trading game sends the trader's whole stock
    /// and what was taken and given (TraderTrade).
    /// A client's is a request: the host checks its own trader is there, the
    /// player is near their copy of him, he had what they took, the new stock
    /// is his old one less that plus what they gave, and his reputation (the
    /// host's) pays for it, as acceptTrade checks; then it takes that stock and
    /// passes it on. Refused, the host sends its stock (TraderStock) and the
    /// client's player gives back what they got and gets back what they gave.
    /// The trade's reputation change travels with it, and each game adds it to
    /// the trader's reputation (which FlagsSync otherwise shares as changes),
    /// so two trades close together both count.
    /// </summary>
    public sealed class TraderSync
    {
        public static TraderSync Instance { get; private set; }
        private readonly Session _session;
        // client: stock for traders not here yet
        private readonly Dictionary<string, string> _waiting =
            new Dictionary<string, string>();

        public TraderSync(Session session)
        {
            Instance = this;
            _session = session;
            session.Register(MsgType.TraderAsk, OnAsk);
            session.Register(MsgType.TraderStock, OnStock);
            session.Register(MsgType.TraderTrade, OnTrade);
            session.OnSessionEnded += () =>
            {
                _waiting.Clear();
                _applied.Clear();
                _copies.Clear();
            };
        }

        /// <summary>
        /// Client: a trader would stock up here. Use the host's stock: what
        /// came already, or ask.
        /// </summary>
        internal void ClientStocking(NPC npc)
        {
            if (npc == null)
            {
                return;
            }

            var facts = StockingOf(npc);
            string key = null;
            if (facts.Trader && facts.HasInventory)
            {
                key = Key(npc.name, ObjRef.Of(npc.gameObject));
                facts = facts with { Waiting = _waiting.ContainsKey(key) };
            }

            var plan = TraderRules.Stocking(facts);
            if (plan == StockPlan.UseWaiting)
            {
                string stock = _waiting[key];
                _waiting.Remove(key);
                Apply(npc, stock);
                return;
            }

            if (plan != StockPlan.AskHost)
            {
                return;
            }

            var w = _session.Begin(MsgType.TraderAsk);
            TraderAsk.Write(w, WhoOf(npc));
            _session.SendToHost(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] asked the host what {npc.name} has");
            }
        }

        /// <summary>Host: this trader restocked; everyone gets it.</summary>
        internal void HostStocked(NPC npc)
        {
            if (npc == null)
            {
                return;
            }

            var plan = TraderRules.Stocking(StockingOf(npc));
            if (plan == StockPlan.SendToAll)
            {
                Send(npc, to: 0, StockOf(npc));
            }
        }

        /// <summary>
        /// What the stocking rule knows of this trader and the session: the
        /// only code that reads the NPC for it. Waiting is the caller's.
        /// </summary>
        private StockFacts StockingOf(NPC npc)
        {
            return new StockFacts
            {
                ClientMode = NetEnemies.ClientMode,
                Trader = npc.trader,
                HasInventory = npc.inventory != null,
                HasStock = Inv(npc) != null,
                Connected = _session.Connected,
                IsHost = _session.IsHost,
                Peers = _session.Peers.Count,
            };
        }

        /// <summary>
        /// Which trader a message is about: his name, his ObjRef, where he
        /// stands in the sender's game (x, z).
        /// </summary>
        private static TraderWho WhoOf(string name, ObjRef r, Vector3 at)
        {
            return new TraderWho
            {
                Name = name,
                Who = ObjRef.NameOf(r),
                At = new Vec2(at.x, at.z),
            };
        }

        private static TraderWho WhoOf(NPC npc)
        {
            return WhoOf(
                npc.name, ObjRef.Of(npc.gameObject), npc.transform.position);
        }

        private static (string name, ObjRef r, Vector3 at) Unpack(
            TraderWho who)
        {
            return (who.Name, ObjRef.RefOf(who.Who), new Vector3(
                who.At.X, 0f, who.At.Z));
        }

        /// <summary>
        /// What stock waiting for a trader is kept under: a shared-save trader
        /// by his ref, any other by name.
        /// </summary>
        private static string Key(string name, ObjRef r)
        {
            return TraderRules.WaitingKey(
                name, r.K == ObjRef.Kind.Saved, r.ToString());
        }

        /// <summary>A trader's inventory. NPC.inventory is set when he first
        /// comes to life (NPC.init), so one in a part of the world this game
        /// hasn't run yet has it only as his component, which init then
        /// takes.</summary>
        internal static Inventory Inv(NPC n)
        {
            if (n.inventory != null)
            {
                return n.inventory;
            }

            return n.GetComponent<Inventory>();
        }

        private static string StockOf(NPC npc) =>
            JsonConvert.SerializeObject(new Inventory.SaveState(Inv(npc)));

        private void Send(NPC npc, ulong to, string stock = null)
        {
            string sent = stock;
            if (sent == null)
            {
                sent = StockOf(npc);
            }

            var w = _session.Begin(MsgType.TraderStock);
            TraderStock.Write(w, WhoOf(npc), sent);
            if (to != 0)
            {
                _session.SendTo(to, w, Channel.Control, reliable: true);
            }
            else
            {
                _session.Broadcast(w, Channel.Control, reliable: true);
            }

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent what {npc.name} has "
                        + $"({Inv(npc).getAllItems().Count} items)");
            }
        }

        private void OnAsk(ulong from, PacketReader r)
        {
            if (!_session.IsHost)
            {
                return;
            }

            var (name, who, at) = Unpack(TraderAsk.Read(r));
            var npc = Find(name, who, at);
            var answer = TraderRules.Answer(
                _session.IsHost, npc != null, who.K == ObjRef.Kind.Saved);
            if (answer == AskAnswer.SendStock)
            {
                Send(npc, from);
                return;
            }

            // A trader from the shared save who isn't here (yet) gets no copy
            // of ours.
            if (answer != AskAnswer.PutCopy)
            {
                return;
            }

            // The morning trader in a joiner's hideout, which this game has no
            // copy of (our player wasn't in that hideout at dawn): we put our
            // own there, as the game's morning does (Location.spawnTrader). Its
            // stock is ours to roll (it stocks up when it appears, and
            // HostStocked sends it to everyone), and their trades with him are
            // checked against it.
            if (SpawnHostCopy(name, at, from))
            {
                return;
            }

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] {_session.NameOf(from)} asked about {name}, who "
                        + "isn't here yet");
            }
        }

        // ---- the host's copies of joiners' morning traders ------------------

        /// <summary>
        /// Host: the traders we put in a joiner's hideout for their morning,
        /// and when.
        /// </summary>
        private readonly List<(Location loc, NPC npc, float since)> _copies =
            new List<(Location, NPC, float)>();
        private Seconds _nextCheck;

        private bool SpawnHostCopy(string name, Vector3 at, ulong from)
        {
            Flags instance = Singleton<Flags>.Instance;
            Flags.NPCState st = null;
            if (instance != null)
            {
                st = instance.getNPCState(name);
            }

            // The wolf comes to a hideout in the morning too
            // (Location.spawnWolf), but he isn't one to trade with.
            if (!TraderCopyRules.MayCopy(name, st != null && st.dead))
            {
                return false;
            }

            var locs = new List<Location>();
            var bases = new List<HideoutBase>();
            foreach (var loc in Object.FindObjectsOfType<Location>(true))
            {
                if (loc != null)
                {
                    locs.Add(loc);
                    bases.Add(FactsOf(loc));
                }
            }

            int nearest = TraderCopyRules.NearestBase(
                bases, new Vec2(at.x, at.z));
            if (nearest < 0)
            {
                return false;
            }

            Location best = locs[nearest];
            bool had = best.trader != null;
            NPC npc;
            try
            {
                npc = best.spawnTrader();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't put {name} in {best.name}: {e.Message}");
                return false;
            }

            string spawned = null;
            if (npc != null)
            {
                spawned = npc.name;
            }

            var result = TraderCopyRules.AfterSpawn(name, spawned, had);
            if (result == SpawnResult.NoTrader)
            {
                return false;
            }

            if (result != SpawnResult.Kept)
            {
                Plugin.Log.LogInfo(
                    $"[world] {_session.NameOf(from)} asked about {name}, but "
                        + $"{best.name}'s morning trader here is {npc.name}");
                if (result == SpawnResult.OtherGoes)
                {
                    best.despawnTrader();
                }

                return false;
            }

            _copies.Add((best, npc, Time.unscaledTime));
            Plugin.Log.LogInfo(
                $"[world] {_session.NameOf(from)}'s morning: our own {name} is "
                    + $"in {best.name} too");
            return true;
        }

        /// <summary>
        /// Host, every frame: a copy we put in a joiner's hideout goes when no
        /// joiner is in a morning any more (their endAfterNight took theirs
        /// away), unless our own player's morning is there. A dead one stays,
        /// as a body does.
        /// </summary>
        internal void HostUpdate()
        {
            var now = new Seconds(Time.unscaledTime);
            if (!TraderCopyRules.Due(_copies.Count, now, _nextCheck))
            {
                return;
            }

            _nextCheck = TraderCopyRules.Next(now);
            var remotes = new List<RemoteMorning>();
            var sync = Sync.PlayerSync.Instance;
            if (sync != null)
            {
                foreach (var kv in sync.Remotes)
                {
                    remotes.Add(FactsOf(kv.Key, kv.Value));
                }
            }

            bool someone = TraderCopyRules.SomeoneInMorning(remotes);
            for (int i = _copies.Count - 1; i >= 0; i--)
            {
                var (loc, npc, since) = _copies[i];
                var facts = FactsOf(loc, npc) with
                {
                    Someone = someone,
                    Now = now,
                    Since = new Seconds(since),
                };

                var fate = TraderCopyRules.Fate(facts);
                if (fate == CopyFate.Keep)
                {
                    continue;
                }

                if (fate == CopyFate.TakeAway)
                {
                    Plugin.Log.LogInfo(
                        $"[world] no joiner is in a morning: our {npc.name} "
                            + $"leaves {loc.name}");
                    if (loc.trader == npc.gameObject)
                    {
                        loc.despawnTrader();
                    }
                    else
                    {
                        Object.Destroy(npc.gameObject);
                    }
                }

                _copies.RemoveAt(i);
            }
        }

        /// <summary>
        /// What the copy rules know of a hideout: the only code that reads a
        /// Location for them.
        /// </summary>
        private static HideoutBase FactsOf(Location loc)
        {
            var spot = loc.traderPosition;
            var facts = new HideoutBase
            {
                PlayerBase = loc.playerBase,
                HasTraderSpot = spot != null,
            };

            if (spot != null)
            {
                var at = spot.transform.position;
                facts = facts with { TraderSpot = new Vec2(at.x, at.z) };
            }

            return facts;
        }

        /// <summary>
        /// Whether a remote player is in a morning, as their last state says.
        /// </summary>
        private static RemoteMorning FactsOf(ulong id, Sync.RemotePlayer p)
        {
            var last = p.Last;
            var facts = new RemoteMorning
            {
                Dummy = id == Sync.PlayerSync.DummyId,
                HasState = last != null,
            };

            if (last != null)
            {
                facts = facts with { AfterNight = last.AfterNight };
            }

            return facts;
        }

        /// <summary>
        /// What the copy rules know of one of our copies: gone, dead, and
        /// whether our own player's morning is in that hideout (ours now).
        /// </summary>
        private static TraderCopyFacts FactsOf(Location loc, NPC npc)
        {
            if (npc == null || loc == null)
            {
                return new TraderCopyFacts { Gone = true };
            }

            var ch = npc.GetComponent<Character>();
            var c = Singleton<Controller>.Instance;
            var p = Player.Instance;
            return new TraderCopyFacts
            {
                Dead = ch != null && !ch.alive,
                OursNow = c != null && c.isAfterNight && p != null
                    && p.whereAmI != null && p.whereAmI.bigLocation == loc,
            };
        }

        private void OnStock(ulong from, PacketReader r)
        {
            if (from != _session.HostId)
            {
                return;
            }

            var (name, who, at) = Unpack(
                TraderStock.Read(r, out string stock));
            if (!ApplyTo(name, who, at, stock))
            {
                _waiting[Key(name, who)] = stock;
            }
        }

        // ---- trades ---------------------------------------------------------

        /// <summary>
        /// Stock applied from the host, per trader: a refused trade the host
        /// sent no stock for is undone here.
        /// </summary>
        // by the NPC's instance id
        private static readonly Dictionary<int, int> _applied =
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
            Authority authority = Coop.Current.Authority;
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
                Coop.Current.Authority.Refuse(verdict);
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
                Reach = Coop.Current.Authority.ReachOf(at),
                Has = TraderTradePatch.Stacks(Inv(npc)),
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

        /// <summary>
        /// The stacks of a saved inventory's items, for the trade rule.
        /// </summary>
        private static List<ItemStack> StacksOf(
            List<InvItemClass.SaveState> items)
        {
            var stacks = new List<ItemStack>();
            foreach (var it in items)
            {
                if (it != null)
                {
                    stacks.Add(
                        new ItemStack { Type = it.type, Amount = it.amount });
                }
            }

            return stacks;
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

        private static void Apply(NPC npc, string stock)
        {
            if (Inv(npc) == null)
            {
                return;
            }

            _applied.TryGetValue(npc.GetInstanceID(), out int seen);
            _applied[npc.GetInstanceID()] = seen + 1;
            Containers instance = Containers.Instance;
            if (instance != null)
            {
                instance.Apply(Inv(npc), stock);
            }

            Plugin.Log.LogInfo(
                $"{npc.name} has the host's stock: "
                    + $"{Inv(npc).getAllItems().Count} items");
        }

        /// <summary>
        /// The host's stock goes to our copy of that trader. False: he isn't
        /// here.
        /// </summary>
        private static bool ApplyTo(
            string name, ObjRef who, Vector3 at, string stock)
        {
            var n = Find(name, who, at);
            if (n == null)
            {
                return false;
            }

            Apply(n, stock);
            return true;
        }

        /// <summary>
        /// Our copy of the trader another game names: by his ref, if it names
        /// one of that name here; else, unless he is one from the shared save
        /// (whose ref names only him), the nearest of that name to where the
        /// other game has him, leaving out the shared-save traders of that name
        /// (each of those is the one his ref names).
        /// </summary>
        private static NPC Find(string name, ObjRef who, Vector3 at)
        {
            NPC byRef = null;
            if (who.Valid)
            {
                byRef = ObjRef.Resolve<NPC>(who);
            }

            var npcs = new List<NPC>();
            var candidates = new List<TraderCandidate>();
            foreach (var n in Object.FindObjectsOfType<NPC>())
            {
                if (n != null && n.trader)
                {
                    npcs.Add(n);
                    candidates.Add(CandidateOf(n));
                }
            }

            var find = new FindFacts
            {
                Name = name,
                Valid = who.Valid,
                Saved = who.K == ObjRef.Kind.Saved,
                Resolved = byRef != null,
                At = new Vec2(at.x, at.z),
                Candidates = candidates,
            };

            if (byRef != null)
            {
                find = find with { ByRef = CandidateOf(byRef) };
            }

            var match = TraderRules.Find(find);
            if (match.How == TraderFound.ByRef)
            {
                return byRef;
            }

            if (match.How == TraderFound.Nearest)
            {
                return npcs[match.Index];
            }

            return null;
        }

        /// <summary>
        /// What the find rule knows of one NPC: the only code that reads an
        /// NPC for it.
        /// </summary>
        private static TraderCandidate CandidateOf(NPC n)
        {
            var at = n.transform.position;
            return new TraderCandidate
            {
                Trader = n.trader,
                Name = n.name,
                HasInventory = Inv(n) != null,
                Saved = ObjRef.Of(n.gameObject).K == ObjRef.Kind.Saved,
                Position = new Vec2(at.x, at.z),
            };
        }
    }

    /// <summary>
    /// A trader stocking up (appearing, a new day): the host's game does it and
    /// sends it; a client's asks.
    /// </summary>
    [HarmonyPatch(typeof(NPC), "randomizeTraderInv")]
    static class TraderStockPatch
    {
        static bool Prefix(NPC __instance)
        {
            if (TraderRules.RollsHere(NetEnemies.ClientMode))
            {
                return true;
            }

            TraderSync instance = TraderSync.Instance;
            if (instance != null)
            {
                instance.ClientStocking(__instance);
            }

            return false;
        }

        static void Postfix(NPC __instance)
        {
            if (TraderRules.RollsHere(NetEnemies.ClientMode))
            {
                TraderSync instance = TraderSync.Instance;
                if (instance != null)
                {
                    instance.HostStocked(__instance);
                }
            }
        }
    }

    /// <summary>An accepted trade (DialogueWindow.acceptTrade moves what's in
    /// the two exchange windows to the trader and to the player): share the
    /// trader's new stock. What was in the windows before is what was
    /// traded.</summary>
    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.acceptTrade))]
    static class TraderTradePatch
    {
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

        static void Prefix(
            DialogueWindow __instance,
            out (Dictionary<string, int> took, Dictionary<string, int> gave,
                int rep) __state)
        {
            __state = default;
            if (__instance == null || __instance.npc == null
                || Coop.Guards.Replaying)
            {
                return;
            }

            __state = (Count(__instance.exchangeTrader), Count(
                __instance.exchangePlayer), __instance.npc.reputation);
        }

        static void Postfix(
            DialogueWindow __instance,
            (Dictionary<string, int> took, Dictionary<string, int> gave,
                int rep) __state)
        {
            var (took, gave, rep) = __state;
            if (took == null || __instance.npc == null)
            {
                return;
            }

            // Accepted: both windows were emptied. Not accepted (not enough
            // reputation, no room): the things are still in them.
            var accept = new AcceptFacts
            {
                Counted = true,
                Took = took.Count,
                Gave = gave.Count,
                TraderLeft = Count(__instance.exchangeTrader).Count,
                PlayerLeft = Count(__instance.exchangePlayer).Count,
            };

            if (!TradeRules.Accepted(accept))
            {
                return;
            }

            TraderSync instance = TraderSync.Instance;
            if (instance != null)
            {
                instance.LocalTrade(
                    __instance.npc, took, gave,
                    __instance.npc.reputation - rep);
            }
        }
    }
}
