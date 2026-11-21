using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Objects;
using DarkwoodMP.World.Switches;
using UnityEngine;
using Action = DarkwoodMP.World.WorldActions.Action;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Smaller world objects, shared as WorldAction kinds:
    ///
    ///   InteractiveSwitch  an InteractiveItem switched in its window
    ///                      (InteractiveItem.switchMe). The others set isOn;
    ///                      the switch's on/off events run in the switcher's
    ///                      game and, for a client's switch, in the host's too
    ///                      (outside the replay, so what they change there
    ///                      reaches everyone).
    ///   Removed            a note picked up off the ground (Item.activate's
    ///                      JournalNoteReference branch), a death bag's
    ///                      experience taken (its DeathDrop branch), and, from
    ///                      the host only, a lure its animals ate
    ///                      (Lure.removeHealth). The others destroy their copy.
    ///   FeederUsed         a feeder used (Feeder.activate): it's empty for
    ///                      everyone until the next dawn. A client's use waits
    ///                      for the host's yes before its player gets the
    ///                      feeder's effect, since the effect can't be taken
    ///                      back.
    ///
    ///   LiquidLit          spilt gasoline catching fire (Liquid.startBurning).
    ///                      Every trail a player pours is shared
    ///                      (Containers.SharedPrefabs), and fire spreads along
    ///                      it by itself in each game, so only where it started
    ///                      is sent: by the host, for any fire that starts
    ///                      there (not a spread), and by a client only when its
    ///                      own player's burning weapon touched it
    ///                      (MeleeSensor); the client's thrown matches and
    ///                      molotovs light it in the host's copy of the throw.
    ///
    ///   VineHit            a vine (ChainParent) cut at (ChainParent.getHit,
    ///                      from MeleeSensor). The host sends every hit in its
    ///                      world, with the health it left; a client sends its
    ///                      own player's, as a request (reach and a damage
    ///                      cap). Each game's own detach follows when health
    ///                      reaches 0, and with it the parent's break
    ///                      (Item.getHit(999), nested, so not sent again).
    ///   ShadowArmorDied    the shadow on a shadow-covered item
    ///                      (ShadowArmor.die) died. The host decides: hits on
    ///                      the item reach it as ItemHit (the shadow takes
    ///                      them), its light check runs there, and it sends the
    ///                      death. A client's own copy of the shadow never dies
    ///                      by itself.
    ///
    /// Keys and quest items aren't objects lying in the world: KeyReference /
    /// QuestItemReference.pickup is called on the item's database prefab when
    /// it's taken from a container or a dropped item, which is a slot change
    /// (SlotShare), so there's nothing more to remove for them.
    /// </summary>
    internal sealed partial class ObjectActions
    {
        private readonly CoopSession _coop;

        public ObjectActions(CoopSession coop)
        {
            _coop = coop;
        }

        public static bool Handles(Action a) => a == Action.InteractiveSwitch
            || a == Action.Removed || a == Action.FeederUsed
            || a == Action.LiquidLit
            || a == Action.VineHit || a == Action.ShadowArmorDied;

        // ---- the host's checks ------------------------------------------

        /// <summary>
        /// Host: whether a client's player could have done this here (the
        /// values' X is a vine hit's damage, F1 a switch's new state).
        /// Refuses the request if not.
        /// </summary>
        public bool Allowed(Action a, ObjRef obj, in ActionValues values)
        {
            var t = ObjRef.Resolve<Transform>(obj);
            var verdict = Verdict.Refuse(Refusal.NotHere);
            switch (a)
            {
                case Action.VineHit:
                    verdict = ObjectRules.MayCutVine(VineOf(t, values.X));
                    break;
                case Action.ShadowArmorDied:
                    verdict = ObjectRules.MayKillShadow(t != null);
                    break;
                case Action.InteractiveSwitch:
                    verdict = SwitchRules.MaySwitch(SwitchOf(t, values.F1));
                    break;
                case Action.Removed:
                    verdict = ObjectRules.MayRemove(PickupOf(t));
                    break;
                case Action.LiquidLit:
                    verdict = ObjectRules.MayLight(LiquidOf(t));
                    break;
                case Action.FeederUsed:
                    verdict = ObjectRules.MayUseFeeder(FeederOf(t));
                    break;
            }

            _coop.Authority.Refuse(verdict);
            return !verdict.Refused;
        }

        /// <summary>
        /// The requester and the thing, for a rule that sets the reach.
        /// </summary>
        private ReachFacts ReachOf(Transform t)
        {
            return _coop.Authority.ReachOf(t.position, t.name);
        }

        /// <summary>
        /// What the object rules know of a vine cut at (damage is the
        /// hit's): the only code that reads a ChainParent for them.
        /// </summary>
        private VineFacts VineOf(Transform t, float damage)
        {
            if (t == null)
            {
                return new VineFacts { Damage = damage };
            }

            var cp = t.GetComponent<ChainParent>();
            if (cp == null)
            {
                return new VineFacts { Exists = true, Damage = damage };
            }

            return new VineFacts
            {
                Exists = true,
                IsVine = true,
                Attached = cp.attached,
                Health = cp.health,
                Damage = damage,
                Requester = ReachOf(t),
            };
        }

        /// <summary>
        /// What the switch rules know of an InteractiveItem a client says
        /// it switched to on: the only code that reads it for them.
        /// </summary>
        private SwitchFacts SwitchOf(Transform t, bool on)
        {
            if (t == null)
            {
                return new SwitchFacts { On = on };
            }

            var ii = t.GetComponent<InteractiveItem>();
            if (ii == null)
            {
                return new SwitchFacts { Exists = true, On = on };
            }

            return new SwitchFacts
            {
                Exists = true,
                IsSwitch = true,
                IsOn = ii.isOn,
                On = on,
                Requester = ReachOf(t),
            };
        }

        /// <summary>
        /// What the object rules know of a thing a client says it took off
        /// the ground: the only code that reads it for them.
        /// </summary>
        private PickupFacts PickupOf(Transform t)
        {
            if (t == null)
            {
                return new PickupFacts();
            }

            return new PickupFacts
            {
                Exists = true,
                Lure = t.GetComponent<Lure>() != null,
                Note = t.GetComponent<JournalNoteReference>() != null,
                DeathDrop = t.GetComponent<DeathDrop>() != null,
                Requester = ReachOf(t),
            };
        }

        /// <summary>
        /// What the object rules know of spilt gasoline a client says it
        /// lit: the only code that reads a Liquid for them.
        /// </summary>
        private LiquidFacts LiquidOf(Transform t)
        {
            if (t == null)
            {
                return new LiquidFacts();
            }

            var liquid = t.GetComponent<Liquid>();
            if (liquid == null)
            {
                return new LiquidFacts { Exists = true };
            }

            return new LiquidFacts
            {
                Exists = true,
                IsLiquid = true,
                Flammable = liquid.flammable,
                Requester = ReachOf(t),
            };
        }

        /// <summary>
        /// What the object rules know of a feeder a client says it used:
        /// the only code that reads a Feeder for them.
        /// </summary>
        private FeederFacts FeederOf(Transform t)
        {
            if (t == null)
            {
                return new FeederFacts();
            }

            var feeder = t.GetComponent<Feeder>();
            if (feeder == null)
            {
                return new FeederFacts { Exists = true };
            }

            return new FeederFacts
            {
                Exists = true,
                IsFeeder = true,
                Active = feeder.Active,
                Requester = ReachOf(t),
            };
        }

        // ---- the apply --------------------------------------------------

        /// <summary>
        /// Make the change in this game (under Coop.Guards.Replaying). On
        /// the host a vine hit's Y becomes the health it left.
        /// </summary>
        public void Apply(Action a, ObjRef obj, ref ActionValues values)
        {
            var t = ObjRef.Resolve<Transform>(obj);
            if (WorldActions.Debug)
            {
                string name = "not found here";
                if (t != null)
                {
                    name = t.name;
                }

                Plugin.Log.LogInfo($"[world] got {a} {obj}: " + name);
            }

            if (t == null)
            {
                return;
            }

            switch (a)
            {
                case Action.VineHit:
                    CutVine(t.GetComponent<ChainParent>(), ref values);
                    break;
                case Action.ShadowArmorDied:
                    {
                        var sa = t.GetComponent<ShadowArmor>();
                        if (sa != null)
                        {
                            sa.die();
                        }

                        break;
                    }

                case Action.InteractiveSwitch:
                    {
                        var ii = t.GetComponent<InteractiveItem>();
                        if (ii != null)
                        {
                            // its events are the switcher's (and the host's),
                            // not ours
                            ii.isOn = values.F1;
                        }

                        break;
                    }

                case Action.Removed:
                    if (t.GetComponent<Item>() != null
                        || t.GetComponent<Lure>() != null)
                    {
                        Containers.CloseIfOpen(t.gameObject);
                        Object.Destroy(t.gameObject);
                    }

                    break;
                case Action.FeederUsed:
                    Feeder feeder = t.GetComponent<Feeder>();
                    if (feeder != null)
                    {
                        feeder.makeInactive();
                    }

                    break;
                case Action.LiquidLit:
                    {
                        var liquid = t.GetComponent<Liquid>();
                        if (liquid != null && !liquid.burning)
                        {
                            liquid.startBurning();
                        }

                        break;
                    }
            }
        }

        /// <summary>
        /// A VineHit from another game: the hit (x the damage, y the health
        /// the host's hit left) on our copy of the vine.
        /// </summary>
        private void CutVine(ChainParent cp, ref ActionValues values)
        {
            if (cp == null || !cp.attached || cp.health <= 0f)
            {
                return;
            }

            float x = values.X;
            var s = _coop.Net;
            // The host's own hit says the health it left, so a copy that
            // missed a hit comes back in step.
            if (s != null && !s.IsHost)
            {
                cp.health = ObjectRules.VineHealthBefore(values.Y, x);
            }

            Vine vine = null;
            if (cp.lastChainSegment != null)
            {
                vine = cp.lastChainSegment.GetComponent<Vine>();
            }

            if (vine == null)
            {
                vine = cp.GetComponentInChildren<Vine>();
            }

            if (vine != null)
            {
                cp.getHit(x, null, vine);
            }
            else // getHit needs a segment for its sound
            {
                cp.health = Mathf.Max(0f, cp.health - x);
                if (cp.health <= 0f)
                {
                    cp.detach();
                }
            }

            if (s != null && s.IsHost)
            {
                values = values with { Y = cp.health };
            }
        }

        /// <summary>
        /// Host, after applying a client's action: what the host's own game
        /// does for it, outside the replay.
        /// </summary>
        public void AfterOnHost(Action a, ObjRef obj, bool on)
        {
            if (a != Action.InteractiveSwitch)
            {
                return;
            }

            var ii = ObjRef.Resolve<InteractiveItem>(obj);
            if (ii == null)
            {
                return;
            }

            try
            {
                EventTriggers trigger;
                if (on)
                {
                    trigger = ii.onTrigger;
                }
                else
                {
                    trigger = ii.offTrigger;
                }

                // Tested by reference, not with Unity's null check: the
                // game's own switch fires its trigger with no check, and
                // fireEventTrigger doesn't touch the native object, so the
                // events of a destroyed trigger run here as they would for
                // the host's own flip.
                if (!ReferenceEquals(trigger, null))
                {
                    trigger.fireEventTrigger(
                        global::EventTrigger.Type.area, "", force: true);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't run the events of switch {ii.name}: "
                        + $"{e.Message}");
            }
        }
    }
}
