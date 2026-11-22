using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class SlotNetTests
    {
        public class Add
        {
            [Fact]
            public void Counts_what_we_put_in_up()
            {
                var net = new SlotNet();

                net.Add("", "plank", 3);

                net.Counts["plank"].ShouldBe(3);
            }

            [Fact]
            public void Counts_what_we_took_down()
            {
                var net = new SlotNet();

                net.Add("plank|3|1000|0|0", null, 0);

                net.Counts["plank"].ShouldBe(-3);
            }

            [Fact]
            public void Nets_a_stack_topped_up_to_the_difference()
            {
                var net = new SlotNet();

                net.Add("plank|3|1000|0|0", "plank", 5);

                net.Counts["plank"].ShouldBe(2);
            }

            [Fact]
            public void Nets_an_item_moved_inside_the_container_to_nothing()
            {
                var net = new SlotNet();

                net.Add("plank|3|1000|0|0", null, 0);
                net.Add("", "plank", 3);

                net.Counts["plank"].ShouldBe(0);
            }

            [Fact]
            public void Counts_each_type_apart()
            {
                var net = new SlotNet();

                net.Add("meat|2|500|0|0", "plank", 3);

                net.Counts.Count.ShouldBe(2);
            }

            [Fact]
            public void Counts_an_old_key_with_no_amount_as_none()
            {
                var net = new SlotNet();

                net.Add("plank", null, 0);

                net.Counts["plank"].ShouldBe(0);
            }

            [Fact]
            public void Counts_an_amount_that_isnt_a_number_as_none()
            {
                var net = new SlotNet();

                net.Add("plank|x|1000|0|0", null, 0);

                net.Counts["plank"].ShouldBe(0);
            }
        }

        public class Put
        {
            [Theory]
            [InlineData(3, 3)]
            [InlineData(0, 0)]
            [InlineData(-3, 0)]
            public void Gives_back_only_what_we_put_in(int net, int expected)
            {
                var put = SlotNet.Put(net);

                put.ShouldBe(expected);
            }
        }

        public class Took
        {
            [Theory]
            [InlineData(-3, 3)]
            [InlineData(0, 0)]
            [InlineData(3, 0)]
            public void Takes_back_only_what_we_took(int net, int expected)
            {
                var took = SlotNet.Took(net);

                took.ShouldBe(expected);
            }
        }
    }
}
