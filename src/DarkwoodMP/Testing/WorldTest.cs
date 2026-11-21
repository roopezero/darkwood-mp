using DarkwoodMP.World.Inventories;
using DarkwoodMP.World.Rolls;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.World.Scripted;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// "--dwmp-test-world": once in a world, the host's character does things
    /// by itself, so the shared world can be tested without anyone at the
    /// keyboard: a door, looting and dropping, a story flag and reputation, the
    /// journal, a trap, a switch, traders and an NPC's death, dragging, a
    /// molotov, a burning door, sprites, object names, a generator, the
    /// workbench, locks, a padlock, a crate, furniture, an explosive, then the
    /// steps in Scripted, ObjectsStep, WorldEventsStep and HideoutsStep.
    /// "--dwmp-test-client-actions" is the client's side.
    /// </summary>
    public static partial class WorldTest
    {
        private static T Nearest<T>(IEnumerable<T> xs, Vector3 at)
            where T : Component =>
            xs.OrderBy(x => (x.transform.position
                        - at).sqrMagnitude).FirstOrDefault();

        /// <summary>
        /// Runs a as our own player's action (WorldActions.TestAsPlayer).
        /// </summary>
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

        /// <summary>
        /// Every item in the game's database, by key (one that fails to load is
        /// skipped).
        /// </summary>
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

        /// <summary>
        /// A prefab placed as a player places a trap, so the others get it too
        /// (ObjSpawn).
        /// </summary>
        private static GameObject PlaceShared(string prefab, Vector3 at)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Containers.Placing = true;
            }

            try
            {
                return Core.AddPrefab(
                    prefab, at, Quaternion.Euler(90f, 0f, 0f),
                    Core.ItemContainer, worldSpace: true);
            }
            finally
            {
                if (coop != null)
                {
                    coop.Containers.Placing = false;
                }
            }
        }

        /// <summary>
        /// Logs the items that place something when used (InvItem.placeOnUse),
        /// which nothing shares.
        /// </summary>
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

            Plugin.Log.LogInfo(
                $"[world test] items placed on use: {found.Count}" + foundList);
        }

        public static IEnumerator Run()
        {
            while (!WorldShare.InWorld)
            {
                yield return null;
            }

            // The test kills an NPC, burns a door, flips story flags and more:
            // only ever on a copy of the saves.
            if (!Plugin.Instance.Host.SaveFolders.Active)
            {
                Plugin.Log.LogError(
                    "[world test] FAIL: not in a sandbox or the test world "
                        + "(add --dwmp-sandbox DIR)");
                yield break;
            }

            while (Plugin.Instance.Host.Session.Peers.Count == 0)
            {
                yield return null;
            }

            yield return new WaitForSeconds(5f);
            var player = Player.Instance;
            var at = player.transform.position;
            LogPlaceOnUse();

            var door = Nearest(
                Object.FindObjectsOfType<Door>().Where(d => !d.destroyed), at);
            if (door != null)
            {
                Plugin.Log.LogInfo(
                    $"[world test] door {door.name} at "
                        + $"{door.transform.position}, opened={door.opened}");
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

                Plugin.Log.LogInfo(
                    $"[world test] door toggled twice, opened={door.opened}");
            }

            yield return new WaitForSeconds(2f);

            var inv = Nearest(Object.FindObjectsOfType<Inventory>()
                    .Where(
                        i => i.invType == Inventory.InvType.itemInv
                            && !i.isWorkbench
                            && i.slots.Any(
                                s => !InvItemClass.isNull(s.invItem))), at);
            if (inv == null)
            {
                Plugin.Log.LogInfo(
                    "[world test] no container with items found");
                yield break;
            }

            var slot = inv.slots.First(s => !InvItemClass.isNull(s.invItem));
            var item = new InvItemClass(slot.invItem);
            Plugin.Log.LogInfo(
                $"[world test] looting {item.type} x{item.amount} from "
                    + $"{inv.name} at {inv.transform.position}");
            // as if its window were open
            player.openedItemInventory = inv;
            yield return new WaitForSeconds(0.6f);
            slot.removeItem();
            yield return new WaitForSeconds(0.6f);
            player.openedItemInventory = null;         // and closed
            yield return new WaitForSeconds(1f);
            var dropped = player.spawnDroppedInvItem(item);
            string droppedName = null;
            if (dropped != null)
            {
                droppedName = dropped.name;
            }

            Plugin.Log.LogInfo(
                $"[world test] dropped {item.type} on the ground: "
                    + $"{droppedName}");
            yield return new WaitForSeconds(3f);

            // The story: flip a flag, and an NPC's reputation.
            var flags = Singleton<Flags>.Instance;
            var dict =
                HarmonyLib.AccessTools
                    .FieldRefAccess<Flags, Dictionary<string, Flags.Flag>>(
                        "flagsDict")(flags);
            var flag = dict.Values.FirstOrDefault(f => f != null);
            if (flag != null)
            {
                flags.setFlag(flag.name, !flag.isTrue);
                Plugin.Log.LogInfo(
                    $"[world test] flag {flag.name} -> {flag.isTrue}");
            }

            var npc = flags.npcStates.FirstOrDefault(n => n != null);
            if (npc != null)
            {
                npc.reputation += 1;
                Plugin.Log.LogInfo(
                    $"[world test] {npc.name}'s reputation -> "
                        + $"{npc.reputation}");
            }

            // The journal: a story entry and a note the host hasn't got yet, as
            // the game adds them.
            var journal = Singleton<UI>.Instance.journal;
            var jdb = Singleton<JournalDatabase>.Instance;
            if (jdb != null && jdb.journalEntriesDict.Count == 0)
            {
                jdb.populateDict();
            }

            string entry = null;
            if (jdb != null)
            {
                entry = jdb.journalEntriesDict.Keys.FirstOrDefault(
                    k => !journal.journalEntriesDict.ContainsKey(k));
            }

            string note = null;
            if (jdb != null)
            {
                note = jdb.notesDict.Keys.FirstOrDefault(
                    k => !journal.notesDict.ContainsKey(k));
            }

            int? count = null;
            if (jdb != null)
            {
                count = jdb.journalEntriesDict.Count;
            }

            int? noteCount = null;
            if (jdb != null)
            {
                noteCount = jdb.notesDict.Count;
            }

            Plugin.Log.LogInfo(
                $"[world test] journal: {journal.journalEntriesDict.Count} "
                    + $"entries, {journal.notesDict.Count} notes; the game "
                    + $"knows {count} and {noteCount}");
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
            var trap = Nearest(
                Object.FindObjectsOfType<Trigger>().Where(
                    t => t.active && t.GetComponent<Item>() != null),
                at);
            if (trap != null)
            {
                Plugin.Log.LogInfo(
                    $"[world test] setting off trap {trap.name} at "
                        + $"{trap.transform.position}");
                HarmonyLib.AccessTools
                    .Method(typeof(Trigger), "OnAfterTrigger").Invoke(
                        trap, new object[] { null, false });
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no trap found");
            }

            yield return new WaitForSeconds(2f);

            // Something that switches (a lamp, a generator...).
            var sw = Nearest(
                Object.FindObjectsOfType<Item>().Where(i => i.switchable), at);
            if (sw != null)
            {
                Plugin.Log.LogInfo(
                    $"[world test] switching {sw.name} (on={sw.isOn}) off and "
                        + "on");
                sw.turnOff();
                yield return new WaitForSeconds(1f);
                sw.turnOn();
            }
            else
            {
                Plugin.Log.LogInfo("[world test] nothing switchable found");
            }

            yield return new WaitForSeconds(2f);

            // One at a time: the looted container again (the observer holds it
            // by now).
            Plugin.Log.LogInfo($"[world test] opening {inv.name} again");
            player.openedItemInventory = inv;
            yield return new WaitForSeconds(1.5f);
            player.openedItemInventory = null;
            yield return new WaitForSeconds(2f);

            // Traders: a restock (the host's to do), and an NPC dying.
            var npcs = Object.FindObjectsOfType<NPC>()
                .Where(
                    n => n.GetComponent<Character>() != null
                        && n.GetComponent<Character>().alive).ToList();
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
                HarmonyLib.AccessTools
                    .Method(typeof(NPC), "randomizeTraderInv").Invoke(
                        trader, null);
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
                Flags.NPCState state =
                    Singleton<Flags>.Instance.getNPCState(victim.name);
                bool? dead = null;
                if (state != null)
                {
                    dead = state.dead;
                }

                Plugin.Log.LogInfo(
                    $"[world test] {victimName} dead in the flags: {dead}");
            }

            yield return ThingsStep(player, at);
            yield return MachinesStep(player, at, item);

            // before ObjectsStep, which enables the feeder that Scripted builds
            yield return Scripted(at);
            yield return ObjectsStep(player, item);
            yield return WorldEventsStep(player);
            yield return HideoutsStep(player);
            yield return StacksStep(player, at);
            Plugin.Log.LogInfo("[world test] done");
        }
    }
}
