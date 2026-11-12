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
    /// Traders are named by the NPC's name, not as containers: NPCs live in each player's game separately, so the
    /// same trader can stand elsewhere, or have another save id, in each. Stock for a trader that hasn't appeared
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
            session.OnSessionEnded += () => { _waiting.Clear(); _applied.Clear(); _copies.Clear(); };
        }

        /// <summary>Client: a trader would stock up here. Use the host's stock: what came already, or ask.</summary>
        internal void ClientStocking(NPC npc)
        {
            if (!_session.Connected || npc == null) return;
            if (_waiting.TryGetValue(npc.name, out var stock)) { _waiting.Remove(npc.name); Apply(npc, stock); return; }
            var w = _session.Begin(MsgType.TraderAsk);
            w.WriteStr(npc.name);
            w.W.Write(npc.transform.position.x);
            w.W.Write(npc.transform.position.z);
            _session.SendToHost(w, Channel.Control, reliable: true);
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] asked the host what {npc.name} has");
        }

        /// <summary>Host: this trader restocked; everyone gets it.</summary>
        internal void HostStocked(NPC npc)
        {
            if (!_session.IsHost || _session.Peers.Count == 0 || npc == null || npc.inventory == null) return;
            Send(npc, to: 0);
            // The others put this stock on every copy of him they have (one stock per name): so do we.
            string stock = null;
            foreach (var n in Object.FindObjectsOfType<NPC>())
            {
                if (n == null || n == npc || !n.trader || n.name != npc.name || n.inventory == null) continue;
                if (stock == null) stock = JsonConvert.SerializeObject(new Inventory.SaveState(npc.inventory));
                Apply(n, stock);
            }
        }

        private void Send(NPC npc, ulong to)
        {
            var w = _session.Begin(MsgType.TraderStock);
            w.WriteStr(npc.name);
            w.WriteBigStr(JsonConvert.SerializeObject(new Inventory.SaveState(npc.inventory)));
            if (to != 0) _session.SendTo(to, w, Channel.Control, reliable: true);
            else _session.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] sent what {npc.name} has ({npc.inventory.getAllItems().Count} items)");
        }

        private void OnAsk(ulong from, PacketReader r)
        {
            if (!_session.IsHost) return;
            string name = r.ReadStr();
            var at = new Vector3(r.R.ReadSingle(), 0f, r.R.ReadSingle());
            var npc = Find(name, at);
            if (npc != null) { Send(npc, from); return; }
            // The morning trader in a joiner's hideout, which this game has no copy of (our player wasn't in that
            // hideout at dawn): we put our own there, as the game's morning does (Location.spawnTrader). Its stock is
            // ours to roll (it stocks up when it appears, and HostStocked sends it to everyone), and their trades with
            // him are checked against it.
            if (SpawnHostCopy(name, at, from)) return;
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] {_session.NameOf(from)} asked about {name}, who isn't here yet");
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
            if (name.IndexOf("wolf", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            var st = Singleton<Flags>.Instance?.getNPCState(name);
            if (st != null && st.dead) return false;
            Location best = null; float bestD = 300f * 300f;
            foreach (var loc in Object.FindObjectsOfType<Location>(true))
            {
                if (loc == null || !loc.playerBase || loc.traderPosition == null) continue;
                var p = loc.traderPosition.transform.position;
                float d = (p.x - at.x) * (p.x - at.x) + (p.z - at.z) * (p.z - at.z);
                if (d < bestD) { bestD = d; best = loc; }
            }
            if (best == null) return false;
            bool had = best.trader != null;
            NPC npc;
            try { npc = best.spawnTrader(); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't put {name} in {best.name}: {e.Message}"); return false; }
            if (npc == null) return false;
            if (npc.name != name)
            {
                Plugin.Log.LogInfo($"[world] {_session.NameOf(from)} asked about {name}, but {best.name}'s morning trader here is {npc.name}");
                if (!had) best.despawnTrader();
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
            if (_copies.Count == 0 || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;
            bool someone = false;
            var sync = Sync.PlayerSync.Instance;
            if (sync != null)
                foreach (var kv in sync.Remotes)
                    if (kv.Key != Sync.PlayerSync.DummyId && kv.Value.Last != null && kv.Value.Last.AfterNight) { someone = true; break; }
            var c = Singleton<Controller>.Instance;
            var p = Player.Instance;
            for (int i = _copies.Count - 1; i >= 0; i--)
            {
                var (loc, npc, since) = _copies[i];
                if (npc == null || loc == null) { _copies.RemoveAt(i); continue; }
                var ch = npc.GetComponent<Character>();
                if (ch != null && !ch.alive) { _copies.RemoveAt(i); continue; }
                if (someone || Time.unscaledTime - since < CopyGrace) continue;
                if (c != null && c.isAfterNight && p != null && p.whereAmI != null && p.whereAmI.bigLocation == loc) { _copies.RemoveAt(i); continue; } // ours now
                Plugin.Log.LogInfo($"[world] no joiner is in a morning: our {npc.name} leaves {loc.name}");
                if (loc.trader == npc.gameObject) loc.despawnTrader(); else Object.Destroy(npc.gameObject);
                _copies.RemoveAt(i);
            }
        }

        private void OnStock(ulong from, PacketReader r)
        {
            if (from != _session.HostId) return;
            string name = r.ReadStr(), stock = r.ReadBigStr();
            if (!ApplyAll(name, stock)) _waiting[name] = stock;
        }

        // ---- trades ---------------------------------------------------------------------------------

        /// <summary>Stock applied from the host, per trader: a refused trade the host sent no stock for is undone here.</summary>
        private static readonly Dictionary<string, int> _applied = new Dictionary<string, int>();

        /// <summary>
        /// Our player's trade with this trader was accepted (the stock in npc.inventory is the result): share it.
        /// took/gave: item type -> count; repChange: what the trade did to the reputation (undone if refused).
        /// </summary>
        internal void LocalTrade(NPC npc, Dictionary<string, int> took, Dictionary<string, int> gave, int repChange)
        {
            if (!_session.Connected || npc == null || npc.inventory == null) return;
            if (_session.IsHost && _session.Peers.Count == 0) return;
            string name = npc.name, stock = JsonConvert.SerializeObject(new Inventory.SaveState(npc.inventory));
            FlagsSync.Instance?.Absorb(name, repChange); // it goes with the trade
            var at = npc.transform.position;
            var w = WriteTrade(name, at, took, gave, stock, repChange);
            Plugin.Log.LogInfo($"Traded with {name}: took {Text(took)}, gave {Text(gave)}");
            if (_session.IsHost) { _session.Broadcast(w, Channel.Control, reliable: true); return; }

            _applied.TryGetValue(name, out int seen);
            var change = new Dictionary<string, int>(); // for InventoryWatch.Undo: + what the player got, - what they gave
            foreach (var kv in took) change[kv.Key] = kv.Value;
            foreach (var kv in gave) { change.TryGetValue(kv.Key, out int n); change[kv.Key] = n - kv.Value; }
            Authority.Request(w, $"trade with {name}", (ok, reason, result) =>
            {
                if (ok) return;
                InventoryWatch.Undo(change);
                var t = npc != null ? npc : Find(name, at);
                if (t == null) return;
                if (repChange != 0) { if (FlagsSync.Instance != null) FlagsSync.Instance.AddReputation(name, -repChange); else t.reputation -= repChange; }
                _applied.TryGetValue(name, out int now);
                if (now != seen || t.inventory == null) return; // the host's stock came: that's what he has
                WorldActions.Replaying = true;
                try
                {
                    foreach (var kv in gave) t.inventory.removeItemAmount(kv.Key, kv.Value);
                    foreach (var kv in took) t.inventory.addItemType(kv.Key, kv.Value);
                }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't undo the trade with {name}: {e.Message}"); }
                finally { WorldActions.Replaying = false; }
            }, inventory: false);
        }

        private PacketWriter WriteTrade(string name, Vector3 at, Dictionary<string, int> took, Dictionary<string, int> gave, string stock, int repChange)
        {
            var w = _session.Begin(MsgType.TraderTrade);
            w.WriteStr(name);
            w.W.Write(at.x);
            w.W.Write(at.z);
            foreach (var d in new[] { took, gave })
            {
                w.W.Write((ushort)d.Count);
                foreach (var kv in d) { w.WriteStr(kv.Key); w.W.Write(kv.Value); }
            }
            w.WriteBigStr(stock);
            w.W.Write(repChange);
            return w;
        }

        private static Dictionary<string, int> ReadItems(PacketReader r)
        {
            var d = new Dictionary<string, int>();
            int n = r.R.ReadUInt16();
            for (int i = 0; i < n; i++) { string t = r.ReadStr(); int c = r.R.ReadInt32(); d.TryGetValue(t, out int was); d[t] = was + c; }
            return d;
        }

        private void OnTrade(ulong from, PacketReader r)
        {
            string name = r.ReadStr();
            float x = r.R.ReadSingle(), z = r.R.ReadSingle();
            var at = new Vector3(x, 0f, z);
            var took = ReadItems(r);
            var gave = ReadItems(r);
            string stock = r.ReadBigStr();
            int repChange = r.R.ReadInt32();
            if (!_session.IsHost)
            {
                if (from != _session.HostId) return;
                FlagsSync.Instance?.AddReputation(name, repChange); // the reputation is in the NPC's state, here whether he is or not
                if (!ApplyAll(name, stock)) _waiting[name] = stock;
                return;
            }

            // Host: a client's trade, as a request, checked against our copy nearest to where they traded (all our
            // copies of him have the same stock).
            var npc = Find(name, at);
            if (npc == null) { Authority.Refuse($"{name} isn't here"); return; }
            if (!Authority.InReach(at, WorldActions.Reach, name)) { Send(npc, from); return; }
            string why = Check(npc.inventory, took, gave, stock) ?? CheckReputation(name, took, gave, repChange);
            if (why != null) { Send(npc, from); Authority.Refuse(why); return; }
            ApplyAll(name, stock);
            FlagsSync.Instance?.AddReputation(name, repChange);
            Plugin.Log.LogInfo($"{_session.NameOf(from)} traded with {name}: took {Text(took)}, gave {Text(gave)}, reputation {repChange:+0;-0;0}");
            _session.Broadcast(WriteTrade(name, at, took, gave, stock, repChange), Channel.Control, reliable: true, except: from);
        }

        /// <summary>
        /// Host: why the trade's reputation change can't be (null: it can). As acceptTrade, his reputation (the host's)
        /// must pay for it; and taking only can't raise it, nor giving only lower it. (The items' values depend on their
        /// state, which the host doesn't have, so the amount itself isn't recomputed.)
        /// </summary>
        private static string CheckReputation(string name, Dictionary<string, int> took, Dictionary<string, int> gave, int repChange)
        {
            var st = Singleton<Flags>.Instance?.getNPCState(name);
            int rep = st != null ? st.reputation : 0;
            if (rep + repChange < 0) return $"his reputation is {rep}, which doesn't pay for {-repChange}";
            if (gave.Count == 0 && repChange > 0) return $"taking only can't raise the reputation ({repChange:+0})";
            if (took.Count == 0 && repChange < 0) return $"giving only can't lower the reputation ({repChange})";
            return null;
        }

        /// <summary>Host: why this trade can't be (null: it can). He must have had what was taken, and the new stock
        /// must be his stock less that, plus what was given.</summary>
        private static string Check(Inventory inv, Dictionary<string, int> took, Dictionary<string, int> gave, string stock)
        {
            var have = TraderTradePatch.Count(inv);
            Inventory.SaveState state;
            try { state = JsonConvert.DeserializeObject<Inventory.SaveState>(stock); }
            catch (System.Exception e) { return "bad stock: " + e.Message; }
            if (state?.invItems == null) return "no stock";
            var after = new Dictionary<string, int>();
            foreach (var it in state.invItems)
            {
                if (it == null || string.IsNullOrEmpty(it.type)) continue;
                after.TryGetValue(it.type, out int n);
                after[it.type] = n + System.Math.Max(1, it.amount);
            }
            foreach (var kv in took)
            {
                if (kv.Value <= 0) return $"took {kv.Value} {kv.Key}";
                have.TryGetValue(kv.Key, out int n);
                if (n < kv.Value) return $"he has {n} {kv.Key}, not {kv.Value}";
                have[kv.Key] = n - kv.Value;
            }
            foreach (var kv in gave)
            {
                if (kv.Value <= 0) return $"gave {kv.Value} {kv.Key}";
                have.TryGetValue(kv.Key, out int n);
                have[kv.Key] = n + kv.Value;
            }
            foreach (var kv in have) { after.TryGetValue(kv.Key, out int n); if (n != kv.Value) return $"his stock would have {kv.Value} {kv.Key}, not {n}"; }
            foreach (var kv in after) if (!have.ContainsKey(kv.Key)) return $"his stock wouldn't have {kv.Key}";
            return null;
        }

        private static string Text(Dictionary<string, int> d)
        {
            if (d.Count == 0) return "nothing";
            var parts = new List<string>();
            foreach (var kv in d) parts.Add($"{kv.Value} {kv.Key}");
            return string.Join(", ", parts);
        }

        private static void Apply(NPC npc, string stock)
        {
            if (npc.inventory == null) return;
            _applied.TryGetValue(npc.name, out int seen);
            _applied[npc.name] = seen + 1;
            Containers.Instance?.Apply(npc.inventory, stock);
            Plugin.Log.LogInfo($"{npc.name} has the host's stock: {npc.inventory.getAllItems().Count} items");
        }

        /// <summary>The host's stock goes to every copy of this trader here (one stock per name). False: none here.</summary>
        private static bool ApplyAll(string name, string stock)
        {
            bool any = false;
            foreach (var n in Object.FindObjectsOfType<NPC>())
                if (n != null && n.trader && n.name == name && n.inventory != null) { Apply(n, stock); any = true; }
            return any;
        }

        /// <summary>The trader of this name here nearest to where another game has him.</summary>
        private static NPC Find(string name, Vector3 at)
        {
            NPC best = null; float bestD = float.MaxValue;
            foreach (var n in Object.FindObjectsOfType<NPC>())
            {
                if (n == null || !n.trader || n.name != name || n.inventory == null) continue;
                var p = n.transform.position;
                float d = (p.x - at.x) * (p.x - at.x) + (p.z - at.z) * (p.z - at.z);
                if (d < bestD) { bestD = d; best = n; }
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
            if (!NetEnemies.ClientMode) return true;
            if (__instance.trader && __instance.inventory != null) TraderSync.Instance?.ClientStocking(__instance);
            return false;
        }

        static void Postfix(NPC __instance) { if (!NetEnemies.ClientMode) TraderSync.Instance?.HostStocked(__instance); }
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
            if (inv == null) return d;
            foreach (var it in inv.getAllItems())
            {
                if (InvItemClass.isNull(it) || string.IsNullOrEmpty(it.type)) continue;
                d.TryGetValue(it.type, out int n);
                d[it.type] = n + System.Math.Max(1, it.amount);
            }
            return d;
        }

        static void Prefix(DialogueWindow __instance, out object[] __state)
        {
            __state = null;
            if (__instance == null || __instance.npc == null || WorldActions.Replaying) return;
            __state = new object[] { Count(__instance.exchangeTrader), Count(__instance.exchangePlayer), __instance.npc.reputation };
        }

        static void Postfix(DialogueWindow __instance, object[] __state)
        {
            if (__state == null || __instance.npc == null) return;
            var took = (Dictionary<string, int>)__state[0];
            var gave = (Dictionary<string, int>)__state[1];
            if (took.Count == 0 && gave.Count == 0) return;
            // Accepted: both windows were emptied. Not accepted (not enough reputation, no room): the things are still in them.
            if (Count(__instance.exchangeTrader).Count != 0 || Count(__instance.exchangePlayer).Count != 0) return;
            TraderSync.Instance?.LocalTrade(__instance.npc, took, gave, __instance.npc.reputation - (int)__state[2]);
        }
    }
}
