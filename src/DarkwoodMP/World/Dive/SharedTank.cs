using DarkwoodMP.World.Dive;
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
    ///
    /// One lives as long as the game, in ModHost.Tank: the flag is kept in a
    /// game played alone too, for when others join.
    /// </summary>
    internal sealed class SharedTank
    {
        public const string Item = SharedTankRules.Item;
        public const string Flag =
            Story.StoryKeys.ModPrefix + "oxygenTankFilled";
        private Seconds _next;
        private Object _tankPrefab;

        /// <summary>
        /// Whether a player has filled the tank in this world.
        /// </summary>
        public static bool Filled
        {
            get
            {
                var f = Singleton<Flags>.Instance;
                if (f == null || !f.flagsDict.TryGetValue(Flag, out var x))
                {
                    return false;
                }

                return SharedTankRules.Filled(x != null, x != null && x.isTrue);
            }
        }

        /// <summary>
        /// Every couple of seconds in a world: if our player holds a full tank,
        /// set the flag.
        /// </summary>
        public void Update()
        {
            var now = new Seconds(Time.unscaledTime);
            if (!SharedTankRules.Due(now, _next))
            {
                return;
            }

            _next = SharedTankRules.Next(now);
            if (!SharedTankRules.Looks(WatchOf()))
            {
                return;
            }

            if (!SharedTankRules.Sets(HoldOf(Player.Instance)))
            {
                return;
            }

            FlagsSync.SetModFlag(Singleton<Flags>.Instance, Flag, true, 0);
            Plugin.Log.LogInfo(
                "[world] our player holds a full oxygen tank: every player "
                    + $"can dive in the village cellar now ({Flag})");
        }

        /// <summary>
        /// What the watcher knows of the game now: the only code that reads
        /// it for SharedTankRules.Looks.
        /// </summary>
        private static WatchFacts WatchOf()
        {
            var p = Player.Instance;
            return new WatchFacts
            {
                InWorld = WorldShare.InWorld,
                Loading = WorldShare.Loading,
                MainMenu = Core.mainMenu,
                Filled = Filled,
                Player = p != null && p.Inventory != null,
            };
        }

        /// <summary>
        /// Whether our player holds the full tank as the requirement looks
        /// for it: in the bags, or among the journal's items (looked at only
        /// when it isn't in the bags).
        /// </summary>
        private static HoldFacts HoldOf(Player p)
        {
            bool bags = p.Inventory.getItemInPlayer(
                Item, 0f, includeAdditionalInventory: true) != null;
            bool journal = false;
            if (!bags)
            {
                UI ui = Singleton<UI>.Instance;
                if (ui != null)
                {
                    Journal book = ui.journal;
                    if (book != null)
                    {
                        var items = book.itemsDict;
                        if (items != null)
                        {
                            journal = items.ContainsKey(Item);
                        }
                    }
                }
            }

            return new HoldFacts
            {
                InBags = bags,
                InJournal = journal,
                Flags = Singleton<Flags>.Instance != null,
            };
        }

        /// <summary>
        /// The requirement's result once the game has checked it: the only
        /// code that reads the requirement for SharedTankRules.Requirement.
        /// The item and the flag are looked at only for a haveItem
        /// requirement on the tank.
        /// </summary>
        internal bool Requirement(
            EventTriggerRequirement need, bool result)
        {
            return SharedTankRules.Requirement(RequirementOf(need, result));
        }

        private RequirementFacts RequirementOf(
            EventTriggerRequirement need, bool result)
        {
            var facts = new RequirementFacts
            {
                HaveItem =
                    need.type == EventTriggerRequirement.Type.playerState
                        && need.playerState == Player.State.haveItem,
                ActiveModifier = need.activeModifier,
                Result = result,
            };

            if (!facts.HaveItem)
            {
                return facts;
            }

            facts = facts with { Tank = IsTank(need.itemType) };
            if (!facts.Tank)
            {
                return facts;
            }

            return facts with { Filled = Filled };
        }

        /// <summary>Whether this requirement's item is the full tank.</summary>
        private bool IsTank(Object itemType)
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
            if (item == null || !SharedTankRules.IsTank(item.type))
            {
                return false;
            }

            _tankPrefab = itemType;
            return true;
        }
    }
}
