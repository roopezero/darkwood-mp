using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class SentBatchesTests
    {
        const string Chest = "Saved:42";

        static readonly SlotChange Took = new SlotChange
        {
            Slot = 1,
            Old = "plank|3|1000|0|0",
            New = "",
        };

        static SentBatches SentAt(float at)
        {
            var sent = new SentBatches();
            sent.Remember(Chest, new[] { Took }, new Seconds(at));
            return sent;
        }

        public class Remember
        {
            [Fact]
            public void Keeps_a_batch_exactly_at_the_limit()
            {
                var sent = SentAt(10f);

                sent.Remember(Chest, new SlotChange[0], new Seconds(25f));

                sent.Count.ShouldBe(2);
            }

            [Fact]
            public void Forgets_a_batch_just_past_the_limit()
            {
                var sent = SentAt(10f);

                sent.Remember(Chest, new SlotChange[0], new Seconds(25.1f));

                sent.Count.ShouldBe(1);
            }
        }

        public class Find
        {
            [Fact]
            public void Finds_the_batch_with_the_change()
            {
                var sent = SentAt(10f);

                var batch = sent.Find(Chest, Took);

                batch.ShouldNotBeNull();
            }

            [Fact]
            public void Finds_nothing_for_another_container()
            {
                var sent = SentAt(10f);

                var batch = sent.Find("Saved:43", Took);

                batch.ShouldBeNull();
            }

            [Theory]
            [InlineData(2, "plank|3|1000|0|0", "")]
            [InlineData(1, "plank|2|1000|0|0", "")]
            [InlineData(1, "plank|3|1000|0|0", "{}")]
            public void Finds_nothing_for_another_change(
                int slot,
                string old,
                string now)
            {
                var sent = SentAt(10f);
                var other = new SlotChange
                {
                    Slot = slot,
                    Old = old,
                    New = now,
                };

                var batch = sent.Find(Chest, other);

                batch.ShouldBeNull();
            }

            [Fact]
            public void Finds_the_oldest_batch_not_undone_first()
            {
                var sent = SentAt(10f);
                var older = sent.Find(Chest, Took);
                older.Undone = true;
                var newer = sent.Remember(
                    Chest,
                    new[] { Took },
                    new Seconds(11f));

                var batch = sent.Find(Chest, Took);

                batch.ShouldBeSameAs(newer);
            }

            [Fact]
            public void Finds_the_oldest_undone_batch_when_all_are()
            {
                var sent = SentAt(10f);
                var older = sent.Find(Chest, Took);
                older.Undone = true;
                var newer = sent.Remember(
                    Chest,
                    new[] { Took },
                    new Seconds(11f));
                newer.Undone = true;

                var batch = sent.Find(Chest, Took);

                batch.ShouldBeSameAs(older);
            }
        }

        public class Clear
        {
            [Fact]
            public void Forgets_every_batch()
            {
                var sent = SentAt(10f);

                sent.Clear();

                sent.Count.ShouldBe(0);
            }
        }
    }
}
