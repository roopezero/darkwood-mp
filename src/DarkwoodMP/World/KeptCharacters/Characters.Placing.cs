using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.KeptCharacters
{
    /// <summary>
    /// Putting a loaded character on our player: its place on the map,
    /// its bags and what it holds.
    /// </summary>
    internal sealed partial class Characters
    {
        /// <summary>
        /// Whether a place is on the loaded world's map: within 0.6 of a
        /// chunk's size (in x and z) of one of WorldGenerator's chunks, by
        /// their places, so a part of the map that's switched off counts too.
        /// True when that can't be told.
        /// </summary>
        internal static bool OnMap(Vector3 at)
        {
            WorldGenerator wg = Singleton<WorldGenerator>.Instance;
            return OurCharacterRules.OnMap(
                ChunksOf(wg), SizeOf(wg), new Vec2(at.x, at.z));
        }

        /// <summary>
        /// How far the nearest of WorldGenerator's chunks is, by the larger of
        /// the x and z distances; 0 when that can't be told.
        /// </summary>
        internal static float NearestChunk(Vector3 at)
        {
            WorldGenerator wg = Singleton<WorldGenerator>.Instance;
            return OurCharacterRules.NearestChunk(
                ChunksOf(wg), SizeOf(wg), new Vec2(at.x, at.z));
        }

        /// The places of the world's chunks (null when there's no list).
        private static List<Vec2> ChunksOf(WorldGenerator wg)
        {
            if (wg == null || wg.worldChunks == null)
            {
                return null;
            }

            var chunks = new List<Vec2>();
            foreach (var c in wg.worldChunks)
            {
                if (c != null)
                {
                    var at = c.transform.position;
                    chunks.Add(new Vec2(at.x, at.z));
                }
            }

            return chunks;
        }

        private static float SizeOf(WorldGenerator wg)
        {
            if (wg == null)
            {
                return 0f;
            }

            return wg.chunkSize;
        }

        /// <summary>
        /// Move our player to a free spot near where it was just put
        /// (Sync.Players.SpawnSpot), so it doesn't appear on top of another
        /// player or in a wall. host: where the host stands, kept clear too
        /// (we may not have heard where it is yet).
        /// </summary>
        internal static void PlaceFree(Player p, Vector3? host)
        {
            var want = p.transform.position;
            Vector3[] taken = null;
            if (host.HasValue)
            {
                taken = new[] { host.Value };
            }

            var at = Sync.Players.SpawnSpot.Find(p, want, taken);
            if (!OurCharacterRules.Moves(
                new Vec3(want.x, want.y, want.z), new Vec3(at.x, at.y, at.z)))
            {
                Plugin.Log.LogInfo($"[spawn] placed at {want}: free");
                return;
            }

            p.transform.position = at;
            var body = p.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = at;
                body.velocity = Vector3.zero;
            }

            float distance =
                new Vector2(at.x - want.x, at.z - want.z).magnitude;
            Plugin.Log.LogInfo(
                $"[spawn] placed at {at}, "
                    + $"{distance:0} from {want}, which wasn't free");
        }

        private static void ApplyRecord(
            Player p, string ps, string inv, string hot)
        {
            var state = JsonConvert.DeserializeObject<Player.SaveState>(ps);
            // loadValues applies upgrades by repeating them, as for a player
            // just loaded with none; ours already has the host's (we loaded the
            // host's save), so apply only the difference, and take back any
            // extra.
            state.healthUpgrades = Delta(state.healthUpgrades, p.healthUpgrades,
                () =>
            {
                p.healthUpgrades--;
                p.maxHealth -= OurCharacterRules.UpgradeCost;
            });

            state.staminaUpgrades = Delta(state.staminaUpgrades,
                p.staminaUpgrades, () =>
            {
                p.staminaUpgrades--;
                p.maxStamina -= OurCharacterRules.UpgradeCost;
            });

            int hotbarUpgrades = state.hotbarUpgrades,
                inventoryUpgrades = state.inventoryUpgrades;
            // these only add slots; the inventory and hotbar below come with
            // their own slot counts (loadValues adds or removes slots to
            // match), fewer included
            state.hotbarUpgrades = 0;
            state.inventoryUpgrades = 0;
            state.loadValues(onlyState: false); // position too
            var invState =
                JsonConvert.DeserializeObject<Inventory.SaveState>(inv);
            var hotState =
                JsonConvert.DeserializeObject<Inventory.SaveState>(hot);
            SetSlots(p.Inventory, invState.slotCount);
            SetSlots(p.Hotbar, hotState.slotCount);
            invState.loadValues(p.Inventory);
            hotState.loadValues(p.Hotbar);
            p.hotbarUpgrades = hotbarUpgrades;
            p.inventoryUpgrades = inventoryUpgrades;
            p.setHealth(OurCharacterRules.Health(state.health, p.maxHealth));
        }

        /// <summary>
        /// Give a bag as many slots as our character's, the game's way:
        /// Inventory.addSlot and removeSlot also make and destroy the slot's
        /// picture when the bag is shown (the hotbar always is).
        /// Inventory.SaveState.loadValues changes the slot list alone, so the
        /// hotbar on screen kept the host's slots (the save we loaded is the
        /// host's): one of ours more had no picture, and one fewer left the
        /// host's last slot showing.
        /// </summary>
        private static void SetSlots(Inventory bag, int count)
        {
            if (bag == null || bag.slots == null
                || OurCharacterRules.SlotChange(bag.slots.Count, count) == 0)
            {
                return;
            }

            while (bag.slots.Count < count)
            {
                bag.addSlot();
            }

            while (bag.slots.Count > count)
            {
                bag.removeSlot();
            }
        }

        private static int Delta(int wanted, int have, Action takeOne)
        {
            int back = OurCharacterRules.UpgradesToTakeBack(wanted, have);
            for (int i = 0; i < back; i++)
            {
                takeOne();
            }

            return OurCharacterRules.UpgradesToAdd(wanted, have);
        }

        /// <summary>
        /// Put the item in the selected hotbar slot in the player's hands.
        /// Inventory.SaveState.loadValues makes new item objects for the
        /// hotbar, but Player.currentItem still held the one from the host's
        /// save, which is in no slot now, so attacking with it did
        /// nothing until the item was put away and taken out again. This is
        /// what the game does after changing the hotbar itself
        /// (special_addFlamethrower): select the current hotbar slot, which
        /// goes through Player.switchToItem (put away what's held, take out the
        /// slot's item, its animations and aiming reset), without dropping
        /// anything.
        /// </summary>
        private static void Reequip(Player p)
        {
            if (p.Hotbar == null)
            {
                return;
            }

            var slot = p.getCurrentlySelectedHotbarSlot();
            InvItemClass item = null;
            if (slot != null)
            {
                item = slot.invItem;
            }

            try
            {
                if (slot != null)
                {
                    p.selectCurrentHotbarSlot();
                }
                else
                {
                    // no slot selected: bare hands rather than an item that
                    // isn't there
                    p.switchToItem(p.fists);
                }

                // An item that can't be held (food in the selected slot) leaves
                // the hands as they were, with no switch under way; if what's
                // held then is the stale item, it becomes bare hands at once.
                if (!p.switchingItem && !InvItemClass.isNull(p.currentItem)
                    && p.currentItem != p.fists && !InBags(p, p.currentItem))
                {
                    p.currentItem = p.fists;
                    p.switchAniLibrary(p.fists.baseClass.aniLibrary);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't re-equip your item: {e.Message}");
                return;
            }

            string type;
            if (InvItemClass.isNull(item))
            {
                type = "none";
            }
            else
            {
                type = item.type;
            }

            Plugin.Log.LogInfo($"[chars] re-equipped {type}");
        }

        private static bool InBags(Player p, InvItemClass item)
        {
            foreach (var bag in new[] { p.Inventory, p.Hotbar })
            {
                if (bag != null && bag.slots != null)
                {
                    foreach (var slot in bag.slots)
                    {
                        if (slot != null && slot.invItem == item)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Someone new: the host's save gave them the host's character; empty
        /// its bags, heal it, set it beside the host.
        /// </summary>
        private static void Fresh(Player p, Vector3 spawn)
        {
            foreach (var bag in new[] { p.Inventory, p.Hotbar })
            {
                var empty = new Inventory.SaveState(bag);
                empty.invItems.Clear(); // keep the slots, not what's in them
                empty.loadValues(bag);
            }

            p.setHealth(p.maxHealth);
            // The host's skills are the host's: a new player takes the shared
            // levels at a stove and picks their own.
            Progress.Progression.ClearSkills(p);
            if (spawn != Vector3.zero)
            {
                p.transform.position = spawn;
            }
        }
    }
}
