using DarkwoodMP.Combat.Joiners;

namespace DarkwoodMP.Tests.Combat.Joiners
{
    public class WardRulesTests
    {
        /// An enemy after a joiner who carries both wards, asked about
        /// the shadow ward; the host's player has none.
        static readonly WardFacts Warded = new WardFacts
        {
            StandIn = true,
            Kind = WardKind.ShadowWard,
            ShadowWard = true,
            ForestSpiritWard = true,
        };

        public class TargetsStandIn
        {
            [Theory]
            [InlineData(1, true, true, true)]
            [InlineData(0, true, true, false)]
            [InlineData(1, false, true, false)]
            [InlineData(1, true, false, false)]
            public void Counts_a_stand_in_target_while_there_are_any(
                int standIns,
                bool target,
                bool standIn,
                bool counts)
            {
                var result = WardRules.TargetsStandIn(
                    standIns,
                    target,
                    standIn);

                result.ShouldBe(counts);
            }
        }

        public class HasEffect
        {
            [Fact]
            public void Gives_the_stand_in_s_shadow_ward()
            {
                WardRules.HasEffect(Warded).ShouldBeTrue();
            }

            [Fact]
            public void Gives_no_shadow_ward_to_a_stand_in_without_one()
            {
                var ward = Warded with { ShadowWard = false };

                WardRules.HasEffect(ward).ShouldBeFalse();
            }

            [Fact]
            public void Gives_the_stand_in_s_forest_spirit_ward()
            {
                var ward = Warded with
                {
                    Kind = WardKind.ForestSpiritWard,
                    ShadowWard = false,
                };

                WardRules.HasEffect(ward).ShouldBeTrue();
            }

            [Fact]
            public void Gives_no_forest_spirit_ward_without_one()
            {
                var ward = Warded with
                {
                    Kind = WardKind.ForestSpiritWard,
                    ForestSpiritWard = false,
                };

                WardRules.HasEffect(ward).ShouldBeFalse();
            }

            [Fact]
            public void Gives_a_stand_in_no_other_effect()
            {
                var ward = Warded with
                {
                    Kind = WardKind.Other,
                    HostHas = true,
                };

                WardRules.HasEffect(ward).ShouldBeFalse();
            }

            [Fact]
            public void Asks_the_host_s_player_for_another_target()
            {
                var ward = Warded with { StandIn = false, HostHas = true };

                WardRules.HasEffect(ward).ShouldBeTrue();
            }

            [Fact]
            public void Ignores_the_stand_in_s_wards_for_another_target()
            {
                var ward = Warded with { StandIn = false };

                WardRules.HasEffect(ward).ShouldBeFalse();
            }
        }

        public class SkillOf
        {
            [Theory]
            [InlineData(true, true, false, true)]
            [InlineData(true, false, true, false)]
            [InlineData(false, false, true, true)]
            [InlineData(false, true, false, false)]
            public void Asks_the_stand_in_else_the_host(
                bool standIn,
                bool standInHas,
                bool hostHas,
                bool has)
            {
                var result = WardRules.SkillOf(standIn, standInHas, hostHas);

                result.ShouldBe(has);
            }
        }
    }
}
