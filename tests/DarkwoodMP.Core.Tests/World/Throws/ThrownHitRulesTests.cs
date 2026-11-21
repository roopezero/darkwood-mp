using DarkwoodMP.World.Throws;

namespace DarkwoodMP.Tests.World.Throws
{
    public class ThrownHitRulesTests
    {
        /// A client inside a thrown thing's explosion, no flame touching,
        /// in the shared world.
        static readonly SelfHitFacts Caught = new SelfHitFacts
        {
            ClientMode = true,
            Depth = 1,
        };

        public class Now
        {
            [Fact]
            public void Counts_a_hit_inside_a_thrown_thing()
            {
                var hit = new ThrownHitFacts { Depth = 1 };

                ThrownHitRules.Now(hit).ShouldBeTrue();
            }

            [Fact]
            public void Counts_a_flame_under_what_a_throw_spawned()
            {
                var hit = new ThrownHitFacts
                {
                    Flame = true,
                    UnderSpawned = true,
                };

                ThrownHitRules.Now(hit).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_any_other_flame()
            {
                var hit = new ThrownHitFacts { Flame = true };

                ThrownHitRules.Now(hit).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_hit_with_no_flame_and_no_throw()
            {
                var hit = new ThrownHitFacts { UnderSpawned = true };

                ThrownHitRules.Now(hit).ShouldBeFalse();
            }
        }

        public class HostMayReport
        {
            [Fact]
            public void Leaves_an_explosion_on_a_client_to_the_host()
            {
                ThrownHitRules.HostMayReport(Caught).ShouldBeTrue();
            }

            [Fact]
            public void Reports_its_own_on_the_host()
            {
                var hit = Caught with { ClientMode = false };

                ThrownHitRules.HostMayReport(hit).ShouldBeFalse();
            }

            [Fact]
            public void Reports_a_hit_outside_anything_thrown()
            {
                var hit = Caught with { Depth = 0 };

                ThrownHitRules.HostMayReport(hit).ShouldBeFalse();
            }

            [Fact]
            public void Reports_a_flame()
            {
                var hit = Caught with { Flame = true };

                ThrownHitRules.HostMayReport(hit).ShouldBeFalse();
            }

            [Fact]
            public void Reports_a_hit_while_away()
            {
                var hit = Caught with { Away = true };

                ThrownHitRules.HostMayReport(hit).ShouldBeFalse();
            }
        }

        public class HostMayReportEffect
        {
            [Fact]
            public void Leaves_an_effect_on_our_player_to_the_host()
            {
                ThrownHitRules.HostMayReportEffect(Caught, true).ShouldBeTrue();
            }

            [Fact]
            public void Applies_an_effect_on_another_character()
            {
                var skip = ThrownHitRules.HostMayReportEffect(Caught, false);

                skip.ShouldBeFalse();
            }

            [Fact]
            public void Applies_an_effect_the_host_doesnt_report()
            {
                var hit = Caught with { Away = true };

                var skip = ThrownHitRules.HostMayReportEffect(hit, true);

                skip.ShouldBeFalse();
            }
        }
    }
}
