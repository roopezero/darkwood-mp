using DarkwoodMP.Menus;

namespace DarkwoodMP.Tests.Menus
{
    public class BrowserOrderTests
    {
        static readonly ServerRow Swamp = new ServerRow
        {
            Address = "10.0.0.3:27015",
            Known = true,
            Password = true,
            Name = "swamp",
            Players = 3,
            World = "Day 4",
            Ping = 80,
        };

        static readonly ServerRow Forest = new ServerRow
        {
            Address = "10.0.0.1:27015",
            Known = true,
            Name = "Forest",
            Players = 1,
            World = "Day 12",
            Ping = 20,
        };

        /// A server that didn't answer.
        static readonly ServerRow Silent = new ServerRow
        {
            Address = "10.0.0.2:27015",
            Password = true,
            Name = "Aardvark",
            Players = 9,
            World = "Day 1",
            Ping = 1,
        };

        /// The saved order: as the player added them.
        static readonly ServerRow[] Saved = { Swamp, Silent, Forest };

        static BrowserOrder By(int column)
        {
            var order = new BrowserOrder();
            if (order.Column != column)
            {
                order.Click(column);
            }

            return order;
        }

        public class Click
        {
            [Fact]
            public void Starts_on_ping_from_the_smallest_up()
            {
                var order = new BrowserOrder();

                order.Column.ShouldBe(BrowserOrder.Ping);
                order.Down.ShouldBeFalse();
            }

            [Fact]
            public void Pins_the_column_numbers()
            {
                var columns = new[]
                {
                    BrowserOrder.Lock,
                    BrowserOrder.Name,
                    BrowserOrder.Players,
                    BrowserOrder.World,
                    BrowserOrder.Ping,
                };

                columns.ShouldBe(new[] { 0, 1, 2, 3, 4 });
            }

            [Fact]
            public void Turns_the_same_column_round()
            {
                var order = new BrowserOrder();

                order.Click(BrowserOrder.Ping);

                order.Down.ShouldBeTrue();
            }

            [Fact]
            public void Turns_it_back_on_a_second_click()
            {
                var order = new BrowserOrder();

                order.Click(BrowserOrder.Ping);
                order.Click(BrowserOrder.Ping);

                order.Down.ShouldBeFalse();
            }

            [Fact]
            public void Sorts_another_column_from_the_smallest_up()
            {
                var order = new BrowserOrder();
                order.Click(BrowserOrder.Ping);

                order.Click(BrowserOrder.Name);

                order.Column.ShouldBe(BrowserOrder.Name);
                order.Down.ShouldBeFalse();
            }
        }

        public class Sorted
        {
            [Fact]
            public void Sorts_by_ping_with_the_silent_last()
            {
                var shown = new BrowserOrder().Sorted(Saved, false);

                shown.ShouldBe(new[]
                {
                    Forest.Address, Swamp.Address, Silent.Address,
                });
            }

            [Fact]
            public void Reverses_the_order_down()
            {
                var order = By(BrowserOrder.Ping);
                order.Click(BrowserOrder.Ping);

                var shown = order.Sorted(Saved, false);

                shown.ShouldBe(new[]
                {
                    Silent.Address, Swamp.Address, Forest.Address,
                });
            }

            [Fact]
            public void Shows_the_saved_order_for_the_lock_column_up()
            {
                var shown = By(BrowserOrder.Lock).Sorted(Saved, false);

                shown.ShouldBe(new[]
                {
                    Swamp.Address, Silent.Address, Forest.Address,
                });
            }

            [Fact]
            public void Sorts_the_LAN_by_lock_with_the_unlocked_first()
            {
                var shown = By(BrowserOrder.Lock).Sorted(Saved, true);

                shown.ShouldBe(new[]
                {
                    Silent.Address, Forest.Address, Swamp.Address,
                });
            }

            [Fact]
            public void Reverses_equal_servers_too_when_sorted_down()
            {
                var order = By(BrowserOrder.Lock);
                order.Click(BrowserOrder.Lock);

                var shown = order.Sorted(Saved, false);

                shown.ShouldBe(new[]
                {
                    Swamp.Address, Forest.Address, Silent.Address,
                });
            }

            [Fact]
            public void Sorts_names_by_their_lower_case()
            {
                var shown = By(BrowserOrder.Name).Sorted(Saved, false);

                shown.ShouldBe(new[]
                {
                    Silent.Address, Forest.Address, Swamp.Address,
                });
            }

            [Fact]
            public void Names_a_server_with_no_name_by_its_address()
            {
                var rows = new[]
                {
                    Forest,
                    Swamp with { Name = null, Address = "A.example:1" },
                };

                var shown = By(BrowserOrder.Name).Sorted(rows, false);

                shown[0].ShouldBe("A.example:1");
            }

            [Fact]
            public void Puts_the_silent_first_by_players()
            {
                var shown = By(BrowserOrder.Players).Sorted(Saved, false);

                shown.ShouldBe(new[]
                {
                    Silent.Address, Forest.Address, Swamp.Address,
                });
            }

            [Fact]
            public void Compares_worlds_as_text()
            {
                var shown = By(BrowserOrder.World).Sorted(Saved, false);

                shown.ShouldBe(new[]
                {
                    Silent.Address, Forest.Address, Swamp.Address,
                });
            }

            [Fact]
            public void Takes_a_missing_world_as_empty()
            {
                var rows = new[] { Forest, Swamp with { World = null } };

                var shown = By(BrowserOrder.World).Sorted(rows, false);

                shown[0].ShouldBe(Swamp.Address);
            }

            [Fact]
            public void Keeps_equal_servers_in_the_saved_order_up()
            {
                var rows = new[]
                {
                    Swamp with { Ping = 20 },
                    Forest,
                };

                var shown = new BrowserOrder().Sorted(rows, false);

                shown.ShouldBe(new[] { Swamp.Address, Forest.Address });
            }
        }
    }
}
