using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The village cellar's dive (chapter 2), shared. Each of the cellar's two
    /// underwater passages is an onActivate trigger with the requirement
    /// "the player has oxygenTank_full" (a playerState/haveItem requirement,
    /// checked against that game's own player), and a second trigger without it
    /// that only says the player has no tank. The full tank is an item in one
    /// player's bags (a quest item, InvItem.isImportantItem, but kept in the
    /// inventory, not the journal): the repaired compressor at med_hideout_05
    /// swaps the empty tanks the player carries for full ones. Neither the
    /// dive's events nor its dream take the tank away, so in a single-player
    /// game one refill means unlimited dives, and there is nothing to skip for
    /// that. With several players only the one holding it could dive.
    ///
    /// So once any player holds a full tank, the world remembers it: a flag of
    /// the mod's own (Flag), set by the game whose player holds the tank and
    /// carried to every game by FlagsSync like the story's flags, so it's in
    /// the host's save and in a joiner's snapshot. Once it's set, the
    /// requirement passes for every player (and its "no tank" counterpart
    /// doesn't), without giving anyone an item.
    /// </summary>
    public static class SharedTank
    {
        public const string Item = "oxygenTank_full";
        public const string Flag = FlagsSync.ModPrefix + "oxygenTankFilled";
        public const float Interval = 2f;
        private static float _next;

        /// <summary>
        /// Whether a player has filled the tank in this world.
        /// </summary>
        public static bool Filled
        {
            get
            {
                var f = Singleton<Flags>.Instance;
                return f != null && f.flagsDict.TryGetValue(Flag, out var x)
                    && x != null && x.isTrue;
            }
        }

        /// <summary>
        /// Every couple of seconds in a world: if our player holds a full tank,
        /// set the flag.
        /// </summary>
        public static void Update()
        {
            if (Time.unscaledTime < _next)
            {
                return;
            }
            _next = Time.unscaledTime + Interval;
            if (!WorldShare.InWorld || WorldShare.Loading || Core.mainMenu
                || Filled)
            {
                return;
            }
            var p = Player.Instance;
            if (p == null || p.Inventory == null)
            {
                return;
            }
            // As the requirement looks: in the bags, or among the journal's
            // items.
            bool holds = p.Inventory.getItemInPlayer(
                Item, 0f, includeAdditionalInventory: true) != null;
            if (!holds)
            {
                UI ui = Singleton<UI>.Instance;
                if (ui != null)
                {
                    Journal journal = ui.journal;
                    if (journal != null)
                    {
                        var items = journal.itemsDict;
                        if (items != null)
                        {
                            holds = items.ContainsKey(Item);
                        }
                    }
                }
            }
            if (!holds)
            {
                return;
            }
            var f = Singleton<Flags>.Instance;
            if (f == null)
            {
                return;
            }
            FlagsSync.SetModFlag(f, Flag, true, 0);
            Plugin.Log.LogInfo(
                "[world] our player holds a full oxygen tank: every player "
                    + $"can dive in the village cellar now ({Flag})");
        }

        private static Object _tankPrefab;

        /// <summary>Whether this requirement's item is the full tank.</summary>
        internal static bool IsTank(Object itemType)
        {
            if (itemType == null)
            {
                return false;
            }
            if (ReferenceEquals(itemType, _tankPrefab))
            {
                return true;
            }
            if (!(itemType is GameObject go))
            {
                return false;
            }
            InvItem item = go.GetComponent<InvItem>();
            if (item == null || item.type != Item)
            {
                return false;
            }
            _tankPrefab = itemType;
            return true;
        }
    }

    /// <summary>
    /// "The player has a full oxygen tank" holds for every player once one of
    /// them has filled it (SharedTank).
    /// </summary>
    [HarmonyPatch(
        typeof(EventTriggerRequirement),
        nameof(EventTriggerRequirement.requirementsMet))]
    static class SharedTankRequirementPatch
    {
        static void Postfix(
            EventTriggerRequirement __instance, ref bool __result)
        {
            if (__instance.type != EventTriggerRequirement.Type.playerState
                || __instance.playerState != Player.State.haveItem)
            {
                return;
            }
            if (!SharedTank.IsTank(__instance.itemType) || !SharedTank.Filled)
            {
                return;
            }
            __result = __instance.activeModifier;
        }
    }
}
