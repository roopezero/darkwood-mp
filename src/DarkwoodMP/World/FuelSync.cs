using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Fuel in generators and the saw, shared. A player pours fuel a little at a time (Generator.addFuel and
    /// Saw.addFuel, from the player's gasoline), and the saw uses some for each log it cuts (Saw.convert); while
    /// the amount changes it goes to the others (Fuel) up to four
    /// times a second, and at once before anything is switched on, so a generator that was just filled also starts
    /// in the other games (Generator.turnOn does nothing without fuel). A client's pour goes to the host, which
    /// checks the player is at it and the amount could be poured, then passes it on; if not, the player gets the
    /// amount back as it is on the host.
    ///
    /// Each game burns its generators' fuel by the same shared clock. The host's is the one that counts: every
    /// 5 seconds it sends each generator's fuel and whether it runs, if that changed, and the others take it
    /// (switching theirs on or off to match).
    /// </summary>
    public sealed class FuelSync
    {
        public static FuelSync Instance { get; private set; }
        private const float SendInterval = 0.25f, HostInterval = 5f;
        private const byte IsGenerator = 1, IsSaw = 2;

        private readonly Session _session;
        private readonly Dictionary<Component, float> _pending = new Dictionary<Component, float>(); // poured here, not sent yet
        private float _nextSend, _nextHost;
        private readonly Dictionary<Generator, (float fuel, bool on)> _sent = new Dictionary<Generator, (float, bool)>();

        internal static readonly HashSet<Generator> Generators = new HashSet<Generator>();

        public FuelSync(Session session)
        {
            Instance = this;
            _session = session;
            session.Register(MsgType.Fuel, OnFuel);
            session.OnSessionEnded += () => { _pending.Clear(); _sent.Clear(); };
        }

        /// <summary>The local player poured fuel into this generator or saw.</summary>
        internal void Poured(Component c)
        {
            if (!WorldActions.Sharing || WorldActions.Replaying) return;
            _pending[c] = 0f;
        }

        public void Update()
        {
            if (!WorldActions.Sharing) { _pending.Clear(); return; }
            if (_pending.Count > 0 && Time.unscaledTime >= _nextSend) Flush();
            if (_session.IsHost && Time.unscaledTime >= _nextHost)
            {
                _nextHost = Time.unscaledTime + HostInterval;
                Generators.RemoveWhere(g => g == null);
                foreach (var g in Generators)
                {
                    if (_sent.TryGetValue(g, out var was) && Mathf.Abs(was.fuel - g.fuel) < 0.5f && was.on == g.isOn) continue;
                    Send(g, except: 0);
                }
            }
        }

        /// <summary>Send what was poured now (before a switch-on goes out).</summary>
        internal void Flush()
        {
            _nextSend = Time.unscaledTime + SendInterval;
            foreach (var c in _pending.Keys) if (c != null) Send(c, except: 0);
            _pending.Clear();
        }

        private void Send(Component c, ulong except, ulong to = 0)
        {
            var r = ObjRef.Of(c.gameObject);
            if (!r.Valid) return;
            var g = c as Generator;
            var w = _session.Begin(MsgType.Fuel);
            r.Write(w);
            w.W.Write(g != null ? IsGenerator : IsSaw);
            w.W.Write(g != null ? g.fuel : ((Saw)c).fuel);
            w.W.Write((byte)(g != null && g.isOn ? 1 : 0));
            if (to != 0) _session.SendTo(to, w, Channel.Control, reliable: true);
            else if (_session.IsHost) _session.Broadcast(w, Channel.Control, reliable: true, except: except);
            else _session.SendToHost(w, Channel.Control, reliable: true);
            if (g != null && _session.IsHost) _sent[g] = (g.fuel, g.isOn);
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] sent fuel {c.name} ({r}): {(g != null ? g.fuel : ((Saw)c).fuel):0.0}{(g != null ? g.isOn ? ", running" : ", off" : "")}");
        }

        private void OnFuel(ulong from, PacketReader r)
        {
            if (!WorldShare.InWorld) return;
            if (_session.IsClient && from != _session.HostId) return;
            var obj = ObjRef.Read(r);
            byte what = r.R.ReadByte();
            float fuel = r.R.ReadSingle();
            bool on = r.R.ReadByte() != 0;
            Component c = what == IsGenerator ? (Component)ObjRef.Resolve<Generator>(obj) : ObjRef.Resolve<Saw>(obj);
            if (c == null) { if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] got fuel for {obj}: not found here"); return; }
            var g = c as Generator;
            float max = g != null ? g.maxFuel : ((Saw)c).maxFuel;
            float have = g != null ? g.fuel : ((Saw)c).fuel;

            if (_session.IsHost)
            {
                // A player pours at the generator, and only adds: up to its tank, and not more than they could pour since.
                bool near = Authority.Near(from, c.transform.position, WorldActions.Reach);
                if (!near || float.IsNaN(fuel) || fuel > max + 0.01f || fuel > have + 60f)
                {
                    Plugin.Log.LogInfo($"[authority] {_session.NameOf(from)}'s fuel for {c.name} ({fuel:0.0}, have {have:0.0}){(near ? "" : " from too far")}: refused, sent back");
                    Send(c, except: 0, to: from);
                    return;
                }
                var saw = c as Saw;
                if (saw != null && fuel < have)
                {
                    // A saw only loses fuel by cutting a log (Saw.convert), fuelToConvert at a time.
                    if (have - fuel < saw.fuelToConvert - 0.5f)
                    {
                        Plugin.Log.LogInfo($"[authority] {_session.NameOf(from)}'s fuel for {c.name} ({fuel:0.0}, have {have:0.0}): less than a log's worth used, sent back");
                        Send(c, except: 0, to: from);
                        return;
                    }
                }
                else if (fuel < have) fuel = have; // burnt meanwhile here: the host's fuel and the pour both stand
            }

            WorldActions.Replaying = true;
            try
            {
                if (g != null)
                {
                    g.fuel = Mathf.Clamp(fuel, 0f, g.maxFuel);
                    if (_session.IsClient)
                    {
                        if (on && !g.isOn) { var item = g.GetComponent<Item>(); if (item != null) item.turnOn(); else g.turnOn(); }
                        else if (!on && g.isOn) g.powerDown();
                    }
                }
                else ((Saw)c).fuel = Mathf.Clamp(fuel, 0f, max);
            }
            finally { WorldActions.Replaying = false; }
            UiRefresh.FuelChanged(c); // an open saw's "Fuel" label, the fuel bar under the cursor
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] got fuel {c.name} ({obj}): {fuel:0.0}{(g != null ? g.isOn ? ", running" : ", off" : "")}");

            if (_session.IsHost)
            {
                if (g != null) _sent[g] = (g.fuel, g.isOn);
                Send(c, except: from);
            }
        }
    }

    [HarmonyPatch(typeof(Generator), nameof(Generator.addFuel))]
    static class GeneratorFuelPatch
    {
        static void Postfix(Generator __instance) => FuelSync.Instance?.Poured(__instance);
    }

    [HarmonyPatch(typeof(Saw), nameof(Saw.addFuel))]
    static class SawFuelPatch
    {
        static void Postfix(Saw __instance) => FuelSync.Instance?.Poured(__instance);
    }

    /// <summary>Cutting a log (Saw.convert) uses fuelToConvert of the saw's fuel; it goes out like a pour.</summary>
    [HarmonyPatch(typeof(Saw), nameof(Saw.convert))]
    static class SawConvertPatch
    {
        // A finalizer, not a postfix: the fuel is taken before Saw.refresh, which throws when the saw's window
        // isn't set up (a test calling it directly).
        static System.Exception Finalizer(Saw __instance, System.Exception __exception)
        {
            FuelSync.Instance?.Poured(__instance);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Generator), "Start")]
    static class GeneratorListPatch
    {
        static void Postfix(Generator __instance) => FuelSync.Generators.Add(__instance);
    }

    /// <summary>Fuel just poured goes out before the switch-on does (ItemOnPatch sends it in its postfix).</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.turnOn))]
    static class FuelBeforeOnPatch
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix() { if (WorldActions.Sharing && !WorldActions.Replaying) FuelSync.Instance?.Flush(); }
    }

    /// <summary>
    /// Host: a generator burns fuel by the electricity skill of the player nearest it (Generator.drainFuel uses our
    /// own player's Player.electricityModifier for every generator). A joiner's skill is read from the character it
    /// last sent (Characters.Electrician); while it isn't known yet, ours is used, as before. The host's fuel is the
    /// one every game keeps (FuelSync), so a joiner's own drain is put right within seconds.
    /// </summary>
    [HarmonyPatch(typeof(Generator), nameof(Generator.drainFuel))]
    static class GeneratorDrainSkillPatch
    {
        /// <summary>For the tests: the last drain's generator, whose skill it used, and the modifier.</summary>
        public static string Last;

        static void Prefix(Generator __instance, out float? __state)
        {
            __state = null;
            var s = Session.Instance;
            var p = Player.Instance;
            if (s == null || !s.IsHost || p == null || !Proxies.Any || Characters.Instance == null) return;
            var at = __instance.transform.position;
            float best = SharedCullingPatch.HostAway ? float.MaxValue : StateDump.Flat(p.transform.position - at);
            NetPlayerProxy nearest = null;
            foreach (var x in NetPlayerProxy.All.Values)
            {
                if (x == null || x.Away || x.InLocation != null || x.Owner == Sync.PlayerSync.DummyId) continue;
                float d = StateDump.Flat(x.transform.position - at);
                if (d < best) { best = d; nearest = x; }
            }
            var has = nearest != null ? Characters.Instance.Electrician(nearest.Owner) : null;
            Last = $"{__instance.name}: {(nearest != null ? nearest.OwnerName : "ours")}'s skill, {(has.HasValue ? (has.Value ? 0.5f : 1f) : p.electricityModifier)}";
            if (has == null) return;
            __state = p.electricityModifier;
            p.electricityModifier = has.Value ? 0.5f : 1f;
        }

        static System.Exception Finalizer(System.Exception __exception, float? __state)
        {
            if (__state.HasValue && Player.Instance != null) Player.Instance.electricityModifier = __state.Value;
            return __exception;
        }
    }
}
