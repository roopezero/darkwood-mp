using DarkwoodMP.World.Scripted;

namespace DarkwoodMP.Tests.World.Scripted
{
    public class LaterRulesTests
    {
        /// Our player sets off an owed trigger while sharing, and after its
        /// delay it is on, its requirements hold and nothing is asked.
        static readonly LaterFacts Owed = new LaterFacts
        {
            Matches = true,
            Owed = true,
            Active = true,
            Sharing = true,
            SettingOff = true,
            Met = true,
        };

        public class LaterFor
        {
            [Fact]
            public void Runs_an_owed_trigger_as_our_part()
            {
                LaterRules.LaterFor(Owed).ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_trigger_of_another_type()
            {
                var f = Owed with { Matches = false };

                LaterRules.LaterFor(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_trigger_not_owed()
            {
                var f = Owed with { Owed = false };

                LaterRules.LaterFor(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_it_with_no_session()
            {
                var f = Owed with { Active = false };

                LaterRules.LaterFor(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_it_when_not_sharing()
            {
                var f = Owed with { Sharing = false };

                LaterRules.LaterFor(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_it_inside_a_part_being_run()
            {
                var f = Owed with { Current = EventPart.SharedOnly };

                LaterRules.LaterFor(f).ShouldBeFalse();
            }

            [Fact]
            public void Skips_what_our_player_isnt_setting_off()
            {
                var f = Owed with { SettingOff = false };

                LaterRules.LaterFor(f).ShouldBeFalse();
            }
        }

        public class MayStart
        {
            [Fact]
            public void Starts_an_owed_trigger()
            {
                LaterRules.MayStart(Owed).ShouldBeTrue();
            }

            [Fact]
            public void Stops_at_a_switched_off_trigger()
            {
                var f = Owed with { Disabled = true };

                LaterRules.MayStart(f).ShouldBeFalse();
            }

            [Fact]
            public void Stops_at_a_requirement_not_met()
            {
                var f = Owed with { Met = false };

                LaterRules.MayStart(f).ShouldBeFalse();
            }

            [Fact]
            public void Stops_at_a_part_no_longer_owed()
            {
                var f = Owed with { Owed = false };

                LaterRules.MayStart(f).ShouldBeFalse();
            }

            [Fact]
            public void Stops_while_asking_for_it()
            {
                var f = Owed with { Asking = true };

                LaterRules.MayStart(f).ShouldBeFalse();
            }
        }

        public class WaitsWarmup
        {
            [Theory]
            [InlineData(2f, true, true)]
            [InlineData(-1f, true, true)]
            [InlineData(0f, true, false)]
            [InlineData(2f, false, false)]
            public void Waits_for_an_areas_warmup(
                float warmup,
                bool area,
                bool waits)
            {
                LaterRules.WaitsWarmup(warmup, area).ShouldBe(waits);
            }
        }

        public class ItemFits
        {
            [Theory]
            [InlineData(true, "key_cellar", "key_cellar", true)]
            [InlineData(true, "key_cellar", "plank", false)]
            [InlineData(true, "", "plank", true)]
            [InlineData(true, null, "plank", true)]
            [InlineData(false, "key_cellar", "plank", true)]
            public void Runs_an_item_trigger_only_for_its_item(
                bool itemType,
                string reacts,
                string value,
                bool fits)
            {
                LaterRules.ItemFits(itemType, reacts, value).ShouldBe(fits);
            }
        }

        public class EventsRun
        {
            [Theory]
            [InlineData(true, true, false, true)]
            [InlineData(true, false, true, true)]
            [InlineData(true, false, false, false)]
            [InlineData(false, true, true, false)]
            public void Runs_events_that_exist_and_are_active_or_may_be(
                bool exists,
                bool active,
                bool inactive,
                bool runs)
            {
                var events = new EventsFacts
                {
                    Exists = exists,
                    Active = active,
                    RunsInactive = inactive,
                };

                LaterRules.EventsRun(events).ShouldBe(runs);
            }

            [Fact]
            public void Runs_events_that_ran_once()
            {
                var events = new EventsFacts
                {
                    Exists = true,
                    Active = true,
                    Fired = true,
                };

                LaterRules.EventsRun(events).ShouldBeTrue();
            }
        }
    }
}
