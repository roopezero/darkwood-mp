using DarkwoodMP.World.Rolls;

namespace DarkwoodMP.Tests.World.Rolls
{
    public class AmbushRulesTests
    {
        /// A call that checks requirements, which are met, on an event not
        /// started today.
        static readonly AmbushGateFacts Open = new AmbushGateFacts
        {
            CheckRequirements = true,
            RequirementsMet = true,
        };

        /// The host's player, alive and outdoors away from any location.
        static readonly AmbushHostFacts Outdoors = new AmbushHostFacts
        {
            Exists = true,
            Alive = true,
            Placed = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_ambush_limits()
            {
                (AmbushRules.FirstDay, AmbushRules.Radius, AmbushRules.Stamina)
                    .ShouldBe((2, 1500f, 1000f));
            }

            [Fact]
            public void Pins_the_redneck()
            {
                AmbushRules.Prefab.ShouldBe("Redneck02");
            }
        }

        public class Ours
        {
            [Theory]
            [InlineData(false, true, true, true)]
            [InlineData(true, true, true, false)]
            [InlineData(false, false, true, false)]
            [InlineData(false, true, false, false)]
            public void Takes_a_redneck_ambush_on_the_host_with_threat(
                bool client,
                bool threat,
                bool redneck,
                bool ours)
            {
                AmbushRules.Ours(client, threat, redneck).ShouldBe(ours);
            }
        }

        public class AsksRequirements
        {
            [Fact]
            public void Asks_when_the_call_checks_them()
            {
                AmbushRules.AsksRequirements(Open).ShouldBeTrue();
            }

            [Fact]
            public void Skips_when_the_call_doesnt_check()
            {
                var gate = Open with { CheckRequirements = false };

                AmbushRules.AsksRequirements(gate).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_disabled_event()
            {
                var gate = Open with { Disabled = true };

                AmbushRules.AsksRequirements(gate).ShouldBeFalse();
            }

            [Fact]
            public void Asks_for_a_forced_disabled_event()
            {
                var gate = Open with { Disabled = true, Force = true };

                AmbushRules.AsksRequirements(gate).ShouldBeTrue();
            }
        }

        public class GameWants
        {
            [Fact]
            public void Opens_with_requirements_met()
            {
                AmbushRules.GameWants(Open).ShouldBeTrue();
            }

            [Fact]
            public void Shuts_with_requirements_unmet()
            {
                var gate = Open with { RequirementsMet = false };

                AmbushRules.GameWants(gate).ShouldBeFalse();
            }

            [Fact]
            public void Opens_without_checking_requirements()
            {
                var gate = Open with
                {
                    CheckRequirements = false,
                    RequirementsMet = false,
                };

                AmbushRules.GameWants(gate).ShouldBeTrue();
            }

            [Fact]
            public void Shuts_once_started_today()
            {
                var gate = Open with { StartedToday = true };

                AmbushRules.GameWants(gate).ShouldBeFalse();
            }

            [Fact]
            public void Opens_again_when_many_times_a_day()
            {
                var gate = Open with
                {
                    StartedToday = true,
                    MultipleTimesPerDay = true,
                };

                AmbushRules.GameWants(gate).ShouldBeTrue();
            }

            [Fact]
            public void Shuts_when_disabled()
            {
                var gate = Open with { Disabled = true };

                AmbushRules.GameWants(gate).ShouldBeFalse();
            }

            [Fact]
            public void Opens_when_forced_after_starting_today()
            {
                var gate = Open with { Force = true, StartedToday = true };

                AmbushRules.GameWants(gate).ShouldBeTrue();
            }

            [Fact]
            public void Opens_when_forced_whatever_else()
            {
                var gate = new AmbushGateFacts
                {
                    Disabled = true,
                    Force = true,
                    CheckRequirements = true,
                    StartedToday = true,
                };

                AmbushRules.GameWants(gate).ShouldBeTrue();
            }
        }

        public class DayAllows
        {
            [Theory]
            [InlineData(true, 1, false)]
            [InlineData(true, 2, true)]
            [InlineData(true, 30, true)]
            [InlineData(false, 5, false)]
            public void Allows_day_two_on(bool controller, int day, bool ok)
            {
                AmbushRules.DayAllows(controller, day).ShouldBe(ok);
            }
        }

        public class HostCan
        {
            [Fact]
            public void Takes_the_host_outdoors()
            {
                AmbushRules.HostCan(Outdoors).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_missing_player()
            {
                var host = Outdoors with { Exists = false };

                AmbushRules.HostCan(host).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_dead_player()
            {
                var host = Outdoors with { Alive = false };

                AmbushRules.HostCan(host).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_player_inside()
            {
                var host = Outdoors with { Inside = true };

                AmbushRules.HostCan(host).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_player_with_no_whereabouts()
            {
                var host = Outdoors with { Placed = false };

                AmbushRules.HostCan(host).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_player_at_a_location()
            {
                var host = Outdoors with { AtLocation = true };

                AmbushRules.HostCan(host).ShouldBeFalse();
            }
        }

        public class JoinerCan
        {
            [Theory]
            [InlineData(true, false, false, true)]
            [InlineData(false, false, false, false)]
            [InlineData(true, true, false, false)]
            [InlineData(true, false, true, false)]
            public void Takes_a_living_joiner_outdoors(
                bool alive,
                bool inside,
                bool atLocation,
                bool can)
            {
                AmbushRules.JoinerCan(alive, inside, atLocation)
                    .ShouldBe(can);
            }
        }

        public class Choices
        {
            [Theory]
            [InlineData(0, true, 0)]
            [InlineData(0, false, 0)]
            [InlineData(1, false, 1)]
            [InlineData(1, true, 2)]
            [InlineData(3, true, 4)]
            public void Counts_the_joiners_and_the_host_if_it_can(
                int joiners,
                bool hostCan,
                int choices)
            {
                AmbushRules.Choices(joiners, hostCan).ShouldBe(choices);
            }
        }

        public class Target
        {
            [Theory]
            [InlineData(2, 0, 0)]
            [InlineData(2, 1, 1)]
            [InlineData(2, 2, -1)]
            public void Picks_a_joiner_or_the_host(
                int joiners,
                int roll,
                int target)
            {
                AmbushRules.Target(joiners, roll).ShouldBe(target);
            }
        }
    }
}
