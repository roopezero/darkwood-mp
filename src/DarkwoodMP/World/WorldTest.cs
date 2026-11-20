using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// "--dwmp-test-world": once in a world, the host's character does things by itself, so the shared world can be
    /// tested without anyone at the keyboard: a door, looting and dropping, a story flag and reputation, the journal,
    /// a trap, a switch, traders and an NPC's death, dragging, a molotov, a burning door, sprites, object names,
    /// a generator, the workbench, locks, a padlock, a crate, furniture, an explosive, then the steps in Scripted,
    /// ObjectsStep, WorldEventsStep and HideoutsStep. "--dwmp-test-client-actions" is the client's side.
    /// </summary>
    public static class WorldTest
    {
        private static T Nearest<T>(IEnumerable<T> xs, Vector3 at) where T : Component =>
            xs.OrderBy(x => (x.transform.position - at).sqrMagnitude).FirstOrDefault();

        /// <summary>Runs a as our own player's action (WorldActions.TestAsPlayer).</summary>
        private static void AsPlayer(System.Action a)
        {
            WorldActions.TestAsPlayer = true;
            try
            {
                a();
            }
            finally
            {
                WorldActions.TestAsPlayer = false;
            }
        }

        /// <summary>Every item in the game's database, by key (one that fails to load is skipped).</summary>
        private static IEnumerable<(string key, InvItem it)> DbItems()
        {
            var db = Singleton<ItemsDatabase>.Instance;
            if (db == null)
            {
                yield break;
            }
            foreach (var k in db.itemsDict.Keys.ToList())
            {
                InvItem it = null;
                try
                {
                    it = db.getItem(k, instantiate: false);
                }
                catch
                {
                }
                if (it != null)
                {
                    yield return (k, it);
                }
            }
        }

        /// <summary>A prefab placed as a player places a trap, so the others get it too (ObjSpawn).</summary>
        private static GameObject PlaceShared(string prefab, Vector3 at)
        {
            Containers.Placing = true;
            try
            {
                return Core.AddPrefab(prefab, at, Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer, worldSpace: true);
            }
            finally
            {
                Containers.Placing = false;
            }
        }

        /// <summary>Logs the items that place something when used (InvItem.placeOnUse), which nothing shares.</summary>
        private static void LogPlaceOnUse()
        {
            if (Singleton<ItemsDatabase>.Instance == null)
            {
                return;
            }
            var found = DbItems().Where(x => x.it.placeOnUse).Select(x =>
            {
                string placed = "nothing";
                if (x.it.item != null)
                {
                    placed = x.it.item.name;
                }
                return $"{x.key} -> {placed}";
            }).ToList();
            string foundList = "";
            if (found.Count > 0)
            {
                foundList = ": " + string.Join(", ", found);
            }
            Plugin.Log.LogInfo($"[world test] items placed on use: {found.Count}" + foundList);
        }

        public static IEnumerator Run()
        {
            while (!WorldShare.InWorld)
            {
                yield return null;
            }
            // The test kills an NPC, burns a door, flips story flags and more: only ever on a copy of the saves.
            if (!SaveRedirect.Active)
            {
                Plugin.Log.LogError("[world test] FAIL: not in a sandbox or the test world (add --dwmp-sandbox DIR)");
                yield break;
            }
            while (Net.Session.Instance == null || Net.Session.Instance.Peers.Count == 0)
            {
                yield return null;
            }
            yield return new WaitForSeconds(5f);
            var player = Player.Instance;
            var at = player.transform.position;
            LogPlaceOnUse();

            var door = Nearest(Object.FindObjectsOfType<Door>().Where(d => !d.destroyed), at);
            if (door != null)
            {
                Plugin.Log.LogInfo($"[world test] door {door.name} at {door.transform.position}, opened={door.opened}");
                if (door.opened)
                {
                    door.close(player.transform);
                }
                else
                {
                    door.open(at, player.transform);
                }
                yield return new WaitForSeconds(3f);
                if (door.opened)
                {
                    door.close(player.transform);
                }
                else
                {
                    door.open(at, player.transform);
                }
                Plugin.Log.LogInfo($"[world test] door toggled twice, opened={door.opened}");
            }
            yield return new WaitForSeconds(2f);

            var inv = Nearest(Object.FindObjectsOfType<Inventory>()
                .Where(i => i.invType == Inventory.InvType.itemInv && !i.isWorkbench && i.slots.Any(s => !InvItemClass.isNull(s.invItem))), at);
            if (inv == null)
            {
                Plugin.Log.LogInfo("[world test] no container with items found");
                yield break;
            }
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
            string name2 = null;
            if (dropped != null)
            {
                name2 = dropped.name;
            }
            Plugin.Log.LogInfo($"[world test] dropped {item.type} on the ground: {name2}");
            yield return new WaitForSeconds(3f);

            // The story: flip a flag, and an NPC's reputation.
            var flags = Singleton<Flags>.Instance;
            var dict = HarmonyLib.AccessTools.FieldRefAccess<Flags, Dictionary<string, Flags.Flag>>("flagsDict")(flags);
            var flag = dict.Values.FirstOrDefault(f => f != null);
            if (flag != null)
            {
                flags.setFlag(flag.name, !flag.isTrue);
                Plugin.Log.LogInfo($"[world test] flag {flag.name} -> {flag.isTrue}");
            }
            var npc = flags.npcStates.FirstOrDefault(n => n != null);
            if (npc != null)
            {
                npc.reputation += 1;
                Plugin.Log.LogInfo($"[world test] {npc.name}'s reputation -> {npc.reputation}");
            }
            // The journal: a story entry and a note the host hasn't got yet, as the game adds them.
            var journal = Singleton<UI>.Instance.journal;
            var jdb = Singleton<JournalDatabase>.Instance;
            if (jdb != null && jdb.journalEntriesDict.Count == 0)
            {
                jdb.populateDict();
            }
            string entry = null;
            if (jdb != null)
            {
                entry = jdb.journalEntriesDict.Keys.FirstOrDefault(k => !journal.journalEntriesDict.ContainsKey(k));
            }
            string note = null;
            if (jdb != null)
            {
                note = jdb.notesDict.Keys.FirstOrDefault(k => !journal.notesDict.ContainsKey(k));
            }
            int? count = null;
            if (jdb != null)
            {
                count = jdb.journalEntriesDict.Count;
            }
            int? count2 = null;
            if (jdb != null)
            {
                count2 = jdb.notesDict.Count;
            }
            Plugin.Log.LogInfo($"[world test] journal: {journal.journalEntriesDict.Count} entries, {journal.notesDict.Count} notes; the game knows {count} and {count2}");
            if (entry != null)
            {
                journal.addJournalEntry(entry);
                Plugin.Log.LogInfo($"[world test] journal entry {entry}");
            }
            if (note != null)
            {
                journal.addNote(new Journal.Note { type = note });
                Plugin.Log.LogInfo($"[world test] journal note {note}");
            }
            yield return new WaitForSeconds(3f);

            // A trap going off.
            var trap = Nearest(Object.FindObjectsOfType<Trigger>().Where(t => t.active && t.GetComponent<Item>() != null), at);
            if (trap != null)
            {
                Plugin.Log.LogInfo($"[world test] setting off trap {trap.name} at {trap.transform.position}");
                HarmonyLib.AccessTools.Method(typeof(Trigger), "OnAfterTrigger").Invoke(trap, new object[] { null, false });
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no trap found");
            }
            yield return new WaitForSeconds(2f);

            // Something that switches (a lamp, a generator...).
            var sw = Nearest(Object.FindObjectsOfType<Item>().Where(i => i.switchable), at);
            if (sw != null)
            {
                Plugin.Log.LogInfo($"[world test] switching {sw.name} (on={sw.isOn}) off and on");
                sw.turnOff();
                yield return new WaitForSeconds(1f);
                sw.turnOn();
            }
            else
            {
                Plugin.Log.LogInfo("[world test] nothing switchable found");
            }
            yield return new WaitForSeconds(2f);

            // One at a time: the looted container again (the observer holds it by now).
            Plugin.Log.LogInfo($"[world test] opening {inv.name} again");
            player.openedItemInventory = inv;
            yield return new WaitForSeconds(1.5f);
            player.openedItemInventory = null;
            yield return new WaitForSeconds(2f);

            // Traders: a restock (the host's to do), and an NPC dying.
            var npcs = Object.FindObjectsOfType<NPC>().Where(n => n.GetComponent<Character>() != null && n.GetComponent<Character>().alive).ToList();
            string names = string.Join(", ", npcs.Select(n =>
            {
                string npcName = n.name;
                string mark = "";
                if (n.trader)
                {
                    mark = " (trader)";
                }
                return npcName + mark;
            }));
            Plugin.Log.LogInfo($"[world test] NPCs here: {names}");
            var trader = npcs.FirstOrDefault(n => n.trader);
            if (trader != null)
            {
                Plugin.Log.LogInfo($"[world test] restocking {trader.name}");
                HarmonyLib.AccessTools.Method(typeof(NPC), "randomizeTraderInv").Invoke(trader, null);
                yield return new WaitForSeconds(2f);
            }
            NPC victim = npcs.FirstOrDefault(n => n != trader);
            if (victim == null)
            {
                victim = trader;
            }
            if (victim != null)
            {
                Plugin.Log.LogInfo($"[world test] killing {victim.name}");
                victim.GetComponent<Character>().die();
                yield return new WaitForSeconds(4f);
                string victimName = victim.name;
                Flags.NPCState state = Singleton<Flags>.Instance.getNPCState(victim.name);
                bool? dead = null;
                if (state != null)
                {
                    dead = state.dead;
                }
                Plugin.Log.LogInfo($"[world test] {victimName} dead in the flags: {dead}");
            }
            // Dragging something: the player takes hold of it (itemBeingDragged, as Item.startDragging sets) and it moves.
            var drag = Nearest(Object.FindObjectsOfType<Item>().Where(i => i.draggable && !i.isLight && !i.hasInventory && i.GetComponent<Rigidbody>() != null), at);
            if (drag != null)
            {
                var start = drag.transform.position;
                // First as if a client had taken hold of it already (the host agreed to its DragClaim): our player's
                // drag is let go of at once, and the game's own Item.startDragging doesn't start one.
                var ds = DragSync.Instance;
                if (ds != null)
                {
                    int cancelled = DragSync.Cancelled;
                    ds.TestOwner(drag.gameObject, 999);
                    int blocked = DragStartPatch.Blocked;
                    drag.startDragging();
                    bool stopped = DragStartPatch.Blocked > blocked && !drag.beingDragged && player.itemBeingDragged != drag;
                    player.itemBeingDragged = drag; // as a script takes hold of it
                    yield return null;
                    yield return null;
                    bool letGo = player.itemBeingDragged != drag && DragSync.Cancelled > cancelled;
                    string text = "started";
                    if (stopped)
                    {
                        text = "not started";
                    }
                    string text2 = "kept";
                    if (letGo)
                    {
                        text2 = "let go";
                    }
                    string text3 = "FAIL";
                    if (stopped && letGo)
                    {
                        text3 = "PASS";
                    }
                    Plugin.Log.LogInfo($"[world test] {drag.name} dragged by another player: the game's drag {text}, ours {text2}: {text3}");
                    if (player.itemBeingDragged == drag)
                    {
                        player.itemBeingDragged = null;
                    }
                    ds.TestOwner(drag.gameObject, 0);
                    yield return new WaitForSeconds(0.5f);
                    start = drag.transform.position;
                }
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
            else
            {
                Plugin.Log.LogInfo("[world test] nothing draggable found");
            }

            // A molotov thrown a little way off: it flies, lands and burns, in every game.
            var db = Singleton<ItemsDatabase>.Instance;
            string molotov = null;
            if (db != null)
            {
                molotov = db.itemsDict.Keys.FirstOrDefault(k => k.ToLowerInvariant().Contains("molotov"));
            }
            InvItem mi = null;
            if (molotov != null)
            {
                mi = db.getItem(molotov, instantiate: false);
            }
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
                    var r = go.GetComponent<Renderer>();
                    if (r != null)
                    {
                        r.enabled = true;
                    }
                    Plugin.Log.LogInfo($"[world test] throwing {molotov} south");
                    ThrowSync.Instance.Thrown(molotov, go, from);
                    yield return new WaitForSeconds(5f);
                }
                else
                {
                    Plugin.Log.LogInfo($"[world test] {molotov} isn't a thrown item");
                }
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no molotov in the item database");
            }

            // A door on fire: the host's world burns it, the others are told.
            var burnDoor = Nearest(Object.FindObjectsOfType<Door>().Where(d => !d.destroyed && d.GetComponent<Burn>() == null), at);
            if (burnDoor != null)
            {
                Plugin.Log.LogInfo($"[world test] setting {burnDoor.name} on fire (health {burnDoor.health})");
                burnDoor.gameObject.AddComponent<Burn>().burnTime = 4f;
                yield return new WaitForSeconds(6f);
                Plugin.Log.LogInfo($"[world test] {burnDoor.name} after burning: health {burnDoor.health}");
            }
            // Another player's look: sprites from an animation library this game hasn't loaded (a weapon or torch
            // our player never held) are still found, by loading that library.
            var loaded = new HashSet<string>(Resources.FindObjectsOfTypeAll<tk2dSpriteAnimation>().Select(l => l.name));
            foreach (var libName in new[] { "PlayerTorchAnims", "PlayerFlashlightAnims", "PlayerPitchforkAnims", "PlayerShotgunAnims" })
            {
                if (loaded.Contains(libName))
                {
                    Plugin.Log.LogInfo($"[world test] look: {libName} already loaded here");
                    continue;
                }
                // A sprite from a collection not loaded here, from a player whose body uses libName: that loads it.
                Sync.SpriteCollections.Get(Net.StableHash.Of("not loaded here"), libName);
                var lib = Resources.FindObjectsOfTypeAll<tk2dSpriteAnimation>().FirstOrDefault(l => l.name == libName);
                List<string> colls = null;
                if (lib != null)
                {
                    tk2dSpriteAnimationClip[] clips = lib.clips;
                    if (clips != null)
                    {
                        colls = clips.SelectMany(c =>
                        {
                            tk2dSpriteAnimationFrame[] frames = c.frames;
                            if (frames != null)
                            {
                                return frames;
                            }
                            return new tk2dSpriteAnimationFrame[0];
                        }).Select(f =>
                        {
                            tk2dSpriteCollectionData collection = f.spriteCollection;
                            if (collection != null)
                            {
                                return collection.spriteCollectionName;
                            }
                            return null;
                        }).Where(n => n != null).Distinct().ToList();
                    }
                }
                if (colls == null)
                {
                    colls = new List<string>();
                }
                int inLib = colls.Count(n => Sync.SpriteCollections.Get(Net.StableHash.Of(n)) != null);
                string text4 = "FAIL: not loaded";
                if (lib != null)
                {
                    text4 = "loaded";
                }
                Plugin.Log.LogInfo($"[world test] look: {libName} {text4}; {inLib} of {colls.Count} of its sprite collections found");
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
            SaveableObject lateHit = null;
            if (late.Count > 0)
            {
                lateHit = ObjRef.Resolve<SaveableObject>(new ObjRef { K = ObjRef.Kind.Saved, Id = late[0].uniqueId });
            }
            bool lateOk = lateSaved == 0 && (late.Count == 0 || late[0].uniqueId < SharedIds.ResolveLimit || lateHit == null);
            int sendLimit = SharedIds.SendLimit;
            int resolveLimit = SharedIds.ResolveLimit;
            string verdict = "FAIL";
            if (lateOk)
            {
                verdict = "ok";
            }
            Plugin.Log.LogInfo($"[world test] save ids: shared below {sendLimit} (resolved below {resolveLimit}); {sharedSaved} named by save id, {late.Count} given out later, {lateSaved} of those named by save id: {verdict}");
            var placed = Object.FindObjectsOfType<Item>().Where(i => ObjRef.Of(i.gameObject).K == ObjRef.Kind.Placed).Take(20).ToList();
            int found = 0;
            foreach (var pi in placed)
            {
                var name = ObjRef.Of(pi.gameObject);
                var was = pi.transform.position;
                pi.transform.position = was + new Vector3(12f, 0f, -9f);
                if (ObjRef.Resolve<Item>(name) == pi)
                {
                    found++;
                }
                pi.transform.position = was;
            }
            Plugin.Log.LogInfo($"[world test] placed objects nudged 15 units: {found} of {placed.Count} found by their old names");
            // A generator: pour fuel into it as the player does, a little at a time, and switch it on.
            var gen = Nearest(Object.FindObjectsOfType<Generator>().Where(g => !g.isSaw), at);
            if (gen != null)
            {
                Plugin.Log.LogInfo($"[world test] generator {gen.name} at {gen.transform.position}: fuel {gen.fuel:0.0} of {gen.maxFuel}, on={gen.isOn}");
                var genItem = gen.GetComponent<Item>();
                if (gen.isOn)
                {
                    if (genItem != null)
                    {
                        genItem.turnOff();
                    }
                    yield return new WaitForSeconds(1f);
                }
                for (int i = 0; i < 20; i++)
                {
                    gen.addFuel(1f);
                    yield return new WaitForSeconds(0.05f);
                }
                if (genItem != null)
                {
                    genItem.turnOn();
                }
                else
                {
                    gen.turnOn();
                }
                Plugin.Log.LogInfo($"[world test] generator after pouring 20 and switching on: fuel {gen.fuel:0.0}, on={gen.isOn}");
                yield return new WaitForSeconds(2f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no generator found");
            }
            // The workbench's storage, slot by slot: put something in and take it out again, as if the window were
            // open; the observer then changes it too (one change that stands, one that comes too late).
            var wb = Nearest(Object.FindObjectsOfType<Workbench>(), at);
            if (wb != null && wb.normalInventory != null)
            {
                var storage = wb.normalInventory;
                // Stand at it, as a player using it does (the observer follows us; the host refuses changes from afar).
                var by = wb.transform.position + (player.transform.position - wb.transform.position).normalized * 120f;
                player.transform.position = new Vector3(by.x, player.transform.position.y, by.z);
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
                    string contents;
                    if (InvItemClass.isNull(got))
                    {
                        contents = "empty";
                    }
                    else
                    {
                        contents = got.type + " x" + got.amount;
                    }
                    Plugin.Log.LogInfo($"[world test] storage slot {free} now: {contents}; workbench level {Singleton<Controller>.Instance.workbenchLevel}");

                    // Sort & Stack (the observer pressed it too, as a client, during its turn): two partial stacks of
                    // one item in two free slots apart, then the button's action. They must end as one stack.
                    string stackType = DbItems().Where(x => x.it.stackable && !x.it.hasAmmo && x.it.maxAmount >= 5).Select(x => x.key).FirstOrDefault();
                    var frees = new List<int>();
                    for (int i = 0; i < storage.slots.Count; i++)
                    {
                        if (InvItemClass.isNull(storage.slots[i].invItem))
                        {
                            frees.Add(i);
                        }
                    }
                    if (stackType != null && frees.Count >= 2)
                    {
                        player.openedItemInventory2 = storage;
                        yield return new WaitForSeconds(0.6f);
                        int s1 = frees[0], s2 = frees[frees.Count - 1];
                        storage.slots[s1].inventory = storage;
                        storage.slots[s1].createItem(stackType, 2);
                        storage.slots[s2].inventory = storage;
                        storage.slots[s2].createItem(stackType, 1);
                        yield return new WaitForSeconds(0.8f);
                        int Filled() => storage.slots.Count(sl => !InvItemClass.isNull(sl.invItem));
                        int stacksBefore = storage.slots.Count(sl => !InvItemClass.isNull(sl.invItem) && sl.invItem.type == stackType), filledBefore = Filled();
                        SlotShare instance = SlotShare.Instance;
                        if (instance != null)
                        {
                            instance.SortPressed(storage);
                        }
                        yield return new WaitForSeconds(0.8f);
                        var mine = storage.slots.Where(sl => !InvItemClass.isNull(sl.invItem) && sl.invItem.type == stackType).ToList();
                        int firstEmpty = storage.slots.FindIndex(sl => InvItemClass.isNull(sl.invItem));
                        bool packed = firstEmpty < 0 || storage.slots.Skip(firstEmpty).All(sl => InvItemClass.isNull(sl.invItem));
                        Plugin.Log.LogInfo($"[world test] sort & stack: {stackType} in {stacksBefore} stack(s) before, {mine.Count} after ({string.Join(", ", mine.Select(sl => "x" + sl.invItem.amount))}); filled slots {filledBefore} -> {Filled()}; items packed from slot 0: {packed}");
                        player.openedItemInventory2 = null;
                        yield return new WaitForSeconds(1f);
                    }
                    else
                    {
                        string stackType2 = stackType;
                        if (stackType2 == null)
                        {
                            stackType2 = "none";
                        }
                        Plugin.Log.LogInfo($"[world test] sort & stack: skipped (stackable type {stackType2}, {frees.Count} free slots)");
                    }
                }
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no workbench here");
            }

            // Locks: a locked door or chest opened with a lockpick, as the player does it (the lockpick is used up).
            var lck = Nearest(Object.FindObjectsOfType<Locked>().Where(l => l.locked && (l.GetComponent<Door>() != null || l.GetComponent<Item>() != null) && ObjRef.Of(l.gameObject).Valid), at);
            if (lck == null)
            {
                var near = Nearest(Object.FindObjectsOfType<Door>().Where(d => !d.destroyed && ObjRef.Of(d.gameObject).Valid), at);
                if (near != null)
                {
                    near.lockMe("");
                    lck = near.GetComponent<Locked>();
                    Plugin.Log.LogInfo($"[world test] nothing locked found: locked {near.name}");
                    yield return new WaitForSeconds(1f);
                }
            }
            if (lck != null)
            {
                string lockName = lck.name;
                ObjRef lockRef = ObjRef.Of(lck.gameObject);
                Vector3 lockAt = lck.transform.position;
                string kind = "item";
                if (lck.GetComponent<Door>() != null)
                {
                    kind = "door";
                }
                Plugin.Log.LogInfo($"[world test] lock on {lockName} {lockRef} at {lockAt} ({kind}, key {lck.keyType})");
                player.Inventory.addItemTypeToPlayer("lockpick", 1);
                yield return null;
                lck.tryToLockpick();
                Plugin.Log.LogInfo($"[world test] after the lockpick: locked={lck.locked}");
                yield return new WaitForSeconds(2f);
            }
            // Other locks, for the authority test: where they are and how they're named.
            foreach (var l in Object.FindObjectsOfType<Locked>().Where(l => l.locked && ObjRef.Of(l.gameObject).Valid).OrderBy(l => (l.transform.position - at).sqrMagnitude).Take(4))
            {
                Plugin.Log.LogInfo($"[world test] still locked: {l.name} {ObjRef.Of(l.gameObject)} at {l.transform.position}");
            }

            // A combination padlock: open its window, set the code's numbers and check it, as the player does.
            var pad = Nearest(Object.FindObjectsOfType<Padlock>().Where(pl => pl.locked && ObjRef.Of(pl.gameObject).Valid), at);
            if (pad != null && !string.IsNullOrEmpty(pad.combination))
            {
                Plugin.Log.LogInfo($"[world test] padlock {pad.name} {ObjRef.Of(pad.gameObject)} at {pad.transform.position}, code {pad.combination}");
                pad.activate();
                yield return new WaitForSecondsRealtime(0.5f);
                PadlockNumber[] numbers;
                if (Singleton<UI>.Instance.padlock != null)
                {
                    numbers = Singleton<UI>.Instance.padlock.GetComponentsInChildren<PadlockNumber>();
                }
                else
                {
                    numbers = new PadlockNumber[0];
                }
                for (int i = 0; i < numbers.Length && i < pad.combination.Length; i++)
                {
                    numbers[i].number = pad.combination[i] - '0';
                }
                pad.checkCombination();
                Plugin.Log.LogInfo($"[world test] after entering the code: locked={pad.locked}");
                yield return new WaitForSecondsRealtime(2f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no locked padlock found");
            }

            // A locked crate picked open (the progress bar's success: Item.disarm, which opens it), then let go of again.
            var crate = Object.FindObjectsOfType<Item>().Where(i => WorldActions.Crate(i) != null && !i.searched && ObjRef.Of(i.gameObject).Valid)
                .OrderBy(i => (i.transform.position - at).sqrMagnitude).ToList();
            foreach (var c in crate.Skip(1).Take(3))
            {
                Plugin.Log.LogInfo($"[world test] another locked crate: {c.name} {ObjRef.Of(c.gameObject)} at {c.transform.position}");
            }
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
            else
            {
                Plugin.Log.LogInfo("[world test] no locked crate found");
            }

            // Furniture smashed by our player: a hit that leaves it standing, then one that breaks it (one message
            // each, the second with no health left).
            at = player.transform.position;
            var furniture = Nearest(Object.FindObjectsOfType<Item>()
                .Where(i => i.destructible && !i.destroyed && i.health > 1 && !i.hasInventory && i.shadowArmor == null && i.GetComponent<Explodes>() == null
                    && i.GetComponent<Trigger>() == null && i.GetComponent<ProxyItem>() == null && ObjRef.Of(i.gameObject).Valid), at);
            if (furniture == null)
            {
                // None in this save near us: place one as a player places a trap, so the others get it too (ObjSpawn).
                foreach (var prefab in new[] { "Furnitures/Cottage/chair_1", "Furnitures/Cottage/table_1", "Furnitures/Cottage/shelf", "Furnitures/Cottage/crate_movable_01", "Furnitures/Cottage/Wardrobe_Big_1" })
                {
                    var made = PlaceShared(prefab, at + new Vector3(250f, 0f, 0f));
                    // its health is set in Start
                    yield return null;
                    yield return null;
                    Item it = null;
                    if (made != null)
                    {
                        it = made.GetComponent<Item>();
                    }
                    string text5;
                    if (it == null)
                    {
                        text5 = "no item";
                    }
                    else
                    {
                        text5 = $"destructible={it.destructible}, health {it.health} of {it.maxHealth}";
                    }
                    Plugin.Log.LogInfo($"[world test] placed {prefab}: {text5}");
                    if (it != null && it.destructible && !it.destroyed && it.health > 1)
                    {
                        furniture = it;
                        Plugin.Log.LogInfo($"[world test] none in this save: placed {prefab}");
                        break;
                    }
                    if (made != null)
                    {
                        Object.Destroy(made);
                    }
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
            else
            {
                Plugin.Log.LogInfo("[world test] no destructible furniture found");
            }

            // An exploding barrel or gas bottle set off (as a shot does), far enough from our player not to hurt it.
            var explosives = Object.FindObjectsOfType<Explodes>().Where(e => e.GetComponent<ThrownItem>() == null && !BreakSync.Activated(e)).ToList();
            var barrel = Nearest(explosives.Where(e => ObjRef.Of(e.gameObject).Valid && Vector3.Distance(e.transform.position, at) > e.radius + 100f), at);
            Plugin.Log.LogInfo($"[world test] explosives here: {explosives.Count}");
            if (barrel == null)
            {
                var go = PlaceShared("Traps/barrelExploding", at + new Vector3(0f, 0f, 900f));
                if (go != null)
                {
                    barrel = go.GetComponentInChildren<Explodes>();
                }
                else
                {
                    barrel = null;
                }
                if (barrel != null)
                {
                    Plugin.Log.LogInfo("[world test] none in this save: placed one");
                }
                yield return new WaitForSeconds(1f);
            }
            if (barrel != null)
            {
                Plugin.Log.LogInfo($"[world test] setting off {barrel.name} {ObjRef.Of(barrel.gameObject)} at {barrel.transform.position}");
                barrel.onActivate();
                yield return new WaitForSeconds(3f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no explosive found");
            }
            yield return Scripted(at); // before ObjectsStep, which enables the feeder that Scripted builds
            yield return ObjectsStep(player, item);
            yield return WorldEventsStep(player);
            yield return HideoutsStep(player);
            yield return StacksStep(player, at);
            Plugin.Log.LogInfo("[world test] done");
        }

        /// <summary>
        /// World.StackSizes at 50: 30 bandages picked up one by one go in one slot; Sort &amp; Stack makes one stack
        /// of 3 + 3 + 40 nails in the workbench's storage and pours gasoline cans at 10%, 15% and 20% into one at 45%;
        /// two gasoline cans picked up stay two (their amount is fuel); with the
        /// setting at 0 a bandage stacks to the game's own size again.
        /// </summary>
        private static IEnumerator StacksStep(Player player, Vector3 at)
        {
            var cfg = Plugin.Cfg.StackSize;
            int setting = cfg.Value;
            if (setting != 50)
            {
                cfg.Value = 50;
            }
            yield return null;
            void Check(bool ok, string what)
            {
                string verdict = "FAIL";
                if (ok)
                {
                    verdict = "PASS";
                }
                Plugin.Log.LogInfo($"[world test] stacks: {verdict} {what}");
            }
            var db = DbItems().ToList();
            foreach (var (k, it) in db.Where(x => x.it.stacksDurability || (x.it.stackable && x.it.maxAmount <= 1)))
            {
                Plugin.Log.LogInfo($"[world test] stacks: left alone {k} (stackable {it.stackable}, stacksDurability {it.stacksDurability}, maxAmount {it.maxAmount})");
            }
            string bandage = db.Select(x => x.key).FirstOrDefault(k => k == "bandage");
            if (bandage == null)
            {
                bandage = db.Select(x => x.key).FirstOrDefault(k => k.Contains("bandage"));
            }
            string gas = db.Where(x => x.it.stacksDurability).Select(x => x.key).FirstOrDefault(k => k.Contains("gas"));
            if (gas == null)
            {
                gas = db.Where(x => x.it.stacksDurability).Select(x => x.key).FirstOrDefault();
            }

            List<InvSlot> Bags()
            {
                List<InvSlot> slots = player.Inventory.slots;
                List<InvSlot> hotbar;
                if (player.Hotbar != null)
                {
                    hotbar = player.Hotbar.slots;
                }
                else
                {
                    hotbar = new List<InvSlot>();
                }
                return slots.Concat(hotbar).ToList();
            }
            void Clear(string type)
            {
                foreach (var sl in Bags())
                {
                    if (!InvItemClass.isNull(sl.invItem) && sl.invItem.type == type)
                    {
                        sl.removeItem();
                    }
                }
            }
            List<int> Stacks(string type) => Bags().Where(sl => !InvItemClass.isNull(sl.invItem) && sl.invItem.type == type).Select(sl => sl.invItem.amount).ToList();

            if (bandage != null)
            {
                Clear(bandage);
                for (int i = 0; i < 30; i++)
                {
                    player.Inventory.addItemTypeToPlayer(bandage, 1);
                }
                yield return null;
                var st = Stacks(bandage);
                Check(st.Count == 1 && st[0] == 30, $"30 {bandage} picked up one by one: {string.Join(", ", st.Select(a => "x" + a))} (game's own size {StackSizes.OriginalOf(bandage)})");
                Clear(bandage);
            }
            else
            {
                Check(false, "no bandage in the items database");
            }

            if (gas != null)
            {
                Clear(gas);
                player.Inventory.addItemTypeToPlayer(gas, 1);
                player.Inventory.addItemTypeToPlayer(gas, 1);
                yield return null;
                var st = Stacks(gas);
                var b = Singleton<ItemsDatabase>.Instance.getItem(gas, instantiate: false);
                Check(st.Count == 2, $"two {gas} stay two: {st.Count} slot(s) (stackable {b.stackable}, maxAmount {b.maxAmount}, stacksDurability {b.stacksDurability})");
                Clear(gas);
            }

            // Sort & Stack in the workbench's storage: 3 + 3 + 40 nails -> 46.
            var wb = Nearest(Object.FindObjectsOfType<Workbench>(), at);
            Inventory storage = null;
            if (wb != null)
            {
                storage = wb.normalInventory;
            }
            var frees = new List<int>();
            void ClearNails()
            {
                foreach (var sl in storage.slots)
                {
                    if (!InvItemClass.isNull(sl.invItem) && sl.invItem.type == "nail")
                    {
                        sl.inventory = storage;
                        sl.removeItem();
                    }
                }
            }
            if (storage != null)
            {
                for (int i = 0; i < storage.slots.Count; i++)
                {
                    if (InvItemClass.isNull(storage.slots[i].invItem))
                    {
                        frees.Add(i);
                    }
                }
            }
            if (storage != null && frees.Count >= 3 && db.Any(x => x.key == "nail"))
            {
                var by = wb.transform.position + (player.transform.position - wb.transform.position).normalized * 120f;
                player.transform.position = new Vector3(by.x, player.transform.position.y, by.z);
                yield return new WaitForSeconds(1f);
                player.openedItemInventory2 = storage;
                yield return new WaitForSeconds(0.6f);
                ClearNails();
                frees.Clear();
                for (int i = 0; i < storage.slots.Count; i++)
                {
                    if (InvItemClass.isNull(storage.slots[i].invItem))
                    {
                        frees.Add(i);
                    }
                }
                int[] amounts = { 3, 3, 40 };
                for (int j = 0; j < 3; j++)
                {
                    var sl = storage.slots[frees[j * (frees.Count - 1) / 2]];
                    sl.inventory = storage;
                    sl.createItem("nail", amounts[j]);
                }
                yield return new WaitForSeconds(0.8f);
                SlotShare instance = SlotShare.Instance;
                if (instance != null)
                {
                    instance.SortPressed(storage);
                }
                yield return new WaitForSeconds(0.8f);
                var nails = storage.slots.Where(sl => !InvItemClass.isNull(sl.invItem) && sl.invItem.type == "nail").Select(sl => sl.invItem.amount).ToList();
                Check(nails.Count == 1 && nails[0] == 46, $"sort & stack of 3 + 3 + 40 nails: {string.Join(", ", nails.Select(a => "x" + a))}");
                ClearNails();
                yield return new WaitForSeconds(0.8f);

                // Gasoline cans at 10%, 15% and 20%: poured into one can at 45%, the other two gone.
                if (gas != null)
                {
                    float max = Singleton<ItemsDatabase>.Instance.getItem(gas, instantiate: false).maxDurability;
                    void ClearGas()
                    {
                        foreach (var sl in storage.slots)
                        {
                            if (!InvItemClass.isNull(sl.invItem) && sl.invItem.type == gas)
                            {
                                sl.inventory = storage;
                                sl.removeItem();
                            }
                        }
                    }
                    ClearGas();
                    frees.Clear();
                    for (int i = 0; i < storage.slots.Count; i++)
                    {
                        if (InvItemClass.isNull(storage.slots[i].invItem))
                        {
                            frees.Add(i);
                        }
                    }
                    float[] parts = { 0.10f, 0.15f, 0.20f };
                    for (int j = 0; j < 3; j++)
                    {
                        var sl = storage.slots[frees[j * (frees.Count - 1) / 2]];
                        sl.inventory = storage;
                        sl.createItem(gas, 1);
                        sl.invItem.durability = max * parts[j];
                        sl.invItem.refresh();
                    }
                    yield return new WaitForSeconds(0.8f);
                    SlotShare instance2 = SlotShare.Instance;
                    if (instance2 != null)
                    {
                        instance2.SortPressed(storage);
                    }
                    yield return new WaitForSeconds(0.8f);
                    var cans = storage.slots.Where(sl => !InvItemClass.isNull(sl.invItem) && sl.invItem.type == gas).Select(sl => sl.invItem.durability).ToList();
                    Check(cans.Count == 1 && Mathf.Abs(cans[0] - max * 0.45f) < 0.01f * max,
                        $"sort & stack of {gas} at 10% + 15% + 20% of {max}: {string.Join(", ", cans.Select(d => d.ToString("0.#")))}");
                    ClearGas();
                    yield return new WaitForSeconds(0.8f);
                }
                player.openedItemInventory2 = null;
            }
            else
            {
                Check(false, $"sort & stack of nails: no workbench storage with 3 free slots ({frees.Count} free)");
            }

            // 0: the game's own sizes again.
            cfg.Value = 0;
            yield return null;
            if (bandage != null)
            {
                int max = Singleton<ItemsDatabase>.Instance.getItem(bandage, instantiate: false).maxAmount;
                Check(max == StackSizes.OriginalOf(bandage) && max < 30, $"StackSize 0: {bandage} stacks to {max} again");
            }
            cfg.Value = setting;
            yield return null;
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
                string ovenName = o.name;
                ObjRef ovenRef = ObjRef.Of(o.gameObject);
                bool ovenOn = o.isOn;
                string location = "no location";
                if (l != null)
                {
                    location = l.name + " hideout " + l.hideoutId + " ward " + (l.shadowWard != null && l.shadowWard.activeSelf);
                }
                string ours = "";
                if (o == player.experienceMachine)
                {
                    ours = " (ours)";
                }
                Plugin.Log.LogInfo($"[world test] oven {ovenName} {ovenRef} on {ovenOn} at {location}{ours}");
            }
            var mine = player.experienceMachine;
            ExperienceMachine other = ovens.FirstOrDefault(o => o != mine);
            if (other == null)
            {
                other = mine;
            }
            if (other != null)
            {
                player.setExperienceMachine(other);
                var l = other.transform.GetLocation();
                string litName = other.name;
                bool litOn = other.isOn;
                bool ward = l != null && l.shadowWard != null && l.shadowWard.activeSelf;
                bool isOurs = player.experienceMachine == other;
                string oldOne = "none";
                if (mine != null)
                {
                    oldOne = mine.name + " on " + mine.isOn;
                }
                Plugin.Log.LogInfo($"[world test] lit oven {litName}: on {litOn}, ward {ward}, ours {isOurs}; the old one {oldOne}");
                yield return new WaitForSeconds(1f);
                if (mine != null && mine != other)
                {
                    player.experienceMachine = mine;
                }
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no oven here");
            }

            var hideouts = Object.FindObjectsOfType<Location>().Where(l => l.hideoutId > 0 && !l.isSubLocation).OrderBy(l => l.hideoutId).ToList();
            player.whereAmI.checkWhereAmI();
            var here = player.whereAmI.bigLocation;
            string listing = string.Join(", ", hideouts.Select(l => $"{l.name} #{l.hideoutId} ({l.inventoriesList.Count} containers, delivery {(l.porterDeliveryPosition != null)})"));
            string place = "none";
            if (here != null)
            {
                place = here.name;
            }
            Plugin.Log.LogInfo($"[world test] hideouts: {listing}; we're at {place}");
            if (here != null && hideouts.Contains(here))
            {
                int before = ItemsIn(here);
                here.transportAllItemsToCurrentHideout();
                Plugin.Log.LogInfo($"[world test] porter asked at the hideout to take from: {before} items there before, {ItemsIn(here)} after");
            }
            var src = hideouts.FirstOrDefault(l => l != here);
            Location dest;
            if (here != null && here.playerBase && here.hideoutId > 0)
            {
                dest = here;
            }
            else
            {
                dest = hideouts.FirstOrDefault(l => l != src);
            }
            if (src != null && dest != null && dest.porterDeliveryPosition != null)
            {
                var chest = src.inventoriesList.Select(i =>
                {
                    if (i != null)
                    {
                        return i.GetComponent<Inventory>();
                    }
                    return null;
                }).FirstOrDefault(i => i != null && !i.transform.isLocked());
                if (chest != null)
                {
                    chest.addItemType("bottle", 1);
                }
                Plugin.Log.LogInfo($"[world test] porter from {src.name} ({ItemsIn(src)} items) to {dest.name}");
                Hideouts.Deliver(src, dest);
                Plugin.Log.LogInfo($"[world test] {src.name} has {ItemsIn(src)} items after");
                yield return new WaitForSeconds(1f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no two hideouts for the porter");
            }
        }

        private static int ItemsIn(Location l) =>
            l.inventoriesList.Sum(i =>
            {
                if (i != null && i.GetComponent<Inventory>() != null && !i.transform.isLocked())
                {
                    return i.GetComponent<Inventory>().getAllItems().Count;
                }
                return 0;
            });

        private static string Parts(GameObject go)
        {
            if (go == null)
            {
                return "none";
            }
            return string.Join(",", go.GetComponents<Component>().Select(c => c.GetType().Name));
        }

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
                Inventory pinv = null;
                if (pf != null)
                {
                    pinv = pf.GetComponent<Inventory>();
                }
                Item pitem = null;
                if (pf != null)
                {
                    pitem = pf.GetComponent<Item>();
                }
                string components = Parts(pf);
                string removeWhenEmpty = "-";
                if (pinv != null)
                {
                    removeWhenEmpty = pinv.removeWhenEmpty.ToString();
                }
                string isDroppedItem = "-";
                if (pitem != null)
                {
                    isDroppedItem = pitem.isDroppedItem.ToString();
                }
                Plugin.Log.LogInfo($"[world test] prefab {name}: {components}; removeWhenEmpty={removeWhenEmpty}, isDroppedItem={isDroppedItem}");
            }
            InvItem lureItem = null;
            if (Singleton<ItemsDatabase>.Instance != null)
            {
                var drops = new List<string>();
                var lures = new List<string>();
                foreach (var (k, it) in DbItems())
                {
                    if (it.dropOnReleaseAim)
                    {
                        drops.Add(k);
                    }
                    if (it.item is GameObject ig && ig.GetComponent<Lure>() != null)
                    {
                        string value = "";
                        if (it.recoverableAfterThrown)
                        {
                            value = " (recoverable)";
                        }
                        lures.Add(k + value);
                        if (lureItem == null)
                        {
                            lureItem = it;
                        }
                    }
                }
                Plugin.Log.LogInfo($"[world test] items dropped on releasing aim: {string.Join(", ", drops)}; lures: {string.Join(", ", lures)}");
            }

            // A dropped item picked up again with a click (Item.getDroppedItem).
            var dropped = player.spawnDroppedInvItem(new InvItemClass(item));
            yield return new WaitForSeconds(2.5f); // shared once at rest
            Item di = null;
            if (dropped != null)
            {
                di = dropped.GetComponent<Item>();
            }
            if (di != null)
            {
                Plugin.Log.LogInfo($"[world test] clicking dropped {item.type} {ObjRef.Of(di.gameObject)}");
                di.getDroppedItem();
                yield return new WaitForSeconds(1f);
                string text = "still there";
                if (dropped == null)
                {
                    text = "gone";
                }
                Plugin.Log.LogInfo($"[world test] dropped item after the click: {text}");
            }

            // A gasoline trail, poured as Player.waitToSpillLiquid does, then set alight.
            var trail = Core.AddPrefab(Containers.GasolineTrail, at + player.transform.up * 40f, Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer);
            if (trail != null)
            {
                Core.addToSaveable(trail, isDynamic: true);
            }
            yield return new WaitForSeconds(1f);
            if (trail != null)
            {
                Plugin.Log.LogInfo($"[world test] gasoline trail {ObjRef.Of(trail)}: {Parts(trail)}");
                var liquid = trail.GetComponent<Liquid>();
                if (liquid != null)
                {
                    liquid.startBurning(); // as a match, a torch or a molotov lights it
                }
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
            else
            {
                Plugin.Log.LogInfo("[world test] no note lying in the world");
            }

            // An InteractiveItem's switch, there and back.
            var ii = Nearest(Object.FindObjectsOfType<InteractiveItem>().Where(x => ObjRef.Of(x.gameObject).Valid), at);
            if (ii != null)
            {
                var ui = Singleton<UI>.Instance.transform.Find("InteractiveItem");
                Plugin.Log.LogInfo($"[world test] switching {ii.name} {ObjRef.Of(ii.gameObject)}, on={ii.isOn}");
                ii.switchMe();
                if (ui != null)
                {
                    ui.gameObject.SetActive(false);
                }
                yield return new WaitForSeconds(1f);
                Plugin.Log.LogInfo($"[world test] switched {ii.name}: on={ii.isOn}");
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no InteractiveItem here");
            }

            // A feeder.
            var feeder = Nearest(Object.FindObjectsOfType<Feeder>(), at);
            if (feeder != null)
            {
                Plugin.Log.LogInfo($"[world test] feeder {feeder.name} {ObjRef.Of(feeder.gameObject)}: enabled={feeder.isEnabled}, full={feeder.Active}");
                if (!feeder.isEnabled)
                {
                    feeder.enable(); // as building it does (Scripted builds it first, shared as Construct)
                }
                feeder.makeActive();
                feeder.activate();
                yield return new WaitForSeconds(1f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no feeder here");
            }

            // A lure eaten up (as the host's animals do, Lure.removeHealth).
            if (lureItem != null)
            {
                var lureGo = Core.AddPrefab(lureItem.item, at + new Vector3(60f, 0f, 60f), Quaternion.Euler(90f, 0f, 0f), Core.ItemContainer, worldSpace: true);
                Lure lure = null;
                if (lureGo != null)
                {
                    lure = lureGo.GetComponent<Lure>();
                }
                if (lure != null)
                {
                    yield return new WaitForSeconds(0.5f);
                    Plugin.Log.LogInfo($"[world test] lure {lureGo.name} {ObjRef.Of(lureGo)} eaten");
                    lure.removeHealth(lure.health + 1, null);
                    yield return new WaitForSeconds(1f);
                }
            }

            // The saw cutting a log.
            var saw = Nearest(Object.FindObjectsOfType<Saw>(), at);
            if (saw != null && saw.GetComponent<Inventory>() != null)
            {
                var sinv = saw.GetComponent<Inventory>();
                saw.fuel = Mathf.Max(saw.fuel, saw.fuelToConvert + 5f);
                if (InvItemClass.isNull(sinv.getItem("woodLog")))
                {
                    sinv.addItemType("woodLog", 1);
                }
                float before = saw.fuel;
                if (saw.canConvert())
                {
                    try
                    {
                        saw.convert();
                    }
                    catch (System.NullReferenceException)
                    {
                        // Saw.refresh wants the saw's window open; the fuel is taken before it
                    }
                }
                Plugin.Log.LogInfo($"[world test] saw {saw.name} {ObjRef.Of(saw.gameObject)}: fuel {before:0.0} -> {saw.fuel:0.0}");
                yield return new WaitForSeconds(1f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no saw here");
            }
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
                if (ns.currentScenario == null)
                {
                    ns.setCurrentScenario();
                }
                var sc = ns.currentScenario;
                string name2 = "none";
                if (sc != null)
                {
                    name2 = sc.name;
                }
                Plugin.Log.LogInfo($"[world test] night scenario {name2}");
                CustomEvent ev = null;
                if (sc != null)
                {
                    ev = sc.customEventAndInts.Select(e =>
                    {
                        if (e != null)
                        {
                            return e.customEvent;
                        }
                        return null;
                    }).FirstOrDefault(e => e != null && e.theEvent != null && e.theEvent.type == RandomEvent.Type.locationEvent);
                }
                var big = player.whereAmI.bigLocation;
                if (ev != null && big != null)
                {
                    ev.fire(force: true);
                    Plugin.Log.LogInfo($"[world test] night event {ev.name} fired at {big.name}");
                }
                else
                {
                    string name3 = "none";
                    if (ev != null)
                    {
                        name3 = ev.name;
                    }
                    string name4 = "none";
                    if (big != null)
                    {
                        name4 = big.name;
                    }
                    Plugin.Log.LogInfo($"[world test] no night event fired (event {name3}, location {name4})");
                }
            }
            var gen = Singleton<WorldGenerator>.Instance;
            if (gen != null)
            {
                RandomWorldObjects instance = Singleton<RandomWorldObjects>.Instance;
                if (instance != null)
                {
                    instance.resetObjects();
                }
                gen.despawnRandomObjects();
                gen.spawnRandomObjects();
                var rwo = Singleton<RandomWorldObjects>.Instance;
                int made = gen.worldChunks.Sum(c =>
                {
                    if (c != null)
                    {
                        return c.GetComponentsInChildren<SaveableObject>().Count(o => rwo != null && rwo.objects.Any(r => r != null && r.prefab != null && r.prefab.name == o.name));
                    }
                    return 0;
                });
                int count = -1;
                if (rwo != null)
                {
                    count = rwo.objects.Count;
                }
                Plugin.Log.LogInfo($"[world test] random objects respawned in {gen.worldChunks.Count} chunks: {count} kinds, {made} in the chunks now");
            }
            bool lights = WorldEvents.Instance != null && WorldEvents.Instance.HostShadowLights(player, anyTime: true);
            string text = "not here (no generator with lamps)";
            if (lights)
            {
                text = "sent";
            }
            Plugin.Log.LogInfo($"[world test] shadow lights {text}");
            yield return new WaitForSeconds(4f); // the random objects go 2 seconds after they're made; the weather within a second
        }

        /// <summary>Triggers a player walks into or uses, near pos, that haven't fired and whose events have no steps of the player's own.</summary>
        private static List<(EventTriggers et, int i)> AreaTriggers(Vector3 pos) =>
            Object.FindObjectsOfType<EventTriggers>()
                .SelectMany(et => et.eventTriggers.Select((t, i) => (et, t, i)))
                .Where(x => x.t != null && (x.t.type == global::EventTrigger.Type.area || x.t.type == global::EventTrigger.Type.onActivate || x.t.type == global::EventTrigger.Type.onExamine) && Unfired(x.t) && x.t.requirementsMet()
                    && x.t.gameEvents.events.All(e => e != null && !ScriptedEvents.IsPlayerStep(e.type)) && ObjRef.Of(x.et.gameObject).Valid)
                .OrderBy(x => (x.et.transform.position - pos).sqrMagnitude)
                .Select(x => (x.et, x.i)).ToList();

        /// <summary>A trigger that hasn't fired, isn't turned off, and has events of its own that haven't run.</summary>
        private static bool Unfired(global::EventTrigger t) =>
            !t.fired && !t.disabled && t.gameEvents != null && !t.gameEvents.fired && t.gameEvents.events.Count > 0;

        private static string Steps(GameEvents ge) => string.Join(",", ge.events.Where(e => e != null).Select(e => e.type.ToString()));

        /// <summary>
        /// Scripted events, building and dialogue: what's near (for the authority test), then a trigger walked into,
        /// a world event and a build, each as our own player does it.
        /// </summary>
        private static IEnumerator Scripted(Vector3 at)
        {
            var triggers = AreaTriggers(at);
            foreach (var (et, i) in triggers.Take(3))
            {
                Plugin.Log.LogInfo($"[world test] trigger {et.eventTriggers[i].type} {et.name} {ObjRef.Of(et.gameObject)} #{i} at {et.transform.position}: {Steps(et.eventTriggers[i].gameEvents)}");
            }
            var cons = Object.FindObjectsOfType<Constructible>()
                .Where(c => c.options.Count > 0 && !c.constructed && !c.onlyWhenDestroyed && ObjRef.Of(c.gameObject).Valid)
                .OrderBy(c => (c.transform.position - at).sqrMagnitude).ToList();
            foreach (var c in cons.Take(3))
            {
                Plugin.Log.LogInfo($"[world test] buildable {c.name} {ObjRef.Of(c.gameObject)} at {c.transform.position}: {c.options.Count} options");
            }
            foreach (var n in Object.FindObjectsOfType<NPC>().Where(n => n.characterDialogue != null && ObjRef.Of(n.gameObject).Valid)
                         .OrderBy(n => (n.transform.position - at).sqrMagnitude).Take(3))
            {
                Plugin.Log.LogInfo($"[world test] NPC with dialogue {n.name} {ObjRef.Of(n.gameObject)} at {n.transform.position}");
            }

            if (triggers.Count > 0)
            {
                var (et, i) = triggers[0];
                var t = et.eventTriggers[i];
                Plugin.Log.LogInfo($"[world test] setting off {et.name} #{i} ({t.type})");
                AsPlayer(() => et.fireEventTrigger(et.eventTriggers[i].type));
                yield return new WaitForSeconds(t.delay + t.warmupTime + 1.5f);
                string text = "";
                if (et == null)
                {
                    text = ", and its object is gone (the event removed it)";
                }
                Plugin.Log.LogInfo($"[world test] #{i} fired={t.fired}{text}");
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no trigger found");
            }

            var events = Singleton<Events>.Instance;
            var we = events.worldEvents.Where(kv => kv.Value != null && !kv.Value.fired && kv.Value.events.Count > 0
                    && kv.Value.events.All(e => e != null && !ScriptedEvents.IsPlayerStep(e.type) && e.type != GameEvent.Type.spawnCharacter))
                .OrderBy(kv => kv.Value.events.Count).Select(kv => kv.Key).FirstOrDefault();
            if (we != null)
            {
                Plugin.Log.LogInfo($"[world test] world event {we}: {Steps(events.worldEvents[we])}");
                AsPlayer(() => events.fireWorldEvent(we));
                yield return new WaitForSeconds(1.5f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no world event to fire");
            }

            if (cons.Count > 0)
            {
                var c = cons[0];
                Plugin.Log.LogInfo($"[world test] building {c.name} {ObjRef.Of(c.gameObject)} option 0");
                AsPlayer(() => c.construct(false, 0));
                yield return new WaitForSeconds(1.5f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] nothing to build");
            }

            // A trigger the world sets off by itself (at a time of day, or something dying), not our player: the host
            // runs it and sends it as kind 3. One whose events have no steps of a player's own and spawn nothing.
            var worldTriggers = Object.FindObjectsOfType<EventTriggers>(true)
                .SelectMany(et => et.eventTriggers.Select((t, i) => (et, t, i)))
                .Where(x => x.t != null && (x.t.type == global::EventTrigger.Type.onTime || x.t.type == global::EventTrigger.Type.onDeath
                    || x.t.type == global::EventTrigger.Type.refreshWhenTurnedOn)).ToList();
            Plugin.Log.LogInfo($"[world test] world triggers (time, death, turned on): {worldTriggers.Count}, not fired {worldTriggers.Count(x => !x.t.fired)}");
            foreach (var x in worldTriggers.OrderBy(x => (x.et.transform.position - at).sqrMagnitude).Take(4))
            {
                global::EventTrigger.Type kind = x.t.type;
                string triggerName = x.et.name;
                ObjRef triggerRef = ObjRef.Of(x.et.gameObject);
                bool active = x.et.gameObject.activeInHierarchy;
                bool disabled = x.t.disabled;
                string script = "none";
                if (x.t.gameEvents != null)
                {
                    string stepTypes = Steps(x.t.gameEvents);
                    string fired = "";
                    if (x.t.gameEvents.fired)
                    {
                        fired = " (fired)";
                    }
                    script = stepTypes + fired;
                }
                Plugin.Log.LogInfo($"[world test] world trigger {kind} {triggerName} {triggerRef} #{x.i} active={active} disabled={disabled} events: {script}");
            }
            var wt = worldTriggers
                .Where(x => Unfired(x.t)
                    && x.t.gameEvents.events.All(e => e != null && (!ScriptedEvents.IsPlayerStep(e.type) || e.type == GameEvent.Type.displayMessage)
                        && e.type != GameEvent.Type.spawnCharacter)
                    && ObjRef.Of(x.et.gameObject).K == ObjRef.Kind.Saved)
                .OrderBy(x => (x.et.transform.position - at).sqrMagnitude).FirstOrDefault();
            if (wt.et != null)
            {
                Plugin.Log.LogInfo($"[world test] the world sets off {wt.t.type} {wt.et.name} {ObjRef.Of(wt.et.gameObject)} #{wt.i}: {Steps(wt.t.gameEvents)}");
                // Started as fireEventTrigger would, also for one whose object is culled (inactive) here.
                try
                {
                    Singleton<Controller>.Instance.StartCoroutine(wt.t.fire(wt.t.type, "", wt.et));
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning($"[world test] {e.Message}");
                }
                yield return new WaitForSeconds(wt.t.delay + 1.5f);
                Plugin.Log.LogInfo($"[world test] world trigger #{wt.i} fired={wt.t.fired}");
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no world trigger to set off");
            }
        }

        /// <summary>
        /// "--dwmp-test-client-actions" (a client): our player uses up a rope and opens (or closes) the nearest door,
        /// as one action; the scripted host refuses it. The door should go back as the host has it, and the rope
        /// come back.
        /// </summary>
        public static IEnumerator ClientActions()
        {
            while (!WorldShare.WorldReady || !Combat.NetEnemies.ClientMode)
            {
                yield return null;
            }
            yield return new WaitForSeconds(3f);
            var p = Player.Instance;
            var door = Nearest(Object.FindObjectsOfType<Door>().Where(d => !d.destroyed && ObjRef.Of(d.gameObject).Valid), p.transform.position);
            float best = 0f;
            if (door != null)
            {
                best = Vector3.Distance(door.transform.position, p.transform.position);
            }
            if (door == null || best >= 2500f)
            {
                Plugin.Log.LogInfo("[client test] no door near");
                yield break;
            }
            p.Inventory.addItemTypeToPlayer("rope", 2);
            yield return null;
            yield return null;
            int Ropes()
            {
                int n = 0;
                foreach (var it in p.Inventory.getAllItems())
                {
                    if (it.type == "rope")
                    {
                        n += it.amount;
                    }
                }
                foreach (var sl in p.Hotbar.slots)
                {
                    if (!InvItemClass.isNull(sl.invItem) && sl.invItem.type == "rope")
                    {
                        n += sl.invItem.amount;
                    }
                }
                return n;
            }
            bool wasOpen = door.opened;
            int ropesBefore = Ropes(), refusedBefore = Authority.RolledBack;
            Plugin.Log.LogInfo($"[client test] {door.name} {ObjRef.Of(door.gameObject)} {best:0} away, open={wasOpen}, ropes={ropesBefore}");
            AsPlayer(() =>
            {
                p.Inventory.removeItemAmountFromPlayer("rope", 1);
                if (wasOpen)
                {
                    door.close(null);
                }
                else
                {
                    door.open(p.transform.position, p.transform, 0f);
                }
            });
            Plugin.Log.LogInfo($"[client test] did it: open={door.opened}, ropes={Ropes()}");
            yield return new WaitForSeconds(4f);
            Plugin.Log.LogInfo($"[client test] after the host's answer: open={door.opened} (was {wasOpen}), ropes={Ropes()}, rolled back {Authority.RolledBack}");
            // The scripted host refuses the door: it is shut or open as before, and the rope it used is back.
            bool back = door.opened == wasOpen && Ropes() == ropesBefore && Authority.RolledBack > refusedBefore;
            string verdict;
            if (back)
            {
                verdict = "PASS";
            }
            else
            {
                verdict = $"FAIL (open={door.opened}, was {wasOpen}; ropes {Ropes()}, were {ropesBefore}; refusals {Authority.RolledBack - refusedBefore})";
            }
            Plugin.Log.LogInfo($"[client test] rollback: {verdict}");

            // A trigger our player walks into: our part and the shared part run here, and the host is asked.
            var triggers = AreaTriggers(p.transform.position);
            if (triggers.Count > 0)
            {
                var (et, i) = triggers[0];
                Plugin.Log.LogInfo($"[client test] setting off {et.eventTriggers[i].type} {et.name} {ObjRef.Of(et.gameObject)} #{i}: {Steps(et.eventTriggers[i].gameEvents)}");
                AsPlayer(() => et.fireEventTrigger(et.eventTriggers[i].type));
                yield return new WaitForSeconds(et.eventTriggers[i].delay + et.eventTriggers[i].warmupTime + 2f);
                string text = "";
                if (et == null)
                {
                    text = ", and its object is gone (the event removed it)";
                }
                Plugin.Log.LogInfo($"[client test] #{i} fired={et.eventTriggers[i].fired}{text}");
            }
            else
            {
                Plugin.Log.LogInfo("[client test] no trigger near");
            }

            // Talking to an NPC someone else talks to: the scripted host refuses it, and the dialogue closes again.
            var npc = Nearest(Object.FindObjectsOfType<NPC>().Where(n => n.characterDialogue != null && n.wantsToTalk && ObjRef.Of(n.gameObject).Valid), p.transform.position);
            if (npc != null)
            {
                Plugin.Log.LogInfo($"[client test] talking to {npc.name} {ObjRef.Of(npc.gameObject)}");
                npc.talkTo();
                var win = Singleton<UI>.Instance.dialogueWindow;
                string name2 = "nobody";
                if (win.npc != null)
                {
                    name2 = win.npc.name;
                }
                Plugin.Log.LogInfo($"[client test] dialogue open with {name2}");
                yield return new WaitForSecondsRealtime(8f);
                string text2 = "closed";
                if (win.npc != null)
                {
                    text2 = "still open with " + win.npc.name;
                }
                Plugin.Log.LogInfo($"[client test] after the host's answer: dialogue {text2}");
            }
            else
            {
                Plugin.Log.LogInfo("[client test] no NPC to talk to");
            }
        }
    }
}
