using DarkwoodMP.World.Dragging;

namespace DarkwoodMP.Tests.World.Dragging
{
    public partial class PushRulesTests
    {
        public class PutsAtHost
        {
            [Fact]
            public void Moves_a_loose_copy_to_the_hosts_place()
            {
                PushRules.PutsAtHost(Copy).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_it_on_the_host()
            {
                var place = Copy with { Client = false };

                PushRules.PutsAtHost(place).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_thing_that_isnt_loose()
            {
                var place = Copy with { Loose = false };

                PushRules.PutsAtHost(place).ShouldBeFalse();
            }

            [Theory]
            [InlineData(true, false, false, false)]
            [InlineData(false, true, false, false)]
            [InlineData(false, false, true, false)]
            [InlineData(false, false, false, true)]
            public void Leaves_a_held_thing(
                bool kinematic,
                bool dragged,
                bool settling,
                bool shown)
            {
                var place = Copy with
                {
                    Kinematic = kinematic,
                    Dragged = dragged,
                    Settling = settling,
                    Shown = shown,
                };

                PushRules.PutsAtHost(place).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_copy_just_under_half_a_unit_away()
            {
                var place = Copy with { Distance = 0.49f };

                PushRules.PutsAtHost(place).ShouldBeFalse();
            }

            [Fact]
            public void Moves_a_copy_exactly_half_a_unit_away()
            {
                var place = Copy with { Distance = 0.5f };

                PushRules.PutsAtHost(place).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_thing_someone_else_drags()
            {
                var place = Copy with { Other = 2 };

                PushRules.PutsAtHost(place).ShouldBeFalse();
            }
        }

        public class Joiner
        {
            [Fact]
            public void Sends_a_thing_that_moved_and_rests()
            {
                PushRules.Joiner(Moved).ShouldBe(JoinerPlace.Send);
            }

            [Fact]
            public void Skips_a_thing_that_barely_moved()
            {
                var thing = Moved with { Moved = 0.49f, Turned = 1.99f };

                PushRules.Joiner(thing).ShouldBe(JoinerPlace.Skip);
            }

            [Fact]
            public void Sends_a_thing_turned_exactly_two_degrees()
            {
                var thing = Moved with { Moved = 0f, Turned = 2f };

                PushRules.Joiner(thing).ShouldBe(JoinerPlace.Send);
            }

            [Fact]
            public void Skips_a_thing_that_is_gone()
            {
                var thing = Moved with { Gone = true };

                PushRules.Joiner(thing).ShouldBe(JoinerPlace.Skip);
            }

            [Theory]
            [InlineData(false, false, false, false)]
            [InlineData(true, true, false, false)]
            [InlineData(true, false, true, false)]
            [InlineData(true, false, false, true)]
            public void Skips_a_thing_not_loose_or_held(
                bool loose,
                bool kinematic,
                bool dragged,
                bool shown)
            {
                var thing = Moved with
                {
                    Loose = loose,
                    Kinematic = kinematic,
                    Dragged = dragged,
                    Shown = shown,
                };

                PushRules.Joiner(thing).ShouldBe(JoinerPlace.Skip);
            }

            [Fact]
            public void Leaves_a_watched_thing_still_moving_for_later()
            {
                var thing = Moved with { AtRest = false, Watched = true };

                PushRules.Joiner(thing).ShouldBe(JoinerPlace.Later);
            }

            [Fact]
            public void Sends_a_watched_thing_that_rests()
            {
                var thing = Moved with { Watched = true };

                PushRules.Joiner(thing).ShouldBe(JoinerPlace.Send);
            }

            [Fact]
            public void Skips_an_unwatched_thing_still_moving()
            {
                var thing = Moved with { AtRest = false };

                PushRules.Joiner(thing).ShouldBe(JoinerPlace.Skip);
            }
        }
    }
}
