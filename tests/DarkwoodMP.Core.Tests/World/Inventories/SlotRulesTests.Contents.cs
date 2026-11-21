using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public partial class SlotRulesTests
    {
        /// The host's answer for a container we waited for, closed here.
        static readonly AnswerFacts Waited = new AnswerFacts
        {
            Found = true,
            Watched = true,
            Waited = true,
        };

        /// Another game fills the slot our player holds an item from.
        static readonly HeldFacts Filled = new HeldFacts
        {
            Filling = true,
            Holding = true,
            FromThisSlot = true,
            PlayerHere = true,
        };

        public class Answer
        {
            [Fact]
            public void Merges_the_answer_we_waited_for()
            {
                var use = SlotRules.Answer(Waited);

                use.ShouldBe(HostContents.Merge);
            }

            [Fact]
            public void Loads_a_closed_container_we_dont_watch_whole()
            {
                var closed = Waited with { Watched = false };

                var use = SlotRules.Answer(closed);

                use.ShouldBe(HostContents.LoadWhole);
            }

            [Fact]
            public void Merges_an_open_container_we_dont_watch()
            {
                var open = Waited with { Watched = false, Open = true };

                var use = SlotRules.Answer(open);

                use.ShouldBe(HostContents.Merge);
            }

            [Fact]
            public void Drops_an_answer_older_than_the_changes_we_sent()
            {
                var late = Waited with { Waited = false };

                var use = SlotRules.Answer(late);

                use.ShouldBe(HostContents.NotUsed);
            }

            [Fact]
            public void Ends_at_an_empty_answer()
            {
                var none = Waited with { Empty = true, Waited = false };

                var use = SlotRules.Answer(none);

                use.ShouldBe(HostContents.HostHasNone);
            }

            [Fact]
            public void Ends_at_a_container_that_isnt_here()
            {
                var gone = Waited with { Found = false, Empty = true };

                var use = SlotRules.Answer(gone);

                use.ShouldBe(HostContents.NotFound);
            }
        }

        public class GrowsRows
        {
            [Theory]
            [InlineData(36, 30, true, true)]
            [InlineData(30, 30, true, false)]
            [InlineData(36, 30, false, false)]
            public void Grows_a_storage_the_host_has_more_rows_of(
                int hostSlots,
                int slots,
                bool storage,
                bool expected)
            {
                var grows = SlotRules.GrowsRows(hostSlots, slots, storage);

                grows.ShouldBe(expected);
            }
        }

        public class Removes
        {
            [Theory]
            [InlineData(0, true, false, true)]
            [InlineData(0, false, true, true)]
            [InlineData(0, false, false, false)]
            [InlineData(1, true, true, false)]
            public void Removes_an_emptied_bag_or_dropped_item(
                int items,
                bool removeWhenEmpty,
                bool dropped,
                bool expected)
            {
                var removes = SlotRules.Removes(
                    items,
                    removeWhenEmpty,
                    dropped);

                removes.ShouldBe(expected);
            }
        }

        public class HeldGoesToBags
        {
            [Fact]
            public void Moves_the_held_item_from_a_slot_another_game_fills()
            {
                var moves = SlotRules.HeldGoesToBags(Filled);

                moves.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_it_when_the_slot_goes_empty()
            {
                var empty = Filled with { Filling = false };

                var moves = SlotRules.HeldGoesToBags(empty);

                moves.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_when_nothing_is_held()
            {
                var none = Filled with { Holding = false };

                var moves = SlotRules.HeldGoesToBags(none);

                moves.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_an_item_from_another_slot()
            {
                var other = Filled with { FromThisSlot = false };

                var moves = SlotRules.HeldGoesToBags(other);

                moves.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_with_no_player()
            {
                var gone = Filled with { PlayerHere = false };

                var moves = SlotRules.HeldGoesToBags(gone);

                moves.ShouldBeFalse();
            }
        }

        public class CreateAmount
        {
            [Theory]
            [InlineData(true, 12, -1)]
            [InlineData(false, 12, 12)]
            public void Creates_a_gun_with_its_loaded_rounds(
                bool hasAmmo,
                int amount,
                int expected)
            {
                var created = SlotRules.CreateAmount(hasAmmo, amount);

                created.ShouldBe(expected);
            }
        }
    }
}
