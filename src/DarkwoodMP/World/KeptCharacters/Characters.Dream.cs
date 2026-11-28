using System.Collections.Generic;
using Newtonsoft.Json;
using Chapters = DarkwoodMP.World.Story.Chapters;

namespace DarkwoodMP.World.KeptCharacters
{
    /// <summary>
    /// Client: the character we send. In a dream the game has put the
    /// player's real bags, place and effects away
    /// (Dreams.saveCurrentPlayerState: inventorySlotsCopy, hotbarSlotsCopy,
    /// positionCopy, effectsCopy) and
    /// gives them back only at the dream's end (Dreams.endDreaming). A client
    /// that left in a dream used to be kept with the dream's bags, and the
    /// real ones were gone for good: the host's world isn't in that dream when
    /// the client comes back (a joiner waits while the host dreams), so no
    /// dream's end ever gave them back. Now the put-away state is sent while
    /// dreaming (OurCharacterRules.Sent), so the kept character is the one
    /// the dream's end would leave.
    /// </summary>
    internal sealed partial class Characters
    {
        private static CharacterSaveBody BodyOf(Player p)
        {
            var player = new Player.SaveState(p);
            var bag = new Inventory.SaveState(p.Inventory);
            var hotbar = new Inventory.SaveState(p.Hotbar);
            var d = Singleton<Dreams>.Instance;
            if (Sent(d) == SentState.BeforeDream)
            {
                bag = StateOf(d.inventorySlotsCopy);
                hotbar = StateOf(d.hotbarSlotsCopy);
                player.pos = Helpers.toSimpleVect3(d.positionCopy);
                if (d.effectsCopy != null)
                {
                    player.chEffS = d.effectsCopy;
                }
            }

            return new CharacterSaveBody
            {
                Player = JsonConvert.SerializeObject(player),
                Inventory = JsonConvert.SerializeObject(bag),
                Hotbar = JsonConvert.SerializeObject(hotbar),
                // the chapter of the world we're in (the host's, until it
                // moves on)
                Chapter = Chapters.HostChapter,
            };
        }

        private static SentState Sent(Dreams d)
        {
            if (d == null)
            {
                return SentState.Current;
            }

            return OurCharacterRules.Sent(
                new SentFacts
                {
                    Dreaming = d.dreaming,
                    Copied = d.inventorySlotsCopy != null
                        && d.inventorySlotsCopy.Count > 0,
                });
        }

        /// <summary>
        /// The put-away slots as an Inventory.SaveState, as the game saves a
        /// bag: its slot count and the items with their slots.
        /// </summary>
        private static Inventory.SaveState StateOf(List<InvSlot> slots)
        {
            var state = new Inventory.SaveState();
            if (slots == null)
            {
                return state;
            }

            state.slotCount = slots.Count;
            for (int i = 0; i < slots.Count; i++)
            {
                var item = slots[i].invItem;
                if (!InvItemClass.isNull(item))
                {
                    state.invItems.Add(new InvItemClass.SaveState(item, i));
                }
            }

            return state;
        }
    }
}
