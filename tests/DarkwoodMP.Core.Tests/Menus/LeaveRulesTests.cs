using System;
using DarkwoodMP.Menus;

namespace DarkwoodMP.Tests.Menus
{
    public class LeaveRulesTests
    {
        static readonly DateTime Noon = new DateTime(2026, 11, 20, 12, 0, 0);

        /// Playing alone in the player's own world, saved 5 minutes ago.
        static readonly LeaveFacts Alone = new LeaveFacts
        {
            SaveKnown = true,
            Now = Noon,
            Saved = Noon.AddMinutes(-5),
        };

        const string Lost =
            "What you did since the last save (5 min ago) will be lost.";

        public class Text
        {
            [Fact]
            public void Says_how_long_ago_the_game_saved()
            {
                var text = LeaveRules.Text(Alone);

                text.ShouldBe(Lost);
            }

            [Fact]
            public void Says_the_host_s_copy_keeps_the_player_s_saves()
            {
                var leave = Alone with { HostCopy = true };

                var text = LeaveRules.Text(leave);

                text.ShouldBe(
                    "This is your copy of the host's world: your own saves"
                    + " are untouched.");
            }

            [Fact]
            public void Says_the_copy_even_when_the_save_time_is_unknown()
            {
                var leave = Alone with { HostCopy = true, SaveKnown = false };

                var text = LeaveRules.Text(leave);

                text.ShouldStartWith("This is your copy");
            }

            [Fact]
            public void Leaves_the_time_out_when_it_could_not_be_read()
            {
                var leave = Alone with { SaveKnown = false };

                var text = LeaveRules.Text(leave);

                text.ShouldBe("What you did since the last save will be lost.");
            }

            [Fact]
            public void Tells_a_host_with_players_they_go_back()
            {
                var leave = Alone with { Host = true, Peers = 1 };

                var text = LeaveRules.Text(leave);

                text.ShouldBe(
                    Lost + " The players in your game go back to their menus.");
            }

            [Fact]
            public void Says_nothing_more_to_a_host_alone()
            {
                var leave = Alone with { Host = true, Peers = 0 };

                var text = LeaveRules.Text(leave);

                text.ShouldBe(Lost);
            }

            [Fact]
            public void Tells_a_joiner_they_leave_the_host_s_game()
            {
                var leave = Alone with { Client = true };

                var text = LeaveRules.Text(leave);

                text.ShouldBe(Lost + " You leave the host's game.");
            }

            [Fact]
            public void Tells_a_host_with_players_before_a_joiner()
            {
                var leave = Alone with
                {
                    Host = true,
                    Peers = 2,
                    Client = true,
                };

                var text = LeaveRules.Text(leave);

                text.ShouldEndWith("go back to their menus.");
            }
        }

        public class Ago
        {
            [Theory]
            [InlineData(0, "0 s")]
            [InlineData(59, "59 s")]
            [InlineData(60, "1 min")]
            [InlineData(119, "1 min")]
            [InlineData(3599, "59 min")]
            [InlineData(3600, "1 h 0 min")]
            [InlineData(3600 + 125, "1 h 2 min")]
            [InlineData(26 * 3600 + 60, "26 h 1 min")]
            [InlineData(-5, "0 s")]
            [InlineData(-7200, "0 s")]
            public void Says_hours_minutes_or_seconds(int seconds, string ago)
            {
                var text = LeaveRules.Ago(TimeSpan.FromSeconds(seconds));

                text.ShouldBe(ago);
            }

            [Fact]
            public void Cuts_a_minute_short_by_a_tick_to_seconds()
            {
                var since = TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(1);

                var text = LeaveRules.Ago(since);

                text.ShouldBe("59 s");
            }
        }
    }
}
