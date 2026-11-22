using System.Collections.Generic;
using System.Reflection;
using DarkwoodMP.Net;
using DarkwoodMP.World.Traps;
using HarmonyLib;
using UnityEngine;
using Action = DarkwoodMP.World.WorldActions.Action;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Traps, shared. In each game its own player sets traps off as they
    /// step on them; the host's enemies set them off on the host only (a
    /// client's traps ignore characters: the ones it shows are the host's,
    /// moved by the host, and their traps went off there). Who saw it go off
    /// tells the others, who set theirs off too: its sound, what it spawns
    /// (an explosion hurts the host's enemies on the host), and its sprung
    /// look. A client tells only of traps its own player (or something it
    /// threw) set off.
    ///
    /// A client's own trap is a request the host may refuse (it had gone off
    /// there already, or isn't there). So on a client the trap goes off at
    /// once (its sound, its sprung look, the bear trap's bite and hold), but
    /// what it spawns (Trigger.prefabToSpawn: a vine trap's vine) waits for
    /// the host's yes, as a feeder's effect does: a spawned thing is shared
    /// world, and a vine that already holds the player can't be taken back
    /// cleanly. If the host refuses, nothing is spawned and the bite is
    /// undone (the hold released, the health it took given back). If the
    /// host's own TrapFired for the same trap comes first (it went off there
    /// just before), that one spawns it, untargeted, as any replay of the
    /// host's trap does.
    ///
    /// A trap disarmed (or failed to be) is taken away, or left sprung, for
    /// everyone (TrapSwitched). A client's disarm is the host's roll.
    /// </summary>
    internal sealed partial class TrapSync
    {
        /// <summary>A client's trap spawn waiting for the host's yes.</summary>
        internal sealed class Held
        {
            public UnityEngine.Object Prefab;
            public Vector3 At;
            public GameObject Target; // a vine trap's: who its vine holds
            public bool Done;
        }

        /// <summary>A client's TrapFired request, for its answer.</summary>
        private sealed class Fired
        {
            public string Key;
            public Held Held;
            public Player Player;
            public bool BearHold;
            public float Lost;
        }

        private static readonly MethodInfo OnAfterTrigger =
            AccessTools.Method(typeof(Trigger), "OnAfterTrigger");

        private readonly CoopSession _coop;

        private readonly Dictionary<string, Held> _held =
            new Dictionary<string, Held>();

        /// <summary>
        /// Client: the player's health before the trap they stepped on bit
        /// them (Trigger.checkCollision); -1 when not noted.
        /// </summary>
        private float _healthBefore = -1f;

        public TrapSync(CoopSession coop)
        {
            _coop = coop;
            Disarms = new TrapDisarms(coop);
        }

        /// <summary>Disarming traps (the host's roll, a client's).</summary>
        public readonly TrapDisarms Disarms;

        /// <summary>
        /// Tests only (the map step "trap-refuse"): the host refuses the next
        /// TrapFired requests, having first set the trap off itself (Race) or
        /// taken it away (Gone).
        /// </summary>
        public TrapTest TestRefuse;

        // ---- the host's checks --------------------------------------------

        /// <summary>
        /// Host: whether a client's player could have set off, switched or
        /// disarmed this trap (WorldActions.Allowed). Refuses the request if
        /// not.
        /// </summary>
        public bool Allowed(Action a, ObjRef obj)
        {
            var trap = ObjRef.Resolve<Trigger>(obj);
            Verdict verdict;
            if (a == Action.TrapFired)
            {
                verdict = TrapRules.MayFire(FactsOf(trap, TestRefuse));
                MakeTestRefusal(trap, verdict);
            }
            else if (a == Action.TrapDisarm)
            {
                verdict = TrapRules.MayDisarm(FactsOf(trap, TrapTest.None));
            }
            else
            {
                verdict = TrapRules.MaySwitch(FactsOf(trap, TrapTest.None));
            }

            _coop.Authority.Refuse(verdict);
            return !verdict.Refused;
        }

        /// <summary>
        /// What the trap rules know of a trap the host is asked about: the
        /// only code that reads the Trigger for them.
        /// </summary>
        private TrapFacts FactsOf(Trigger trap, TrapTest test)
        {
            if (trap == null)
            {
                return new TrapFacts();
            }

            return new TrapFacts
            {
                Exists = true,
                Active = trap.active,
                CanDisarm = trap.canDisarm,
                Test = test,
                Requester = _coop.Authority.ReachOf(
                    trap.transform.position, trap.name),
            };
        }

        /// <summary>
        /// Tests ("trap-refuse"): what makes a real refusal, made here just
        /// before refusing this request.
        /// </summary>
        private static void MakeTestRefusal(Trigger trap, Verdict verdict)
        {
            if (verdict.Reason == Refusal.TestTrapWentOff)
            {
                // it went off here first, and that went to everyone
                OnAfterTrigger.Invoke(trap, new object[] { null, false });
            }
            else if (verdict.Reason == Refusal.TestTrapGone)
            {
                // it isn't here
                trap.active = false;
                UnityEngine.Object.DestroyImmediate(trap.gameObject);
            }
        }

        // ---- applying another game's ----------------------------------------

        /// <summary>
        /// A TrapFired or TrapSwitched from another game: the trap goes off,
        /// or is switched, here too. False when there's nothing more to do
        /// with the action (the trap isn't here, or our own firing of it held
        /// its spawn for this and spawned it now).
        /// </summary>
        public bool Apply(Action a, ObjRef obj)
        {
            var trap = WorldActions.Got<Trigger>(a, obj);
            // Our own firing of it, not answered yet, left its spawn for this.
            if (a == Action.TrapFired && _coop.Net.IsClient && SpawnHeld(obj))
            {
                return false;
            }

            if (trap == null)
            {
                return false;
            }

            if (a == Action.TrapFired)
            {
                if (trap.active)
                {
                    OnAfterTrigger.Invoke(trap, new object[] { null, false });
                }
            }
            else
            {
                Item item = trap.GetComponent<Item>();
                if (item != null)
                {
                    item.switchTriggerState();
                }
            }

            return true;
        }

        /// <summary>
        /// Host: a trap's state in ObjState, after its kind: u8 flags (1
        /// armed).
        /// </summary>
        public void WriteState(PacketWriter w, Trigger trap)
        {
            int activeFlag = 0;
            if (trap.active)
            {
                activeFlag = 1;
            }

            w.W.Write((byte)activeFlag);
        }

        /// <summary>
        /// Client: put our copy of the trap as the host has it (a refused
        /// request's rollback). go may be null: the flags are read anyway.
        /// </summary>
        public void ReadState(PacketReader r, Transform go)
        {
            bool armed = (r.R.ReadByte() & 1) != 0;
            Trigger trap = null;
            if (go != null)
            {
                trap = go.GetComponent<Trigger>();
            }

            if (trap != null && trap.active != armed)
            {
                trap.active = armed;
            }
        }

        // ---- a trap going off here (Trigger.OnAfterTrigger) -----------------

        /// <summary>
        /// Before a trap goes off: counts the call, and on a client holds
        /// back what our own trap spawns until the host says yes. Returns the
        /// held spawn, or null.
        /// </summary>
        public Held BeforeTrigger(Trigger trap, Collider other, bool connects)
        {
            _coop.Guards.Depth++;
            if (!TrapRules.HoldsSpawn(HoldOf(trap, other)))
            {
                return null;
            }

            var held = new Held { Prefab = trap.prefabToSpawn };
            if (TrapRules.TargetsCatch(
                trap.isChainTrap, connects, other != null))
            {
                held.Target = other.gameObject;
            }

            // nonPooledPrefabToSpawn alone spawns
            // nothing (Trigger.OnAfterTrigger)
            trap.prefabToSpawn = null;
            return held;
        }

        /// <summary>
        /// After a trap went off: the outermost call tells the others, if our
        /// own player (or what it threw) set it off. On a client that is a
        /// request; held is its spawn waiting for the host's yes.
        /// </summary>
        public void AfterTrigger(Trigger trap, Collider other, Held held)
        {
            if (held != null && trap != null)
            {
                // pooled: the next one spawns again
                trap.prefabToSpawn = held.Prefab;
            }

            float healthBefore = _healthBefore;
            _healthBefore = -1f;
            var guards = _coop.Guards;
            if (!guards.Outermost() || !guards.Sharing || trap == null)
            {
                return;
            }

            if (!IsOurs(trap, other))
            {
                return;
            }

            if (_coop.Net.IsHost)
            {
                WorldActions.Send(
                    Action.TrapFired, trap.gameObject, byPlayerKnown: true);
                return;
            }

            var r = ObjRef.Of(trap.gameObject);
            if (!r.Valid)
            {
                return;
            }

            var fired = new Fired { Key = r.ToString(), Held = held };
            if (held != null)
            {
                held.At = trap.transform.position + new Vector3(0f, 1f, 0f);
                _held[fired.Key] = held;
            }

            if (other != null)
            {
                fired.Player = other.GetComponent<Player>();
            }

            float now = 0f;
            if (fired.Player != null)
            {
                now = fired.Player.health;
            }

            fired.Lost = TrapRules.HealthLost(
                fired.Player != null, healthBefore, now);
            fired.BearHold = fired.Player != null && trap.isBearTrap;
            Request(trap, r, fired);
        }

        private void Request(Trigger trap, ObjRef r, Fired fired)
        {
            var w = _coop.Net.Begin(MsgType.WorldAction);
            WorldActions.WriteAction(
                w, Action.TrapFired, r, 0f, 0f, false, false, "");
            if (WorldActions.Debug)
            {
                string waits = "";
                if (fired.Held != null)
                {
                    waits = ", its " + fired.Held.Prefab.name
                        + " waits for the host";
                }

                Plugin.Log.LogInfo(
                    $"[world] sent TrapFired {r} ({trap.name}){waits}");
            }

            _coop.Authority.Request(
                w, $"TrapFired {r} ({trap.name})",
                (ok, reason, result) => Answered(fired, ok), inventory: false);
        }

        /// <summary>
        /// Client: the host's answer to our TrapFired. A yes spawns what was
        /// held; a refusal spawns nothing and undoes the bite.
        /// </summary>
        private void Answered(Fired fired, bool ok)
        {
            var held = fired.Held;
            string key = fired.Key;
            if (held != null && _held.TryGetValue(key, out var h) && h == held)
            {
                _held.Remove(key);
            }

            if (ok)
            {
                if (held != null)
                {
                    Spawn(held, key);
                }

                return;
            }

            if (held != null && !held.Done)
            {
                Plugin.Log.LogInfo(
                    $"[world] TrapFired {key}: refused, so its "
                        + $"{held.Prefab.name} isn't spawned");
            }

            var player = fired.Player;
            if (player == null || player != Player.Instance)
            {
                return;
            }

            if (fired.BearHold && player.inBearTrap)
            {
                player.stopAllPerformingActionAnims();
            }

            float health = TrapRules.HealthGivenBack(
                player.health, fired.Lost, player.maxHealth);
            if (health != player.health)
            {
                player.health = health;
            }

            if (fired.BearHold || fired.Lost > 0f)
            {
                Plugin.Log.LogInfo(
                    $"[world] TrapFired {key}: refused, so the bite is "
                        + $"undone (hold released, {fired.Lost:0} health "
                        + "back)");
            }
        }

        /// <summary>
        /// Client, applying the host's TrapFired: if our own firing of it
        /// holds a spawn, it's spawned now, for the host's.
        /// </summary>
        private bool SpawnHeld(ObjRef r)
        {
            string key = r.ToString();
            if (!_held.TryGetValue(key, out var held))
            {
                return false;
            }

            _held.Remove(key);
            // as the host's replay does: it went off there, not on our player
            held.Target = null;
            Spawn(held, key);
            return true;
        }

        private static void Spawn(Held held, string key)
        {
            if (held.Done)
            {
                return;
            }

            held.Done = true;
            var go = Core.AddPrefab(
                held.Prefab, held.At, Quaternion.Euler(90f, 0f, 0f), null);
            if (go == null)
            {
                return;
            }

            if (held.Target != null)
            {
                var cp = go.GetComponent<ChainParent>();
                if (cp != null)
                {
                    cp.target = held.Target;
                }
            }

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] TrapFired {key}: spawned {held.Prefab.name}");
            }
        }

        /// <summary>
        /// What the hold rule knows of a trap going off. Reads in the rule's
        /// order and no further than it needs (whether it is ours reads the
        /// most).
        /// </summary>
        private HoldFacts HoldOf(Trigger trap, Collider other)
        {
            var guards = _coop.Guards;
            var facts = new HoldFacts { Depth = guards.Depth };
            if (facts.Depth != 1)
            {
                return facts;
            }

            facts = facts with { Sharing = guards.Sharing };
            if (!facts.Sharing)
            {
                return facts;
            }

            facts = facts with { Client = _coop.Net.IsClient };
            if (!facts.Client)
            {
                return facts;
            }

            facts = facts with { LoadedFromSave = trap.loadedFromSave };
            if (facts.LoadedFromSave)
            {
                return facts;
            }

            facts = facts with { Spawns = trap.prefabToSpawn != null };
            if (!facts.Spawns)
            {
                return facts;
            }

            return facts with { Ours = IsOurs(trap, other) };
        }

        /// <summary>
        /// A trap set off by this game's own player or something it threw
        /// (on a client, only those are sent).
        /// </summary>
        private bool IsOurs(Trigger trap, Collider other)
        {
            return TrapRules.IsOurs(CatchOf(trap, other));
        }

        /// <summary>
        /// What the rule knows of who set a trap off: the only code that
        /// reads that for it, in its order and no further than it needs.
        /// </summary>
        private CatchFacts CatchOf(Trigger trap, Collider other)
        {
            var facts = new CatchFacts
            {
                SetByPlayer = trap.setByPlayer,
                HasItem = trap.GetComponent<Item>() != null,
            };

            if (!facts.SetByPlayer && !facts.HasItem)
            {
                return facts; // a level's scripted trigger zone, not a trap
            }

            facts = facts with { Client = _coop.Net.IsClient };
            if (!facts.Client)
            {
                return facts;
            }

            // in a dream of our own: the game's own, as alone
            facts = facts with
            {
                OursAlone = WorldActions.OursAlone(trap.gameObject),
            };

            if (facts.OursAlone || other == null)
            {
                return facts;
            }

            return facts with
            {
                ByPlayer = other.GetComponent<Player>() != null,
                ByThrown = other.GetComponent<ThrownItem>() != null,
            };
        }
    }
}
