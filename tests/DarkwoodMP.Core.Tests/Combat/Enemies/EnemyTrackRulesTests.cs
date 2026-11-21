using DarkwoodMP.Combat.Enemies;

namespace DarkwoodMP.Tests.Combat.Enemies
{
    public class EnemyTrackRulesTests
    {
        /// A hostile NPC in play, with no calm timer.
        static readonly CalmFacts Hostile = new CalmFacts
        {
            Exists = true,
            Npc = true,
            InPlay = true,
            Hostile = true,
            Now = new Seconds(100f),
        };

        /// The same NPC calm since 92 s, at 100 s: exactly CalmTime.
        static readonly CalmFacts CalmAtLimit = Hostile with
        {
            Hostile = false,
            Calming = true,
            CalmSince = new Seconds(92f),
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_scan_and_calm_times()
            {
                var limits = new[]
                {
                    EnemyTrackRules.ScanInterval,
                    EnemyTrackRules.CalmTime,
                };

                limits.ShouldBe(new[] { 0.5f, 8f });
            }
        }

        public class Eligible
        {
            [Theory]
            [InlineData(false, false, true)]
            [InlineData(false, true, true)]
            [InlineData(true, false, false)]
            [InlineData(true, true, true)]
            public void Takes_creatures_and_the_npcs_it_runs(
                bool npc,
                bool running,
                bool eligible)
            {
                var result = EnemyTrackRules.Eligible(npc, running);

                result.ShouldBe(eligible);
            }
        }

        public class InPlay
        {
            [Theory]
            [InlineData(true, false, 0f, true)]
            [InlineData(false, true, 0.1f, true)]
            [InlineData(false, true, 0f, false)]
            [InlineData(false, false, 50f, false)]
            public void Counts_the_living_and_a_pre_death_with_health(
                bool alive,
                bool preDeath,
                float health,
                bool inPlay)
            {
                var result = EnemyTrackRules.InPlay(alive, preDeath, health);

                result.ShouldBe(inPlay);
            }
        }

        public class Tracks
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Tracks_an_eligible_character_it_has_not_got(
                bool eligible,
                bool tracked,
                bool tracks)
            {
                var result = EnemyTrackRules.Tracks(eligible, tracked);

                result.ShouldBe(tracks);
            }
        }

        public class Calm
        {
            [Fact]
            public void Switches_off_the_timer_of_a_hostile_npc()
            {
                var step = EnemyTrackRules.Calm(Hostile);

                step.ShouldBe(CalmStep.Hostile);
            }

            [Fact]
            public void Lets_go_of_a_destroyed_character()
            {
                var c = Hostile with { Exists = false };

                var step = EnemyTrackRules.Calm(c);

                step.ShouldBe(CalmStep.Gone);
            }

            [Fact]
            public void Keeps_a_creature_however_calm()
            {
                var c = CalmAtLimit with
                {
                    Npc = false,
                    Now = new Seconds(1000f),
                };

                var step = EnemyTrackRules.Calm(c);

                step.ShouldBe(CalmStep.Keep);
            }

            [Fact]
            public void Keeps_a_dead_npc_however_calm()
            {
                var c = CalmAtLimit with
                {
                    InPlay = false,
                    Now = new Seconds(1000f),
                };

                var step = EnemyTrackRules.Calm(c);

                step.ShouldBe(CalmStep.Keep);
            }

            [Fact]
            public void Starts_the_timer_of_an_npc_that_calmed_down()
            {
                var c = Hostile with { Hostile = false };

                var step = EnemyTrackRules.Calm(c);

                step.ShouldBe(CalmStep.StartCalm);
            }

            [Fact]
            public void Keeps_an_npc_calm_for_exactly_the_calm_time()
            {
                var step = EnemyTrackRules.Calm(CalmAtLimit);

                step.ShouldBe(CalmStep.Keep);
            }

            [Fact]
            public void Lets_go_of_an_npc_calm_just_past_the_calm_time()
            {
                var c = CalmAtLimit with { Now = new Seconds(100.01f) };

                var step = EnemyTrackRules.Calm(c);

                step.ShouldBe(CalmStep.Gone);
            }

            [Fact]
            public void Switches_off_the_timer_when_a_calm_npc_turns_again()
            {
                var c = CalmAtLimit with { Hostile = true };

                var step = EnemyTrackRules.Calm(c);

                step.ShouldBe(CalmStep.Hostile);
            }
        }

        public class KindOf
        {
            [Fact]
            public void Takes_the_prefab_name()
            {
                var kind = EnemyTrackRules.KindOf("dog", "dog_village(Clone)");

                kind.ShouldBe("dog");
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            public void Takes_the_name_without_clone_with_no_prefab(
                string prefab)
            {
                var kind = EnemyTrackRules.KindOf(prefab, "savage (Clone)");

                kind.ShouldBe("savage");
            }
        }

        public class Due
        {
            [Theory]
            [InlineData(9.99f, false)]
            [InlineData(10f, true)]
            [InlineData(10.5f, true)]
            public void Is_due_once_now_reaches_the_next_time(
                float now,
                bool due)
            {
                var result = EnemyTrackRules.Due(
                    new Seconds(now),
                    new Seconds(10f));

                result.ShouldBe(due);
            }
        }
    }
}
