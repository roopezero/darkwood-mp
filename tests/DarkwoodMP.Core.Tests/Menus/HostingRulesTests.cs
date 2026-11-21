using DarkwoodMP.Menus;

namespace DarkwoodMP.Tests.Menus
{
    public class HostingRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_port_and_player_limits()
            {
                var limits = new[]
                {
                    HostingRules.LowestPort,
                    HostingRules.HighestPort,
                    HostingRules.FewestPlayers,
                    HostingRules.MostPlayers,
                };

                limits.ShouldBe(new[] { 1, 65535, 2, 5 });
            }
        }

        public class TypedPort
        {
            [Theory]
            [InlineData("1", 1)]
            [InlineData("65535", 65535)]
            [InlineData("7777", 7777)]
            [InlineData(" 7777 ", 7777)]
            [InlineData("+7777", 7777)]
            public void Takes_a_port_from_1_to_65535(string typed, int port)
            {
                var result = HostingRules.TypedPort(typed, 27015);

                result.ShouldBe(port);
            }

            [Theory]
            [InlineData("0")]
            [InlineData("65536")]
            [InlineData("-1")]
            [InlineData("")]
            [InlineData(null)]
            [InlineData("77a")]
            [InlineData("7777.0")]
            [InlineData("99999999999")]
            public void Keeps_the_port_for_anything_else(string typed)
            {
                var result = HostingRules.TypedPort(typed, 27015);

                result.ShouldBe(27015);
            }
        }

        public class MayRemovePlayer
        {
            [Theory]
            [InlineData(2, false)]
            [InlineData(3, true)]
            [InlineData(5, true)]
            public void Holds_above_two_players(int most, bool may)
            {
                var result = HostingRules.MayRemovePlayer(most);

                result.ShouldBe(may);
            }
        }

        public class MayAddPlayer
        {
            [Theory]
            [InlineData(4, true)]
            [InlineData(5, false)]
            [InlineData(2, true)]
            public void Holds_under_five_players(int most, bool may)
            {
                var result = HostingRules.MayAddPlayer(most);

                result.ShouldBe(may);
            }
        }

        public class StackSizes
        {
            [Fact]
            public void Lists_the_five_choices_for_one_of_them()
            {
                var sizes = HostingRules.StackSizes(20);

                sizes.ShouldBe(new[] { 0, 10, 20, 50, 99 });
            }

            [Theory]
            [InlineData(30, new[] { 0, 10, 20, 30, 50, 99 })]
            [InlineData(500, new[] { 0, 10, 20, 50, 99, 500 })]
            [InlineData(-1, new[] { -1, 0, 10, 20, 50, 99 })]
            public void Adds_another_size_in_order(int current, int[] sizes)
            {
                var result = HostingRules.StackSizes(current);

                result.ShouldBe(sizes);
            }
        }

        public class HostsWithSteam
        {
            [Theory]
            [InlineData(true, "steam", true)]
            [InlineData(true, "", true)]
            [InlineData(true, null, true)]
            [InlineData(true, "Direct", true)]
            [InlineData(true, "direct", false)]
            [InlineData(false, "steam", false)]
            [InlineData(false, "direct", false)]
            public void Hosts_with_Steam_unless_it_is_down_or_direct(
                bool steamUp,
                string hostWith,
                bool steam)
            {
                var result = HostingRules.HostsWithSteam(steamUp, hostWith);

                result.ShouldBe(steam);
            }
        }
    }
}
