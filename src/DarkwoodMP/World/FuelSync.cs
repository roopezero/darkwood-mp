using System.Collections.Generic;
using DarkwoodMP.World.Fuel;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
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
    /// (GeneratorDrainSkillPatch): every 5 seconds it sends each generator's
    /// fuel and whether it runs, if that changed, and the others take it
    /// (switching theirs on or off to match).
    /// </summary>
    internal sealed class FuelSync
    {

        /// <summary>
        /// What the local player did to one tank since the last send: fuel
        /// added (or a saw's cut), and the can it came from.
        /// </summary>
        private sealed class Pour
        {
            public float Amount, Drained;
            public InvItemClass Can;
        }

        private readonly CoopSession _coop;
        // Poured here, not sent yet. A saw's cut (negative) is kept apart from
        // a pour, since the host checks them differently.
        private readonly Dictionary<(Component c, bool cut), Pour> _pending =
            new Dictionary<(Component, bool), Pour>();
        private Seconds _nextSend, _nextHost;
        private readonly Dictionary<Generator, (float fuel, bool on)> _sent =
            new Dictionary<Generator, (float, bool)>();

        public FuelSync(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.Fuel, OnFuel);
        }

        /// <summary>
        /// For the tests: the last drain's generator, whose skill it used, and
        /// the modifier (GeneratorDrainSkillPatch).
        /// </summary>
        public string LastDrain { get; set; }

        private Session Net => _coop.Net;

        /// The generators the game started, kept as long as the game runs
        /// (ModHost.Generators): a world loaded before hosting has them too.
        private static HashSet<Generator> Generators =>
            Plugin.Instance.Host.Generators;

        private static Seconds Now => new Seconds(Time.unscaledTime);

        /// <summary>
        /// The local player changed this generator's or saw's fuel by delta (a
        /// pour, or a log cut). Each pour drains the can in the player's hand
        /// by one (Player.waitToSpillLiquid); a refusal gives that back.
        /// </summary>
        internal void Poured(Component c, float delta)
        {
            var guards = Coop.Guards;
            if (!PourRules.Records(guards.Sharing, guards.Replaying, delta))
            {
                return;
            }

            var key = (c, PourRules.IsCut(delta));
            if (!_pending.TryGetValue(key, out var p))
            {
                _pending[key] = p = new Pour();
            }

            p.Amount += delta;
            InvItemClass can = null;
            if (Player.Instance != null)
            {
                can = Player.Instance.currentItem;
            }

            bool holds = !InvItemClass.isNull(can);
            string type = null;
            if (holds)
            {
                type = can.type;
            }

            if (PourRules.FromCan(delta, holds, type))
            {
                p.Can = can;
                p.Drained += 1f;
            }
        }

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

        /// <summary>
        /// What a gasoline can holds: the most one pour can add.
        /// </summary>
        private static float CanHolds()
        {
            float most = 0f;
            try
            {
                ItemsDatabase instance = Singleton<ItemsDatabase>.Instance;
                InvItem it = null;
                if (instance != null)
                {
                    it = instance.getItem("gasoline", instantiate: false);
                }

                if (it != null)
                {
                    most = it.maxDurability;
                }
            }
            catch (System.Exception)
            {
                most = 0f;
            }

            return FuelRules.CanHolds(most);
        }

        /// <summary>
        /// Client: ask the host to add what our player poured (or used, cutting
        /// a log).
        /// </summary>
        private void Request(Component c, Pour p)
        {
            var r = ObjRef.Of(c.gameObject);
            if (!r.Valid)
            {
                return;
            }

            var w = Net.Begin(MsgType.Fuel);
            FuelMessage.Write(w, new TankFuel
            {
                Tank = ObjRef.NameOf(r),
                Kind = TankOf(c),
                Value = p.Amount,
            });

            string name = c.name;
            Coop.Current.Authority.Request(
                w, $"fuel {p.Amount:+0.0;-0.0} for {name}",
                (ok, reason, result) =>
            {
                // Refused: all of it back to the can. Allowed, but only part of
                // it fitted: the rest back.
                float back = PourRules.Back(new RefundFacts
                {
                    Allowed = ok,
                    Amount = p.Amount,
                    Drained = p.Drained,
                    Result = result,
                });

                if (PourRules.Refunds(back))
                {
                    Refund(p.Can, back, name);
                }
            }, inventory: false);

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] asked to add fuel {p.Amount:0.0} to {name} "
                        + $"({r})");
            }
        }

        /// <summary>
        /// Put fuel back in the can it came from (or a can, if that one was
        /// used up by the pour).
        /// </summary>
        private static void Refund(InvItemClass can, float amount, string to)
        {
            var player = Player.Instance;
            try
            {
                int left = 0;
                if (can != null)
                {
                    left = can.amount;
                }

                var kind = PourRules.RefundOf(
                    player != null, can != null, left);
                if (kind == RefundKind.Nothing)
                {
                    if (player != null)
                    {
                        Plugin.Log.LogInfo(
                            $"[authority] {amount:0.0} fuel for {to} wasn't "
                                + "from a can: nothing to give back");
                    }

                    return;
                }

                if (kind == RefundKind.NewCan)
                {
                    // InvItemClass.drainDurability removed the emptied can:
                    // give a can back instead.
                    player.Inventory.addItemTypeToPlayer(
                        "gasoline", 1, dropIfNoRoom: true);
                    Plugin.Log.LogInfo(
                        "[authority] gave back a gasoline can for the fuel "
                            + $"{to} didn't take");
                    return;
                }

                float max = float.MaxValue;
                if (can.baseClass != null)
                {
                    max = can.baseClass.maxDurability;
                }

                can.durability = PourRules.Refilled(
                    can.durability, amount, max);
                can.refresh();
                Plugin.Log.LogInfo(
                    $"[authority] gave back {amount:0.0} fuel to the gasoline "
                        + $"can ({to} didn't take it)");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[authority] couldn't give the fuel back: {e.Message}");
            }
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

    [HarmonyPatch(typeof(Generator), nameof(Generator.addFuel))]
    static class GeneratorFuelPatch
    {
        static void Prefix(Generator __instance, out float __state) =>
            __state = __instance.fuel;
        static void Postfix(Generator __instance, float __state)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Fuel.Poured(__instance, __instance.fuel - __state);
            }
        }
    }

    [HarmonyPatch(typeof(Saw), nameof(Saw.addFuel))]
    static class SawFuelPatch
    {
        static void Prefix(Saw __instance, out float __state) =>
            __state = __instance.fuel;
        static void Postfix(Saw __instance, float __state)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Fuel.Poured(__instance, __instance.fuel - __state);
            }
        }
    }

    /// <summary>
    /// Cutting a log (Saw.convert) uses fuelToConvert of the saw's fuel; it
    /// goes out like a pour.
    /// </summary>
    [HarmonyPatch(typeof(Saw), nameof(Saw.convert))]
    static class SawConvertPatch
    {
        // A finalizer, not a postfix: the fuel is taken before Saw.refresh,
        // which throws when the saw's window isn't set up (a test calling it
        // directly).
        static void Prefix(Saw __instance, out float __state) =>
            __state = __instance.fuel;

        static System.Exception Finalizer(
            Saw __instance, System.Exception __exception, float __state)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Fuel.Poured(__instance, __instance.fuel - __state);
            }

            return __exception;
        }
    }

    [HarmonyPatch(typeof(Generator), "Start")]
    static class GeneratorListPatch
    {
        static void Postfix(Generator __instance) =>
            Plugin.Instance.Host.Generators.Add(__instance);
    }

    /// <summary>
    /// Fuel just poured goes out before the switch-on does (ItemOnPatch sends
    /// it in its postfix).
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.turnOn))]
    static class FuelBeforeOnPatch
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix()
        {
            if (Coop.Guards.Sharing && !Coop.Guards.Replaying)
            {
                CoopSession coop = Coop.Current;
                if (coop != null)
                {
                    coop.Fuel.Flush();
                }
            }
        }
    }

    /// <summary>
    /// Host: a generator burns fuel by the electricity skill of the player
    /// nearest it (Generator.drainFuel uses our own player's
    /// Player.electricityModifier for every generator). A joiner's skill is
    /// read from the character it last sent (Characters.Electrician); while it
    /// isn't known yet, ours is used, as before. The host's fuel is the one
    /// every game keeps (FuelSync).
    ///
    /// Client: its generators burn nothing (the modifier is 0 for the call);
    /// the level comes from the host. The rest of drainFuel still runs, so the
    /// lights dim on low fuel as the host's level says.
    /// </summary>
    [HarmonyPatch(typeof(Generator), nameof(Generator.drainFuel))]
    static class GeneratorDrainSkillPatch
    {
        static void Prefix(Generator __instance, out float? __state)
        {
            __state = null;
            var s = Session.Instance;
            var p = Player.Instance;
            var start = DrainRules.Start(new DrainFacts
            {
                Session = s != null,
                Client = s != null && s.IsClient,
                Host = s != null && s.IsHost,
                Player = p != null,
                Sharing = Coop.Guards.Sharing,
                Proxies = Proxies.Any,
                Characters = Characters.Instance != null,
            });

            if (start == DrainStart.Zero)
            {
                __state = p.electricityModifier;
                p.electricityModifier = 0f;
                return;
            }

            if (start == DrainStart.Leave)
            {
                return;
            }

            var standIns = new List<NetPlayerProxy>(NetPlayerProxy.All.Values);
            var players = new List<DrainPlayer>(standIns.Count);
            foreach (var x in standIns)
            {
                players.Add(PlayerOf(x));
            }

            int i = DrainRules.Nearest(new NearestFacts
            {
                Generator = Flat(__instance.transform.position),
                Host = Flat(p.transform.position),
                HostAway = SharedCullingPatch.HostAway,
                Players = players,
            });

            NetPlayerProxy nearest = null;
            if (i >= 0)
            {
                nearest = standIns[i];
            }

            bool? has = null;
            string ownerName = "ours";
            if (nearest != null)
            {
                has = Characters.Instance.Electrician(nearest.Owner);
                ownerName = nearest.OwnerName;
            }

            float modifier = p.electricityModifier;
            if (has.HasValue)
            {
                modifier = DrainRules.Modifier(has.Value);
            }

            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Fuel.LastDrain =
                    $"{__instance.name}: {ownerName}'s skill, {modifier}";
            }

            if (has == null)
            {
                return;
            }

            __state = p.electricityModifier;
            p.electricityModifier = modifier;
        }

        /// <summary>
        /// A stand-in, for DrainRules.Nearest: the only code reading it
        /// there.
        /// </summary>
        static DrainPlayer PlayerOf(NetPlayerProxy x)
        {
            if (x == null)
            {
                return default;
            }

            return new DrainPlayer
            {
                Exists = true,
                Away = x.Away,
                InLocation = x.InLocation != null,
                Dummy = x.Owner == Sync.PlayerSync.DummyId,
                Pos = Flat(x.transform.position),
            };
        }

        static Vec2 Flat(Vector3 v) => new Vec2(v.x, v.z);

        static System.Exception Finalizer(
            System.Exception __exception, float? __state)
        {
            if (__state.HasValue && Player.Instance != null)
            {
                Player.Instance.electricityModifier = __state.Value;
            }

            return __exception;
        }
    }
}
