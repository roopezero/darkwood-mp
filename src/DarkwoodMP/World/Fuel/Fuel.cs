using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Fuel
{
    /// <summary>
    /// Fuel in generators and the saw, shared. A player pours fuel a little at
    /// a time (Generator.addFuel and Saw.addFuel, from the gasoline can in
    /// their hand), and the saw uses some for each log it cuts (Saw.convert).
    ///
    /// The host's fuel is the one that counts. A client adds what it poured to
    /// its own game at once (a prediction) and sends the amount, not the level
    /// it now has, as a Request up to four times a second (Fuel): two players
    /// pouring into the same tank at the same moment would otherwise each send
    /// "what I have now", and the last one would win. The host checks the
    /// player is at it and the amount (no more than a can holds; a saw only
    /// loses a log's worth at a time), adds it to its own level, up to the
    /// tank, and sends everyone the level it now has.
    /// A pour into a full tank is refused, and the part of a pour that didn't
    /// fit comes back: the can gets that fuel back. The host's own pours go out
    /// as its level.
    ///
    /// Fuel goes out at once before anything is switched on, so a generator
    /// that was just filled also starts in the other games (Generator.turnOn
    /// does nothing without fuel). Only the host burns its generators' fuel
    /// (GeneratorDrain): every 5 seconds it sends each generator's
    /// fuel and whether it runs, if that changed, and the others take it
    /// (switching theirs on or off to match).
    /// </summary>
    internal sealed partial class Fuel
    {

        private readonly CoopSession _coop;
        // Poured here, not sent yet. A saw's cut (negative) is kept apart from
        // a pour, since the host checks them differently.
        private readonly Dictionary<(Component c, bool cut), Pour> _pending =
            new Dictionary<(Component, bool), Pour>();
        private Seconds _nextSend, _nextHost;
        private readonly Dictionary<Generator, (float fuel, bool on)> _sent =
            new Dictionary<Generator, (float, bool)>();

        public Fuel(CoopSession coop)
        {
            _coop = coop;
            Drain = new GeneratorDrain(coop);
            coop.Handle(MsgType.Fuel, OnFuel);
        }

        /// <summary>
        /// Generator.drainFuel: whose skill burns the fuel.
        /// </summary>
        public readonly GeneratorDrain Drain;

        private Session Net => _coop.Net;

        /// The generators the game started, kept as long as the game runs
        /// (ModHost.Generators): a world loaded before hosting has them too.
        private static HashSet<Generator> Generators =>
            Plugin.Instance.Host.Generators;

        private static Seconds Now => new Seconds(Time.unscaledTime);

        public void Update()
        {
            if (!Coop.Guards.Sharing)
            {
                _pending.Clear();
                return;
            }

            if (PourRules.FlushDue(_pending.Count, Now, _nextSend))
            {
                Flush();
            }

            if (!FuelRules.CheckDue(Net.IsHost, Now, _nextHost))
            {
                return;
            }

            _nextHost = FuelRules.NextCheck(Now);
            Generators.RemoveWhere(g => g == null);
            foreach (var g in Generators)
            {
                if (FuelRules.Changed(SentOf(g)))
                {
                    Send(g, except: 0);
                }
            }
        }

        /// <summary>
        /// What was last sent about a generator, and what it is now: the
        /// only code reading them for FuelRules.Changed.
        /// </summary>
        private SentFacts SentOf(Generator g)
        {
            var facts = new SentFacts { Fuel = g.fuel, On = g.isOn };
            if (_sent.TryGetValue(g, out var was))
            {
                facts = facts with
                {
                    Sent = true,
                    SentFuel = was.fuel,
                    SentOn = was.on,
                };
            }

            return facts;
        }

        /// <summary>
        /// Item.turnOn, before it: what was poured goes out first, so a
        /// generator that was just filled also starts in the other games
        /// (Generator.turnOn does nothing without fuel).
        /// </summary>
        internal void BeforeOn()
        {
            var guards = _coop.Guards;
            if (PourRules.FlushesBeforeOn(guards.Sharing, guards.Replaying))
            {
                Flush();
            }
        }

        /// <summary>
        /// Send what was poured now (before a switch-on goes out).
        /// </summary>
        internal void Flush()
        {
            _nextSend = PourRules.NextFlush(Now);
            foreach (var kv in _pending)
            {
                var c = kv.Key.c;
                if (c == null)
                {
                    continue;
                }

                if (Net.IsHost)
                {
                    Send(c, except: 0);
                }
                else
                {
                    Request(c, kv.Value);
                }
            }

            _pending.Clear();
        }

        private static float FuelOf(Component c)
        {
            if (c is Generator g)
            {
                return g.fuel;
            }

            return ((Saw)c).fuel;
        }

        private static float MaxOf(Component c)
        {
            if (c is Generator g)
            {
                return g.maxFuel;
            }

            return ((Saw)c).maxFuel;
        }

        private static FuelTank TankOf(Component c)
        {
            if (c is Generator)
            {
                return FuelTank.Generator;
            }

            return FuelTank.Saw;
        }

        private void Send(Component c, ulong except, ulong to = 0)
        {
            var r = ObjRef.Of(c.gameObject);
            if (!r.Valid)
            {
                return;
            }

            var g = c as Generator;
            bool on = g != null && g.isOn;
            var w = Net.Begin(MsgType.Fuel);
            FuelMessage.Write(w, new TankFuel
            {
                Tank = ObjRef.NameOf(r),
                Kind = TankOf(c),
                Value = FuelOf(c),
                On = on,
            });

            if (to != 0)
            {
                Net.SendTo(to, w, Channel.Control, reliable: true);
            }
            else
            {
                Net.Broadcast(
                    w, Channel.Control, reliable: true, except: except);
            }

            if (g != null && to == 0)
            {
                _sent[g] = (g.fuel, g.isOn);
            }

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent fuel {c.name} ({r}): "
                        + $"{FuelOf(c):0.0}{StateOf(g)}");
            }
        }

        /// <summary>
        /// ", running" or ", off" for a generator's log line; "" for a saw.
        /// </summary>
        private static string StateOf(Generator g)
        {
            if (g == null)
            {
                return "";
            }

            if (g.isOn)
            {
                return ", running";
            }

            return ", off";
        }

        /// <summary>
        /// What the host knows about the tank a client's Fuel names: the
        /// only code reading the generator or saw for FuelRules.MayAdd. c is
        /// null when it isn't here.
        /// </summary>
        private static FuelFacts FactsOf(Component c, in TankFuel sent)
        {
            var facts = new FuelFacts
            {
                Tank = sent.Kind,
                Ref = sent.Tank,
                Value = sent.Value,
            };

            if (c == null)
            {
                return facts;
            }

            int perLog = 0;
            if (c is Saw saw)
            {
                perLog = saw.fuelToConvert;
            }

            return facts with
            {
                Found = true,
                Have = FuelOf(c),
                Max = MaxOf(c),
                CanHolds = CanHolds(),
                FuelToConvert = perLog,
                Reach = Coop.Current.Authority.ReachOf(c.transform.position),
            };
        }

        /// <summary>
        /// The pours of ours that haven't gone out yet, for this tank.
        /// </summary>
        private float PendingFor(Component c)
        {
            float pending = 0f;
            foreach (var kv in _pending)
            {
                if (kv.Key.c == c)
                {
                    pending += kv.Value.Amount;
                }
            }

            return pending;
        }

        private void OnFuel(ulong from, PacketReader r)
        {
            if (!WorldShare.InWorld)
            {
                return;
            }

            if (Net.IsClient && from != Net.HostId)
            {
                return;
            }

            TankFuel sent = FuelMessage.Read(r);
            var obj = ObjRef.RefOf(sent.Tank);
            // from a client: the amount it added; from the host: the level
            float value = sent.Value;
            Component c;
            if (sent.Kind == FuelTank.Generator)
            {
                c = (Component)ObjRef.Resolve<Generator>(obj);
            }
            else
            {
                c = ObjRef.Resolve<Saw>(obj);
            }

            FuelAnswer answer = default;
            if (Net.IsHost)
            {
                // Only taken inside a Request (Authority.RequestOnly): a player
                // poured this much, or cut a log at the saw.
                answer = FuelRules.MayAdd(FactsOf(c, sent));
                if (c == null)
                {
                    Coop.Current.Authority.Refuse(answer.Verdict);
                }
            }

            if (c == null)
            {
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] got fuel for {obj}: not found here");
                }

                return;
            }

            var g = c as Generator;
            float max = MaxOf(c);
            float have = FuelOf(c);
            float fuel;
            if (Net.IsHost)
            {
                if (answer.Verdict.Refused)
                {
                    Plugin.Log.LogInfo(
                        $"[authority] {Net.NameOf(from)}'s fuel "
                            + $"{value:+0.0;-0.0} for {c.name} (have "
                            + $"{have:0.0}): "
                            + $"{Refusals.Text(answer.Verdict)}; sent back");
                    // the level to roll back to, before the Verdict
                    Send(c, except: 0, to: from);
                    Coop.Current.Authority.Refuse(answer.Verdict);
                    return;
                }

                fuel = answer.Level;
                Coop.Current.Authority.Result(FuelRules.ResultOf(answer.Took));
            }
            else
            {
                // The host's level, plus what our player poured since that
                // hasn't gone out yet.
                fuel = PourRules.Shown(value, PendingFor(c), max);
            }

            Coop.Guards.Replaying = true;
            try
            {
                if (g != null)
                {
                    g.fuel = fuel;
                    if (Net.IsClient)
                    {
                        Switch(g, sent.On);
                    }
                }
                else
                {
                    ((Saw)c).fuel = fuel;
                }
            }
            finally
            {
                Coop.Guards.Replaying = false;
            }

            // an open saw's "Fuel" label, the fuel bar under the cursor
            UiRefresh.FuelChanged(c);
            if (WorldActions.Debug)
            {
                string change = "";
                if (Net.IsHost)
                {
                    change = $"{have:0.0} {value:+0.0;-0.0} -> ";
                }

                Plugin.Log.LogInfo(
                    $"[world] got fuel {c.name} ({obj}): "
                        + $"{change}{fuel:0.0}{StateOf(g)}");
            }

            if (Net.IsHost)
            {
                // everyone, the pourer too: the level with everyone's pours in
                // it
                Send(c, except: 0);
            }
        }

        /// <summary>
        /// Client: switch its generator on or off to match the host's.
        /// </summary>
        private static void Switch(Generator g, bool on)
        {
            var item = g.GetComponent<Item>();
            var act = PourRules.Switch(on, g.isOn, item != null);
            if (act == GeneratorSwitch.TurnOnItem)
            {
                item.turnOn();
            }
            else if (act == GeneratorSwitch.TurnOnGenerator)
            {
                g.turnOn();
            }
            else if (act == GeneratorSwitch.PowerDown)
            {
                g.powerDown();
            }
        }
    }
}
