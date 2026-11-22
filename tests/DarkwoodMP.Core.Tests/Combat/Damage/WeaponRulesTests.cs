using DarkwoodMP.Combat.Damage;

namespace DarkwoodMP.Tests.Combat.Damage
{
    public class WeaponRulesTests
    {
        /// A weapon of 10 damage with no upgrades: 10 * 2 * 1.25 = 25.
        static readonly WeaponFacts Plain = new WeaponFacts
        {
            HasClass = true,
            Damage = 10,
            SpecialDamage = 0,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_weapon_limits()
            {
                var limits = new[]
                {
                    WeaponRules.BareHandsCap,
                    WeaponRules.LeastCap,
                    WeaponRules.StrongSkill,
                    WeaponRules.Margin,
                };

                limits.ShouldBe(new[] { 40f, 10f, 2f, 1.25f });
            }
        }

        public class DamageCap
        {
            [Fact]
            public void Gives_bare_hands_40()
            {
                var weapon = new WeaponFacts();

                var cap = WeaponRules.DamageCap(weapon);

                cap.ShouldBe(40f);
            }

            [Fact]
            public void Gives_bare_hands_40_whatever_the_numbers()
            {
                var weapon = Plain with { HasClass = false, Damage = 500 };

                var cap = WeaponRules.DamageCap(weapon);

                cap.ShouldBe(40f);
            }

            [Fact]
            public void Doubles_the_damage_with_a_margin()
            {
                var cap = WeaponRules.DamageCap(Plain);

                cap.ShouldBe(25f);
            }

            [Fact]
            public void Keeps_the_floor_at_zero_damage()
            {
                var weapon = Plain with { Damage = 0 };

                var cap = WeaponRules.DamageCap(weapon);

                cap.ShouldBe(10f);
            }

            [Theory]
            [InlineData(4, 10f)]
            [InlineData(5, 12.5f)]
            public void Gives_the_floor_until_the_damage_passes_it(
                int damage, float expected)
            {
                var weapon = Plain with { Damage = damage };

                var cap = WeaponRules.DamageCap(weapon);

                cap.ShouldBe(expected);
            }

            [Fact]
            public void Takes_the_special_damage_when_larger()
            {
                var weapon = Plain with { SpecialDamage = 20 };

                var cap = WeaponRules.DamageCap(weapon);

                cap.ShouldBe(50f);
            }

            [Fact]
            public void Adds_up_the_upgrades_over_one()
            {
                var weapon = Plain with
                {
                    UpgradeModifiers = new[] { 1.5f, 1.2f },
                };

                var cap = WeaponRules.DamageCap(weapon);

                cap.ShouldBe(42.5f, 0.001f);
            }

            [Theory]
            [InlineData(0.8f)]
            [InlineData(1f)]
            public void Ignores_an_upgrade_of_one_or_less(float modifier)
            {
                var weapon = Plain with
                {
                    UpgradeModifiers = new[] { modifier },
                };

                var cap = WeaponRules.DamageCap(weapon);

                cap.ShouldBe(25f);
            }

            [Fact]
            public void Gives_NaN_for_an_endless_upgrade_on_no_damage()
            {
                var weapon = Plain with
                {
                    Damage = 0,
                    UpgradeModifiers = new[] { float.PositiveInfinity },
                };

                var cap = WeaponRules.DamageCap(weapon);

                float.IsNaN(cap).ShouldBeTrue();
            }
        }

        public class Applied
        {
            [Theory]
            [InlineData(-5f, 0f)]
            [InlineData(0f, 0f)]
            [InlineData(20f, 20f)]
            [InlineData(25f, 25f)]
            [InlineData(25.5f, 25f)]
            [InlineData(float.PositiveInfinity, 25f)]
            public void Keeps_the_hit_between_zero_and_the_cap(
                float damage, float expected)
            {
                var applied = WeaponRules.Applied(damage, 25f);

                applied.ShouldBe(expected);
            }

            [Fact]
            public void Leaves_NaN_as_NaN()
            {
                var applied = WeaponRules.Applied(float.NaN, 25f);

                float.IsNaN(applied).ShouldBeTrue();
            }
        }

        public class Cut
        {
            [Theory]
            [InlineData(30f, 25f, true)]
            [InlineData(25f, 25f, false)]
            [InlineData(-5f, 0f, false)]
            public void Says_when_the_applied_hit_is_less(
                float damage, float applied, bool expected)
            {
                var cut = WeaponRules.Cut(damage, applied);

                cut.ShouldBe(expected);
            }
        }
    }
}
