using DarkwoodMP.Net;
using DarkwoodMP.World.Locks;
using Action = DarkwoodMP.World.WorldActions.Action;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Locks on things that aren't doors, containers and padlocks, shared.
    /// A lock opened with a key or a lockpick (Locked.tryToOpenWithKey /
    /// tryToLockpick, which call Locked.unlock) goes as DoorUnlock for a
    /// door's lock and as ItemUnlock for any other's (a chest, a cabinet),
    /// with x = 1 for a key, 2 for a lockpick, 0 for anything else (the
    /// story). Locked.unlock only clears Locked.locked and never goes through
    /// Door.unlock, so it's patched itself; the two try methods are patched
    /// too, so the outermost call is the one sent: tryToLockpick takes the
    /// lockpick after unlocking, and a refused request must give it back.
    ///
    /// A container opened (searched) goes as ItemSearched, a combination
    /// padlock opened as PadlockUnlock. A client's pick of a crate's lock is
    /// a TrapDisarm request the host rolls. The host decides each with
    /// LockRules.
    /// </summary>
    internal sealed class LockShare
    {
        private readonly CoopSession _coop;

        public LockShare(CoopSession coop)
        {
            _coop = coop;
        }

        // ---- the host's checks --------------------------------------------

        /// <summary>
        /// Host: whether a client's player could have opened this lock
        /// (ItemUnlock), with x how. Refuses the request if not.
        /// </summary>
        public bool MayUnlock(ObjRef obj, float how)
        {
            var l = ObjRef.Resolve<Locked>(obj);
            return Answer(LockRules.MayUnlock(FactsOf(l, how)));
        }

        /// <summary>
        /// Host: whether a client's player could have opened this container
        /// (ItemSearched). Only the host says a crate was picked open (its
        /// TrapDisarm roll).
        /// </summary>
        public bool MaySearch(ObjRef obj)
        {
            var item = ObjRef.Resolve<Item>(obj);
            return Answer(LockRules.MaySearch(SearchFactsOf(item)));
        }

        /// <summary>
        /// Host: whether a client's player could have opened this padlock
        /// (PadlockUnlock), manually with this code.
        /// </summary>
        public bool MayUnlockPadlock(ObjRef obj, bool manual, string code)
        {
            var pad = ObjRef.Resolve<Padlock>(obj);
            var facts = FactsOf(pad) with { Manual = manual, Code = code };
            return Answer(LockRules.MayUnlockPadlock(facts));
        }

        /// <summary>
        /// Host: whether a client's player could have picked this item's
        /// lock (a TrapDisarm on an Item with no Trigger).
        /// </summary>
        public bool MayPick(Item item)
        {
            var facts = new CrateFacts
            {
                Crate = WorldActions.Crate(item) != null,
                Requester = _coop.Authority.ReachOf(
                    item.transform.position, item.name),
            };

            return Answer(LockRules.MayPick(facts));
        }

        private bool Answer(Verdict verdict)
        {
            _coop.Authority.Refuse(verdict);
            return !verdict.Refused;
        }

        /// <summary>
        /// What the lock rules know of a Locked the host is asked about: the
        /// only code that reads it for them. The journal is asked about the
        /// key only for a locked lock opened with one, as the rule looks.
        /// </summary>
        private LockFacts FactsOf(Locked l, float how)
        {
            if (l == null)
            {
                return new LockFacts();
            }

            bool known = false;
            if (l.locked && how == LockRules.ByKey)
            {
                known = WorldActions.HasKey(l);
            }

            return new LockFacts
            {
                Exists = true,
                Locked = l.locked,
                How = how,
                KeyKnown = known,
                KeyType = l.keyType,
                Requester = _coop.Authority.ReachOf(
                    l.transform.position, l.name),
            };
        }

        /// <summary>What the lock rules know of a container.</summary>
        private SearchFacts SearchFactsOf(Item item)
        {
            if (item == null)
            {
                return new SearchFacts();
            }

            return new SearchFacts
            {
                Exists = true,
                Crate = WorldActions.Crate(item) != null,
                Searched = item.searched,
                HasInventory = item.GetComponent<Inventory>() != null,
                Requester = _coop.Authority.ReachOf(
                    item.transform.position, item.name),
            };
        }

        /// <summary>What the lock rules know of a padlock.</summary>
        private PadlockFacts FactsOf(Padlock pad)
        {
            if (pad == null)
            {
                return new PadlockFacts();
            }

            return new PadlockFacts
            {
                Exists = true,
                Locked = pad.locked,
                Combination = pad.combination,
                Requester = _coop.Authority.ReachOf(
                    pad.transform.position, pad.name),
            };
        }

        // ---- applying ------------------------------------------------------

        /// <summary>
        /// Host: a client's pick of a crate's lock (a TrapDisarm on a
        /// crate). The host rolls it, as the game would for its own player,
        /// and answers "1" or "0". If it worked the crate is searched here,
        /// and true says the caller passes it on as ItemSearched; a failed
        /// pick, or one of a crate already open, changes nothing.
        /// </summary>
        public bool Pick(ulong from, ObjRef obj, Item crate)
        {
            float roll = 0f;
            if (!crate.searched)
            {
                roll = UnityEngine.Random.Range(0f, 1f);
            }

            var outcome = LockRules.Pick(
                crate.searched, crate.disarmChance, roll);
            string result = "0";
            if (outcome != PickOutcome.Failed)
            {
                result = "1";
            }

            _coop.Authority.Result(result);
            if (WorldActions.Debug)
            {
                string pick = "failed";
                if (outcome == PickOutcome.AlreadyOpen)
                {
                    pick = "already open";
                }
                else if (outcome == PickOutcome.Opened)
                {
                    pick = "worked";
                }

                Plugin.Log.LogInfo(
                    $"[world] {_coop.Net.NameOf(from)} picks the lock of "
                        + $"{obj} ({crate.name}): {pick}");
            }

            if (outcome != PickOutcome.Opened)
            {
                return false;
            }

            crate.searched = true;
            return true;
        }

        /// <summary>
        /// An ItemUnlock, ItemSearched or PadlockUnlock from another game,
        /// applied here. False when what it names isn't here and it goes no
        /// further (an ItemUnlock is passed on all the same).
        /// </summary>
        public bool Apply(Action a, ObjRef obj)
        {
            if (a == Action.ItemUnlock)
            {
                var l = WorldActions.Got<Locked>(a, obj);
                if (l != null)
                {
                    l.unlock();
                }

                return true;
            }

            if (a == Action.ItemSearched)
            {
                var item = WorldActions.Got<Item>(a, obj);
                if (item == null)
                {
                    return false;
                }

                item.searched = true;
                var corpse = item.GetComponent<Character>();
                if (corpse != null)
                {
                    corpse.searched = true;
                }

                return true;
            }

            var padlock = WorldActions.Got<Padlock>(a, obj);
            if (padlock == null)
            {
                return false;
            }

            // without its window, message and events: those are the opener's
            padlock.unlock(false);
            return true;
        }

        // ---- the state to roll back to --------------------------------------

        /// <summary>
        /// Host: the padlock's state for an ObjState (u8 flags, 1 locked).
        /// </summary>
        public void WriteState(PacketWriter w, Padlock pad)
        {
            int lockedFlag = 0;
            if (pad.locked)
            {
                lockedFlag = 1;
            }

            w.W.Write((byte)lockedFlag);
        }

        /// <summary>
        /// Client: put our copy of the padlock as the host has it (a refused
        /// request's rollback).
        /// </summary>
        public void ReadState(PacketReader r, UnityEngine.Transform go)
        {
            bool locked = (r.R.ReadByte() & 1) != 0;
            Padlock pad = null;
            if (go != null)
            {
                pad = go.GetComponent<Padlock>();
            }

            if (pad != null && pad.locked != locked)
            {
                pad.locked = locked;
                if (locked)
                {
                    pad.currentCombination = "0000";
                }
            }
        }

        // ---- what changed ---------------------------------------------------

        /// <summary>
        /// A patched Locked call starts: one deeper in patched calls. Says
        /// whether it was locked.
        /// </summary>
        public bool BeforeUnlock(Locked l)
        {
            _coop.Guards.Depth++;
            return l.locked;
        }

        /// <summary>
        /// A patched Locked call returned (unlock, or a try with a key or a
        /// lockpick, as how says): send the lock opened if it did, at the
        /// outermost call.
        /// </summary>
        public void AfterUnlock(Locked l, float how, bool wasLocked)
        {
            var facts = new UnlockSentFacts
            {
                Outermost = _coop.Guards.Outermost(),
                Sharing = _coop.Guards.Sharing,
                WasLocked = wasLocked,
                Locked = l.locked,
                How = how,
            };

            if (LockRules.SendsUnlock(facts) == UnlockMessage.Nothing)
            {
                return;
            }

            Send(l, how);
        }

        /// <summary>
        /// Send this lock opened, as DoorUnlock for a door's or ItemUnlock.
        /// With a key, the host checks the key is in its journal, so our
        /// journal's news (the key just picked up) goes first, and the key
        /// itself again, in case the host didn't take it before.
        /// </summary>
        public void Send(Locked l, float how)
        {
            var door = l.GetComponent<Door>();
            var facts = new UnlockSentFacts
            {
                How = how,
                Client = _coop.Net.IsClient,
                HasDoor = door != null,
            };

            if (LockRules.SendsKeyFirst(facts))
            {
                var instance = _coop.Story;
                if (instance != null)
                {
                    instance.FlushNow();
                }

                var flags = _coop.Story;
                if (flags != null)
                {
                    flags.SendKey(l.keyType);
                }
            }

            if (facts.HasDoor)
            {
                WorldActions.Send(
                    Action.DoorUnlock, door.gameObject, how);
            }
            else
            {
                WorldActions.Send(Action.ItemUnlock, l.gameObject, how);
            }
        }

        /// <summary>
        /// A locked crate picked open in the host's game (Item.disarm on an
        /// item with no Trigger sets searched and opens it): the others are
        /// told it's searched, so nobody has to pick it again. A client's
        /// picks go to the host as TrapDisarm requests instead
        /// (TrapDisarms.AskHost), and the host sends ItemSearched itself.
        /// </summary>
        public void AfterDisarm(Item item, bool wasSearched)
        {
            var facts = new SearchSentFacts
            {
                WasSearched = wasSearched,
                Searched = item.searched,
                Crate = WorldActions.Crate(item) != null,
                Depth = _coop.Guards.Depth,
                Sharing = _coop.Guards.Sharing,
                Host = _coop.Net.IsHost,
            };

            if (LockRules.SendsCrateSearched(facts))
            {
                WorldActions.Send(Action.ItemSearched, item.gameObject);
            }
        }

        /// <summary>
        /// A container opened the game's way (Item.openInventory) sets
        /// Item.searched in the opener's game only: send it as ItemSearched
        /// when it changed. A client sends only its own player's open (Send
        /// checks ByLocalPlayer), and the host checks reach.
        /// </summary>
        public void AfterOpen(Item item, bool wasSearched)
        {
            var facts = new SearchSentFacts
            {
                WasSearched = wasSearched,
                Searched = item.searched,
                Sharing = _coop.Guards.Sharing,
            };

            if (LockRules.SendsSearched(facts))
            {
                WorldActions.Send(Action.ItemSearched, item.gameObject);
            }
        }

        /// <summary>
        /// A padlock's unlock starts: one deeper in patched calls. Says
        /// whether it was locked.
        /// </summary>
        public bool BeforePadlock(Padlock pad)
        {
            _coop.Guards.Depth++;
            return pad.locked;
        }

        /// <summary>
        /// A padlock's unlock returned: send it opened if it did, with the
        /// code entered when a player opened it.
        /// </summary>
        public void AfterPadlock(Padlock pad, bool manually, bool wasLocked)
        {
            var facts = new PadlockSentFacts
            {
                Outermost = _coop.Guards.Outermost(),
                Sharing = _coop.Guards.Sharing,
                WasLocked = wasLocked,
                Locked = pad.locked,
                Manually = manually,
                Code = pad.currentCombination,
            };

            if (!LockRules.SendsPadlock(facts))
            {
                return;
            }

            WorldActions.Send(
                Action.PadlockUnlock, pad.gameObject, f1: manually,
                str: LockRules.PadlockCode(facts));
        }
    }
}
