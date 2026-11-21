using System.Collections.Generic;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Hideout
{
    /// <summary>
    /// Hideout ovens (ExperienceMachine): a lit oven and its hideout's shadow
    /// ward are world state, the same in every game and checked by the host
    /// (reach). Which oven is a player's own (Player.experienceMachine: where
    /// they respawn and cook) stays each player's. Lighting one doesn't put out
    /// the player's previous oven in another game, nor while another player
    /// still has it as theirs: the host keeps each joiner's oven and decides.
    /// The host saves one oven (its own player's,
    /// Player.SaveState.expMachineId); after the host reloads, the other ovens
    /// are cold until lit again.
    /// </summary>
    internal sealed class Ovens
    {
        private readonly CoopSession _coop;

        /// <summary>
        /// Host: each joiner's own oven, as far as the host knows.
        /// </summary>
        private readonly Dictionary<ulong, ExperienceMachine> _ovens =
            new Dictionary<ulong, ExperienceMachine>();

        public Ovens(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.OvenLit, OnOvenLit);
            coop.Net.OnPeerConnected += OnPeerConnected;
            coop.Hook(() => coop.Net.OnPeerConnected -= OnPeerConnected);
            coop.Net.OnPeerDisconnected += OnPeerDisconnected;
            coop.Hook(
                () => coop.Net.OnPeerDisconnected -= OnPeerDisconnected);
        }

        private Session S => _coop.Net;

        private static bool Debug => WorldActions.Debug;

        /// <summary>
        /// A joiner loaded the host's save, so its oven is the host's until
        /// it lights another.
        /// </summary>
        private void OnPeerConnected(ulong id)
        {
            if (OvenRules.GivesHostsOven(S.IsHost, Player.Instance != null))
            {
                _ovens[id] = Player.Instance.experienceMachine;
            }
        }

        private void OnPeerDisconnected(ulong id)
        {
            _ovens.Remove(id);
        }

        /// <summary>
        /// Player.setExperienceMachine is about to run; false: done here
        /// (Light).
        /// </summary>
        internal bool SetOven(Player p, ExperienceMachine machine, bool enable)
        {
            var call = new OvenCallFacts
            {
                HasOven = machine != null,
                Enable = enable,
                Replaying = Coop.Guards.Replaying,
                Together = Hideouts.Together,
            };

            if (!OvenRules.TakesOver(call))
            {
                return true;
            }

            return Light(p, machine);
        }

        /// <summary>
        /// Player.setExperienceMachine(machine, doEnable: true) while playing
        /// together; false: done here.
        /// </summary>
        private bool Light(Player p, ExperienceMachine machine)
        {
            var s = S;
            var prev = p.experienceMachine;
            bool wasOn = machine.isOn;
            machine.enable(); // also makes it our player's own
            if (s.IsHost)
            {
                var putOut = PutOut(prev, machine, except: 0);
                SendOven(machine, putOut);
                return false;
            }

            // Client: lit here at once; the host says whether the old one goes
            // out.
            _coop.Authority.Request(BeginOven(machine, prev),
                $"light the oven {machine.name}", (ok, reason, result) =>
            {
                var back = OvenRules.Rollback(new RollbackFacts
                {
                    Allowed = ok,
                    Exists = machine != null,
                    WasOn = wasOn,
                    StillOurs = Player.Instance != null
                        && Player.Instance.experienceMachine == machine,
                });

                if (back.PutOut)
                {
                    machine.disable();
                }

                if (back.GiveBack)
                {
                    Player.Instance.experienceMachine = prev;
                }
            }, inventory: false);

            return false;
        }

        /// <summary>
        /// Host: put out prev (one player's old oven) unless it's the new one
        /// or someone (except who) still has it as theirs.
        /// </summary>
        private ExperienceMachine PutOut(
            ExperienceMachine prev, ExperienceMachine lit, ulong except)
        {
            bool others = false;
            foreach (var kv in _ovens)
            {
                if (kv.Key != except && kv.Value == prev)
                {
                    others = true;
                }
            }

            var old = new PutOutFacts
            {
                HasOld = prev != null,
                OldIsLit = prev == lit,
                OldIsHosts = Player.Instance != null
                    && Player.Instance.experienceMachine == prev,
                OldIsOthers = others,
            };

            if (!OvenRules.PutsOut(old))
            {
                return null;
            }

            bool was = Coop.Guards.Replaying;
            Coop.Guards.Replaying = true;
            try
            {
                prev.disable();
            }
            finally
            {
                Coop.Guards.Replaying = was;
            }

            return prev;
        }

        /// <summary>
        /// OvenLit: the lit oven, then the one that goes out (an empty ref for
        /// none).
        /// </summary>
        private PacketWriter BeginOven(
            ExperienceMachine lit, ExperienceMachine other)
        {
            var w = S.Begin(MsgType.OvenLit);
            ObjRef goesOut = default;
            if (other != null)
            {
                goesOut = ObjRef.Of(other.gameObject);
            }

            OvenLitMessage.Write(w, new LitOven
            {
                Lit = ObjRef.NameOf(ObjRef.Of(lit.gameObject)),
                Out = ObjRef.NameOf(goesOut),
            });

            return w;
        }

        private void SendOven(
            ExperienceMachine lit, ExperienceMachine putOut)
        {
            var w = BeginOven(lit, putOut);
            if (Debug)
            {
                string note = "";
                if (putOut != null)
                {
                    note = $", {putOut.name} out";
                }

                Plugin.Log.LogInfo($"[world] sent oven {lit.name} lit" + note);
            }

            S.Broadcast(w, Channel.Control, reliable: true);
        }

        private void OnOvenLit(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !WorldShare.InWorld)
            {
                return;
            }

            if (s.IsClient && from != s.HostId)
            {
                return;
            }

            LitOven oven = OvenLitMessage.Read(r);
            var litRef = ObjRef.RefOf(oven.Lit);
            var otherRef = ObjRef.RefOf(oven.Out);
            var lit = ObjRef.Resolve<ExperienceMachine>(litRef);
            var other = ObjRef.Resolve<ExperienceMachine>(otherRef);
            if (s.IsHost)
            {
                if (!_coop.Authority.Handling)
                {
                    return;
                }

                Verdict may = OvenRules.MayLight(FactsOf(lit));
                if (may.Refused)
                {
                    _coop.Authority.Refuse(may);
                    return;
                }

                LightHere(lit);
                _ovens[from] = lit;
                if (Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {s.NameOf(from)} lit the oven {lit.name}");
                }

                SendOven(lit, PutOut(other, lit, except: from));
                return;
            }

            if (Debug)
            {
                string line = $"[world] got oven {litRef} lit";
                string otherOut = "";
                if (otherRef.Valid)
                {
                    otherOut = $", {otherRef} out";
                }

                string missing = "";
                if (lit == null)
                {
                    missing = ": not found here";
                }

                Plugin.Log.LogInfo(line + otherOut + missing);
            }

            var change = OvenRules.FromHost(FactsOf(lit, other));
            if (change.Light)
            {
                LightHere(lit);
            }

            if (change.PutOut)
            {
                Coop.Guards.Replaying = true;
                try
                {
                    other.disable();
                }
                finally
                {
                    Coop.Guards.Replaying = false;
                }
            }
        }

        /// <summary>
        /// Host: what OvenRules knows of a joiner's lit oven (null for none
        /// here): the only code that reads it for MayLight.
        /// </summary>
        private OvenFacts FactsOf(ExperienceMachine lit)
        {
            if (lit == null)
            {
                return default;
            }

            var reach = _coop.Authority.ReachOf(
                lit.transform.position, lit.name);
            return new OvenFacts
            {
                Exists = true,
                Reach = reach with { Reach = WorldActions.Reach },
                AtLocation = lit.transform.GetLocation() != null,
            };
        }

        /// <summary>
        /// A client: what OvenRules knows of the host's lit oven and the one
        /// it put out (either may be null).
        /// </summary>
        private HostOvenFacts FactsOf(
            ExperienceMachine lit, ExperienceMachine other)
        {
            var p = Player.Instance;
            var facts = new HostOvenFacts
            {
                Found = lit != null,
                HasOther = other != null,
                OtherIsLit = other == lit,
                HasPlayer = p != null,
            };

            if (lit != null)
            {
                facts = facts with { On = lit.isOn };
            }

            if (p != null)
            {
                facts = facts with
                {
                    OtherIsOurs = p.experienceMachine == other,
                };
            }

            return facts;
        }

        /// <summary>
        /// Light an oven for the world (its ward, its flags) without making it
        /// our own player's.
        /// </summary>
        private void LightHere(ExperienceMachine lit)
        {
            var p = Player.Instance;
            ExperienceMachine mine = null;
            if (p != null)
            {
                mine = p.experienceMachine;
            }

            bool was = Coop.Guards.Replaying;
            Coop.Guards.Replaying = true;
            try
            {
                lit.enable();
            }
            finally
            {
                Coop.Guards.Replaying = was;
                if (p != null)
                {
                    p.experienceMachine = mine;
                }
            }
        }
    }
}
