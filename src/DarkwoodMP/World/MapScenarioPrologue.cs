using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for the prologue played together (World.Prologue). The list is
    /// tools/tests/map_scenario_prologue.py; the host plays a new game (the prologue not skipped) and the client
    /// joins it in the forest after the intro film.
    /// </summary>
    public static class MapScenarioPrologue
    {
        private static Player P => Player.Instance;
        private static Dreams D => Singleton<Dreams>.Instance;

        public static void Register()
        {
            Add("prologue-in", In, timeout: 120f);
            Add("prologue-next", Next, timeout: 120f);
            Add("prologue-over", Over, timeout: 120f);
            Add("prologue-key-look", KeyLook);
            Add("prologue-key-take", KeyTake, timeout: 60f);
            Add("prologue-key-has", KeyHas);
            Add("prologue-key-door", KeyDoor, timeout: 60f);
            Add("prologue-key-door-is", KeyDoorIs);
            Add("prologue-key-reset", KeyReset, timeout: 60f);
        }

        // ---- the key in the wardrobe of the house at the forest's start (dream_tutorial_00) and the door it opens ----------------------------------

        /// <summary>
        /// The things of this kind in the stage's place, switched on or not: within 4000 of our player (the stage is
        /// a separate location far from the world, whose own things must not be picked: walking there leaves the dream).
        /// </summary>
        private static System.Collections.Generic.IEnumerable<T> Here<T>() where T : Component =>
            StateDump.All<T>().Where(c => WorldActions.FlatSq(c.transform.position, P.transform.position) < 4000f * 4000f);

        /// <summary>The locks here that a key opens, and the containers with a key in them, for the log.</summary>
        private static string Locks() =>
            string.Join("; ", Here<Locked>().Where(l => !string.IsNullOrEmpty(l.keyType))
                .Select(l => $"{l.name} key {l.keyType} locked {l.locked} door {l.GetComponent<Door>() != null} at {Fmt(l.transform.position)}"))
            + " | " + string.Join("; ", Here<Inventory>().SelectMany(i => i.slots.Where(s => s != null && !InvItemClass.isNull(s.invItem)
                    && s.invItem.baseClass != null && s.invItem.baseClass.GetComponent<KeyReference>() != null)
                .Select(s => $"{s.invItem.type} (key {s.invItem.baseClass.GetComponent<KeyReference>().type}) in {i.name} at {Fmt(i.transform.position)}")))
            + " | items with key in their name: " + string.Join("; ", Here<Inventory>().SelectMany(i => i.slots.Where(s => s != null && !InvItemClass.isNull(s.invItem)
                    && s.invItem.type.ToLower().Contains("key")).Select(s => $"{s.invItem.type} in {i.name} ({i.invType}) at {Fmt(i.transform.position)}")))
            + $" | {Here<Locked>().Count()} locks, doors near us: " + string.Join("; ", Here<Door>()
                .Select(d => $"{d.name} at {Fmt(d.transform.position)} locked {d.GetComponent<Locked>()?.locked} key '{d.GetComponent<Locked>()?.keyType}' blocked {d.blocked} active {d.gameObject.activeInHierarchy}"));

        /// <summary>"prologue-key-look" (either): what Locks() finds here, and the events here that give a key, for the log.</summary>
        private static IEnumerator KeyLook(string[] a, Result r)
        {
            var gives = Here<GameEvents>().SelectMany(g => g.events.Where(e => e != null && e.type == GameEvent.Type.addJournalItem)
                .SelectMany(e => e.prefabList.OfType<GameObject>().Select(o => o.GetComponent<InvItem>()).Where(i => i != null && i.GetComponent<KeyReference>() != null)
                .Select(i => $"{g.name} ({(g.transform.parent != null ? g.transform.parent.name : "-")}) at {Fmt(g.transform.position)} gives {i.type} (key {i.GetComponent<KeyReference>().type})")));
            var anywhere = StateDump.All<Locked>().Where(l => l.keyType != null && l.keyType.Contains("tutorial"))
                .Select(l => $"{l.name} ({(l.transform.parent != null ? l.transform.parent.name : "-")}) key {l.keyType} locked {l.locked} at {Fmt(l.transform.position)} active {l.gameObject.activeInHierarchy}");
            var keyItems = StateDump.All<KeyReference>().Select(k => $"{k.name} key {k.type} at {Fmt(k.transform.position)} active {k.gameObject.activeInHierarchy}");
            r.Info = $"at {Fmt(P.transform.position)}: {Locks()} | events giving a key: {string.Join("; ", gives)} | tutorial locks anywhere: {string.Join("; ", anywhere)} | keys lying about: {string.Join("; ", keyItems)}";
            yield break;
        }

        /// <summary>The door here whose lock this key opens.</summary>
        private static Door KeyDoorOf(string key) =>
            Nearest(Here<Door>().Where(d => d.GetComponent<Locked>() is Locked l && l.keyType == key), P.transform.position);

        private static string _keyItem; // the key's item type, as the last take here found it

        /// <summary>
        /// "prologue-key-take" (either): take the key from the container it's in (the wardrobe), the real way: the
        /// container open, its slot's default action (InvSlot.defaultActivateItem: KeyReference.pickup puts the key
        /// in the journal, marked inDream, and the item leaves the container). Our journal must then have it.
        /// </summary>
        private static IEnumerator KeyTake(string[] a, Result r)
        {
            InvSlot slot = null;
            Inventory inv = null;
            foreach (var i in Here<Inventory>())
            {
                if (i.invType != Inventory.InvType.itemInv || i == P.Inventory || i == P.Hotbar)
                {
                    continue;
                }
                var s = i.slots.FirstOrDefault(x => x != null && !InvItemClass.isNull(x.invItem) && x.invItem.baseClass != null
                    && x.invItem.baseClass.GetComponent<KeyReference>() is KeyReference kr && KeyDoorOf(kr.type) != null);
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
            string key = slot.invItem.baseClass.GetComponent<KeyReference>().type;
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
            r.Info = $"{_keyItem} (key {key}) from {inv.name} {ObjRef.Of(inv.gameObject)} for {KeyDoorOf(key).name}: taken {took}, "
                   + $"in our journal {k != null} (inDream {k?.inDream}), left in it {inv.slots.Count(x => !InvItemClass.isNull(x.invItem) && x.invItem.type == _keyItem)}";
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

        /// <summary>"prologue-key-has KEY yes|no" (either): our journal has the key (marked inDream), or hasn't.</summary>
        private static IEnumerator KeyHas(string[] a, Result r)
        {
            var j = Singleton<UI>.Instance.journal;
            bool want = a.Length > 1 && a[1] == "yes";
            float until = Time.realtimeSinceStartup + 8f;
            while (j.keysDict.ContainsKey(a[0]) != want && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            j.keysDict.TryGetValue(a[0], out var k);
            r.Info = $"key {a[0]} in our journal {k != null} (inDream {k?.inDream}), dreaming {D.dreaming}, shared dream {DreamSync.Shared ?? "none"}";
            if ((k != null) != want)
            {
                r.Fail($"{r.Info}, wanted {(want ? "it" : "none")}");
            }
            else if (k != null && !k.inDream)
            {
                r.Fail($"{r.Info}: not marked as found in the dream");
            }
        }

        /// <summary>
        /// "prologue-key-door KEY [--reopen]" (either): at the door the key opens, as the player does: a click on
        /// the locked door opens its lock with the key (Locked.tryToOpenWithKey), the next opens the door. Neither
        /// may be refused, and the door must stay unlocked and open. --reopen: close it first (it's unlocked).
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
            int refused = Authority.RolledBack;
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
            yield return new WaitForSeconds(2.5f); // a refusal would lock or close it again
            r.Info = $"{d.name} {ObjRef.Of(d.gameObject)} key {a[0]}: unlocked with it {unlocked}, locked {l.locked}, open {d.opened}, refusals {Authority.RolledBack - refused}";
            if (Authority.RolledBack != refused)
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

        /// <summary>"prologue-key-door-is KEY open|locked" (either): the key's door here is unlocked and open, or locked and shut.</summary>
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
            while ((open ? l.locked || !d.opened : !l.locked || d.opened) && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            r.Info = $"{d.name} {ObjRef.Of(d.gameObject)}: locked {l.locked}, open {d.opened}";
            if (open ? l.locked || !d.opened : !l.locked || d.opened)
            {
                r.Fail($"{r.Info}, wanted {(open ? "unlocked and open" : "locked and shut")}");
            }
        }

        /// <summary>
        /// "prologue-key-reset KEY" (host, after its own prologue-key-take): as before the key was found, for the next
        /// test: the door shut and locked again (Door.lockMe, sent as the story's), the key out of our journal (sent
        /// as a removal, "-") and back in the wardrobe (put there by our player, with it open).
        /// </summary>
        private static IEnumerator KeyReset(string[] a, Result r)
        {
            var d = KeyDoorOf(a[0]);
            if (d == null || _keyItem == null)
            {
                r.Fail($"no door for {a[0]}, or no key taken here before");
                yield break;
            }
            var inv = Nearest(Here<Inventory>().Where(i => i.invType == Inventory.InvType.itemInv && i != P.Inventory && i != P.Hotbar
                && !i.name.StartsWith("DroppedItem") && !i.transform.IsChildOf(P.transform)), d.transform.position);
            yield return Walk(d.transform.position);
            if (d.opened)
            {
                AsPlayer(() => d.close(P.transform));
                yield return new WaitForSeconds(1f);
            }
            d.lockMe(a[0]);
            Singleton<UI>.Instance.journal.keysDict.Remove(a[0]);
            FlagsSync.Instance?.FlushNow();
            var free = inv.slots.FirstOrDefault(s => s != null && InvItemClass.isNull(s.invItem));
            if (free == null)
            {
                r.Fail($"no free slot in {inv.name}");
                yield break;
            }
            yield return Walk(inv.transform.position);
            P.openedItemInventory = inv;
            yield return new WaitForSeconds(0.6f);
            AsPlayer(() => inv.addItem(new InvItemClass(_keyItem, 1f, 1), false));
            yield return new WaitForSeconds(0.6f);
            P.openedItemInventory = null;
            yield return new WaitForSeconds(0.5f);
            var l = d.GetComponent<Locked>();
            r.Info = $"{d.name} locked {l.locked} open {d.opened}, {_keyItem} back in {inv.name} {ObjRef.Of(inv.gameObject)}: "
                   + $"{inv.slots.Count(s => !InvItemClass.isNull(s.invItem) && s.invItem.type == _keyItem)}, key in our journal {Singleton<UI>.Instance.journal.keysDict.ContainsKey(a[0])}";
            if (!l.locked || d.opened)
            {
                r.Fail($"{r.Info}: the door isn't shut and locked");
            }
            else if (!inv.slots.Any(s => !InvItemClass.isNull(s.invItem) && s.invItem.type == _keyItem))
            {
                r.Fail($"{r.Info}: the key isn't in {inv.name}");
            }
        }

        private static bool FriendSeen()
        {
            var ps = Sync.PlayerSync.Instance;
            return ps != null && ps.Remotes.Values.Any(rp => rp.Root != null && rp.Root.activeInHierarchy);
        }

        private static string Items()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var bag in new[] { P.Inventory, P.Hotbar })
            {
                foreach (var s in bag.slots)
                {
                    if (s != null && !InvItemClass.isNull(s.invItem))
                    {
                        names.Add(s.invItem.type);
                    }
                }
            }
            return names.Count == 0 ? "nothing" : string.Join(",", names);
        }

        /// <summary>"prologue-in STAGE" (either): we're in that stage of the prologue, dreamt together, a first play, and we see the other player.</summary>
        private static IEnumerator In(string[] a, Result r)
        {
            string stage = a.Length > 0 ? a[0] : "dream_tutorial_00";
            float until = Time.realtimeSinceStartup + 90f;
            while (!(Prologue.InStage && Prologue.Stage == stage && DreamSync.Shared == stage && WorldShare.WorldReady && FriendSeen())
                   && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.5f);
            }
            yield return new WaitForSeconds(1f);
            r.Info = $"stage {Prologue.Stage ?? "none"} (shared {DreamSync.Shared ?? "none"}), first play {P.firstPlay}, antagonist {P.isAntagonist}, "
                   + $"at {Locations.Current()} {Fmt(P.transform.position)}, carrying {Items()}, the other player seen {FriendSeen()}, time {Singleton<Controller>.Instance.CurrentTime}";
            if (Prologue.Stage != stage || DreamSync.Shared != stage)
            {
                r.Fail($"not in the shared {stage}: {r.Info}");
            }
            else if (!P.firstPlay)
            {
                r.Fail($"not a first play: {r.Info}");
            }
            else if (!FriendSeen())
            {
                r.Fail($"the other player isn't seen: {r.Info}");
            }
        }

        /// <summary>"prologue-next" (either): our stage of the prologue ends as the game ends it (its outcome to the next stage, or the end); wait until we're through.</summary>
        private static IEnumerator Next(string[] a, Result r)
        {
            var d = D;
            string stage = Prologue.Stage;
            if (!Prologue.InStage)
            {
                r.Fail("not in a stage of the prologue");
                yield break;
            }
            var outcomes = d.preset.outcomes.Where(o => o != null).ToList();
            bool last = stage == "dream_tutorial_01";
            var pick = outcomes.FirstOrDefault(o => o.name != "playerDeath"
                && o.effects.Any(e => e.type == DreamPreset.Outcome.Effect.Type.transferToDream) != last);
            string names = string.Join(",", outcomes.Select(o => o.name));
            if (pick == null)
            {
                r.Fail($"no outcome to {(last ? "the end" : "the next stage")} in {stage} ({names})");
                yield break;
            }
            d.outcome = pick.name;
            AsPlayer(() => d.initiateEndDreaming());
            float until = Time.realtimeSinceStartup + 100f;
            while ((Prologue.Stage == stage || !WorldShare.WorldReady || (!last && !Prologue.InStage) || (last && (d.dreaming || d.switchingDream)))
                   && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.5f);
            }
            yield return new WaitForSeconds(2f);
            r.Info = $"ended {stage} ({pick.name} of {names}): now {Prologue.Stage ?? "out of the prologue"} at {Locations.Current()} {Fmt(P.transform.position)}";
            if (Prologue.Stage == stage)
            {
                r.Fail($"still in {stage}: {r.Info}");
            }
        }

        /// <summary>"prologue-over" (either): the prologue has ended here too: awake in the world, not a first play, the clock just after 5, beside the other player but not on them.</summary>
        private static IEnumerator Over(string[] a, Result r)
        {
            var d = D;
            float until = Time.realtimeSinceStartup + 90f;
            while ((Prologue.Active || d.dreaming || !WorldShare.WorldReady) && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.5f);
            }
            yield return new WaitForSeconds(3f);
            int time = Singleton<Controller>.Instance.CurrentTime;
            float nearest = float.MaxValue;
            var ps = Sync.PlayerSync.Instance;
            if (ps != null)
            {
                foreach (var rp in ps.Remotes.Values)
                {
                    if (rp.Root != null && rp.Root.activeInHierarchy)
                    {
                        nearest = Mathf.Min(nearest, Vector2.Distance(new Vector2(rp.Root.transform.position.x, rp.Root.transform.position.z), new Vector2(P.transform.position.x, P.transform.position.z)));
                    }
                }
            }
            r.Info = $"in the prologue {Prologue.Active}, dreaming {d.dreaming}, first play {P.firstPlay}, shared dream {DreamSync.Shared ?? "none"}, at {Locations.Current()} {Fmt(P.transform.position)}, "
                   + $"time {time}, the other player {(nearest == float.MaxValue ? "not seen" : $"{nearest:0} away")}, carrying {Items()}";
            // The traders that came with the prologue (the wolf), where they are: each is in both games once.
            var wolves = StateDump.All<NPC>().Where(n => n.trader)
                .Select(n => $"{n.name} at {Fmt(n.transform.position)} under {(n.transform.parent != null ? n.transform.parent.name : "-")}/{(n.transform.parent?.parent != null ? n.transform.parent.parent.name : "-")} active {n.gameObject.activeInHierarchy} id {ObjRef.Of(n.gameObject)} ({n.inventory?.getAllItems().Count ?? -1} for sale)");
            Plugin.Log.LogInfo($"[map] traders after the prologue: {string.Join("; ", wolves)}");
            if (Prologue.Active || d.dreaming || P.firstPlay || DreamSync.Shared != null)
            {
                r.Fail($"the prologue isn't over: {r.Info}");
            }
            else if (Locations.Current() != "")
            {
                r.Fail($"not in the world: {r.Info}");
            }
            else if (time < 5 || time > 200)
            {
                r.Fail($"the clock isn't just after the prologue's 5: {r.Info}");
            }
            else if (nearest == float.MaxValue || nearest > 600f)
            {
                r.Fail($"the other player isn't beside us: {r.Info}");
            }
            else if (nearest < 20f)
            {
                r.Fail($"on top of the other player: {r.Info}");
            }
            // The host keeps our character from now on; send it now (in the prologue it isn't sent).
            if (NetEnemies.ClientMode)
            {
                Characters.Instance?.SaveNow();
            }
        }
    }
}
