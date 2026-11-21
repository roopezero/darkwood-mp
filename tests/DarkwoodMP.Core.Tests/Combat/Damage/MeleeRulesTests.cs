using DarkwoodMP.Combat.Damage;

namespace DarkwoodMP.Tests.Combat.Damage
{
    public class MeleeRulesTests
    {
        /// An enemy that sees the stand-in bites it for the first time.
        static readonly MeleeFacts Bite = new MeleeFacts
        {
            StandIn = true,
            EnemySensor = true,
            Attacker = true,
            AlreadyHit = false,
            NoSightNeeded = false,
            Sees = true,
            Damage = 20f,
            Strength = true,
            Modifier = 1.5f,
        };

        public class Touch
        {
            [Fact]
            public void Hits_a_stand_in_it_sees()
            {
                var outcome = MeleeRules.Touch(Bite);

                outcome.ShouldBe(MeleeOutcome.Hits);
            }

            [Fact]
            public void Leaves_anything_else_to_the_game()
            {
                var touch = Bite with { StandIn = false, Attacker = false };

                var outcome = MeleeRules.Touch(touch);

                outcome.ShouldBe(MeleeOutcome.Game);
            }

            [Fact]
            public void Skips_a_player_s_sensor()
            {
                var touch = Bite with { EnemySensor = false };

                var outcome = MeleeRules.Touch(touch);

                outcome.ShouldBe(MeleeOutcome.NotAnEnemyHit);
            }

            [Fact]
            public void Skips_a_sensor_with_no_attacker()
            {
                var touch = Bite with { Attacker = false, AlreadyHit = true };

                var outcome = MeleeRules.Touch(touch);

                outcome.ShouldBe(MeleeOutcome.NotAnEnemyHit);
            }

            [Fact]
            public void Skips_a_stand_in_hit_already()
            {
                var touch = Bite with { AlreadyHit = true, Sees = false };

                var outcome = MeleeRules.Touch(touch);

                outcome.ShouldBe(MeleeOutcome.AlreadyHit);
            }

            [Fact]
            public void Skips_a_stand_in_out_of_sight()
            {
                var touch = Bite with { Sees = false };

                var outcome = MeleeRules.Touch(touch);

                outcome.ShouldBe(MeleeOutcome.Unseen);
            }

            [Fact]
            public void Hits_out_of_sight_when_no_sight_is_needed()
            {
                var touch = Bite with { Sees = false, NoSightNeeded = true };

                var outcome = MeleeRules.Touch(touch);

                outcome.ShouldBe(MeleeOutcome.Hits);
            }
        }

        public class Damage
        {
            [Fact]
            public void Multiplies_by_the_attacker_s_strength()
            {
                var damage = MeleeRules.Damage(Bite);

                damage.ShouldBe(30f);
            }

            [Fact]
            public void Takes_the_sensor_s_damage_with_no_CharBase()
            {
                var touch = Bite with { Strength = false };

                var damage = MeleeRules.Damage(touch);

                damage.ShouldBe(20f);
            }
        }
    }
}
