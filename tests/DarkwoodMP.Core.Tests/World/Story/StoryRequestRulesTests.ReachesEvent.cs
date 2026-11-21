using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public partial class StoryRequestRulesTests
    {
        public class ReachesEvent
        {
            [Fact]
            public void Reaches_the_event_of_the_next_chapter()
            {
                StoryRequestRules.ReachesEvent(Next).ShouldBeTrue();
            }

            [Fact]
            public void Reaches_it_from_a_joiner_who_wasnt_away()
            {
                var facts = Next with { EventFound = false };

                StoryRequestRules.ReachesEvent(facts).ShouldBeTrue();
            }

            [Fact]
            public void Stops_short_while_the_host_isnt_in_its_world()
            {
                var facts = Next with { HostInWorld = false };

                StoryRequestRules.ReachesEvent(facts).ShouldBeFalse();
            }

            [Fact]
            public void Stops_short_of_a_chapter_that_isnt_the_next()
            {
                var facts = Next with { Chapter = 3 };

                StoryRequestRules.ReachesEvent(facts).ShouldBeFalse();
            }

            [Fact]
            public void Stops_short_of_a_step_that_already_ran()
            {
                var facts = Next with { Ran = true };

                StoryRequestRules.ReachesEvent(facts).ShouldBeFalse();
            }
        }
    }
}
