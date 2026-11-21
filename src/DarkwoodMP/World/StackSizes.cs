using System.Collections.Generic;
using DarkwoodMP.Net;
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
    /// </summary>
    public static class StackSizes
    {
        private static readonly Dictionary<InvItem, int> Original =
            new Dictionary<InvItem, int>();
        // holds the changed prefabs loaded
        private static readonly List<InvItem> Kept = new List<InvItem>();
        private static Session _session;
        // client: the host's size, -1 when not in a session
        private static int _hostSize = -1;

        /// <summary>
        /// The size in use now: the host's in a session as a client, otherwise
        /// our own setting.
        /// </summary>
        public static int Current
        {
            get
            {
                if (_hostSize >= 0)
                {
                    return _hostSize;
                }

                return Mathf.Max(0, Plugin.Cfg.StackSize.Value);
            }
        }

        public static void Register(Session s)
        {
            _session = s;
            s.Register(MsgType.StackSize, OnStackSize);
            s.OnPeerConnected += id =>
            {
                if (s.IsHost)
                {
                    SendTo(id);
                }
            };

            s.OnSessionEnded += () =>
            {
                if (_hostSize < 0)
                {
                    return;
                }

                _hostSize = -1;
                Plugin.Log.LogInfo(
                    "[stacks] session over: our own setting again "
                        + $"({Current})");
                Apply(Current);
            };

            Plugin.Cfg.StackSize.SettingChanged += (_, __) =>
            {
                if (_hostSize >= 0)
                {
                    return; // a client keeps the host's until it leaves
                }

                Apply(Current);
                if (s.IsHost)
                {
                    s.Broadcast(
                        Write(Current), Channel.Control, reliable: true);
                    Plugin.Log.LogInfo($"[stacks] sent {Current} to everyone");
                }
            };

            // a database made before the mod loaded (if none yet, this does
            // nothing)
            Apply(Current);
        }

        private static PacketWriter Write(int size)
        {
            var w = _session.Begin(MsgType.StackSize);
            w.W.Write((ushort)Mathf.Clamp(size, 0, ushort.MaxValue));
            return w;
        }

        private static void SendTo(ulong id) => _session.SendTo(
            id, Write(Current), Channel.Control, reliable: true);

        private static void OnStackSize(ulong from, PacketReader r)
        {
            if (!_session.IsClient || from != _session.HostId)
            {
                return;
            }

            int size = r.R.ReadUInt16();
            _hostSize = size;
            Plugin.Log.LogInfo($"[stacks] the host's stack size: {size}");
            Apply(size);
        }

        /// <summary>
        /// Whether this item may stand in one slot: no more than its stack size
        /// (always, for an item that doesn't stack by amount).
        /// </summary>
        public static bool Fits(InvItemClass it)
        {
            if (InvItemClass.isNull(it) || it.baseClass == null)
            {
                return true;
            }

            var b = it.baseClass;
            if (!Eligible(b, b.maxAmount))
            {
                return true;
            }

            return it.amount <= b.maxAmount;
        }

        private static bool Eligible(InvItem b, int orig) =>
            b.stackable && !b.stacksDurability && !b.hasAmmo && orig > 1;

        /// <summary>
        /// Make every eligible item stack to size (0: the game's own sizes).
        /// </summary>
        public static void Apply(int size)
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

                if (!Original.TryGetValue(it, out int orig))
                {
                    orig = it.maxAmount;
                    Original[it] = orig;
                }

                if (!Eligible(it, orig))
                {
                    continue;
                }

                eligible++;
                int max;
                if (size > 0)
                {
                    max = Mathf.Max(orig, size);
                }
                else
                {
                    max = orig;
                }

                if (max != orig)
                {
                    changed++;
                }

                if (it.maxAmount == max)
                {
                    continue;
                }

                it.maxAmount = max;
                if (!Kept.Contains(it))
                {
                    Kept.Add(it);
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
        internal static int OriginalOf(string type)
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

            if (Original.TryGetValue(it, out int o))
            {
                return o;
            }

            return it.maxAmount;
        }

        /// <summary>
        /// The items database is made with each world (and the main menu's):
        /// set the sizes on it.
        /// </summary>
        [HarmonyPatch(typeof(ItemsDatabase), "Awake")]
        private static class AwakePatch
        {
            private static void Postfix() => Apply(Current);
        }
    }
}
