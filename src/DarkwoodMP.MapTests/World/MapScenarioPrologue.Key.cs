using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Steps for the first house's key and its door.</summary>
    public static partial class MapScenarioPrologue
    {
        // ---- the key in the first house's wardrobe and the door it opens ----
        // The house at the forest's start (dream_tutorial_00).

        /// <summary>
        /// The things of this kind in the stage's place, switched on or not:
        /// within 4000 of our player (the stage is a separate location far from
        /// the world, whose own things must not be picked: walking there leaves
        /// the dream).
        /// </summary>
        private static System.Collections.Generic.IEnumerable<T> Here<T>()
            where T : Component =>
            StateDump.All<T>().Where(
                c => WorldActions.FlatSq(
                    c.transform.position,
                    P.transform.position) < 4000f * 4000f);

        /// <summary>
        /// The locks here that a key opens, and the containers with a key in
        /// them, for the log.
        /// </summary>
        private static string Locks() =>
            string.Join(
                "; ", Here<Locked>().Where(
                    l => !string.IsNullOrEmpty(l.keyType))
                    .Select(
                        l => $"{l.name} key {l.keyType} locked {l.locked} door "
                            + $"{l.GetComponent<Door>() != null} at "
                            + $"{Fmt(l.transform.position)}"))
                + " | " + string.Join(
                "; ", Here<Inventory>().SelectMany(
                    i => i.slots.Where(
                        s => s != null && !InvItemClass.isNull(s.invItem)
                            && s.invItem.baseClass != null
                            && s.invItem.baseClass
                                .GetComponent<KeyReference>() != null)
                        .Select(
                            s => $"{s.invItem.type} (key "
                                + s.invItem.baseClass
                                    .GetComponent<KeyReference>().type
                                + $") in {i.name} "
                                + $"at {Fmt(i.transform.position)}")))
                + " | items with key in their name: " + string.Join(
                "; ", Here<Inventory>().SelectMany(
                    i => i.slots.Where(
                        s => s != null && !InvItemClass.isNull(s.invItem)
                            && s.invItem.type.ToLower().Contains("key"))
                        .Select(
                            s => $"{s.invItem.type} in {i.name} ({i.invType}) "
                                + $"at {Fmt(i.transform.position)}")))
                + $" | {Here<Locked>().Count()} locks, doors near us: "
                + string.Join("; ", Here<Door>()
                    .Select(d =>
                    {
                        string name = d.name;
                        string spot = Fmt(d.transform.position);
                        Locked latch = d.GetComponent<Locked>();
                        bool? locked = null;
                        if (latch != null)
                        {
                            locked = latch.locked;
                        }

                        latch = d.GetComponent<Locked>();
                        string key = null;
                        if (latch != null)
                        {
                            key = latch.keyType;
                        }

                        return $"{name} at {spot} locked {locked} key '{key}' "
                            + $"blocked {d.blocked} active "
                            + $"{d.gameObject.activeInHierarchy}";
                    }));

        /// <summary>
        /// "prologue-key-look" (either): what Locks() finds here, and the
        /// events here that give a key, for the log.
        /// </summary>
        private static IEnumerator KeyLook(string[] a, Result r)
        {
            var gives = Here<GameEvents>().SelectMany(g => g.events.Where(
                e => e != null && e.type == GameEvent.Type.addJournalItem)
                    .SelectMany(e => e.prefabList.OfType<GameObject>()
                        .Select(o => o.GetComponent<InvItem>())
                        .Where(
                            i => i != null
                                && i.GetComponent<KeyReference>() != null)
                        .Select(i =>
                        {
                            string giver = g.name;
                            string parent = "-";
                            if (g.transform.parent != null)
                            {
                                parent = g.transform.parent.name;
                            }

                            return $"{giver} ({parent}) at "
                                + $"{Fmt(g.transform.position)} gives {i.type} "
                                + "(key "
                                + $"{i.GetComponent<KeyReference>().type})";
                        })));

            var anywhere = StateDump.All<Locked>().Where(
                l => l.keyType != null && l.keyType.Contains("tutorial"))
                .Select(l =>
                {
                    string lockName = l.name;
                    string holder = "-";
                    if (l.transform.parent != null)
                    {
                        holder = l.transform.parent.name;
                    }

                    return $"{lockName} ({holder}) key {l.keyType} locked "
                        + $"{l.locked} at {Fmt(l.transform.position)} active "
                        + $"{l.gameObject.activeInHierarchy}";
                });

            var keyItems = StateDump.All<KeyReference>().Select(
                k => $"{k.name} key {k.type} at {Fmt(k.transform.position)} "
                    + $"active {k.gameObject.activeInHierarchy}");
            r.Info =
                $"at {Fmt(P.transform.position)}: {Locks()} | events giving a "
                    + $"key: {string.Join("; ", gives)} | tutorial locks "
                    + $"anywhere: {string.Join("; ", anywhere)} | keys lying "
                    + $"about: {string.Join("; ", keyItems)}";
            yield break;
        }

        /// <summary>The door here whose lock this key opens.</summary>
        private static Door KeyDoorOf(string key) =>
            Nearest(
                Here<Door>().Where(
                    d => d.GetComponent<Locked>() is Locked l
                        && l.keyType == key),
                P.transform.position);

        // the key's item type, as the last take here found it
        private static string _keyItem;

        /// <summary>
        /// "prologue-key-take" (either): take the key from the container it's
        /// in (the wardrobe), the real way: the container open, its slot's
        /// default action (InvSlot.defaultActivateItem: KeyReference.pickup
        /// puts the key in the journal, marked inDream, and the item leaves the
        /// container). Our journal must then have it.
        /// </summary>
        private static IEnumerator KeyTake(string[] a, Result r)
        {
            InvSlot slot = null;
            Inventory inv = null;
            foreach (var i in Here<Inventory>())
            {
                if (i.invType != Inventory.InvType.itemInv || i == P.Inventory
                    || i == P.Hotbar)
                {
                    continue;
                }

                var s = i.slots.FirstOrDefault(
                    x => x != null && !InvItemClass.isNull(x.invItem)
                        && x.invItem.baseClass != null
                        && x.invItem.baseClass
                            .GetComponent<KeyReference>() is KeyReference kr
                        && KeyDoorOf(kr.type) != null);
                if (s != null)
                {
                    slot = s;
                    inv = i;
                    break;
                }
            }

            if (slot == null)
            {
                r.Fail($"no container here with the key to a door: {Locks()}");
                yield break;
            }

            string key =
                slot.invItem.baseClass.GetComponent<KeyReference>().type;
            _keyItem = slot.invItem.type;
            yield return Walk(inv.transform.position);
            P.openedItemInventory = inv; // as if its window were open
            yield return new WaitForSeconds(0.6f);
            bool took = false;
            AsPlayer(() => took = slot.defaultActivateItem());
            yield return new WaitForSeconds(0.6f);
            P.openedItemInventory = null;
            yield return new WaitForSeconds(0.5f);
            var j = Singleton<UI>.Instance.journal;
            j.keysDict.TryGetValue(key, out var k);
            string line =
                $"{_keyItem} (key {key}) from {inv.name} "
                    + $"{ObjRef.Of(inv.gameObject)} for {KeyDoorOf(key).name}: "
                    + $"taken {took}, ";
            bool? inDream = null;
            if (k != null)
            {
                inDream = k.inDream;
            }

            int left = inv.slots.Count(
                x => !InvItemClass.isNull(x.invItem)
                    && x.invItem.type == _keyItem);
            r.Info = line
                + $"in our journal {k != null} (inDream {inDream}), left in it "
                + $"{left}";
            if (k == null)
            {
                r.Fail($"{r.Info}: not in our journal");
            }
            else if (!k.inDream)
            {
                r.Fail($"{r.Info}: not marked as found in the dream");
            }
            else if (!InvItemClass.isNull(slot.invItem))
            {
                r.Fail($"{r.Info}: still in the container");
            }
        }

        /// <summary>
        /// "prologue-key-has KEY yes|no" (either): our journal has the key
        /// (marked inDream), or hasn't.
        /// </summary>
        private static IEnumerator KeyHas(string[] a, Result r)
        {
            var j = Singleton<UI>.Instance.journal;
            bool want = a.Length > 1 && a[1] == "yes";
            float until = Time.realtimeSinceStartup + 8f;
            while (j.keysDict.ContainsKey(a[0]) != want
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            j.keysDict.TryGetValue(a[0], out var k);
            bool? inDream = null;
            if (k != null)
            {
                inDream = k.inDream;
            }

            string wanted = a[0];
            bool dreaming = D.dreaming;
            string shared = DreamSync.Shared;
            if (shared == null)
            {
                shared = "none";
            }

            r.Info =
                $"key {wanted} in our journal {k != null} (inDream {inDream}), "
                    + $"dreaming {dreaming}, shared dream {shared}";
            if ((k != null) != want)
            {
                string text = "none";
                if (want)
                {
                    text = "it";
                }

                r.Fail($"{r.Info}, wanted {text}");
            }
            else if (k != null && !k.inDream)
            {
                r.Fail($"{r.Info}: not marked as found in the dream");
            }
        }

        /// <summary>
        /// "prologue-key-door KEY [--reopen]" (either): at the door the key
        /// opens, as the player does: a click on the locked door opens its lock
        /// with the key (Locked.tryToOpenWithKey), the next opens the door.
        /// Neither may be refused, and the door must stay unlocked and open.
        /// --reopen: close it first (it's unlocked).
        /// </summary>
        private static IEnumerator KeyDoor(string[] a, Result r)
        {
            var d = KeyDoorOf(a[0]);
            if (d == null)
            {
                r.Fail($"no door here for {a[0]}: {Locks()}");
                yield break;
            }

            var l = d.GetComponent<Locked>();
            yield return Walk(d.transform.position);
            int refused = Coop.Current.Authority.RolledBack;
            if (a.Contains("--reopen") && d.opened)
            {
                AsPlayer(() => d.close(P.transform));
                yield return new WaitForSeconds(1f);
            }

            bool unlocked = false;
            if (l.locked)
            {
                AsPlayer(() => unlocked = l.tryToOpenWithKey());
            }

            yield return new WaitForSeconds(1f);
            if (!d.opened)
            {
                AsPlayer(() => d.open(P.transform.position, P.transform, 0f));
            }

            // a refusal would lock or close it again
            yield return new WaitForSeconds(2.5f);
            r.Info =
                $"{d.name} "
                    + $"{ObjRef.Of(d.gameObject)} key {a[0]}: "
                    + $"unlocked with it {unlocked}, locked {l.locked}, "
                    + $"open {d.opened}, "
                    + $"refusals {Coop.Current.Authority.RolledBack - refused}";
            if (Coop.Current.Authority.RolledBack != refused)
            {
                r.Fail($"{r.Info}: the host refused it");
            }
            else if (l.locked)
            {
                r.Fail($"{r.Info}: still locked");
            }
            else if (!d.opened)
            {
                r.Fail($"{r.Info}: not open");
            }
        }

        /// <summary>
        /// "prologue-key-door-is KEY open|locked" (either): the key's door here
        /// is unlocked and open, or locked and shut.
        /// </summary>
        private static IEnumerator KeyDoorIs(string[] a, Result r)
        {
            var d = KeyDoorOf(a[0]);
            if (d == null)
            {
                r.Fail($"no door here for {a[0]}");
                yield break;
            }

            var l = d.GetComponent<Locked>();
            bool open = a.Length > 1 && a[1] == "open";
            float until = Time.realtimeSinceStartup + 8f;
            while (true)
            {
                bool waiting;
                if (open)
                {
                    waiting = l.locked || !d.opened;
                }
                else
                {
                    waiting = !l.locked || d.opened;
                }

                if (!(waiting && Time.realtimeSinceStartup < until))
                {
                    break;
                }

                yield return null;
            }

            r.Info =
                $"{d.name} "
                    + $"{ObjRef.Of(d.gameObject)}: "
                    + $"locked {l.locked}, open {d.opened}";
            bool condition;
            if (open)
            {
                condition = l.locked || !d.opened;
            }
            else
            {
                condition = !l.locked || d.opened;
            }

            if (condition)
            {
                string text = "locked and shut";
                if (open)
                {
                    text = "unlocked and open";
                }

                r.Fail($"{r.Info}, wanted {text}");
            }
        }

        /// <summary>
        /// "prologue-key-reset KEY" (host, after its own prologue-key-take): as
        /// before the key was found, for the next test: the door shut and
        /// locked again (Door.lockMe, sent as the story's), the key out of our
        /// journal (sent as a removal, "-") and back in the wardrobe (put there
        /// by our player, with it open).
        /// </summary>
        private static IEnumerator KeyReset(string[] a, Result r)
        {
            var d = KeyDoorOf(a[0]);
            if (d == null || _keyItem == null)
            {
                r.Fail($"no door for {a[0]}, or no key taken here before");
                yield break;
            }

            var inv = Nearest(
                Here<Inventory>().Where(
                    i => i.invType == Inventory.InvType.itemInv
                        && i != P.Inventory && i != P.Hotbar
                        && !i.name.StartsWith("DroppedItem")
                        && !i.transform.IsChildOf(P.transform)),
                d.transform.position);
            yield return Walk(d.transform.position);
            if (d.opened)
            {
                AsPlayer(() => d.close(P.transform));
                yield return new WaitForSeconds(1f);
            }

            d.lockMe(a[0]);
            Singleton<UI>.Instance.journal.keysDict.Remove(a[0]);
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Story.FlushNow();
            }

            var free = inv.slots.FirstOrDefault(
                s => s != null && InvItemClass.isNull(s.invItem));
            if (free == null)
            {
                r.Fail($"no free slot in {inv.name}");
                yield break;
            }

            yield return Walk(inv.transform.position);
            P.openedItemInventory = inv;
            yield return new WaitForSeconds(0.6f);
            AsPlayer(
                () => inv.addItem(new InvItemClass(_keyItem, 1f, 1), false));
            yield return new WaitForSeconds(0.6f);
            P.openedItemInventory = null;
            yield return new WaitForSeconds(0.5f);
            var l = d.GetComponent<Locked>();
            int back = inv.slots.Count(
                s => !InvItemClass.isNull(s.invItem)
                    && s.invItem.type == _keyItem);
            bool known =
                Singleton<UI>.Instance.journal.keysDict.ContainsKey(a[0]);
            r.Info =
                $"{d.name} locked {l.locked} open {d.opened}, {_keyItem} back "
                    + $"in {inv.name} {ObjRef.Of(inv.gameObject)}: "
                    + $"{back}, key in our journal {known}";
            if (!l.locked || d.opened)
            {
                r.Fail($"{r.Info}: the door isn't shut and locked");
            }
            else if (!inv.slots.Any(
                s => !InvItemClass.isNull(s.invItem)
                    && s.invItem.type == _keyItem))
            {
                r.Fail($"{r.Info}: the key isn't in {inv.name}");
            }
        }
    }
}
