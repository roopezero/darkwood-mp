using DarkwoodMP.Net.Steam;

namespace DarkwoodMP.Tests.Net.Steam
{
    public class SteamStartupRulesTests
    {
        /// Steam not up yet, and a look due now.
        static readonly SteamCheckFacts Due = new SteamCheckFacts
        {
            Now = new Seconds(3f),
            Next = new Seconds(3f),
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_time_between_looks()
            {
                var every = SteamStartupRules.CheckEvery;

                every.ShouldBe(1f);
            }
        }

        public class CheckDue
        {
            [Fact]
            public void Looks_once_due()
            {
                var due = SteamStartupRules.CheckDue(Due);

                due.ShouldBeTrue();
            }

            [Fact]
            public void Waits_until_due()
            {
                var check = Due with { Now = new Seconds(2.99f) };

                var due = SteamStartupRules.CheckDue(check);

                due.ShouldBeFalse();
            }

            [Fact]
            public void Stops_once_steam_is_up()
            {
                var check = Due with { Up = true };

                var due = SteamStartupRules.CheckDue(check);

                due.ShouldBeFalse();
            }

            [Fact]
            public void Stops_once_steam_is_missing()
            {
                var check = Due with { Missing = true };

                var due = SteamStartupRules.CheckDue(check);

                due.ShouldBeFalse();
            }
        }

        public class NextCheck
        {
            [Fact]
            public void Looks_again_a_second_on()
            {
                var next = SteamStartupRules.NextCheck(new Seconds(3f));

                next.Value.ShouldBe(4f);
            }
        }

        public class Found
        {
            [Theory]
            [InlineData(false, true, true, SteamFound.Missing)]
            [InlineData(true, false, false, SteamFound.NotYet)]
            [InlineData(true, true, false, SteamFound.NotYet)]
            [InlineData(true, true, true, SteamFound.Ready)]
            public void Finds_steam_only_with_an_initialised_manager(
                bool fields,
                bool manager,
                bool ready,
                SteamFound found)
            {
                var result = SteamStartupRules.Found(fields, manager, ready);

                result.ShouldBe(found);
            }
        }
    }
}
