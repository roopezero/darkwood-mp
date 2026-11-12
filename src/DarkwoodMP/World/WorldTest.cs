using System.Collections;
using System.Linq;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// "--dwmp-test-world": once in a world, the host's character does a few things by itself, so the shared world
    /// can be tested without anyone at the keyboard: opens and closes the nearest door, takes an item out of the
    /// nearest container (as if looting it) and drops it on the ground.
    /// </summary>
    public static class WorldTest
    {
        /// <summary>The items that place something when used (InvItem.placeOnUse), which nothing shares: none were found (docs/INTERACTIONS.md, gap 67).</summary>
        private static void LogPlaceOnUse()
        {
            var db = Singleton<ItemsDatabase>.Instance;
            if (db == null) return;
            var found = new System.Collections.Generic.List<string>();
            foreach (var key in db.itemsDict.Keys.ToList())
            {
                var it = db.getItem(key, instantiate: false);
                if (it != null && it.placeOnUse) found.Add($"{key} -> {(it.item != null ? it.item.name : "nothing")}");
            }
            Plugin.Log.LogInfo($"[world test] items placed on use: {found.Count}" + (found.Count > 0 ? ": " + string.Join(", ", found) : ""));
        }

        public static IEnumerator Run()
        {
            while (!WorldShare.InWorld) yield return null;
            while (Net.Session.Instance == null || Net.Session.Instance.Peers.Count == 0) yield return null;
            yield return new WaitForSeconds(5f);
            var player = Player.Instance;
            var at = player.transform.position;
            LogPlaceOnUse();

            var door = Object.FindObjectsOfType<Door>().Where(d => !d.destroyed).OrderBy(d => (d.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (door != null)
            {
                Plugin.Log.LogInfo($"[world test] door {door.name} at {door.transform.position}, opened={door.opened}");
                if (door.opened) door.close(player.transform); else door.open(at, player.transform);
                yield return new WaitForSeconds(3f);
                if (door.opened) door.close(player.transform); else door.open(at, player.transform);
                Plugin.Log.LogInfo($"[world test] door toggled twice, opened={door.opened}");
            }
            yield return new WaitForSeconds(2f);

            var inv = Object.FindObjectsOfType<Inventory>()
                .Where(i => i.invType == Inventory.InvType.itemInv && !i.isWorkbench && i.slots.Any(s => !InvItemClass.isNull(s.invItem)))
                .OrderBy(i => (i.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (inv == null) { Plugin.Log.LogInfo("[world test] no container with items found"); yield break; }
            var slot = inv.slots.First(s => !InvItemClass.isNull(s.invItem));
            var item = new InvItemClass(slot.invItem);
            Plugin.Log.LogInfo($"[world test] looting {item.type} x{item.amount} from {inv.name} at {inv.transform.position}");
            player.openedItemInventory = inv;          // as if its window were open
            yield return new WaitForSeconds(0.6f);
            slot.removeItem();
            yield return new WaitForSeconds(0.6f);
            player.openedItemInventory = null;         // and closed
            yield return new WaitForSeconds(1f);
            var dropped = player.spawnDroppedInvItem(item);
            Plugin.Log.LogInfo($"[world test] dropped {item.type} on the ground: {dropped?.name}");
            yield return new WaitForSeconds(3f);

            // The story: flip a flag, and an NPC's reputation.
            var flags = Singleton<Flags>.Instance;
            var dict = HarmonyLib.AccessTools.FieldRefAccess<Flags, System.Collections.Generic.Dictionary<string, Flags.Flag>>("flagsDict")(flags);
            var flag = dict.Values.FirstOrDefault(f => f != null);
            if (flag != null) { flags.setFlag(flag.name, !flag.isTrue); Plugin.Log.LogInfo($"[world test] flag {flag.name} -> {flag.isTrue}"); }
            var npc = flags.npcStates.FirstOrDefault(n => n != null);
            if (npc != null) { npc.reputation += 1; Plugin.Log.LogInfo($"[world test] {npc.name}'s reputation -> {npc.reputation}"); }
            // The journal: a story entry and a note the host hasn't got yet, as the game adds them.
            var journal = Singleton<UI>.Instance.journal;
            var jdb = Singleton<JournalDatabase>.Instance;
            if (jdb != null && jdb.journalEntriesDict.Count == 0) jdb.populateDict();
            string entry = jdb?.journalEntriesDict.Keys.FirstOrDefault(k => !journal.journalEntriesDict.ContainsKey(k));
            string note = jdb?.notesDict.Keys.FirstOrDefault(k => !journal.notesDict.ContainsKey(k));
            Plugin.Log.LogInfo($"[world test] journal: {journal.journalEntriesDict.Count} entries, {journal.notesDict.Count} notes; the game knows {jdb?.journalEntriesDict.Count} and {jdb?.notesDict.Count}");
            if (entry != null) { journal.addJournalEntry(entry); Plugin.Log.LogInfo($"[world test] journal entry {entry}"); }
            if (note != null) { journal.addNote(new Journal.Note { type = note }); Plugin.Log.LogInfo($"[world test] journal note {note}"); }
            yield return new WaitForSeconds(3f);

            // A trap going off.
            var trap = Object.FindObjectsOfType<Trigger>().Where(t => t.active && t.GetComponent<Item>() != null)
                .OrderBy(t => (t.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (trap != null)
            {
                Plugin.Log.LogInfo($"[world test] setting off trap {trap.name} at {trap.transform.position}");
                HarmonyLib.AccessTools.Method(typeof(Trigger), "OnAfterTrigger").Invoke(trap, new object[] { null, false });
            }
            else Plugin.Log.LogInfo("[world test] no trap found");
            yield return new WaitForSeconds(2f);

            // Something that switches (a lamp, a generator...).
            var sw = Object.FindObjectsOfType<Item>().Where(i => i.switchable).OrderBy(i => (i.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (sw != null)
            {
                Plugin.Log.LogInfo($"[world test] switching {sw.name} (on={sw.isOn}) off and on");
                sw.turnOff();
                yield return new WaitForSeconds(1f);
                sw.turnOn();
            }
            else Plugin.Log.LogInfo("[world test] nothing switchable found");
            yield return new WaitForSeconds(2f);

            // One at a time: the looted container again (the observer holds it by now).
            Plugin.Log.LogInfo($"[world test] opening {inv.name} again");
            player.openedItemInventory = inv;
            yield return new WaitForSeconds(1.5f);
            player.openedItemInventory = null;
            yield return new WaitForSeconds(2f);

            // Traders: a restock (the host's to do), and an NPC dying.
            var npcs = Object.FindObjectsOfType<NPC>().Where(n => n.GetComponent<Character>() != null && n.GetComponent<Character>().alive).ToList();
            Plugin.Log.LogInfo($"[world test] NPCs here: {string.Join(", ", npcs.Select(n => n.name + (n.trader ? " (trader)" : "")))}");
            var trader = npcs.FirstOrDefault(n => n.trader);
            if (trader != null)
            {
                Plugin.Log.LogInfo($"[world test] restocking {trader.name}");
                HarmonyLib.AccessTools.Method(typeof(NPC), "randomizeTraderInv").Invoke(trader, null);
                yield return new WaitForSeconds(2f);
            }
            var victim = npcs.FirstOrDefault(n => n != trader) ?? trader;
            if (victim != null)
            {
                Plugin.Log.LogInfo($"[world test] killing {victim.name}");
                victim.GetComponent<Character>().die();
                yield return new WaitForSeconds(4f);
                Plugin.Log.LogInfo($"[world test] {victim.name} dead in the flags: {Singleton<Flags>.Instance.getNPCState(victim.name)?.dead}");
            }
            // Dragging something: the player takes hold of it (itemBeingDragged, as Item.startDragging sets) and it moves.
            var drag = Object.FindObjectsOfType<Item>().Where(i => i.draggable && !i.isLight && !i.hasInventory && i.GetComponent<Rigidbody>() != null)
                .OrderBy(i => (i.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (drag != null)
            {
                var start = drag.transform.position;
                Plugin.Log.LogInfo($"[world test] dragging {drag.name} from {start}");
                player.itemBeingDragged = drag;
                for (float t = 0f; t < 2f; t += Time.deltaTime)
                {
                    drag.transform.position = start + new Vector3(30f * t, 0f, 0f);
                    yield return null;
                }
                player.itemBeingDragged = null;
                Plugin.Log.LogInfo($"[world test] let go of {drag.name} at {drag.transform.position}");
                yield return new WaitForSeconds(2.5f);
            }
            else Plugin.Log.LogInfo("[world test] nothing draggable found");

            // A molotov thrown a little way off: it flies, lands and burns, in every game.
            var db = Singleton<ItemsDatabase>.Instance;
            string molotov = db?.itemsDict.Keys.FirstOrDefault(k => k.ToLowerInvariant().Contains("molotov"));
            var mi = molotov != null ? db.getItem(molotov, instantiate: false) : null;
            if (mi != null && mi.item != null)
            {
                var from = player.transform.position;
                var go = Core.AddPrefab(mi.item, from, Quaternion.Euler(90f, 0f, 0f), null);
                var ti = go.GetComponent<ThrownItem>();
                var rb = go.GetComponent<Rigidbody>();
                if (ti != null && rb != null)
                {
                    var dir = new Vector3(0f, 0f, -1f);
                    ti.landTarget = from + dir * 300f;
                    rb.drag = 2f;
                    rb.velocity = dir * 300f * 2.5f;
                    ti.thrown = true;
                    ti.objectThatSpawnedMe = player.transform;
                    ti.setFallSpeed(300f);
                    var r = go.GetComponent<Renderer>(); if (r != null) r.enabled = true;
                    Plugin.Log.LogInfo($"[world test] throwing {molotov} south");
                    ThrowSync.Instance.Thrown(molotov, go, from);
                    yield return new WaitForSeconds(5f);
                }
                else Plugin.Log.LogInfo($"[world test] {molotov} isn't a thrown item");
            }
            else Plugin.Log.LogInfo("[world test] no molotov in the item database");

            // A door on fire: the host's world burns it, the others are told.
            var burnDoor = Object.FindObjectsOfType<Door>().Where(d => !d.destroyed && d.GetComponent<Burn>() == null)
                .OrderBy(d => (d.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (burnDoor != null)
            {
                Plugin.Log.LogInfo($"[world test] setting {burnDoor.name} on fire (health {burnDoor.health})");
                burnDoor.gameObject.AddComponent<Burn>().burnTime = 4f;
                yield return new WaitForSeconds(6f);
                Plugin.Log.LogInfo($"[world test] {burnDoor.name} after burning: health {burnDoor.health}");
            }
            // Another player's look: sprites from an animation library this game hasn't loaded (a weapon or torch
            // our player never held) are still found, by loading that library.
            var loaded = new System.Collections.Generic.HashSet<string>(Resources.FindObjectsOfTypeAll<tk2dSpriteAnimation>().Select(l => l.name));
            foreach (var libName in new[] { "PlayerTorchAnims", "PlayerFlashlightAnims", "PlayerPitchforkAnims", "PlayerShotgunAnims" })
            {
                if (loaded.Contains(libName)) { Plugin.Log.LogInfo($"[world test] look: {libName} already loaded here"); continue; }
                // A sprite from a collection not loaded here, from a player whose body uses libName: that loads it.
                Sync.SpriteCollections.Get(Net.StableHash.Of("not loaded here"), libName);
                var lib = Resources.FindObjectsOfTypeAll<tk2dSpriteAnimation>().FirstOrDefault(l => l.name == libName);
                var colls = lib?.clips?.SelectMany(c => c.frames ?? new tk2dSpriteAnimationFrame[0]).Select(f => f.spriteCollection?.spriteCollectionName)
                    .Where(n => n != null).Distinct().ToList() ?? new System.Collections.Generic.List<string>();
                int inLib = colls.Count(n => Sync.SpriteCollections.Get(Net.StableHash.Of(n)) != null);
                Plugin.Log.LogInfo($"[world test] look: {libName} {(lib != null ? "loaded" : "NOT loaded")}; {inLib} of {colls.Count} of its sprite collections found");
            }
            // Objects named by where they stand (no save id) are still found when they lie a little off that spot,
            // as after physics settled differently in another game: nudge some, look each up by its old name.
            var draggable = Object.FindObjectsOfType<Item>().Where(i => i.draggable).ToList();
            Plugin.Log.LogInfo($"[world test] draggable things: {draggable.Count}, named by save id {draggable.Count(i => ObjRef.Of(i.gameObject).K == ObjRef.Kind.Saved)}, by place {draggable.Count(i => ObjRef.Of(i.gameObject).K == ObjRef.Kind.Placed)}; e.g. {string.Join(", ", draggable.Take(6).Select(i => i.name))}");
            // Save ids this game gave out after the joiner's snapshot aren't the joiner's: never named by save id,
            // and a Saved ref to one names nothing here.
            var saveables = Object.FindObjectsOfType<SaveableObject>();
            var late = saveables.Where(o => o.uniqueId >= SharedIds.SendLimit).ToList();
            int lateSaved = late.Count(o => ObjRef.Of(o.gameObject).K == ObjRef.Kind.Saved);
            int sharedSaved = saveables.Count(o => o.uniqueId > 0 && o.uniqueId < SharedIds.SendLimit && ObjRef.Of(o.gameObject).K == ObjRef.Kind.Saved);
            var lateHit = late.Count > 0 ? ObjRef.Resolve<SaveableObject>(new ObjRef { K = ObjRef.Kind.Saved, Id = late[0].uniqueId }) : null;
            bool lateOk = lateSaved == 0 && (late.Count == 0 || late[0].uniqueId < SharedIds.ResolveLimit || lateHit == null);
            Plugin.Log.LogInfo($"[world test] save ids: shared below {SharedIds.SendLimit} (resolved below {SharedIds.ResolveLimit}); {sharedSaved} named by save id, {late.Count} given out later, {lateSaved} of those named by save id: {(lateOk ? "ok" : "WRONG")}");
            var placed = Object.FindObjectsOfType<Item>().Where(i => ObjRef.Of(i.gameObject).K == ObjRef.Kind.Placed).Take(20).ToList();
            int found = 0;
            foreach (var pi in placed)
            {
                var name = ObjRef.Of(pi.gameObject);
                var was = pi.transform.position;
                pi.transform.position = was + new Vector3(12f, 0f, -9f);
                if (ObjRef.Resolve<Item>(name) == pi) found++;
                pi.transform.position = was;
            }
            Plugin.Log.LogInfo($"[world test] placed objects nudged 15 units: {found} of {placed.Count} found by their old names");
            // A generator: pour fuel into it as the player does, a little at a time, and switch it on.
            var gen = Object.FindObjectsOfType<Generator>().Where(g => !g.isSaw).OrderBy(g => (g.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (gen != null)
            {
                Plugin.Log.LogInfo($"[world test] generator {gen.name} at {gen.transform.position}: fuel {gen.fuel:0.0} of {gen.maxFuel}, on={gen.isOn}");
                if (gen.isOn) { gen.GetComponent<Item>()?.turnOff(); yield return new WaitForSeconds(1f); }
                for (int i = 0; i < 20; i++) { gen.addFuel(1f); yield return new WaitForSeconds(0.05f); }
                var genItem = gen.GetComponent<Item>();
                if (genItem != null) genItem.turnOn(); else gen.turnOn();
                Plugin.Log.LogInfo($"[world test] generator after pouring 20 and switching on: fuel {gen.fuel:0.0}, on={gen.isOn}");
                yield return new WaitForSeconds(2f);
            }
            else Plugin.Log.LogInfo("[world test] no generator found");
            // The workbench's storage, slot by slot: put something in and take it out again, as if the window were
            // open; the observer then changes it too (one change that stands, one that comes too late).
            var wb = Object.FindObjectsOfType<Workbench>().OrderBy(w => (w.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (wb != null && wb.normalInventory != null)
            {
                var storage = wb.normalInventory;
                // Stand at it, as a player using it does (the observer follows us; the host refuses changes from afar).
                var p = Player.Instance;
                var by = wb.transform.position + (p.transform.position - wb.transform.position).normalized * 120f;
                p.transform.position = new Vector3(by.x, p.transform.position.y, by.z);
                yield return new WaitForSeconds(2f);
                var free = storage.slots.FindIndex(sl => InvItemClass.isNull(sl.invItem));
                Plugin.Log.LogInfo($"[world test] workbench storage {storage.name}: {storage.slots.Count} slots, first free {free}, workbench level {Singleton<Controller>.Instance.workbenchLevel}");
                if (free >= 0)
                {
                    player.openedItemInventory2 = storage;
                    yield return new WaitForSeconds(0.6f);
                    storage.slots[free].inventory = storage;
                    storage.slots[free].createItem(item.type, 2);
                    yield return new WaitForSeconds(0.8f);
                    storage.slots[free].removeItem();
                    yield return new WaitForSeconds(0.8f);
                    player.openedItemInventory2 = null;
                    yield return new WaitForSeconds(6f); // the observer's turn
                    var got = storage.slots[free].invItem;
                    Plugin.Log.LogInfo($"[world test] storage slot {free} now: {(InvItemClass.isNull(got) ? "empty" : got.type + " x" + got.amount)}; workbench level {Singleton<Controller>.Instance.workbenchLevel}");

                    // Sort & Stack (the observer pressed it too, as a client, during its turn): two partial stacks of
                    // one item in two free slots apart, then the button's action. They must end as one stack.
                    string stackType = null;
                    var idb = Singleton<ItemsDatabase>.Instance;
                    if (idb != null)
                        foreach (var k in idb.itemsDict.Keys)
                        {
                            var b = idb.getItem(k, instantiate: false);
                            if (b != null && b.stackable && !b.hasAmmo && b.maxAmount >= 5) { stackType = k; break; }
                        }
                    var frees = new System.Collections.Generic.List<int>();
                    for (int i = 0; i < storage.slots.Count; i++) if (InvItemClass.isNull(storage.slots[i].invItem)) frees.Add(i);
                    if (stackType != null && frees.Count >= 2)
                    {
                        player.openedItemInventory2 = storage;
                        yield return new WaitForSeconds(0.6f);
                        int s1 = frees[0], s2 = frees[frees.Count - 1];
                        storage.slots[s1].inventory = storage; storage.slots[s1].createItem(stackType, 2);
                        storage.slots[s2].inventory = storage; storage.slots[s2].createItem(stackType, 1);
                        yield return new WaitForSeconds(0.8f);
                        int Filled() { int n = 0; foreach (var sl in storage.slots) if (!InvItemClass.isNull(sl.invItem)) n++; return n; }
                        int stacksBefore = storage.slots.Count(sl => !InvItemClass.isNull(sl.invItem) && sl.invItem.type == stackType), filledBefore = Filled();
                        SlotShare.Instance?.SortPressed(storage);
                        yield return new WaitForSeconds(0.8f);
                        var mine = storage.slots.Where(sl => !InvItemClass.isNull(sl.invItem) && sl.invItem.type == stackType).ToList();
                        int firstEmpty = storage.slots.FindIndex(sl => InvItemClass.isNull(sl.invItem));
                        bool packed = firstEmpty < 0 || storage.slots.Skip(firstEmpty).All(sl => InvItemClass.isNull(sl.invItem));
                        Plugin.Log.LogInfo($"[world test] sort & stack: {stackType} in {stacksBefore} stack(s) before, {mine.Count} after ({string.Join(", ", mine.Select(sl => "x" + sl.invItem.amount))}); filled slots {filledBefore} -> {Filled()}; items packed from slot 0: {packed}");
                        player.openedItemInventory2 = null;
                        yield return new WaitForSeconds(1f);
                    }
                    else Plugin.Log.LogInfo($"[world test] sort & stack: skipped (stackable type {stackType ?? "none"}, {frees.Count} free slots)");
                }
            }
            else Plugin.Log.LogInfo("[world test] no workbench here");

            // Locks: a locked door or chest opened with a lockpick, as the player does it (the lockpick is used up).
            var lck = Object.FindObjectsOfType<Locked>().Where(l => l.locked && (l.GetComponent<Door>() != null || l.GetComponent<Item>() != null) && ObjRef.Of(l.gameObject).Valid)
                .OrderBy(l => (l.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (lck == null)
            {
                var near = Object.FindObjectsOfType<Door>().Where(d => !d.destroyed && ObjRef.Of(d.gameObject).Valid).OrderBy(d => (d.transform.position - at).sqrMagnitude).FirstOrDefault();
                if (near != null) { near.lockMe(""); lck = near.GetComponent<Locked>(); Plugin.Log.LogInfo($"[world test] nothing locked found: locked {near.name}"); yield return new WaitForSeconds(1f); }
            }
            if (lck != null)
            {
                Plugin.Log.LogInfo($"[world test] lock on {lck.name} {ObjRef.Of(lck.gameObject)} at {lck.transform.position} ({(lck.GetComponent<Door>() != null ? "door" : "item")}, key {lck.keyType})");
                player.Inventory.addItemTypeToPlayer("lockpick", 1);
                yield return null;
                lck.tryToLockpick();
                Plugin.Log.LogInfo($"[world test] after the lockpick: locked={lck.locked}");
                yield return new WaitForSeconds(2f);
            }
            // Other locks, for the authority test: where they are and how they're named.
            foreach (var l in Object.FindObjectsOfType<Locked>().Where(l => l.locked && ObjRef.Of(l.gameObject).Valid).OrderBy(l => (l.transform.position - at).sqrMagnitude).Take(4))
                Plugin.Log.LogInfo($"[world test] still locked: {l.name} {ObjRef.Of(l.gameObject)} at {l.transform.position}");

            // A combination padlock: open its window, set the code's numbers and check it, as the player does.
            var pad = Object.FindObjectsOfType<Padlock>().Where(pl => pl.locked && ObjRef.Of(pl.gameObject).Valid)
                .OrderBy(pl => (pl.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (pad != null && !string.IsNullOrEmpty(pad.combination))
            {
                Plugin.Log.LogInfo($"[world test] padlock {pad.name} {ObjRef.Of(pad.gameObject)} at {pad.transform.position}, code {pad.combination}");
                pad.activate();
                yield return new WaitForSecondsRealtime(0.5f);
                var numbers = Singleton<UI>.Instance.padlock != null ? Singleton<UI>.Instance.padlock.GetComponentsInChildren<PadlockNumber>() : new PadlockNumber[0];
                for (int i = 0; i < numbers.Length && i < pad.combination.Length; i++) numbers[i].number = pad.combination[i] - '0';
                pad.checkCombination();
                Plugin.Log.LogInfo($"[world test] after entering the code: locked={pad.locked}");
                yield return new WaitForSecondsRealtime(2f);
            }
            else Plugin.Log.LogInfo("[world test] no locked padlock found");

            // A locked crate picked open (the progress bar's success: Item.disarm, which opens it), then let go of again.
            var crate = Object.FindObjectsOfType<Item>().Where(i => WorldActions.Crate(i) != null && !i.searched && ObjRef.Of(i.gameObject).Valid)
                .OrderBy(i => (i.transform.position - at).sqrMagnitude).ToList();
            foreach (var c in crate.Skip(1).Take(3))
                Plugin.Log.LogInfo($"[world test] another locked crate: {c.name} {ObjRef.Of(c.gameObject)} at {c.transform.position}");
            if (crate.Count > 0)
            {
                var c = crate[0];
                Plugin.Log.LogInfo($"[world test] picking crate {c.name} {ObjRef.Of(c.gameObject)} at {c.transform.position} (chance {c.disarmChance})");
                c.disarm();
                Plugin.Log.LogInfo($"[world test] crate searched={c.searched}");
                yield return new WaitForSeconds(1.5f);
                player.openedItemInventory = null;
                yield return new WaitForSeconds(1.5f);
            }
            else Plugin.Log.LogInfo("[world test] no locked crate found");

            // Furniture smashed by our player: a hit that leaves it standing, then one that breaks it (one message
            // each, the second with no health left).
            at = player.transform.position;
            var furniture = Object.FindObjectsOfType<Item>()
                .Where(i => i.destructible && !i.destroyed && i.health > 1 && !i.hasInventory && i.shadowArmor == null && i.GetComponent<Explodes>() == null
                    && i.GetComponent<Trigger>() == null && i.GetComponent<ProxyItem>() == null && ObjRef.Of(i.gameObject).Valid)
                .OrderBy(i => (i.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (furniture == null)
            {
                // None in this save near us: place one as a player places a trap, so the others get it too (ObjSpawn).
                foreach (var prefab in new[] { "Furnitures/Cottage/chair_1", "Furnitures/Cottage/table_1", "Furnitures/Cottage/shelf", "Furnitures/Cottage/crate_movable_01", "Furnitures/Cottage/Wardrobe_Big_1" })
                {
                    GameObject made;
                    Containers.Placing = true;
                    try { made = Core.AddPrefab(prefab, at + new Vector3(250f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer, worldSpace: true); }
                    finally { Containers.Placing = false; }
                    yield return null; yield return null; // its health is set in Start
                    var it = made != null ? made.GetComponent<Item>() : null;
                    Plugin.Log.LogInfo($"[world test] placed {prefab}: {(it == null ? "no item" : $"destructible={it.destructible}, health {it.health} of {it.maxHealth}")}");
                    if (it != null && it.destructible && !it.destroyed && it.health > 1) { furniture = it; Plugin.Log.LogInfo($"[world test] none in this save: placed {prefab}"); break; }
                    if (made != null) Object.Destroy(made);
                }
                yield return new WaitForSeconds(1f); // its ObjSpawn goes out first
            }
            if (furniture != null)
            {
                Plugin.Log.LogInfo($"[world test] smashing {furniture.name} {ObjRef.Of(furniture.gameObject)} at {furniture.transform.position}: health {furniture.health}");
                furniture.getHit(furniture.health / 2, player.transform);
                yield return new WaitForSeconds(1f);
                furniture.getHit(furniture.health + 1, player.transform);
                Plugin.Log.LogInfo($"[world test] {furniture.name} after two hits: health {furniture.health}, broken={furniture.destroyed}");
                yield return new WaitForSeconds(2f);
                if (furniture != null && furniture.destroyed)
                {
                    furniture.setNormalState(); // as building it again does (Constructible.construct)
                    Plugin.Log.LogInfo($"[world test] {furniture.name} repaired: health {furniture.health}");
                    yield return new WaitForSeconds(2f);
                }
            }
            else Plugin.Log.LogInfo("[world test] no destructible furniture found");

            // An exploding barrel or gas bottle set off (as a shot does), far enough from our player not to hurt it.
            var explosives = Object.FindObjectsOfType<Explodes>().Where(e => e.GetComponent<ThrownItem>() == null && !BreakSync.Activated(e)).ToList();
            var barrel = explosives.Where(e => ObjRef.Of(e.gameObject).Valid && Vector3.Distance(e.transform.position, at) > e.radius + 100f)
                .OrderBy(e => (e.transform.position - at).sqrMagnitude).FirstOrDefault();
            Plugin.Log.LogInfo($"[world test] explosives here: {explosives.Count}");
            if (barrel == null)
            {
                GameObject go;
                Containers.Placing = true; // shared as a placed trap is
                try { go = Core.AddPrefab("Traps/barrelExploding", at + new Vector3(0f, 0f, 900f), Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer, worldSpace: true); }
                finally { Containers.Placing = false; }
                barrel = go != null ? go.GetComponentInChildren<Explodes>() : null;
                if (barrel != null) Plugin.Log.LogInfo("[world test] none in this save: placed one");
                yield return new WaitForSeconds(1f);
            }
            if (barrel != null)
            {
                Plugin.Log.LogInfo($"[world test] setting off {barrel.name} {ObjRef.Of(barrel.gameObject)} at {barrel.transform.position}");
                barrel.onActivate();
                yield return new WaitForSeconds(3f);
            }
            else Plugin.Log.LogInfo("[world test] no explosive found");
            yield return Scripted(at); // before ObjectsStep, which enables the feeder that Scripted builds
            yield return ObjectsStep(player, item);
            yield return WorldEventsStep(player);
            yield return HideoutsStep(player);
            Plugin.Log.LogInfo("[world test] done");
        }

        /// <summary>
        /// World.Hideouts: an oven lit by the host's player (OvenLit; the old one stays on, as the observer has it as
        /// theirs), the porter asked while at the hideout to take from (nothing emptied), and the porter's delivery
        /// from another hideout to this one (PorterDelivered).
        /// </summary>
        private static IEnumerator HideoutsStep(Player player)
        {
            var ovens = Object.FindObjectsOfType<ExperienceMachine>();
            foreach (var o in ovens)
            {
                var l = o.transform.GetLocation();
                Plugin.Log.LogInfo($"[world test] oven {o.name} {ObjRef.Of(o.gameObject)} on {o.isOn} at {(l != null ? l.name + " hideout " + l.hideoutId + " ward " + (l.shadowWard != null && l.shadowWard.activeSelf) : "no location")}{(o == player.experienceMachine ? " (ours)" : "")}");
            }
            var mine = player.experienceMachine;
            var other = ovens.FirstOrDefault(o => o != mine) ?? mine;
            if (other != null)
            {
                player.setExperienceMachine(other);
                var l = other.transform.GetLocation();
                Plugin.Log.LogInfo($"[world test] lit oven {other.name}: on {other.isOn}, ward {(l != null && l.shadowWard != null && l.shadowWard.activeSelf)}, ours {player.experienceMachine == other}; the old one {(mine != null ? mine.name + " on " + mine.isOn : "none")}");
                yield return new WaitForSeconds(1f);
                if (mine != null && mine != other) player.experienceMachine = mine;
            }
            else Plugin.Log.LogInfo("[world test] no oven here");

            var hideouts = Object.FindObjectsOfType<Location>().Where(l => l.hideoutId > 0 && !l.isSubLocation).OrderBy(l => l.hideoutId).ToList();
            player.whereAmI.checkWhereAmI();
            var here = player.whereAmI.bigLocation;
            Plugin.Log.LogInfo($"[world test] hideouts: {string.Join(", ", hideouts.Select(l => $"{l.name} #{l.hideoutId} ({l.inventoriesList.Count} containers, delivery {(l.porterDeliveryPosition != null)})"))}; we're at {(here != null ? here.name : "none")}");
            if (here != null && hideouts.Contains(here))
            {
                int before = ItemsIn(here);
                here.transportAllItemsToCurrentHideout();
                Plugin.Log.LogInfo($"[world test] porter asked at the hideout to take from: {before} items there before, {ItemsIn(here)} after");
            }
            var src = hideouts.FirstOrDefault(l => l != here);
            var dest = here != null && here.playerBase && here.hideoutId > 0 ? here : hideouts.FirstOrDefault(l => l != src);
            if (src != null && dest != null && dest.porterDeliveryPosition != null)
            {
                var chest = src.inventoriesList.Select(i => i != null ? i.GetComponent<Inventory>() : null).FirstOrDefault(i => i != null && !i.transform.isLocked());
                chest?.addItemType("bottle", 1);
                Plugin.Log.LogInfo($"[world test] porter from {src.name} ({ItemsIn(src)} items) to {dest.name}");
                Hideouts.Deliver(src, dest);
                Plugin.Log.LogInfo($"[world test] {src.name} has {ItemsIn(src)} items after");
                yield return new WaitForSeconds(1f);
            }
            else Plugin.Log.LogInfo("[world test] no two hideouts for the porter");
        }

        private static int ItemsIn(Location l) =>
            l.inventoriesList.Sum(i => i != null && i.GetComponent<Inventory>() != null && !i.transform.isLocked() ? i.GetComponent<Inventory>().getAllItems().Count : 0);

        private static string Parts(GameObject go) =>
            go == null ? "none" : string.Join(",", go.GetComponents<Component>().Select(c => c.GetType().Name));

        /// <summary>
        /// Smaller world objects (World.ObjectActions and friends): what some prefabs are made of; a dropped item
        /// picked up with a click; a gasoline trail poured and set alight; a note picked up off the ground; an
        /// InteractiveItem switched; a feeder used; a lure eaten; the saw cutting a log. Each that's here goes to
        /// the observer.
        /// </summary>
        private static IEnumerator ObjectsStep(Player player, InvItemClass item)
        {
            var at = player.transform.position;
            foreach (var name in new[] { "Items/GasolineTrail", "Items/Gasoline", "Items/DroppedItem", "Objects/_Unique/deathDrop" })
            {
                var pf = Resources.Load("Prefabs/" + name) as GameObject;
                var pinv = pf != null ? pf.GetComponent<Inventory>() : null;
                var pitem = pf != null ? pf.GetComponent<Item>() : null;
                Plugin.Log.LogInfo($"[world test] prefab {name}: {Parts(pf)}; removeWhenEmpty={(pinv != null ? pinv.removeWhenEmpty.ToString() : "-")}, isDroppedItem={(pitem != null ? pitem.isDroppedItem.ToString() : "-")}");
            }
            var db = Singleton<ItemsDatabase>.Instance;
            if (db != null)
            {
                var drops = new System.Collections.Generic.List<string>(); var lures = new System.Collections.Generic.List<string>();
                foreach (var k in db.itemsDict.Keys.ToList())
                {
                    InvItem it = null;
                    try { it = db.getItem(k, instantiate: false); } catch { }
                    if (it == null) continue;
                    if (it.dropOnReleaseAim) drops.Add(k);
                    if (it.item is GameObject ig && ig.GetComponent<Lure>() != null) lures.Add(k + (it.recoverableAfterThrown ? " (recoverable)" : ""));
                }
                Plugin.Log.LogInfo($"[world test] items dropped on releasing aim: {string.Join(", ", drops)}; lures: {string.Join(", ", lures)}");
            }

            // A dropped item picked up again with a click (Item.getDroppedItem).
            var dropped = player.spawnDroppedInvItem(new InvItemClass(item));
            yield return new WaitForSeconds(2.5f); // shared once at rest
            var di = dropped != null ? dropped.GetComponent<Item>() : null;
            if (di != null)
            {
                Plugin.Log.LogInfo($"[world test] clicking dropped {item.type} {ObjRef.Of(di.gameObject)}");
                di.getDroppedItem();
                yield return new WaitForSeconds(1f);
                Plugin.Log.LogInfo($"[world test] dropped item after the click: {(dropped == null ? "gone" : "still there")}");
            }

            // A gasoline trail, poured as Player.waitToSpillLiquid does, then set alight.
            var trail = Core.AddPrefab(Containers.GasolineTrail, at + player.transform.up * 40f, Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer);
            Core.addToSaveable(trail, isDynamic: true);
            yield return new WaitForSeconds(1f);
            if (trail != null)
            {
                Plugin.Log.LogInfo($"[world test] gasoline trail {ObjRef.Of(trail)}: {Parts(trail)}");
                var liquid = trail.GetComponent<Liquid>();
                if (liquid != null) liquid.startBurning(); // as a match, a torch or a molotov lights it
                Plugin.Log.LogInfo($"[world test] gasoline trail lit: burning={(liquid != null && liquid.burning)}");
                yield return new WaitForSeconds(1f);
            }

            // A note lying in the world, picked up.
            var noteItem = Object.FindObjectsOfType<JournalNoteReference>()
                .Where(n => !n.dontDestroy && n.GetComponent<Item>() != null && n.gameObject.scene.IsValid() && ObjRef.Of(n.gameObject).Valid)
                .OrderBy(n => (n.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (noteItem != null)
            {
                Plugin.Log.LogInfo($"[world test] picking up note {noteItem.name} {ObjRef.Of(noteItem.gameObject)} at {noteItem.transform.position}");
                noteItem.GetComponent<Item>().activate();
                yield return new WaitForSeconds(0.5f);
                Singleton<UI>.Instance.journal.hideNote();
                yield return new WaitForSeconds(1f);
            }
            else Plugin.Log.LogInfo("[world test] no note lying in the world");

            // An InteractiveItem's switch, there and back.
            var ii = Object.FindObjectsOfType<InteractiveItem>().Where(x => ObjRef.Of(x.gameObject).Valid)
                .OrderBy(x => (x.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (ii != null)
            {
                var ui = Singleton<UI>.Instance.transform.Find("InteractiveItem");
                Plugin.Log.LogInfo($"[world test] switching {ii.name} {ObjRef.Of(ii.gameObject)}, on={ii.isOn}");
                ii.switchMe();
                if (ui != null) ui.gameObject.SetActive(false);
                yield return new WaitForSeconds(1f);
                Plugin.Log.LogInfo($"[world test] switched {ii.name}: on={ii.isOn}");
            }
            else Plugin.Log.LogInfo("[world test] no InteractiveItem here");

            // A feeder.
            var feeder = Object.FindObjectsOfType<Feeder>().OrderBy(f => (f.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (feeder != null)
            {
                Plugin.Log.LogInfo($"[world test] feeder {feeder.name} {ObjRef.Of(feeder.gameObject)}: enabled={feeder.isEnabled}, full={feeder.Active}");
                if (!feeder.isEnabled) feeder.enable(); // as building it does (Scripted builds it first, shared as Construct)
                feeder.makeActive(); feeder.activate();
                yield return new WaitForSeconds(1f);
            }
            else Plugin.Log.LogInfo("[world test] no feeder here");

            // A lure eaten up (as the host's animals do, Lure.removeHealth).
            if (db != null)
            {
                InvItem lureItem = null;
                foreach (var k in db.itemsDict.Keys.ToList())
                {
                    InvItem it = null;
                    try { it = db.getItem(k, instantiate: false); } catch { }
                    if (it != null && it.item is GameObject ig && ig.GetComponent<Lure>() != null) { lureItem = it; break; }
                }
                if (lureItem != null)
                {
                    var lureGo = Core.AddPrefab(lureItem.item, at + new Vector3(60f, 0f, 60f), Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer, worldSpace: true);
                    var lure = lureGo != null ? lureGo.GetComponent<Lure>() : null;
                    if (lure != null)
                    {
                        yield return new WaitForSeconds(0.5f);
                        Plugin.Log.LogInfo($"[world test] lure {lureGo.name} {ObjRef.Of(lureGo)} eaten");
                        lure.removeHealth(lure.health + 1, null);
                        yield return new WaitForSeconds(1f);
                    }
                }
            }

            // The saw cutting a log.
            var saw = Object.FindObjectsOfType<Saw>().OrderBy(x => (x.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (saw != null && saw.GetComponent<Inventory>() != null)
            {
                var sinv = saw.GetComponent<Inventory>();
                saw.fuel = Mathf.Max(saw.fuel, saw.fuelToConvert + 5f);
                if (InvItemClass.isNull(sinv.getItem("woodLog"))) sinv.addItemType("woodLog", 1);
                float before = saw.fuel;
                if (saw.canConvert())
                {
                    try { saw.convert(); }
                    catch (System.NullReferenceException) { } // Saw.refresh wants the saw's window open; the fuel is taken before it
                }
                Plugin.Log.LogInfo($"[world test] saw {saw.name} {ObjRef.Of(saw.gameObject)}: fuel {before:0.0} -> {saw.fuel:0.0}");
                yield return new WaitForSeconds(1f);
            }
            else Plugin.Log.LogInfo("[world test] no saw here");
        }

        /// <summary>
        /// The world's own rolls, as the host makes them: a new day's weather, the night scenario and one of its
        /// events, the dawn's random objects, and the shadows' lights at this hideout (if it has a generator).
        /// Each goes to the observer (NightScenario, NightEvent, RandomObjects, Weather).
        /// </summary>
        private static IEnumerator WorldEventsStep(Player player)
        {
            var rain = Singleton<Rain>.Instance;
            if (rain != null)
            {
                rain.setUpNextRain();
                rain.onNewDay();
                Plugin.Log.LogInfo($"[world test] weather rolled: rain {rain.rainToday} at {rain.timeToStart:0} for {rain.duration:0}");
            }
            var ns = Singleton<NightScenarios>.Instance;
            if (ns != null)
            {
                if (ns.currentScenario == null) ns.setCurrentScenario();
                var sc = ns.currentScenario;
                Plugin.Log.LogInfo($"[world test] night scenario {(sc != null ? sc.name : "none")}");
                var ev = sc?.customEventAndInts.Select(e => e?.customEvent).FirstOrDefault(e => e != null && e.theEvent != null && e.theEvent.type == RandomEvent.Type.locationEvent);
                var big = player.whereAmI.bigLocation;
                if (ev != null && big != null)
                {
                    ev.fire(force: true);
                    Plugin.Log.LogInfo($"[world test] night event {ev.name} fired at {big.name}");
                }
                else Plugin.Log.LogInfo($"[world test] no night event fired (event {(ev != null ? ev.name : "none")}, location {(big != null ? big.name : "none")})");
            }
            var gen = Singleton<WorldGenerator>.Instance;
            if (gen != null)
            {
                Singleton<RandomWorldObjects>.Instance?.resetObjects();
                gen.despawnRandomObjects();
                gen.spawnRandomObjects();
                var rwo = Singleton<RandomWorldObjects>.Instance;
                int made = gen.worldChunks.Sum(c => c != null ? c.GetComponentsInChildren<SaveableObject>().Count(o => rwo != null && rwo.objects.Any(r => r?.prefab != null && r.prefab.name == o.name)) : 0);
                Plugin.Log.LogInfo($"[world test] random objects respawned in {gen.worldChunks.Count} chunks: {(rwo != null ? rwo.objects.Count : -1)} kinds, {made} in the chunks now");
            }
            bool lights = WorldEvents.Instance != null && WorldEvents.Instance.HostShadowLights(player, anyTime: true);
            Plugin.Log.LogInfo($"[world test] shadow lights {(lights ? "sent" : "not here (no generator with lamps)")}");
            yield return new WaitForSeconds(4f); // the random objects go 2 seconds after they're made; the weather within a second
        }

        /// <summary>Triggers a player walks into or uses, near pos, that haven't fired and whose events have no steps of the player's own.</summary>
        private static System.Collections.Generic.List<(EventTriggers et, int i)> AreaTriggers(Vector3 pos) =>
            Object.FindObjectsOfType<EventTriggers>()
                .SelectMany(et => et.eventTriggers.Select((t, i) => (et, t, i)))
                .Where(x => x.t != null && (x.t.type == global::EventTrigger.Type.area || x.t.type == global::EventTrigger.Type.onActivate || x.t.type == global::EventTrigger.Type.onExamine) && !x.t.fired && !x.t.disabled && x.t.requirementsMet()
                    && x.t.gameEvents != null && !x.t.gameEvents.fired && x.t.gameEvents.events.Count > 0
                    && x.t.gameEvents.events.All(e => e != null && !ScriptedEvents.IsPlayerStep(e.type)) && ObjRef.Of(x.et.gameObject).Valid)
                .OrderBy(x => (x.et.transform.position - pos).sqrMagnitude)
                .Select(x => (x.et, x.i)).ToList();

        private static string Steps(GameEvents ge) => string.Join(",", ge.events.Where(e => e != null).Select(e => e.type.ToString()));

        /// <summary>
        /// Scripted events, building and dialogue: what's near (for the authority test), then a trigger walked into,
        /// a world event and a build, each as our own player does it.
        /// </summary>
        private static IEnumerator Scripted(Vector3 at)
        {
            var triggers = AreaTriggers(at);
            foreach (var (et, i) in triggers.Take(3))
                Plugin.Log.LogInfo($"[world test] trigger {et.eventTriggers[i].type} {et.name} {ObjRef.Of(et.gameObject)} #{i} at {et.transform.position}: {Steps(et.eventTriggers[i].gameEvents)}");
            var cons = Object.FindObjectsOfType<Constructible>()
                .Where(c => c.options.Count > 0 && !c.constructed && !c.onlyWhenDestroyed && ObjRef.Of(c.gameObject).Valid)
                .OrderBy(c => (c.transform.position - at).sqrMagnitude).ToList();
            foreach (var c in cons.Take(3))
                Plugin.Log.LogInfo($"[world test] buildable {c.name} {ObjRef.Of(c.gameObject)} at {c.transform.position}: {c.options.Count} options");
            foreach (var n in Object.FindObjectsOfType<NPC>().Where(n => n.characterDialogue != null && ObjRef.Of(n.gameObject).Valid)
                         .OrderBy(n => (n.transform.position - at).sqrMagnitude).Take(3))
                Plugin.Log.LogInfo($"[world test] NPC with dialogue {n.name} {ObjRef.Of(n.gameObject)} at {n.transform.position}");

            if (triggers.Count > 0)
            {
                var (et, i) = triggers[0];
                var t = et.eventTriggers[i];
                Plugin.Log.LogInfo($"[world test] setting off {et.name} #{i} ({t.type})");
                WorldActions.TestAsPlayer = true;
                try { et.fireEventTrigger(et.eventTriggers[i].type); }
                finally { WorldActions.TestAsPlayer = false; }
                yield return new WaitForSeconds(t.delay + t.warmupTime + 1.5f);
                Plugin.Log.LogInfo($"[world test] #{i} fired={t.fired}{(et == null ? ", and its object is gone (the event removed it)" : "")}");
            }
            else Plugin.Log.LogInfo("[world test] no trigger found");

            var events = Singleton<Events>.Instance;
            var we = events.worldEvents.Where(kv => kv.Value != null && !kv.Value.fired && kv.Value.events.Count > 0
                    && kv.Value.events.All(e => e != null && !ScriptedEvents.IsPlayerStep(e.type) && e.type != GameEvent.Type.spawnCharacter))
                .OrderBy(kv => kv.Value.events.Count).Select(kv => kv.Key).FirstOrDefault();
            if (we != null)
            {
                Plugin.Log.LogInfo($"[world test] world event {we}: {Steps(events.worldEvents[we])}");
                WorldActions.TestAsPlayer = true;
                try { events.fireWorldEvent(we); }
                finally { WorldActions.TestAsPlayer = false; }
                yield return new WaitForSeconds(1.5f);
            }
            else Plugin.Log.LogInfo("[world test] no world event to fire");

            if (cons.Count > 0)
            {
                var c = cons[0];
                Plugin.Log.LogInfo($"[world test] building {c.name} {ObjRef.Of(c.gameObject)} option 0");
                WorldActions.TestAsPlayer = true;
                try { c.construct(false, 0); }
                finally { WorldActions.TestAsPlayer = false; }
                yield return new WaitForSeconds(1.5f);
            }
            else Plugin.Log.LogInfo("[world test] nothing to build");

            // A trigger the world sets off by itself (at a time of day, or something dying), not our player: the host
            // runs it and sends it as kind 3. One whose events have no steps of a player's own and spawn nothing.
            var worldTriggers = Object.FindObjectsOfType<EventTriggers>(true)
                .SelectMany(et => et.eventTriggers.Select((t, i) => (et, t, i)))
                .Where(x => x.t != null && (x.t.type == global::EventTrigger.Type.onTime || x.t.type == global::EventTrigger.Type.onDeath
                    || x.t.type == global::EventTrigger.Type.refreshWhenTurnedOn)).ToList();
            Plugin.Log.LogInfo($"[world test] world triggers (time, death, turned on): {worldTriggers.Count}, not fired {worldTriggers.Count(x => !x.t.fired)}");
            foreach (var x in worldTriggers.OrderBy(x => (x.et.transform.position - at).sqrMagnitude).Take(4))
                Plugin.Log.LogInfo($"[world test] world trigger {x.t.type} {x.et.name} {ObjRef.Of(x.et.gameObject)} #{x.i} active={x.et.gameObject.activeInHierarchy} disabled={x.t.disabled} events: {(x.t.gameEvents != null ? Steps(x.t.gameEvents) + (x.t.gameEvents.fired ? " (fired)" : "") : "none")}");
            var wt = worldTriggers
                .Where(x => !x.t.fired && !x.t.disabled && x.t.gameEvents != null && !x.t.gameEvents.fired && x.t.gameEvents.events.Count > 0
                    && x.t.gameEvents.events.All(e => e != null && (!ScriptedEvents.IsPlayerStep(e.type) || e.type == GameEvent.Type.displayMessage)
                        && e.type != GameEvent.Type.spawnCharacter)
                    && ObjRef.Of(x.et.gameObject).K == ObjRef.Kind.Saved)
                .OrderBy(x => (x.et.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (wt.et != null)
            {
                Plugin.Log.LogInfo($"[world test] the world sets off {wt.t.type} {wt.et.name} {ObjRef.Of(wt.et.gameObject)} #{wt.i}: {Steps(wt.t.gameEvents)}");
                // Started as fireEventTrigger would, also for one whose object is culled (inactive) here.
                try { Singleton<Controller>.Instance.StartCoroutine(wt.t.fire(wt.t.type, "", wt.et)); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"[world test] {e.Message}"); }
                yield return new WaitForSeconds(wt.t.delay + 1.5f);
                Plugin.Log.LogInfo($"[world test] world trigger #{wt.i} fired={wt.t.fired}");
            }
            else Plugin.Log.LogInfo("[world test] no world trigger to set off");
        }

        /// <summary>
        /// "--dwmp-test-client-actions" (a client): our player uses up a rope and opens (or closes) the nearest door,
        /// as one action; the scripted host refuses it. The door should go back as the host has it, and the rope
        /// come back.
        /// </summary>
        public static IEnumerator ClientActions()
        {
            while (!WorldShare.WorldReady || !Combat.NetEnemies.ClientMode) yield return null;
            yield return new WaitForSeconds(3f);
            var p = Player.Instance;
            Door door = null; float best = 2500f;
            foreach (var d in Object.FindObjectsOfType<Door>())
            {
                float dist = Vector3.Distance(d.transform.position, p.transform.position);
                if (dist < best && !d.destroyed && ObjRef.Of(d.gameObject).Valid) { best = dist; door = d; }
            }
            if (door == null) { Plugin.Log.LogInfo("[client test] no door near"); yield break; }
            p.Inventory.addItemTypeToPlayer("rope", 2);
            yield return null; yield return null;
            int Ropes() { int n = 0; foreach (var it in p.Inventory.getAllItems()) if (it.type == "rope") n += it.amount; foreach (var sl in p.Hotbar.slots) if (!InvItemClass.isNull(sl.invItem) && sl.invItem.type == "rope") n += sl.invItem.amount; return n; }
            bool wasOpen = door.opened;
            Plugin.Log.LogInfo($"[client test] {door.name} {ObjRef.Of(door.gameObject)} {best:0} away, open={wasOpen}, ropes={Ropes()}");
            WorldActions.TestAsPlayer = true;
            try
            {
                p.Inventory.removeItemAmountFromPlayer("rope", 1);
                if (wasOpen) door.close(null); else door.open(p.transform.position, p.transform, 0f);
            }
            finally { WorldActions.TestAsPlayer = false; }
            Plugin.Log.LogInfo($"[client test] did it: open={door.opened}, ropes={Ropes()}");
            yield return new WaitForSeconds(4f);
            Plugin.Log.LogInfo($"[client test] after the host's answer: open={door.opened} (was {wasOpen}), ropes={Ropes()}, rolled back {Authority.RolledBack}");

            // A trigger our player walks into: our part and the shared part run here, and the host is asked.
            var triggers = AreaTriggers(p.transform.position);
            if (triggers.Count > 0)
            {
                var (et, i) = triggers[0];
                Plugin.Log.LogInfo($"[client test] setting off {et.eventTriggers[i].type} {et.name} {ObjRef.Of(et.gameObject)} #{i}: {Steps(et.eventTriggers[i].gameEvents)}");
                WorldActions.TestAsPlayer = true;
                try { et.fireEventTrigger(et.eventTriggers[i].type); }
                finally { WorldActions.TestAsPlayer = false; }
                yield return new WaitForSeconds(et.eventTriggers[i].delay + et.eventTriggers[i].warmupTime + 2f);
                Plugin.Log.LogInfo($"[client test] #{i} fired={et.eventTriggers[i].fired}{(et == null ? ", and its object is gone (the event removed it)" : "")}");
            }
            else Plugin.Log.LogInfo("[client test] no trigger near");

            // Talking to an NPC someone else talks to: the scripted host refuses it, and the dialogue closes again.
            var npc = Object.FindObjectsOfType<NPC>().Where(n => n.characterDialogue != null && n.wantsToTalk && ObjRef.Of(n.gameObject).Valid)
                .OrderBy(n => (n.transform.position - p.transform.position).sqrMagnitude).FirstOrDefault();
            if (npc != null)
            {
                Plugin.Log.LogInfo($"[client test] talking to {npc.name} {ObjRef.Of(npc.gameObject)}");
                npc.talkTo();
                var win = Singleton<UI>.Instance.dialogueWindow;
                Plugin.Log.LogInfo($"[client test] dialogue open with {(win.npc != null ? win.npc.name : "nobody")}");
                yield return new WaitForSecondsRealtime(8f);
                Plugin.Log.LogInfo($"[client test] after the host's answer: dialogue {(win.npc != null ? "still open with " + win.npc.name : "closed")}");
            }
            else Plugin.Log.LogInfo("[client test] no NPC to talk to");

        }
    }
}
