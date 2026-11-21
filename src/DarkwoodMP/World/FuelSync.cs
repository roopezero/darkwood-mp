using System.Collections.Generic;
using System.Globalization;
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
    public sealed class FuelSync
    {
        public static FuelSync Instance { get; private set; }
        private const float SendInterval = 0.25f, HostInterval = 5f;
        private const byte IsGenerator = 1, IsSaw = 2;
        /// <summary>
        /// The most a pour may add when the game's gasoline can can't be looked
        /// up.
        /// </summary>
        private const float FallbackCan = 100f;

        /// <summary>
        /// What the local player did to one tank since the last send: fuel
        /// added (or a saw's cut), and the can it came from.
        /// </summary>
        private sealed class Pour
        {
            public float Amount, Drained;
            public InvItemClass Can;
        }

        private readonly Session _session;
        // Poured here, not sent yet. A saw's cut (negative) is kept apart from
        // a pour, since the host checks them differently.
        private readonly Dictionary<(Component c, bool cut), Pour> _pending =
            new Dictionary<(Component, bool), Pour>();
        private float _nextSend, _nextHost;
        private readonly Dictionary<Generator, (float fuel, bool on)> _sent =
            new Dictionary<Generator, (float, bool)>();

        internal static readonly HashSet<Generator> Generators =
            new HashSet<Generator>();

        public FuelSync(Session session)
        {
            Instance = this;
            _session = session;
            session.Register(MsgType.Fuel, OnFuel);
            session.OnSessionEnded += () =>
            {
                _pending.Clear();
                _sent.Clear();
            };
        }

        /// <summary>
        /// The local player changed this generator's or saw's fuel by delta (a
        /// pour, or a log cut). Each pour drains the can in the player's hand
        /// by one (Player.waitToSpillLiquid); a refusal gives that back.
        /// </summary>
        internal void Poured(Component c, float delta)
        {
            if (!WorldActions.Sharing || WorldActions.Replaying
                || Mathf.Abs(delta) < 0.0001f)
            {
                return;
            }
            var key = (c, delta < 0f);
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
            if (delta > 0f && !InvItemClass.isNull(can)
                && can.type == "gasoline")
            {
                p.Can = can;
                p.Drained += 1f;
            }
        }

        public void Update()
        {
            if (!WorldActions.Sharing)
            {
                _pending.Clear();
                return;
            }
            if (_pending.Count > 0 && Time.unscaledTime >= _nextSend)
            {
                Flush();
            }
            if (_session.IsHost && Time.unscaledTime >= _nextHost)
            {
                _nextHost = Time.unscaledTime + HostInterval;
                Generators.RemoveWhere(g => g == null);
                foreach (var g in Generators)
                {
                    if (_sent.TryGetValue(g, out var was)
                        && Mathf.Abs(was.fuel - g.fuel) < 0.5f
                        && was.on == g.isOn)
                    {
                        continue;
                    }
                    Send(g, except: 0);
                }
            }
        }

        /// <summary>
        /// Send what was poured now (before a switch-on goes out).
        /// </summary>
        internal void Flush()
        {
            _nextSend = Time.unscaledTime + SendInterval;
            foreach (var kv in _pending)
            {
                var c = kv.Key.c;
                if (c == null)
                {
                    continue;
                }
                if (_session.IsHost)
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

        /// <summary>
        /// What a gasoline can holds: the most one pour can add.
        /// </summary>
        private static float CanHolds()
        {
            try
            {
                ItemsDatabase instance = Singleton<ItemsDatabase>.Instance;
                InvItem it = null;
                if (instance != null)
                {
                    it = instance.getItem("gasoline", instantiate: false);
                }
                if (it != null && it.maxDurability > 0f)
                {
                    return it.maxDurability;
                }
            }
            catch (System.Exception)
            {
            }
            return FallbackCan;
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
            var w = _session.Begin(MsgType.Fuel);
            r.Write(w);
            byte kind;
            if (c is Generator)
            {
                kind = IsGenerator;
            }
            else
            {
                kind = IsSaw;
            }
            w.W.Write(kind);
            w.W.Write(p.Amount);
            w.W.Write((byte)0);
            string name = c.name;
            Authority.Request(w, $"fuel {p.Amount:+0.0;-0.0} for {name}",
                (ok, reason, result) =>
            {
                // Refused: all of it back to the can. Allowed, but only part of
                // it fitted: the rest back.
                float back = 0f;
                if (!ok)
                {
                    back = p.Drained;
                }
                else if (p.Amount > 0f
                    && float.TryParse(
                        result, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float took)
                    && took < p.Amount - 0.001f)
                {
                    back = p.Drained * (1f - Mathf.Clamp01(took / p.Amount));
                }
                if (back > 0.001f)
                {
                    Refund(p.Can, back, name);
                }
            }, inventory: false);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] asked to add fuel {p.Amount:0.0} to {name} ({r})");
            }
        }

        /// <summary>
        /// Put fuel back in the can it came from (or a can, if that one was
        /// used up by the pour).
        /// </summary>
        private static void Refund(InvItemClass can, float amount, string to)
        {
            var player = Player.Instance;
            if (player == null)
            {
                return;
            }
            if (can == null)
            {
                Plugin.Log.LogInfo(
                    $"[authority] {amount:0.0} fuel for {to} wasn't from a can: nothing to give back");
                return;
            }
            try
            {
                if (can.amount <= 0)
                {
                    // InvItemClass.drainDurability removed the emptied can:
                    // give a can back instead.
                    player.Inventory.addItemTypeToPlayer(
                        "gasoline", 1, dropIfNoRoom: true);
                    Plugin.Log.LogInfo(
                        $"[authority] gave back a gasoline can for the fuel {to} didn't take");
                    return;
                }
                float max;
                if (can.baseClass != null)
                {
                    max = can.baseClass.maxDurability;
                }
                else
                {
                    max = float.MaxValue;
                }
                can.durability = Mathf.Min(max, can.durability + amount);
                can.refresh();
                Plugin.Log.LogInfo(
                    $"[authority] gave back {amount:0.0} fuel to the gasoline can ({to} didn't take it)");
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
            var w = _session.Begin(MsgType.Fuel);
            r.Write(w);
            byte kind;
            if (g != null)
            {
                kind = IsGenerator;
            }
            else
            {
                kind = IsSaw;
            }
            w.W.Write(kind);
            w.W.Write(FuelOf(c));
            int onFlag = 0;
            if (g != null && g.isOn)
            {
                onFlag = 1;
            }
            w.W.Write((byte)onFlag);
            if (to != 0)
            {
                _session.SendTo(to, w, Channel.Control, reliable: true);
            }
            else
            {
                _session.Broadcast(
                    w, Channel.Control, reliable: true, except: except);
            }
            if (g != null && to == 0)
            {
                _sent[g] = (g.fuel, g.isOn);
            }
            if (WorldActions.Debug)
            {
                string name = c.name;
                float fuel = FuelOf(c);
                string state = "";
                if (g != null)
                {
                    if (g.isOn)
                    {
                        state = ", running";
                    }
                    else
                    {
                        state = ", off";
                    }
                }
                Plugin.Log.LogInfo(
                    $"[world] sent fuel {name} ({r}): {fuel:0.0}{state}");
            }
        }

        private void OnFuel(ulong from, PacketReader r)
        {
            if (!WorldShare.InWorld)
            {
                return;
            }
            if (_session.IsClient && from != _session.HostId)
            {
                return;
            }
            var obj = ObjRef.Read(r);
            byte what = r.R.ReadByte();
            // from a client: the amount it added; from the host: the level
            float value = r.R.ReadSingle();
            bool on = r.R.ReadByte() != 0;
            Component c;
            if (what == IsGenerator)
            {
                c = (Component)ObjRef.Resolve<Generator>(obj);
            }
            else
            {
                c = ObjRef.Resolve<Saw>(obj);
            }
            if (c == null)
            {
                if (_session.IsHost)
                {
                    string text = "saw";
                    if (what == IsGenerator)
                    {
                        text = "generator";
                    }
                    Authority.Refuse($"no {text} {obj} here");
                }
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

            if (_session.IsHost)
            {
                // Only taken inside a Request (Authority.RequestOnly): a player
                // poured this much, or cut a log at the saw.
                string refused = null;
                float took = value;
                bool near = Authority.Near(
                    from, c.transform.position, WorldActions.Reach);
                if (!near)
                {
                    refused = "too far from it";
                }
                else if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    refused = "not a number";
                }
                else if (value > 0f)
                {
                    float can = CanHolds();
                    if (value > can + 0.01f)
                    {
                        refused =
                            $"{value:0.0} is more than a can holds ({can:0})";
                    }
                    else if (have >= max - 0.01f)
                    {
                        refused = $"the tank is full ({have:0.0} of {max:0})";
                    }
                    else
                    {
                        took = Mathf.Min(value, max - have);
                    }
                }
                else
                {
                    // Fuel only goes down at the saw, a log's worth
                    // (Saw.fuelToConvert) for each log cut.
                    var saw = c as Saw;
                    float used = -value;
                    float logs = 0f;
                    if (saw != null)
                    {
                        logs = used / Mathf.Max(1, saw.fuelToConvert);
                    }
                    if (saw == null)
                    {
                        refused = "a generator's fuel only goes down by itself";
                    }
                    else if (logs < 0.99f
                        || Mathf.Abs(logs - Mathf.Round(logs)) > 0.02f)
                    {
                        refused =
                            $"{used:0.0} isn't a whole number of logs ({saw.fuelToConvert} each)";
                    }
                    else if (used > have + 0.5f)
                    {
                        refused = $"the saw has only {have:0.0}";
                    }
                }
                if (refused != null)
                {
                    Plugin.Log.LogInfo(
                        $"[authority] {_session.NameOf(from)}'s fuel {value:+0.0;-0.0} for {c.name} (have {have:0.0}): {refused}; sent back");
                    // the level to roll back to, before the Verdict
                    Send(c, except: 0, to: from);
                    Authority.Refuse(refused);
                    return;
                }
                fuel = Mathf.Clamp(have + took, 0f, max);
                Authority.Result(
                    took.ToString("0.###", CultureInfo.InvariantCulture));
            }
            else
            {
                // The host's level, plus what our player poured since that
                // hasn't gone out yet.
                fuel = value;
                foreach (var kv in _pending)
                {
                    if (kv.Key.c == c)
                    {
                        fuel += kv.Value.Amount;
                    }
                }
                fuel = Mathf.Clamp(fuel, 0f, max);
            }

            WorldActions.Replaying = true;
            try
            {
                if (g != null)
                {
                    g.fuel = fuel;
                    if (_session.IsClient)
                    {
                        if (on && !g.isOn)
                        {
                            var item = g.GetComponent<Item>();
                            if (item != null)
                            {
                                item.turnOn();
                            }
                            else
                            {
                                g.turnOn();
                            }
                        }
                        else if (!on && g.isOn)
                        {
                            g.powerDown();
                        }
                    }
                }
                else
                {
                    ((Saw)c).fuel = fuel;
                }
            }
            finally
            {
                WorldActions.Replaying = false;
            }
            // an open saw's "Fuel" label, the fuel bar under the cursor
            UiRefresh.FuelChanged(c);
            if (WorldActions.Debug)
            {
                string name = c.name;
                string change = "";
                if (_session.IsHost)
                {
                    change = $"{have:0.0} {value:+0.0;-0.0} -> ";
                }
                string state = "";
                if (g != null)
                {
                    if (g.isOn)
                    {
                        state = ", running";
                    }
                    else
                    {
                        state = ", off";
                    }
                }
                Plugin.Log.LogInfo(
                    $"[world] got fuel {name} ({obj}): {change}{fuel:0.0}{state}");
            }

            if (_session.IsHost)
            {
                // everyone, the pourer too: the level with everyone's pours in
                // it
                Send(c, except: 0);
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
            FuelSync instance = FuelSync.Instance;
            if (instance != null)
            {
                instance.Poured(__instance, __instance.fuel - __state);
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
            FuelSync instance = FuelSync.Instance;
            if (instance != null)
            {
                instance.Poured(__instance, __instance.fuel - __state);
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
            FuelSync instance = FuelSync.Instance;
            if (instance != null)
            {
                instance.Poured(__instance, __instance.fuel - __state);
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Generator), "Start")]
    static class GeneratorListPatch
    {
        static void Postfix(Generator __instance) =>
            FuelSync.Generators.Add(__instance);
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
            if (WorldActions.Sharing && !WorldActions.Replaying)
            {
                FuelSync instance = FuelSync.Instance;
                if (instance != null)
                {
                    instance.Flush();
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
        /// <summary>
        /// For the tests: the last drain's generator, whose skill it used, and
        /// the modifier.
        /// </summary>
        public static string Last;

        static void Prefix(Generator __instance, out float? __state)
        {
            __state = null;
            var s = Session.Instance;
            var p = Player.Instance;
            if (s != null && s.IsClient && p != null && WorldActions.Sharing)
            {
                __state = p.electricityModifier;
                p.electricityModifier = 0f;
                return;
            }
            if (s == null || !s.IsHost || p == null || !Proxies.Any
                || Characters.Instance == null)
            {
                return;
            }
            var at = __instance.transform.position;
            float best;
            if (SharedCullingPatch.HostAway)
            {
                best = float.MaxValue;
            }
            else
            {
                best = StateDump.Flat(p.transform.position - at);
            }
            NetPlayerProxy nearest = null;
            foreach (var x in NetPlayerProxy.All.Values)
            {
                if (x == null || x.Away || x.InLocation != null
                    || x.Owner == Sync.PlayerSync.DummyId)
                {
                    continue;
                }
                float d = StateDump.Flat(x.transform.position - at);
                if (d < best)
                {
                    best = d;
                    nearest = x;
                }
            }
            bool? has = null;
            if (nearest != null)
            {
                has = Characters.Instance.Electrician(nearest.Owner);
            }
            string ownerName = "ours";
            if (nearest != null)
            {
                ownerName = nearest.OwnerName;
            }
            float modifier;
            if (has.HasValue)
            {
                if (has.Value)
                {
                    modifier = 0.5f;
                }
                else
                {
                    modifier = 1f;
                }
            }
            else
            {
                modifier = p.electricityModifier;
            }
            Last = $"{__instance.name}: {ownerName}'s skill, {modifier}";
            if (has == null)
            {
                return;
            }
            __state = p.electricityModifier;
            if (has.Value)
            {
                p.electricityModifier = 0.5f;
            }
            else
            {
                p.electricityModifier = 1f;
            }
        }

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
