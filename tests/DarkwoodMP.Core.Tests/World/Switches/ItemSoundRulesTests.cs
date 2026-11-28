using DarkwoodMP.World.Switches;

namespace DarkwoodMP.Tests.World.Switches
{
    public class ItemSoundRulesTests
    {
        public class Starts
        {
            [Fact]
            public void Starts_an_item_with_nothing_playing()
            {
                var starts = ItemSoundRules.Starts(new ItemSoundFacts());

                starts.ShouldBeTrue();
            }

            [Fact]
            public void Keeps_the_loop_that_plays()
            {
                var sounds = new ItemSoundFacts { LoopPlays = true };

                var starts = ItemSoundRules.Starts(sounds);

                starts.ShouldBeFalse();
            }

            [Fact]
            public void Keeps_the_start_sound_that_plays()
            {
                var sounds = new ItemSoundFacts { StartPlays = true };

                var starts = ItemSoundRules.Starts(sounds);

                starts.ShouldBeFalse();
            }
        }
    }
}
