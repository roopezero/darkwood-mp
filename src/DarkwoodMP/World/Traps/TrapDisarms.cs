using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Traps;
using HarmonyLib;
using Action = DarkwoodMP.World.WorldActions.Action;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Disarming a shared trap: the host's roll of a client's disarm, a
    /// client's handing of its disarm to the host, and the crouch co-op adds
    /// (DisarmAnimation). TrapSync owns it.
    /// </summary>
    internal sealed class TrapDisarms
    {
        private static readonly AccessTools.FieldRef<Player, Item> Disarmed =
            AccessTools.FieldRefAccess<Player, Item>("trapBeingDisarmed");

        private readonly CoopSession _coop;

        public TrapDisarms(CoopSession coop)
        {
            _coop = coop;
            Animation = new DisarmAnimation();
        }

        /// <summary>The crouch co-op adds to a disarm.</summary>
        public readonly DisarmAnimation Animation;

        /// <summary>
        /// Host: a client's TrapDisarm on a trap. Only ever a request to the
        /// host: rolled here, as the game rolls it for its own player. The
        /// trap is gone (or set off) here either way; the caller sends it to
        /// the others as TrapSwitched.
        /// </summary>
        public void Roll(ulong from, ObjRef obj)
        {
            var item = ObjRef.Resolve<Item>(obj);
            float chance = 0f;
            float roll = 0f;
            if (item != null)
            {
                chance = item.disarmChance;
                roll = UnityEngine.Random.Range(0f, 1f);
            }

            bool worked = DisarmRules.Works(item != null, chance, roll);
            string outcome = DisarmRules.Failed;
            if (worked)
            {
                outcome = DisarmRules.Worked;
            }

            _coop.Authority.Result(outcome);
            if (WorldActions.Debug)
            {
                string disarm = "failed";
                if (worked)
                {
                    disarm = "worked";
                }

                Plugin.Log.LogInfo(
                    $"[world] {_coop.Net.NameOf(from)} disarms {obj}: "
                        + $"{disarm}");
            }

            if (item != null)
            {
                item.switchTriggerState();
            }
        }

        /// <summary>
        /// Client: disarming a shared trap is the host's roll. When our
        /// player's progress bar for it completes, the game would roll here
        /// (disarmChance against a random number) and disarm it or set it
        /// off. Instead the trap goes to the host as a TrapDisarm request;
        /// the host rolls with its own random numbers and says how it went
        /// (Verdict result "1" or "0"), and then the game's own disarm (the
        /// item comes to our player) or failDisarm runs here, with its
        /// message. While the host decides, the trap stays as it is.
        /// </summary>
        public void AskHost(Player p)
        {
            var trap = Disarmed(p);
            if (!DisarmRules.AsksHost(DisarmOf(p, trap, out var r)))
            {
                // not a shared trap (or one only we have): the game rolls
                // it, as alone
                return;
            }

            p.disarmingTrap = false; // the game's own roll is skipped
            // The crouch co-op adds (DisarmAnimation) ends here too: with
            // disarmingTrap already cleared, the stand-up after the bar
            // may not see that a disarm ended, which left our player
            // crouched for good.
            Animation.StandUp(p);
            var trigger = trap.GetComponent<Trigger>();
            if (trigger != null && trigger.bendDownToDisarm)
            {
                p.hidingInventory = true;
                p.gotInventory = false;
            }

            var w = _coop.Net.Begin(MsgType.WorldAction);
            WorldActions.WriteAction(
                w, Action.TrapDisarm, r, 0f, 0f, false, false, "");
            _coop.Authority.Request(
                w, $"TrapDisarm {r} ({trap.name})",
                (ok, reason, result) => Answered(trap, ok, result));
        }

        /// <summary>
        /// What the disarm rule knows of our player's finished disarm: the
        /// only code that reads the player for it, in the rule's order and
        /// no further than it needs (naming the trap can give it a name).
        /// r is the trap's name once read.
        /// </summary>
        private DisarmFacts DisarmOf(Player p, Item trap, out ObjRef r)
        {
            r = default;
            var facts = new DisarmFacts { Disarming = p.disarmingTrap };
            if (!facts.Disarming)
            {
                return facts;
            }

            facts = facts with { ClientMode = NetEnemies.ClientMode };
            if (!facts.ClientMode)
            {
                return facts;
            }

            facts = facts with { Sharing = _coop.Guards.Sharing };
            if (!facts.Sharing)
            {
                return facts;
            }

            facts = facts with { HasTrap = trap != null };
            if (!facts.HasTrap)
            {
                return facts;
            }

            r = ObjRef.Of(trap.gameObject);
            facts = facts with { Named = r.Valid };
            if (!facts.Named)
            {
                return facts;
            }

            return facts with
            {
                OursAlone = WorldActions.OursAlone(trap.gameObject),
            };
        }

        /// <summary>Client: the host's answer to our disarm.</summary>
        private static void Answered(Item trap, bool ok, string result)
        {
            if (trap == null || Player.Instance == null)
            {
                return;
            }

            var outcome = DisarmRules.Outcome(ok, result);
            Coop.Guards.Replaying = true;
            try
            {
                if (outcome == DisarmOutcome.Disarmed)
                {
                    // Our player's disarm: its onDisarmed triggers are ours
                    // to set off and share.
                    Coop.Current.Scripted.AsOwnAction(trap.disarm);
                    Player.Instance.displayMessage(
                        Language.Get("Playermsg_disarmedSuccess", "UI"));
                }
                else if (outcome == DisarmOutcome.Failed)
                {
                    Player.Instance.displayMessage(
                        Language.Get("Playermsg_disarmedFailure", "UI"));
                    trap.failDisarm();
                }

                // refused: the trap was gone or had gone off on the host;
                // ObjState put ours as it is there
            }
            finally
            {
                Coop.Guards.Replaying = false;
            }
        }
    }
}
