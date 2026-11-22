using DarkwoodMP.World.Pause;

namespace DarkwoodMP.Tests.World.Pause
{
    public class PauseRulesTests
    {
        /// A host in a world with one joiner.
        static readonly CoopFacts Hosting = new CoopFacts
        {
            Active = true,
            Connected = true,
            InWorld = true,
            IsClient = false,
            Peers = 1,
        };

        /// In co-op, paused, frozen, with a player that can slide.
        static readonly PauseFacts Frozen = new PauseFacts
        {
            InCoop = true,
            Paused = true,
            TimeScale = 0f,
            Alive = true,
            HasBody = true,
            Kinematic = false,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_normal_scale_and_the_sound_fade()
            {
                var limits = new[] { PauseRules.Normal, PauseRules.SoundFade };

                limits.ShouldBe(new[] { 1f, 0.5f });
            }
        }

        public class InCoop
        {
            [Fact]
            public void Counts_a_host_with_a_joiner()
            {
                PauseRules.InCoop(Hosting).ShouldBeTrue();
            }

            [Fact]
            public void Counts_a_client_with_no_peers()
            {
                var coop = Hosting with { IsClient = true, Peers = 0 };

                PauseRules.InCoop(coop).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_out_a_host_alone()
            {
                var coop = Hosting with { Peers = 0 };

                PauseRules.InCoop(coop).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_an_inactive_session()
            {
                var coop = Hosting with { Active = false };

                PauseRules.InCoop(coop).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_session_not_connected()
            {
                var coop = Hosting with { Connected = false };

                PauseRules.InCoop(coop).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_out_a_game_with_no_world()
            {
                var coop = Hosting with { InWorld = false };

                PauseRules.InCoop(coop).ShouldBeFalse();
            }
        }

        public class RunsGamePause
        {
            [Theory]
            [InlineData(true, false)]
            [InlineData(false, true)]
            public void Runs_the_games_pause_only_alone(bool coop, bool runs)
            {
                PauseRules.RunsGamePause(coop).ShouldBe(runs);
            }
        }

        public class RunsScriptedSlowMotion
        {
            [Theory]
            [InlineData(true, false)]
            [InlineData(false, true)]
            public void Runs_slow_motion_only_alone(bool coop, bool runs)
            {
                PauseRules.RunsScriptedSlowMotion(coop).ShouldBe(runs);
            }
        }

        public class TimeScaleFor
        {
            [Theory]
            [InlineData(0.2f, true, 1f)]
            [InlineData(2f, true, 1f)]
            [InlineData(1f, true, 1f)]
            [InlineData(0f, true, 0f)]
            [InlineData(-1f, true, -1f)]
            [InlineData(0.2f, false, 0.2f)]
            public void Keeps_the_normal_rate_in_coop(
                float scale,
                bool coop,
                float kept)
            {
                PauseRules.TimeScaleFor(scale, coop).ShouldBe(kept);
            }
        }

        public class Tick
        {
            [Fact]
            public void Unfreezes_and_stands_still_when_frozen()
            {
                var tick = PauseRules.Tick(Frozen);

                tick.Unfreeze.ShouldBeTrue();
                tick.StandStill.ShouldBeTrue();
            }

            [Fact]
            public void Only_stands_still_while_time_runs()
            {
                var tick = PauseRules.Tick(Frozen with { TimeScale = 1f });

                tick.Unfreeze.ShouldBeFalse();
                tick.StandStill.ShouldBeTrue();
            }

            [Fact]
            public void Does_nothing_unpaused()
            {
                var tick = PauseRules.Tick(Frozen with { Paused = false });

                tick.ShouldBe(default(PauseTick));
            }

            [Fact]
            public void Does_nothing_alone()
            {
                var tick = PauseRules.Tick(Frozen with { InCoop = false });

                tick.ShouldBe(default(PauseTick));
            }

            [Fact]
            public void Leaves_a_kinematic_player_be()
            {
                var tick = PauseRules.Tick(Frozen with { Kinematic = true });

                tick.StandStill.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_dead_player_be()
            {
                var tick = PauseRules.Tick(Frozen with { Alive = false });

                tick.StandStill.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_player_with_no_body_be()
            {
                var tick = PauseRules.Tick(Frozen with { HasBody = false });

                tick.StandStill.ShouldBeFalse();
            }
        }
    }
}
