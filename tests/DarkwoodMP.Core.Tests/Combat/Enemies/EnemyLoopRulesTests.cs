using DarkwoodMP.Combat.Enemies;

namespace DarkwoodMP.Tests.Combat.Enemies
{
    public class EnemyLoopRulesTests
    {
        static readonly LoopNames Dog = new LoopNames
        {
            Exists = true,
            Idle = "dog_idle",
            IdleAggressive = "dog_idle_aggr",
            Aggressive = "dog_aggr",
            Defensive = "dog_def",
            Sleeping = "dog_sleep",
            Escaping = "dog_escape",
        };

        /// A living enemy whose loop is the one the clients were told.
        static readonly LoopFacts Told = new LoopFacts
        {
            Exists = true,
            InPlay = true,
            Current = "dog_idle",
            IsLoop = true,
            Told = "dog_idle",
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_check_and_resend_times()
            {
                var limits = new[]
                {
                    EnemyLoopRules.LoopCheckInterval,
                    EnemyLoopRules.LoopResendInterval,
                };

                limits.ShouldBe(new[] { 0.5f, 2f });
            }
        }

        public class IsLoop
        {
            [Theory]
            [InlineData("dog_idle")]
            [InlineData("dog_idle_aggr")]
            [InlineData("dog_aggr")]
            [InlineData("dog_def")]
            [InlineData("dog_sleep")]
            [InlineData("dog_escape")]
            public void Takes_each_of_the_six_loops(string audio)
            {
                var result = EnemyLoopRules.IsLoop(Dog, audio);

                result.ShouldBeTrue();
            }

            [Fact]
            public void Refuses_another_sound()
            {
                var result = EnemyLoopRules.IsLoop(Dog, "dog_bark");

                result.ShouldBeFalse();
            }

            [Fact]
            public void Refuses_any_sound_with_no_character_sounds()
            {
                var loops = Dog with { Exists = false };

                var result = EnemyLoopRules.IsLoop(loops, "dog_idle");

                result.ShouldBeFalse();
            }
        }

        public class CurrentLoop
        {
            [Fact]
            public void Takes_the_playing_loop()
            {
                var loop = EnemyLoopRules.CurrentLoop(true, "dog_idle");

                loop.ShouldBe("dog_idle");
            }

            [Theory]
            [InlineData(false, "dog_idle")]
            [InlineData(true, "")]
            [InlineData(true, null)]
            public void Says_none_without_a_playing_loop(
                bool playing,
                string audio)
            {
                var loop = EnemyLoopRules.CurrentLoop(playing, audio);

                loop.ShouldBe("");
            }
        }

        public class Check
        {
            [Fact]
            public void Sends_nothing_for_the_same_loop_before_the_resend()
            {
                var send = EnemyLoopRules.Check(Told);

                send.ShouldBe(LoopSend.None);
            }

            [Fact]
            public void Sends_the_same_loop_again_at_the_resend()
            {
                var loop = Told with { Resend = true };

                var send = EnemyLoopRules.Check(loop);

                send.ShouldBe(LoopSend.Again);
            }

            [Fact]
            public void Sends_a_changed_loop()
            {
                var loop = Told with { Current = "dog_aggr" };

                var send = EnemyLoopRules.Check(loop);

                send.ShouldBe(LoopSend.Changed);
            }

            [Fact]
            public void Sends_a_stopped_loop_as_a_change()
            {
                var loop = Told with { Current = "", IsLoop = false };

                var send = EnemyLoopRules.Check(loop);

                send.ShouldBe(LoopSend.Changed);
            }

            [Fact]
            public void Sends_a_change_even_when_the_resend_is_due()
            {
                var loop = Told with { Current = "dog_aggr", Resend = true };

                var send = EnemyLoopRules.Check(loop);

                send.ShouldBe(LoopSend.Changed);
            }

            [Fact]
            public void Sends_nothing_for_a_sound_that_is_not_a_loop()
            {
                var loop = Told with
                {
                    Current = "dog_bark",
                    IsLoop = false,
                    Resend = true,
                };

                var send = EnemyLoopRules.Check(loop);

                send.ShouldBe(LoopSend.None);
            }

            [Fact]
            public void Sends_nothing_for_a_destroyed_enemy()
            {
                var loop = Told with { Exists = false, Resend = true };

                var send = EnemyLoopRules.Check(loop);

                send.ShouldBe(LoopSend.None);
            }

            [Fact]
            public void Sends_nothing_once_its_death_is_timed()
            {
                var loop = Told with { Dead = true, Current = "dog_aggr" };

                var send = EnemyLoopRules.Check(loop);

                send.ShouldBe(LoopSend.None);
            }

            [Fact]
            public void Sends_nothing_for_an_enemy_out_of_play()
            {
                var loop = Told with { InPlay = false, Current = "dog_aggr" };

                var send = EnemyLoopRules.Check(loop);

                send.ShouldBe(LoopSend.None);
            }
        }

        public class Stopped
        {
            [Theory]
            [InlineData(true, "", false, true)]
            [InlineData(true, null, false, true)]
            [InlineData(false, "", false, false)]
            [InlineData(true, "dog_aggr", false, false)]
            [InlineData(true, "", true, false)]
            public void Sees_a_loop_stopped_with_none_after(
                bool before,
                string asked,
                bool after,
                bool stopped)
            {
                var result = EnemyLoopRules.Stopped(before, asked, after);

                result.ShouldBe(stopped);
            }
        }
    }
}
