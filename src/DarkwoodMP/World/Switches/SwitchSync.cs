using DarkwoodMP.World.Switches;
using UnityEngine;
using Action = DarkwoodMP.World.WorldActions.Action;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Lamps, generators, radios...: an Item switched on or off for
    /// everyone (ItemOn, ItemOff). The host checks a client's with
    /// SwitchRules.MayTurn.
    /// </summary>
    internal sealed class SwitchSync
    {
        private readonly CoopSession _coop;

        public SwitchSync(CoopSession coop)
        {
            _coop = coop;
        }

        /// <summary>
        /// Host: whether a client's player could have switched this item
        /// (WorldActions.Allowed). Refuses the request if not.
        /// </summary>
        public bool MayTurn(ObjRef obj)
        {
            var item = ObjRef.Resolve<Item>(obj);
            var verdict = SwitchRules.MayTurn(FactsOf(item));
            _coop.Authority.Refuse(verdict);
            return !verdict.Refused;
        }

        /// <summary>
        /// What the switch rules know of an Item the host is asked about:
        /// the only code that reads it for them.
        /// </summary>
        private SwitchFacts FactsOf(Item item)
        {
            if (item == null)
            {
                return new SwitchFacts();
            }

            return new SwitchFacts
            {
                Exists = true,
                Requester = _coop.Authority.ReachOf(
                    item.transform.position, item.name),
            };
        }

        /// <summary>
        /// An ItemOn or ItemOff from another game, applied here. False when
        /// the item isn't here, and it goes no further.
        /// </summary>
        public bool Apply(Action a, ObjRef obj)
        {
            var item = WorldActions.Got<Item>(a, obj);
            if (item == null)
            {
                return false;
            }

            if (a == Action.ItemOn)
            {
                item.turnOn();
            }
            else
            {
                item.turnOff();
            }

            return true;
        }

        /// <summary>
        /// ItemSounds.playStart: whether the item's start and loop sounds
        /// start (ItemSoundRules.Starts). False keeps the ones playing, so
        /// no loop is left that a switch-off can't stop.
        /// </summary>
        public bool StartsSounds(ItemSounds sounds)
        {
            var facts = new ItemSoundFacts
            {
                LoopPlays = Plays(
                    sounds.loopSoundAO, sounds.loopSound, sounds.transform),
                StartPlays = Plays(
                    sounds.startSoundAO, sounds.startSound, sounds.transform),
            };

            bool starts = ItemSoundRules.Starts(facts);
            if (!starts)
            {
                Plugin.Log.LogInfo(
                    $"[world] {sounds.name}: its sound plays already, "
                        + "not started again");
            }

            return starts;
        }

        /// <summary>
        /// The audio object plays that sound under that item. The game's
        /// audio objects are pooled: one the item still names may have
        /// been stopped and handed to another sound.
        /// </summary>
        private static bool Plays(AudioObject o, string id, Transform under)
        {
            if (o == null || string.IsNullOrEmpty(id) || o.audioID != id)
            {
                return false;
            }

            return o.IsPlaying() && o.transform.IsChildOf(under);
        }

        /// <summary>
        /// Item.turnOn or turnOff starts: one deeper in patched calls.
        /// </summary>
        public void BeforeTurn()
        {
            _coop.Guards.Depth++;
        }

        /// <summary>
        /// Item.turnOn or turnOff returned: at the outermost call, while
        /// sharing, send it (a is ItemOn or ItemOff).
        /// </summary>
        public void AfterTurn(Item item, Action a)
        {
            if (_coop.Guards.Outermost() && _coop.Guards.Sharing)
            {
                WorldActions.Send(a, item.gameObject);
            }
        }
    }
}
