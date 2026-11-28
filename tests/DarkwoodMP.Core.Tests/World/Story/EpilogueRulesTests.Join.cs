using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public partial class EpilogueRulesTests
    {
        /// A dream in this preset.
        static DreamFacts DreamIn(string preset) => new DreamFacts
        {
            Exists = true,
            Dreaming = true,
            HasPreset = true,
            Preset = preset,
        };

        public class JoinRefusal
        {
            /// The host playing the epilogue, before its end.
            static readonly EpilogueJoinFacts Playing = new EpilogueJoinFacts
            {
                Playing = true,
                Host = true,
            };

            [Fact]
            public void Allows_a_joiner_while_the_host_plays_the_epilogue()
            {
                EpilogueRules.JoinRefusal(Playing).ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_joiner_at_the_epilogues_end()
            {
                var verdict =
                    EpilogueRules.JoinRefusal(Playing with { AtEnd = true });

                verdict.Reason.ShouldBe(Refusal.PlayingEnding);
                Refusals.Text(verdict).ShouldBe(
                    "The host's game is at the very end of the epilogue,"
                        + " about to go to the credits.");
            }

            [Theory]
            [InlineData(false, true)]
            [InlineData(true, false)]
            public void Allows_a_joiner_otherwise(bool playing, bool host)
            {
                var facts = new EpilogueJoinFacts
                {
                    Playing = playing,
                    Host = host,
                    AtEnd = true,
                };

                EpilogueRules.JoinRefusal(facts).ShouldBe(Verdict.Allow);
            }
        }

        public class BetweenParts
        {
            [Fact]
            public void Lets_a_joiner_into_a_part_that_plays()
            {
                var dreams = DreamIn("epilog_part1b_corridor_dream");

                EpilogueRules.BetweenParts(dreams, false).ShouldBeFalse();
            }

            [Fact]
            public void Holds_a_joiner_while_a_part_switches()
            {
                var dreams =
                    DreamIn("epilog_part1b_corridor_dream") with
                    {
                        Switching = true,
                    };

                EpilogueRules.BetweenParts(dreams, false).ShouldBeTrue();
            }

            [Fact]
            public void Holds_a_joiner_while_the_first_part_is_on_its_way()
            {
                var dreams = new DreamFacts { Exists = true, Wanted = true };

                EpilogueRules.BetweenParts(dreams, false).ShouldBeTrue();
            }

            [Fact]
            public void Holds_a_joiner_while_a_location_loads()
            {
                var dreams = DreamIn("epilog_part1c_room_dream");

                EpilogueRules.BetweenParts(dreams, true).ShouldBeTrue();
            }
        }

        public class PartToJoin
        {
            [Fact]
            public void Names_the_hosts_part()
            {
                var dreams = DreamIn("epilog_part1c_room_dream");

                EpilogueRules.PartToJoin(dreams)
                    .ShouldBe("epilog_part1c_room_dream");
            }

            [Fact]
            public void Names_the_first_part_outside_an_epilogue_dream()
            {
                var dreams = DreamIn("dream_tutorial_01");

                EpilogueRules.PartToJoin(dreams)
                    .ShouldBe("epilog_part1a_dream");
            }

            [Fact]
            public void Names_the_first_part_with_no_dream()
            {
                EpilogueRules.PartToJoin(default)
                    .ShouldBe("epilog_part1a_dream");
            }
        }

        public class LoadedPart
        {
            [Fact]
            public void Puts_the_hosts_part_in_place_of_the_saves()
            {
                var part = EpilogueRules.LoadedPart(
                    "epilog_part1c_room_dream", "epilog_part1a_dream", true);

                part.ShouldBe("epilog_part1c_room_dream");
            }

            [Fact]
            public void Keeps_the_saves_dream_when_not_joining()
            {
                var part = EpilogueRules.LoadedPart(
                    null, "epilog_part1a_dream", true);

                part.ShouldBe("epilog_part1a_dream");
            }

            [Fact]
            public void Keeps_a_dream_that_isnt_loading_from_a_save()
            {
                var part = EpilogueRules.LoadedPart(
                    "epilog_part1c_room_dream", "epilog_part1b_dream", false);

                part.ShouldBe("epilog_part1b_dream");
            }

            [Fact]
            public void Keeps_a_dream_that_isnt_the_epilogues()
            {
                var part = EpilogueRules.LoadedPart(
                    "epilog_part1c_room_dream", "dream_lvl2", true);

                part.ShouldBe("dream_lvl2");
            }
        }

        public class Adopts
        {
            [Fact]
            public void Shares_a_hosts_epilogue_played_alone()
            {
                EpilogueRules.Adopts(true, true, false).ShouldBeTrue();
            }

            [Theory]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            [InlineData(true, true, true)]
            public void Leaves_it_otherwise(bool host, bool alone, bool over)
            {
                EpilogueRules.Adopts(host, alone, over).ShouldBeFalse();
            }
        }
    }
}
