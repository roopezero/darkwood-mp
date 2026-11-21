using System;
using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World.Stacks;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// One stack size for every stackable item (the host's "StackSize" setting,
    /// 50 by default; 0 keeps the game's own sizes, which run from 2 to 50).
    ///
    /// Every piece of the game's stacking (merging, splitting, picking up,
    /// adding to bags, crafting output, the item's "Max: N" line) and the mod's
    /// own (Sort &amp; Stack, GiveBack) reads the item's InvItem.maxAmount from
    /// its prefab (InvItemClass.baseClass is the prefab's component, from
    /// ItemsDatabase.getItem(type, instantiate: false)), so raising maxAmount
    /// on the prefabs changes all of it at once, also for items already in
    /// bags. A size only ever raises a stack (an item that stacks to 50 already
    /// keeps 50). Left alone: items that don't stack, items whose amount is
    /// their fuel (InvItem.stacksDurability: gasoline, the lantern; putting one
    /// on another pours fuel), items that hold ammo (their amount is their
    /// loaded rounds) and items that stack to 1.
    ///
    /// Saves keep only an item's amount, and the game doesn't clamp it on load,
    /// so a save with big stacks loads in any game. The changed prefabs are
    /// kept in a list, so Unity doesn't unload them (a reloaded prefab would
    /// have the game's size again).
    ///
    /// In a session everyone uses the host's size: the host sends it
    /// (StackSize) to each player who joins, and to everyone when it's changed;
    /// a client uses its own setting again when the session ends. The host
    /// refuses a container slot holding more than one stack of its size
    /// (SlotShare).
    ///
    /// One lives as long as the game, in ModHost.Stacks: the main menu and a
    /// game played alone use it too. The session's part (the host's size, its
    /// message) is StackShare, in CoopSession.Stacks.
    /// </summary>
    internal sealed class StackSizes : IDisposable
    {
        private readonly Dictionary<InvItem, int> _original =
            new Dictionary<InvItem, int>();
        // holds the changed prefabs loaded
        private readonly List<InvItem> _kept = new List<InvItem>();

        public StackSizes()
        {
            Plugin.Cfg.StackSize.SettingChanged += OnSettingChanged;
            // a database made before the mod loaded (if none yet, this does
            // nothing)
            Apply(Current);
        }

        /// <summary>
        /// The size in use now: the host's in a session as a client, otherwise
        /// our own setting.
        /// </summary>
        public static int Current
        {
            get
            {
                int host = StackRules.NoHostSize;
                CoopSession coop = Coop.Current;
                if (coop != null)
                {
                    host = coop.Stacks.HostSize;
                }

                return StackRules.Current(host, Plugin.Cfg.StackSize.Value);
            }
        }

        private void OnSettingChanged(object sender, EventArgs args)
        {
            int host = StackRules.NoHostSize;
            bool hosting = false;
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                host = coop.Stacks.HostSize;
                hosting = coop.Net.IsHost;
            }

            var change = StackRules.OnSettingChanged(host, hosting);
            if (change == StackChange.Ignore)
            {
                return; // a client keeps the host's until it leaves
            }

            Apply(Current);
            if (change == StackChange.ApplyAndSend)
            {
                coop.Stacks.SendAll(Current);
            }
        }

        /// <summary>
        /// Whether this item may stand in one slot: no more than its stack size
        /// (always, for an item that doesn't stack by amount).
        /// </summary>
        public static bool Fits(InvItemClass it)
        {
            return StackRules.Fits(SlotOf(it));
        }

        /// <summary>
        /// An item in a slot, for StackRules.Fits: the only code that reads
        /// it. Its prefab is judged on its maxAmount now.
        /// </summary>
        private static StackSlotFacts SlotOf(InvItemClass it)
        {
            if (InvItemClass.isNull(it) || it.baseClass == null)
            {
                return default;
            }

            var b = it.baseClass;
            return new StackSlotFacts
            {
                Exists = true,
                Item = ItemOf(b, b.maxAmount),
                Amount = it.amount,
            };
        }

        /// <summary>
        /// An item prefab's flags, judged on stack size max, for
        /// StackRules.Eligible: the only code that reads them.
        /// </summary>
        private static StackItemFacts ItemOf(InvItem b, int max)
        {
            return new StackItemFacts
            {
                Stackable = b.stackable,
                StacksFuel = b.stacksDurability,
                HoldsAmmo = b.hasAmmo,
                Max = max,
            };
        }

        /// <summary>
        /// Make every eligible item stack to size (0: the game's own sizes).
        /// </summary>
        public void Apply(int size)
        {
            var db = Singleton<ItemsDatabase>.Instance;
            if (db == null || db.itemsDict == null)
            {
                return;
            }

            int changed = 0, eligible = 0;
            foreach (var k in new List<string>(db.itemsDict.Keys))
            {
                InvItem it = null;
                try
                {
                    it = db.getItem(k, instantiate: false);
                }
                catch
                {
                }

                if (it == null)
                {
                    continue;
                }

                if (!_original.TryGetValue(it, out int orig))
                {
                    orig = it.maxAmount;
                    _original[it] = orig;
                }

                if (!StackRules.Eligible(ItemOf(it, orig)))
                {
                    continue;
                }

                eligible++;
                int max = StackRules.SizeFor(orig, size);

                if (max != orig)
                {
                    changed++;
                }

                if (it.maxAmount == max)
                {
                    continue;
                }

                it.maxAmount = max;
                if (!_kept.Contains(it))
                {
                    _kept.Add(it);
                }
            }

            string data;
            if (size > 0)
            {
                data =
                    $"[stacks] {eligible} stackable items stack to {size} "
                        + $"({changed} raised)";
            }
            else
            {
                data =
                    $"[stacks] {eligible} stackable items stack to the game's "
                        + "own sizes";
            }

            Plugin.Log.LogInfo(data);
        }

        /// <summary>
        /// For tests: the game's own stack size of an item type (before Apply),
        /// or its size now if never seen.
        /// </summary>
        internal int OriginalOf(string type)
        {
            ItemsDatabase instance = Singleton<ItemsDatabase>.Instance;
            InvItem it = null;
            if (instance != null)
            {
                it = instance.getItem(type, instantiate: false);
            }

            if (it == null)
            {
                return 0;
            }

            if (_original.TryGetValue(it, out int o))
            {
                return o;
            }

            return it.maxAmount;
        }

        public void Dispose()
        {
            Plugin.Cfg.StackSize.SettingChanged -= OnSettingChanged;
        }

        /// <summary>
        /// The items database is made with each world (and the main menu's):
        /// set the sizes on it.
        /// </summary>
        [HarmonyPatch(typeof(ItemsDatabase), "Awake")]
        private static class AwakePatch
        {
            private static void Postfix()
            {
                Plugin plugin = Plugin.Instance;
                if (plugin != null && plugin.Host != null)
                {
                    plugin.Host.Stacks.Apply(Current);
                }
            }
        }
    }
}
