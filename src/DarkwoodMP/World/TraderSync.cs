using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// What traders have for sale, the same for everyone. A trader stocks up at random when they appear and on
    /// each new day (NPC.randomizeTraderInv). That's the host's game's to do: a client's doesn't, and asks the host
    /// instead (TraderAsk); the host sends what its trader has (TraderStock) when asked and whenever it restocks.
    ///
    /// Traders are named by the NPC's name and ObjRef, not as containers: NPCs live in each player's game separately.
    /// One from the shared save (a Saved ref) is that one only: a new chapter-1 world has three "wolfman" traders
    /// (the two camps and the wolf's hideout), each with his own stock. Any other (the morning trader, the wolf,
    /// which each game spawns at its own player's hideout, with another id in each) is the nearest of that name to
    /// where the sender's stands that isn't one from the shared save. Stock for a trader that hasn't appeared
    /// in a client's game yet is kept until they do.
    ///
    /// Trades: the game moves the things traded through the trade window's two exchange inventories, straight into
    /// the trader's inventory (DialogueWindow.acceptTrade), so the container sharing never sees them. After an
    /// accepted trade the trading game sends the trader's whole stock and what was taken and given (TraderTrade).
    /// A client's is a request: the host checks its own trader is there, the player is near their copy of him,
    /// he had what they took, the new stock is his old one less that plus what they gave, and his reputation (the
    /// host's) pays for it, as acceptTrade checks; then it takes that stock and passes it on. Refused, the host sends
    /// its stock (TraderStock) and the client's player gives back what they got and gets back what they gave.
    /// The trade's reputation change travels with it, and each game adds it to the trader's reputation (which
    /// FlagsSync otherwise shares as changes), so two trades close together both count.
    /// </summary>
    public sealed class TraderSync
    {
        public static TraderSync Instance { get; private set; }
        private readonly Session _session;
        private readonly Dictionary<string, string> _waiting = new Dictionary<string, string>(); // client: stock for traders not here yet

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

        /// <summary>Client: a trader would stock up here. Use the host's stock: what came already, or ask.</summary>
        internal void ClientStocking(NPC npc)
        {
            if (!_session.Connected || npc == null)
            {
                return;
            }
            string key = Key(npc.name, ObjRef.Of(npc.gameObject));
            if (_waiting.TryGetValue(key, out var stock))
            {
                _waiting.Remove(key);
                Apply(npc, stock);
                return;
            }
            var w = _session.Begin(MsgType.TraderAsk);
            WriteWho(w, npc.name, ObjRef.Of(npc.gameObject), npc.transform.position);
            _session.SendToHost(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[world] asked the host what {npc.name} has");
            }
        }

        /// <summary>Host: this trader restocked; everyone gets it.</summary>
        internal void HostStocked(NPC npc)
        {
            if (!_session.IsHost || _session.Peers.Count == 0 || npc == null || Inv(npc) == null)
            {
                return;
            }
            Send(npc, to: 0, StockOf(npc));
        }

        /// <summary>Which trader a message is about: his name, his ObjRef, where he stands in the sender's game (x, z).</summary>
        private static void WriteWho(PacketWriter w, string name, ObjRef r, Vector3 at)
        {
            w.WriteStr(name);
            r.Write(w);
            w.W.Write(at.x);
            w.W.Write(at.z);
        }

        private static (string name, ObjRef r, Vector3 at) ReadWho(PacketReader r)
        {
            string name = r.ReadStr();
            var o = ObjRef.Read(r);
            return (name, o, new Vector3(r.R.ReadSingle(), 0f, r.R.ReadSingle()));
        }

        /// <summary>What stock waiting for a trader is kept under: a shared-save trader by his ref, any other by name.</summary>
        private static string Key(string name, ObjRef r)
        {
            if (r.K == ObjRef.Kind.Saved)
            {
                return $"{name}@{r}";
            }
            return name;
        }

        /// <summary>A trader's inventory. NPC.inventory is set when he first comes to life (NPC.init), so one in a part
        /// of the world this game hasn't run yet has it only as his component, which init then takes.</summary>
        internal static Inventory Inv(NPC n)
        {
            if (n.inventory != null)
            {
                return n.inventory;
            }
            return n.GetComponent<Inventory>();
        }

        private static string StockOf(NPC npc) => JsonConvert.SerializeObject(new Inventory.SaveState(Inv(npc)));

        private void Send(NPC npc, ulong to, string stock = null)
        {
            var w = _session.Begin(MsgType.TraderStock);
            WriteWho(w, npc.name, ObjRef.Of(npc.gameObject), npc.transform.position);
            string stock2 = stock;
            if (stock2 == null)
            {
                stock2 = StockOf(npc);
            }
            w.WriteBigStr(stock2);
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
                Plugin.Log.LogInfo($"[world] sent what {npc.name} has ({Inv(npc).getAllItems().Count} items)");
            }
        }

        private void OnAsk(ulong from, PacketReader r)
        {
            if (!_session.IsHost)
            {
                return;
            }
            var (name, who, at) = ReadWho(r);
            var npc = Find(name, who, at);
            if (npc != null)
            {
                Send(npc, from);
                return;
            }
            if (who.K == ObjRef.Kind.Saved)
            {
                return; // one from the shared save that isn't here (yet): no copy of ours
            }
            // The morning trader in a joiner's hideout, which this game has no copy of (our player wasn't in that
            // hideout at dawn): we put our own there, as the game's morning does (Location.spawnTrader). Its stock is
            // ours to roll (it stocks up when it appears, and HostStocked sends it to everyone), and their trades with
            // him are checked against it.
            if (SpawnHostCopy(name, at, from))
            {
                return;
            }
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[world] {_session.NameOf(from)} asked about {name}, who isn't here yet");
            }
        }

        // ---- the host's copies of joiners' morning traders -------------------------------------------

        /// <summary>Host: the traders we put in a joiner's hideout for their morning, and when.</summary>
        private readonly List<(Location loc, NPC npc, float since)> _copies = new List<(Location, NPC, float)>();
        private float _nextCheck;
        /// <summary>A joiner's PlayerState says they're in a morning a moment after their trader appears: wait this long.</summary>
        private const float CopyGrace = 10f;

        private bool SpawnHostCopy(string name, Vector3 at, ulong from)
        {
            // The wolf comes to a hideout in the morning too (Location.spawnWolf), but he isn't one to trade with.
            if (name.IndexOf("wolf", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
            Flags instance = Singleton<Flags>.Instance;
            Flags.NPCState st = null;
            if (instance != null)
            {
                st = instance.getNPCState(name);
            }
            if (st != null && st.dead)
            {
                return false;
            }
            Location best = null;
            float bestD = 300f * 300f;
            foreach (var loc in Object.FindObjectsOfType<Location>(true))
            {
                if (loc == null || !loc.playerBase || loc.traderPosition == null)
                {
                    continue;
                }
                float d = WorldActions.FlatSq(loc.traderPosition.transform.position, at);
                if (d < bestD)
                {
                    bestD = d;
                    best = loc;
                }
            }
            if (best == null)
            {
                return false;
            }
            bool had = best.trader != null;
            NPC npc;
            try
            {
                npc = best.spawnTrader();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Couldn't put {name} in {best.name}: {e.Message}");
                return false;
            }
            if (npc == null)
            {
                return false;
            }
            if (npc.name != name)
            {
                Plugin.Log.LogInfo($"[world] {_session.NameOf(from)} asked about {name}, but {best.name}'s morning trader here is {npc.name}");
                if (!had)
                {
                    best.despawnTrader();
                }
                return false;
            }
            _copies.Add((best, npc, Time.unscaledTime));
            Plugin.Log.LogInfo($"[world] {_session.NameOf(from)}'s morning: our own {name} is in {best.name} too");
            return true;
        }

        /// <summary>
        /// Host, every frame: a copy we put in a joiner's hideout goes when no joiner is in a morning any more (their
        /// endAfterNight took theirs away), unless our own player's morning is there. A dead one stays, as a body does.
        /// </summary>
        internal void HostUpdate()
        {
            if (_copies.Count == 0 || Time.unscaledTime < _nextCheck)
            {
                return;
            }
            _nextCheck = Time.unscaledTime + 1f;
            bool someone = false;
            var sync = Sync.PlayerSync.Instance;
            if (sync != null)
            {
                foreach (var kv in sync.Remotes)
                {
                    if (kv.Key != Sync.PlayerSync.DummyId && kv.Value.Last != null && kv.Value.Last.AfterNight)
                    {
                        someone = true;
                        break;
                    }
                }
            }
            var c = Singleton<Controller>.Instance;
            var p = Player.Instance;
            for (int i = _copies.Count - 1; i >= 0; i--)
            {
                var (loc, npc, since) = _copies[i];
                if (npc == null || loc == null)
                {
                    _copies.RemoveAt(i);
                    continue;
                }
                var ch = npc.GetComponent<Character>();
                if (ch != null && !ch.alive)
                {
                    _copies.RemoveAt(i);
                    continue;
                }
                if (someone || Time.unscaledTime - since < CopyGrace)
                {
                    continue;
                }
                if (c != null && c.isAfterNight && p != null && p.whereAmI != null && p.whereAmI.bigLocation == loc) // ours now
                {
                    _copies.RemoveAt(i);
                    continue;
                }
                Plugin.Log.LogInfo($"[world] no joiner is in a morning: our {npc.name} leaves {loc.name}");
                if (loc.trader == npc.gameObject)
                {
                    loc.despawnTrader();
                }
                else
                {
                    Object.Destroy(npc.gameObject);
                }
                _copies.RemoveAt(i);
            }
        }

        private void OnStock(ulong from, PacketReader r)
        {
            if (from != _session.HostId)
            {
                return;
            }
            var (name, who, at) = ReadWho(r);
            string stock = r.ReadBigStr();
            if (!ApplyTo(name, who, at, stock))
            {
                _waiting[Key(name, who)] = stock;
            }
        }

        // ---- trades ---------------------------------------------------------------------------------

        /// <summary>Stock applied from the host, per trader: a refused trade the host sent no stock for is undone here.</summary>
        private static readonly Dictionary<int, int> _applied = new Dictionary<int, int>(); // by the NPC's instance id

        /// <summary>
        /// Our player's trade with this trader was accepted (the stock in npc.inventory is the result): share it.
        /// took/gave: item type -> count; repChange: what the trade did to the reputation (undone if refused).
        /// </summary>
        internal void LocalTrade(NPC npc, Dictionary<string, int> took, Dictionary<string, int> gave, int repChange)
        {
            if (!_session.Connected || npc == null || npc.inventory == null)
            {
                return;
            }
            if (_session.IsHost && _session.Peers.Count == 0)
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
            // Reputation our player earned just before (in the same dialogue, or since the last two-second FlagsDelta)
            // goes first, on the same reliable Control channel, so the host's copy has it when it checks the trade's
            // (CheckReputation). The trade's own change was absorbed above, so it isn't sent twice. Before WriteTrade:
            // the flush builds its message with Session.Begin, in the same writer the trade's is built in.
            if (!_session.IsHost)
            {
                FlagsSync instance2 = FlagsSync.Instance;
                if (instance2 != null)
                {
                    instance2.FlushNow();
                }
            }
            var at = npc.transform.position;
            var who = ObjRef.Of(npc.gameObject);
            var w = WriteTrade(name, who, at, took, gave, stock, repChange);
            Plugin.Log.LogInfo($"Traded with {name}: took {Text(took)}, gave {Text(gave)}");
            if (_session.IsHost)
            {
                _session.Broadcast(w, Channel.Control, reliable: true);
                return;
            }

            _applied.TryGetValue(npc.GetInstanceID(), out int seen);
            var change = new Dictionary<string, int>(); // for InventoryWatch.Undo: + what the player got, - what they gave
            foreach (var kv in took)
            {
                change[kv.Key] = kv.Value;
            }
            foreach (var kv in gave)
            {
                change.TryGetValue(kv.Key, out int n);
                change[kv.Key] = n - kv.Value;
            }
            Authority.Request(w, $"trade with {name}", (ok, reason, result) =>
            {
                if (ok)
                {
                    return;
                }
                InventoryWatch.Undo(change);
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
                if (now != seen || t.inventory == null)
                {
                    return; // the host's stock came: that's what he has
                }
                WorldActions.Replaying = true;
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
                    Plugin.Log.LogWarning($"Couldn't undo the trade with {name}: {e.Message}");
                }
                finally
                {
                    WorldActions.Replaying = false;
                }
            }, inventory: false);
        }

        private PacketWriter WriteTrade(string name, ObjRef who, Vector3 at, Dictionary<string, int> took, Dictionary<string, int> gave, string stock, int repChange)
        {
            var w = _session.Begin(MsgType.TraderTrade);
            WriteWho(w, name, who, at);
            foreach (var d in new[] { took, gave })
            {
                w.W.Write((ushort)d.Count);
                foreach (var kv in d)
                {
                    w.WriteStr(kv.Key);
                    w.W.Write(kv.Value);
                }
            }
            w.WriteBigStr(stock);
            w.W.Write(repChange);
            return w;
        }

        private static Dictionary<string, int> ReadItems(PacketReader r)
        {
            var d = new Dictionary<string, int>();
            int n = r.R.ReadUInt16();
            for (int i = 0; i < n; i++)
            {
                string t = r.ReadStr();
                int c = r.R.ReadInt32();
                d.TryGetValue(t, out int was);
                d[t] = was + c;
            }
            return d;
        }

        private void OnTrade(ulong from, PacketReader r)
        {
            var (name, who, at) = ReadWho(r);
            var took = ReadItems(r);
            var gave = ReadItems(r);
            string stock = r.ReadBigStr();
            int repChange = r.R.ReadInt32();
            if (!_session.IsHost)
            {
                if (from != _session.HostId)
                {
                    return;
                }
                // the reputation is in the NPC's state, here whether he is or not
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

            // Host: a client's trade, as a request, checked against our copy of that trader.
            var npc = Find(name, who, at);
            if (npc == null)
            {
                Authority.Refuse($"{name} isn't here");
                return;
            }
            if (!Authority.InReach(at, WorldActions.Reach, name))
            {
                Send(npc, from);
                return;
            }
            string why = Check(Inv(npc), took, gave, stock);
            if (why == null)
            {
                why = CheckReputation(name, took, gave, repChange);
            }
            if (why != null)
            {
                Send(npc, from);
                Authority.Refuse(why);
                return;
            }
            Apply(npc, stock);
            FlagsSync instance2 = FlagsSync.Instance;
            if (instance2 != null)
            {
                instance2.AddReputation(name, repChange);
            }
            Plugin.Log.LogInfo($"{_session.NameOf(from)} traded with {name}: took {Text(took)}, gave {Text(gave)}, reputation {repChange:+0;-0;0}");
            _session.Broadcast(WriteTrade(name, ObjRef.Of(npc.gameObject), npc.transform.position, took, gave, stock, repChange), Channel.Control, reliable: true, except: from);
        }

        /// <summary>
        /// Host: why the trade's reputation change can't be (null: it can). As acceptTrade, his reputation (the host's)
        /// must pay for it; and taking only can't raise it, nor giving only lower it. (The items' values depend on their
        /// state, which the host doesn't have, so the amount itself isn't recomputed.)
        /// </summary>
        private static string CheckReputation(string name, Dictionary<string, int> took, Dictionary<string, int> gave, int repChange)
        {
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
            if (rep + repChange < 0)
            {
                return $"his reputation is {rep}, which doesn't pay for {-repChange}";
            }
            if (gave.Count == 0 && repChange > 0)
            {
                return $"taking only can't raise the reputation ({repChange:+0})";
            }
            if (took.Count == 0 && repChange < 0)
            {
                return $"giving only can't lower the reputation ({repChange})";
            }
            return null;
        }

        /// <summary>Host: why this trade can't be (null: it can). He must have had what was taken, and the new stock
        /// must be his stock less that, plus what was given.</summary>
        private static string Check(Inventory inv, Dictionary<string, int> took, Dictionary<string, int> gave, string stock)
        {
            var have = TraderTradePatch.Count(inv);
            Inventory.SaveState state;
            try
            {
                state = JsonConvert.DeserializeObject<Inventory.SaveState>(stock);
            }
            catch (System.Exception e)
            {
                return "bad stock: " + e.Message;
            }
            if (state == null || state.invItems == null)
            {
                return "no stock";
            }
            var after = new Dictionary<string, int>();
            foreach (var it in state.invItems)
            {
                if (it == null || string.IsNullOrEmpty(it.type))
                {
                    continue;
                }
                after.TryGetValue(it.type, out int n);
                after[it.type] = n + System.Math.Max(1, it.amount);
            }
            foreach (var kv in took)
            {
                if (kv.Value <= 0)
                {
                    return $"took {kv.Value} {kv.Key}";
                }
                have.TryGetValue(kv.Key, out int n);
                if (n < kv.Value)
                {
                    return $"he has {n} {kv.Key}, not {kv.Value}";
                }
                have[kv.Key] = n - kv.Value;
            }
            foreach (var kv in gave)
            {
                if (kv.Value <= 0)
                {
                    return $"gave {kv.Value} {kv.Key}";
                }
                have.TryGetValue(kv.Key, out int n);
                have[kv.Key] = n + kv.Value;
            }
            foreach (var kv in have)
            {
                after.TryGetValue(kv.Key, out int n);
                if (n != kv.Value)
                {
                    return $"his stock would have {kv.Value} {kv.Key}, not {n}";
                }
            }
            foreach (var kv in after)
            {
                if (!have.ContainsKey(kv.Key))
                {
                    return $"his stock wouldn't have {kv.Key}";
                }
            }
            return null;
        }

        private static string Text(Dictionary<string, int> d)
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
            Plugin.Log.LogInfo($"{npc.name} has the host's stock: {Inv(npc).getAllItems().Count} items");
        }

        /// <summary>The host's stock goes to our copy of that trader. False: he isn't here.</summary>
        private static bool ApplyTo(string name, ObjRef who, Vector3 at, string stock)
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
        /// Our copy of the trader another game names: by his ref, if it names one of that name here; else, unless he
        /// is one from the shared save (whose ref names only him), the nearest of that name to where the other game
        /// has him, leaving out the shared-save traders of that name (each of those is the one his ref names).
        /// </summary>
        private static NPC Find(string name, ObjRef who, Vector3 at)
        {
            if (who.Valid)
            {
                var n = ObjRef.Resolve<NPC>(who);
                if (n != null && n.trader && n.name == name && Inv(n) != null)
                {
                    return n;
                }
                if (who.K == ObjRef.Kind.Saved)
                {
                    return null;
                }
            }
            NPC best = null;
            float bestD = float.MaxValue;
            foreach (var n in Object.FindObjectsOfType<NPC>())
            {
                if (n == null || !n.trader || n.name != name || Inv(n) == null)
                {
                    continue;
                }
                if (ObjRef.Of(n.gameObject).K == ObjRef.Kind.Saved)
                {
                    continue;
                }
                float d = WorldActions.FlatSq(n.transform.position, at);
                if (d < bestD)
                {
                    bestD = d;
                    best = n;
                }
            }
            return best;
        }
    }

    /// <summary>A trader stocking up (appearing, a new day): the host's game does it and sends it; a client's asks.</summary>
    [HarmonyPatch(typeof(NPC), "randomizeTraderInv")]
    static class TraderStockPatch
    {
        static bool Prefix(NPC __instance)
        {
            if (!NetEnemies.ClientMode)
            {
                return true;
            }
            if (__instance.trader && __instance.inventory != null)
            {
                TraderSync instance = TraderSync.Instance;
                if (instance != null)
                {
                    instance.ClientStocking(__instance);
                }
            }
            return false;
        }

        static void Postfix(NPC __instance)
        {
            if (!NetEnemies.ClientMode)
            {
                TraderSync instance = TraderSync.Instance;
                if (instance != null)
                {
                    instance.HostStocked(__instance);
                }
            }
        }
    }

    /// <summary>An accepted trade (DialogueWindow.acceptTrade moves what's in the two exchange windows to the trader
    /// and to the player): share the trader's new stock. What was in the windows before is what was traded.</summary>
    [HarmonyPatch(typeof(DialogueWindow), nameof(DialogueWindow.acceptTrade))]
    static class TraderTradePatch
    {
        /// <summary>Item type -> how many, in this inventory.</summary>
        internal static Dictionary<string, int> Count(Inventory inv)
        {
            var d = new Dictionary<string, int>();
            if (inv == null)
            {
                return d;
            }
            foreach (var it in inv.getAllItems())
            {
                if (InvItemClass.isNull(it) || string.IsNullOrEmpty(it.type))
                {
                    continue;
                }
                d.TryGetValue(it.type, out int n);
                d[it.type] = n + System.Math.Max(1, it.amount);
            }
            return d;
        }

        static void Prefix(DialogueWindow __instance, out (Dictionary<string, int> took, Dictionary<string, int> gave, int rep) __state)
        {
            __state = default;
            if (__instance == null || __instance.npc == null || WorldActions.Replaying)
            {
                return;
            }
            __state = (Count(__instance.exchangeTrader), Count(__instance.exchangePlayer), __instance.npc.reputation);
        }

        static void Postfix(DialogueWindow __instance, (Dictionary<string, int> took, Dictionary<string, int> gave, int rep) __state)
        {
            var (took, gave, rep) = __state;
            if (took == null || __instance.npc == null)
            {
                return;
            }
            if (took.Count == 0 && gave.Count == 0)
            {
                return;
            }
            // Accepted: both windows were emptied. Not accepted (not enough reputation, no room): the things are still in them.
            if (Count(__instance.exchangeTrader).Count != 0 || Count(__instance.exchangePlayer).Count != 0)
            {
                return;
            }
            TraderSync instance = TraderSync.Instance;
            if (instance != null)
            {
                instance.LocalTrade(__instance.npc, took, gave, __instance.npc.reputation - rep);
            }
        }
    }
}
