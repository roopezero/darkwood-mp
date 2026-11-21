using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Traders;
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
    internal sealed partial class TraderSync
    {
        private readonly CoopSession _coop;
        private readonly Session _session;
        // client: stock for traders not here yet
        private readonly Dictionary<string, string> _waiting =
            new Dictionary<string, string>();

        public TraderSync(CoopSession coop)
        {
            _coop = coop;
            _session = coop.Net;
            coop.Handle(MsgType.TraderAsk, OnAsk);
            coop.Handle(MsgType.TraderStock, OnStock);
            coop.Handle(MsgType.TraderTrade, OnTrade);
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

        private void Apply(NPC npc, string stock)
        {
            if (Inv(npc) == null)
            {
                return;
            }

            _applied.TryGetValue(npc.GetInstanceID(), out int seen);
            _applied[npc.GetInstanceID()] = seen + 1;
            _coop.Containers.Apply(Inv(npc), stock);

            Plugin.Log.LogInfo(
                $"{npc.name} has the host's stock: "
                    + $"{Inv(npc).getAllItems().Count} items");
        }

        /// <summary>
        /// The host's stock goes to our copy of that trader. False: he isn't
        /// here.
        /// </summary>
        private bool ApplyTo(
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
}
