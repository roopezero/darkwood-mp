using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class StoryRulesTests
    {
        /// A host with one joiner, in a ready world.
        static readonly SharingFacts Hosting = new SharingFacts
        {
            WorldReady = true,
            Host = true,
            Peers = 1,
        };

        /// A client in a ready world.
        static readonly SharingFacts Joined = new SharingFacts
        {
            WorldReady = true,
            Client = true,
        };

        /// A host with a joiner, paused in the world its baseline is of.
        static readonly BaselineFacts Paused = new BaselineFacts
        {
            Known = true,
            Active = true,
            InWorld = true,
            SameScene = true,
            Host = true,
            Peers = 1,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_interval_and_the_message_size()
            {
                (StoryRules.Interval, StoryRules.PerMessage)
                    .ShouldBe((2f, 64));
            }
        }

        public class Sharing
        {
            [Fact]
            public void Shares_on_a_host_with_a_joiner()
            {
                StoryRules.Sharing(Hosting).ShouldBeTrue();
            }

            [Fact]
            public void Shares_on_a_client()
            {
                StoryRules.Sharing(Joined).ShouldBeTrue();
            }

            [Fact]
            public void Holds_back_on_a_host_with_nobody_in()
            {
                var game = Hosting with { Peers = 0 };

                StoryRules.Sharing(game).ShouldBeFalse();
            }

            [Fact]
            public void Holds_back_before_the_world_is_ready()
            {
                var game = Joined with { WorldReady = false };

                StoryRules.Sharing(game).ShouldBeFalse();
            }

            [Fact]
            public void Holds_back_with_no_session()
            {
                var game = new SharingFacts { WorldReady = true };

                StoryRules.Sharing(game).ShouldBeFalse();
            }
        }

        public class KeepsBaseline
        {
            [Fact]
            public void Keeps_it_through_a_pause_in_the_same_world()
            {
                StoryRules.KeepsBaseline(Paused).ShouldBeTrue();
            }

            [Fact]
            public void Keeps_it_on_a_client()
            {
                var game = Paused with { Host = false, Client = true };

                StoryRules.KeepsBaseline(game).ShouldBeTrue();
            }

            [Fact]
            public void Drops_it_while_a_world_loads()
            {
                var game = Paused with { Loading = true };

                StoryRules.KeepsBaseline(game).ShouldBeFalse();
            }

            [Fact]
            public void Drops_it_in_another_scene()
            {
                var game = Paused with { SameScene = false };

                StoryRules.KeepsBaseline(game).ShouldBeFalse();
            }

            [Fact]
            public void Drops_it_outside_a_world()
            {
                var game = Paused with { InWorld = false };

                StoryRules.KeepsBaseline(game).ShouldBeFalse();
            }

            [Fact]
            public void Drops_it_without_an_active_session()
            {
                var game = Paused with { Active = false };

                StoryRules.KeepsBaseline(game).ShouldBeFalse();
            }

            [Fact]
            public void Drops_it_on_a_host_with_nobody_in()
            {
                var game = Paused with { Peers = 0 };

                StoryRules.KeepsBaseline(game).ShouldBeFalse();
            }

            [Fact]
            public void Drops_it_when_neither_hosting_nor_joined()
            {
                var game = Paused with { Host = false };

                StoryRules.KeepsBaseline(game).ShouldBeFalse();
            }

            [Fact]
            public void Has_none_to_keep_when_there_is_none()
            {
                var game = Paused with { Known = false };

                StoryRules.KeepsBaseline(game).ShouldBeFalse();
            }
        }

        public class IsDue
        {
            [Fact]
            public void Looks_exactly_at_the_next_time()
            {
                var due = StoryRules.IsDue(new Seconds(4f), new Seconds(4f));

                due.ShouldBeTrue();
            }

            [Fact]
            public void Waits_just_before_it()
            {
                var due = StoryRules.IsDue(
                    new Seconds(3.99f),
                    new Seconds(4f));

                due.ShouldBeFalse();
            }

            [Fact]
            public void Looks_at_the_start()
            {
                var due = StoryRules.IsDue(default, default);

                due.ShouldBeTrue();
            }
        }

        public class NextLook
        {
            [Fact]
            public void Looks_again_an_interval_later()
            {
                StoryRules.NextLook(new Seconds(3f)).Value.ShouldBe(5f);
            }
        }

        public class KeepsDreamEntry
        {
            [Theory]
            [InlineData(false, false, true)]
            [InlineData(false, true, true)]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            public void Keeps_a_dream_entry_only_in_a_shared_dream(
                bool inDream,
                bool sharedDream,
                bool kept)
            {
                StoryRules.KeepsDreamEntry(inDream, sharedDream)
                    .ShouldBe(kept);
            }
        }

        public class Route
        {
            [Theory]
            [InlineData("ra:Trader", false, StoryRoute.Reward)]
            [InlineData("ra:Trader", true, StoryRoute.Reward)]
            [InlineData("r:Trader", true, StoryRoute.Reputation)]
            [InlineData("r:Trader", false, StoryRoute.Reputation)]
            [InlineData("k", true, StoryRoute.HostsKarma)]
            [InlineData("k", false, StoryRoute.Apply)]
            [InlineData("n:Wolf", true, StoryRoute.NpcCheck)]
            [InlineData("n:Wolf", false, StoryRoute.Apply)]
            [InlineData("f:flag", true, StoryRoute.Apply)]
            [InlineData("je:a", true, StoryRoute.Apply)]
            [InlineData("", true, StoryRoute.Apply)]
            public void Routes_a_key_by_its_prefix(
                string key,
                bool host,
                StoryRoute route)
            {
                StoryRules.Route(key, host).ShouldBe(route);
            }
        }

        public class FlagHomeOf
        {
            [Fact]
            public void Leaves_the_players_own_flag()
            {
                var home = StoryRules.FlagHomeOf("player_inFirstHideout");

                home.ShouldBe(FlagHome.PlayersOwn);
            }

            [Fact]
            public void Puts_a_mod_flag_in_the_dictionary()
            {
                StoryRules.FlagHomeOf("dwmp_tank").ShouldBe(FlagHome.Mod);
            }

            [Fact]
            public void Gives_any_other_flag_to_the_game()
            {
                StoryRules.FlagHomeOf("met_wolf").ShouldBe(FlagHome.Game);
            }
        }

        public class PlayersOwn
        {
            [Theory]
            [InlineData("player_inFirstHideout")]
            [InlineData("player_inSecondHideout")]
            [InlineData("player_inThirdHideout")]
            [InlineData("player_shownSecondaryAttackPopup")]
            [InlineData("player_shownMapPopup")]
            [InlineData("player_shownActiveSkillPopup")]
            [InlineData("player_firstDrainReloadableItem")]
            public void Keeps_the_players_place_and_popups(string flag)
            {
                StoryRules.PlayersOwn(flag).ShouldBeTrue();
            }

            [Theory]
            [InlineData("player_diedAtLeastOneTime")]
            [InlineData("player_inFourthHideout")]
            [InlineData("Player_inFirstHideout")]
            public void Shares_other_flags(string flag)
            {
                StoryRules.PlayersOwn(flag).ShouldBeFalse();
            }
        }

        public class PlayersPlace
        {
            [Fact]
            public void Leaves_out_a_popup()
            {
                var place = StoryRules.PlayersPlace("player_shownMapPopup");

                place.ShouldBeFalse();
            }
        }

        public class PlayersPopup
        {
            [Fact]
            public void Leaves_out_a_place()
            {
                var popup = StoryRules.PlayersPopup("player_inFirstHideout");

                popup.ShouldBeFalse();
            }
        }

        public class ChunkLength
        {
            [Theory]
            [InlineData(0, 10, 10)]
            [InlineData(0, 64, 64)]
            [InlineData(0, 65, 64)]
            [InlineData(64, 65, 1)]
            [InlineData(64, 128, 64)]
            public void Sends_at_most_64_entries_a_message(
                int start,
                int total,
                int length)
            {
                StoryRules.ChunkLength(start, total).ShouldBe(length);
            }
        }
    }
}
