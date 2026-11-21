using System.Collections;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Fire;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Things on fire (the game's Burn: flames and the fire sound on an enemy,
    /// a door, a window, furniture), shared from the host. The host's world is
    /// the one that burns: there, fire hurts enemies and breaks doors and
    /// furniture, and those hits reach the others as before (enemy health,
    /// DoorHit...). When something starts or stops burning there, the clients
    /// light or put out their copy of it, for the look and sound.
    ///
    /// On a client, fire only shows: Burn's damage over time
    /// (Burn.waitToGetHit) doesn't run there, except on the client's own
    /// player, whose health is the client's. Fire a client's own throw starts
    /// reaches the host through the host's copy of the throw (ThrowSync). Fire
    /// a client's own flamethrower starts on a door, a window or furniture is
    /// sent to the host as a Burning request: the host checks the player is
    /// within reach and the thing can burn, and lights its copy, which then
    /// burns for everyone; a refused one is put out again.
    ///
    /// Burning: the core's Burning (ObjRef, u8 what, f32 burn time, u8 on).
    /// </summary>
    public sealed class FireSync
    {
        public static FireSync Instance { get; private set; }
        private readonly Session _session;

        public FireSync(Session session)
        {
            Instance = this;
            _session = session;
            session.Register(MsgType.Burning, OnBurning);
        }

        /// <summary>
        /// What a GameObject is for the fire rules (null: nothing). The only
        /// code that reads its components for them.
        /// </summary>
        private static FireKind KindOf(GameObject go)
        {
            if (go == null)
            {
                return FireKind.None;
            }

            return FireRules.KindOf(new KindFacts
            {
                Player = go.GetComponent<Player>() != null,
                Proxy = go.GetComponent<NetPlayerProxy>() != null,
                Character = go.GetComponent<Character>() != null,
                Door = go.GetComponent<Door>() != null,
                Window = go.GetComponent<Window>() != null,
                Item = go.GetComponent<Item>() != null,
            });
        }

        /// <summary>
        /// Host: something started (on) or stopped burning here.
        /// </summary>
        internal void Changed(Burn b, bool on)
        {
            var facts = FactsOf(b, out var r);
            if (!FireRules.SendsChange(facts))
            {
                return;
            }

            var w = _session.Begin(MsgType.Burning);
            Burning.Write(w, new FireReport
            {
                Thing = ObjRef.NameOf(r),
                Kind = facts.Kind,
                Time = b.burnTime,
                On = on,
            });

            _session.Broadcast(w, Channel.Control, reliable: true);
            if (WorldActions.Debug)
            {
                string text = "stopped burning";
                if (on)
                {
                    text = "caught fire";
                }

                Plugin.Log.LogInfo($"[world] {b.name} {text} ({r})");
            }
        }

        /// <summary>
        /// Host: what the fire rules know of a fire starting or going out
        /// here, and its ObjRef.
        /// </summary>
        private ChangeFacts FactsOf(Burn b, out ObjRef r)
        {
            r = default;
            if (b == null)
            {
                return default;
            }

            var kind = KindOf(b.gameObject);
            if (kind != FireKind.None)
            {
                r = ObjRef.Of(b.gameObject);
            }

            return new ChangeFacts
            {
                Exists = true,
                Replaying = Coop.Guards.Replaying,
                Host = _session.IsHost,
                Peers = _session.Peers.Count,
                InWorld = WorldShare.InWorld,
                Kind = kind,
                Named = r.Valid,
            };
        }

        /// <summary>
        /// What a flame touching this would set alight, found as
        /// Flame.onCollideWith finds it: furniture that can break, a door, a
        /// window. Null for a character (its burn is an effect on the hit) or
        /// anything else.
        /// </summary>
        internal static GameObject Flammable(Transform t, Collider col)
        {
            switch (FireRules.Flammable(FactsOf(t, col)))
            {
                case FlameTarget.ColliderItem:
                    return col.transform.GetComponent<Item>().gameObject;
                case FlameTarget.TransformItem:
                    return t.GetComponent<Item>().gameObject;
                case FlameTarget.Door:
                    return Door.getDoorScript(col.transform).gameObject;
                case FlameTarget.Window:
                    return col.GetComponent<Window>().gameObject;
                default:
                    return null;
            }
        }

        /// <summary>
        /// What the fire rules know of what a flame touched. The only code
        /// that reads the collider and the transform for them.
        /// </summary>
        private static FlameFacts FactsOf(Transform t, Collider col)
        {
            if (col == null)
            {
                return default;
            }

            var colItem = col.transform.GetComponent<Item>();
            Item item = null;
            bool charOnT = false;
            if (t != null)
            {
                item = t.GetComponent<Item>();
                charOnT = t.GetComponent<CharBase>() != null;
            }

            return new FlameFacts
            {
                Collider = true,
                CharOnCollider =
                    col.transform.GetComponent<CharBase>() != null,
                CharOnTransform = charOnT,
                ColliderItem = colItem != null,
                ColliderItemDestructible =
                    colItem != null && colItem.destructible,
                TransformItem = item != null,
                TransformItemDestructible = item != null && item.destructible,
                Door = Door.getDoorScript(col.transform) != null,
                Window = col.GetComponent<Window>() != null,
            };
        }

        /// <summary>
        /// Client: our player's flame set this alight here; ask the host to
        /// light its copy (put ours out if it says no).
        /// </summary>
        internal void ClientIgnited(GameObject go)
        {
            var facts = FactsOf(go, out var r);
            if (!FireRules.MayAskToIgnite(facts))
            {
                return;
            }

            var burn = go.GetComponent<Burn>();
            float seconds = 0f;
            if (burn != null)
            {
                seconds = burn.burnTime;
            }

            var w = _session.Begin(MsgType.Burning);
            Burning.Write(w, new FireReport
            {
                Thing = ObjRef.NameOf(r),
                Kind = facts.Kind,
                Time = FireRules.AskedTime(burn != null, seconds),
                On = true,
            });

            Coop.Current.Authority.Request(w, $"set {go.name} alight",
                (ok, reason, result) => Answered(go, ok));
        }

        /// <summary>
        /// Client: what the fire rules know of a thing our player's flame
        /// set alight, and its ObjRef.
        /// </summary>
        private static AskFacts FactsOf(GameObject go, out ObjRef r)
        {
            r = default;
            if (go == null)
            {
                return default;
            }

            var kind = KindOf(go);
            if (kind != FireKind.None)
            {
                r = ObjRef.Of(go);
            }

            return new AskFacts
            {
                Exists = true,
                ClientMode = NetEnemies.ClientMode,
                Replaying = Coop.Guards.Replaying,
                OursAlone = WorldActions.OursAlone(go),
                Kind = kind,
                Named = r.Valid,
            };
        }

        /// <summary>
        /// Client: the host's answer to our Burning; a refused one puts our
        /// fire out again.
        /// </summary>
        private static void Answered(GameObject go, bool ok)
        {
            Burn b = null;
            if (go != null)
            {
                b = go.GetComponent<Burn>();
            }

            if (!FireRules.PutsOut(ok, go != null, b != null))
            {
                return;
            }

            Coop.Guards.Replaying = true;
            try
            {
                b.stop();
            }
            finally
            {
                Coop.Guards.Replaying = false;
            }
        }

        /// <summary>
        /// Host: a client's player set this alight (a Burning request).
        /// </summary>
        private void OnClientIgnited(in FireReport sent)
        {
            var obj = ObjRef.RefOf(sent.Thing);
            Component c = null;
            if (sent.Kind != FireKind.Enemy)
            {
                c = Resolve(obj, sent.Kind);
            }

            var fire = FireRules.MayIgnite(FactsOf(c, sent));
            if (fire.Verdict.Refused)
            {
                Coop.Current.Authority.Refuse(fire.Verdict);
                return;
            }

            if (!fire.Lights)
            {
                return; // not a request, or burning already
            }

            c.gameObject.AddComponent<Burn>().burnTime = fire.Time;
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    "[world] "
                        + _session.NameOf(Coop.Current.Authority.From)
                        + $" set {c.name} alight ({obj})");
            }
        }

        /// <summary>
        /// Host: what the fire rules know of the thing a client's Burning
        /// names (null: not found here). The only code that reads it for
        /// them.
        /// </summary>
        private static IgniteFacts FactsOf(Component c, in FireReport sent)
        {
            var authority = Coop.Current.Authority;
            var facts = new IgniteFacts
            {
                On = sent.On,
                Kind = sent.Kind,
                Time = sent.Time,
                Reach = new ReachFacts { Handling = authority.Handling },
            };

            if (c == null)
            {
                return facts;
            }

            bool destructible = false;
            bool destroyed = false;
            if (c is Item item)
            {
                destructible = item.destructible;
                destroyed = item.destroyed;
            }
            else if (c is Door door)
            {
                destroyed = door.destroyed;
            }

            return facts with
            {
                Found = true,
                Destructible = destructible,
                Destroyed = destroyed,
                Burning = c.GetComponent<Burn>() != null,
                Name = c.name,
                Reach = authority.ReachOf(c.transform.position),
            };
        }

        private static Component Resolve(ObjRef obj, FireKind kind)
        {
            switch (kind)
            {
                case FireKind.Enemy:
                    return ObjRef.Resolve<Character>(obj);
                case FireKind.Door:
                    return ObjRef.Resolve<Door>(obj);
                case FireKind.Window:
                    return ObjRef.Resolve<Window>(obj);
                case FireKind.Item:
                    return ObjRef.Resolve<Item>(obj);
                default:
                    return null;
            }
        }

        private void OnBurning(ulong from, PacketReader r)
        {
            var s = _session;
            var arrival = new ArrivalFacts
            {
                Session = true,
                InWorld = WorldShare.InWorld,
                Client = s.IsClient,
                FromHost = from == s.HostId,
            };

            if (s.IsHost)
            {
                if (ArrivalRules.Takes(arrival))
                {
                    OnClientIgnited(Burning.Read(r));
                }

                return;
            }

            if (ArrivalRules.TakesFromHost(arrival))
            {
                OnHostBurning(Burning.Read(r));
            }
        }

        /// <summary>
        /// Client: something started or stopped burning on the host.
        /// </summary>
        private static void OnHostBurning(in FireReport sent)
        {
            var obj = ObjRef.RefOf(sent.Thing);
            Component c = Resolve(obj, sent.Kind);
            Burn burn = null;
            if (c != null)
            {
                burn = c.GetComponent<Burn>();
            }

            var change = FireRules.FromHost(new HostFireFacts
            {
                Kind = sent.Kind,
                Found = c != null,
                On = sent.On,
                Burning = burn != null,
            });

            if (WorldActions.Debug && Resolves(sent.Kind))
            {
                string state = "off";
                if (sent.On)
                {
                    state = "on";
                }

                string line = $"[world] got Burning {obj} {state}: ";
                string name = "not found here";
                if (c != null)
                {
                    name = c.name;
                }

                Plugin.Log.LogInfo(line + name);
            }

            if (change == FireChange.Nothing)
            {
                return;
            }

            Coop.Guards.Replaying = true;
            try
            {
                if (change == FireChange.Light)
                {
                    c.gameObject.AddComponent<Burn>().burnTime = sent.Time;
                }
                else if (change == FireChange.PutOut)
                {
                    burn.stop();
                }
            }
            finally
            {
                Coop.Guards.Replaying = false;
            }
        }

        /// <summary>
        /// Whether a Burning of this kind names something here: the kinds
        /// the message has, for the debug line.
        /// </summary>
        private static bool Resolves(FireKind kind)
        {
            return kind >= FireKind.Enemy && kind <= FireKind.Item;
        }
    }

    [HarmonyPatch(typeof(Burn), "Start")]
    static class BurnStartPatch
    {
        static void Postfix(Burn __instance)
        {
            FireSync instance = FireSync.Instance;
            if (instance != null)
            {
                instance.Changed(__instance, on: true);
            }
        }
    }

    [HarmonyPatch(typeof(Burn), nameof(Burn.stop))]
    static class BurnStopPatch
    {
        static void Prefix(Burn __instance)
        {
            FireSync instance = FireSync.Instance;
            if (instance != null)
            {
                instance.Changed(__instance, on: false);
            }
        }
    }

    /// <summary>
    /// Client: fire hurts only our own player here; the rest burns on the host.
    /// </summary>
    [HarmonyPatch(typeof(Burn), nameof(Burn.waitToGetHit))]
    static class ClientBurnDamagePatch
    {
        static bool Prefix(Burn __instance, ref IEnumerator __result)
        {
            if (!NetEnemies.ClientMode
                || __instance.GetComponent<Player>() != null)
            {
                return true;
            }

            __result = Nothing();
            return false;
        }

        private static IEnumerator Nothing()
        {
            yield break;
        }
    }
}
