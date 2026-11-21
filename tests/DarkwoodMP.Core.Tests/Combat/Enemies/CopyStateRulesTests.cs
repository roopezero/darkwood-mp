using DarkwoodMP.Combat.Enemies;

namespace DarkwoodMP.Tests.Combat.Enemies
{
    public class CopyStateRulesTests
    {
        /// A copy that died 2 s ago here: exactly the death animation.
        static readonly SettleFacts Dying = new SettleFacts
        {
            Exists = true,
            Dead = true,
            SinceDeath = new Seconds(2f),
        };

        /// A copy playing the host's clip, two frames behind it.
        static readonly ClipFacts Behind = new ClipFacts
        {
            Clip = 1234,
            Frame = 7,
            Library = true,
            Same = true,
            Current = 5,
            HasFrames = true,
            Frames = 12,
        };

        /// A dead copy made here, inside a location switched on, now off.
        static readonly CorpseFacts Corpse = new CorpseFacts
        {
            MadeHere = true,
            Exists = true,
            Within = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_death_time_health_step_and_frames()
            {
                var limits = new[]
                {
                    CopyStateRules.DeathAnimTime,
                    CopyStateRules.HealthStep,
                    CopyStateRules.MaxFramesOff,
                };

                limits.ShouldBe(new[] { 2f, 0.01f, 2f });
            }

            [Fact]
            public void Pins_the_one_event_a_copy_plays()
            {
                var name = CopyStateRules.TweenShadows;

                name.ShouldBe("TweenShadows");
            }
        }

        public class Health
        {
            [Fact]
            public void Sets_the_host_fraction_of_the_maximum()
            {
                var change = CopyStateRules.Health(200f, 200f, 0.5f);

                change.Health.ShouldBe(100f);
            }

            [Fact]
            public void Says_health_going_down_is_lost()
            {
                var change = CopyStateRules.Health(200f, 200f, 0.5f);

                change.Lost.ShouldBeTrue();
            }

            [Fact]
            public void Says_health_going_up_is_not_lost()
            {
                var change = CopyStateRules.Health(200f, 50f, 0.5f);

                change.Lost.ShouldBeFalse();
            }

            [Fact]
            public void Changes_nothing_with_no_maximum()
            {
                var change = CopyStateRules.Health(0f, 10f, 0.5f);

                change.Changes.ShouldBeFalse();
            }

            [Fact]
            public void Changes_nothing_just_under_the_health_step()
            {
                var change = CopyStateRules.Health(1f, 0f, 0.0099f);

                change.Changes.ShouldBeFalse();
            }

            [Fact]
            public void Changes_the_health_by_exactly_the_health_step()
            {
                var change = CopyStateRules.Health(1f, 0f, 0.01f);

                change.Changes.ShouldBeTrue();
            }

            [Theory]
            [InlineData(1.5f, 100f)]
            [InlineData(-0.5f, 0f)]
            public void Keeps_the_fraction_between_none_and_all(
                float fraction,
                float health)
            {
                var change = CopyStateRules.Health(100f, 50f, fraction);

                change.Health.ShouldBe(health);
            }

            [Fact]
            public void Takes_a_fraction_that_is_not_a_number_as_it_is()
            {
                var change = CopyStateRules.Health(100f, 50f, float.NaN);

                float.IsNaN(change.Health).ShouldBeTrue();
            }
        }

        public class SleepChanges
        {
            [Theory]
            [InlineData(true, false, true, true)]
            [InlineData(true, true, false, true)]
            [InlineData(true, true, true, false)]
            [InlineData(false, false, true, false)]
            public void Changes_a_living_copy_to_the_host_sleep(
                bool alive,
                bool sleeping,
                bool asleep,
                bool changes)
            {
                var result = CopyStateRules.SleepChanges(
                    alive,
                    sleeping,
                    asleep);

                result.ShouldBe(changes);
            }
        }

        public class ReplaysIdle
        {
            [Theory]
            [InlineData(true, true, false, true)]
            [InlineData(false, false, false, true)]
            [InlineData(false, true, true, true)]
            [InlineData(false, true, false, false)]
            public void Replays_the_idle_loop_unless_the_host_s_plays(
                bool asleep,
                bool playing,
                bool sleepingLoop,
                bool replays)
            {
                var sleep = new SleepFacts
                {
                    Asleep = asleep,
                    Playing = playing,
                    SleepingLoop = sleepingLoop,
                };

                var result = CopyStateRules.ReplaysIdle(sleep);

                result.ShouldBe(replays);
            }
        }

        public class MarksCut
        {
            [Theory]
            [InlineData(false, true, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Marks_a_dead_one_cut_in_half(
                bool alive,
                bool cut,
                bool marks)
            {
                var result = CopyStateRules.MarksCut(alive, cut);

                result.ShouldBe(marks);
            }
        }

        public class FirstDeath
        {
            [Theory]
            [InlineData(false, false, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Kills_a_copy_once(bool alive, bool timed, bool dies)
            {
                var result = CopyStateRules.FirstDeath(alive, timed);

                result.ShouldBe(dies);
            }
        }

        public class DeathHealth
        {
            [Fact]
            public void Lowers_the_health_to_the_host_fraction()
            {
                var health = CopyStateRules.DeathHealth(100f, 80f, 0.2f);

                health.ShouldBe(20f);
            }

            [Fact]
            public void Never_raises_the_health()
            {
                var health = CopyStateRules.DeathHealth(100f, 10f, 0.2f);

                health.ShouldBe(10f);
            }

            [Fact]
            public void Leaves_the_health_with_no_maximum()
            {
                var health = CopyStateRules.DeathHealth(0f, 10f, 0f);

                health.ShouldBe(10f);
            }
        }

        public class Settles
        {
            [Fact]
            public void Waits_for_exactly_the_death_time()
            {
                var settles = CopyStateRules.Settles(Dying);

                settles.ShouldBeFalse();
            }

            [Fact]
            public void Settles_just_past_the_death_time()
            {
                var copy = Dying with { SinceDeath = new Seconds(2.01f) };

                var settles = CopyStateRules.Settles(copy);

                settles.ShouldBeTrue();
            }

            [Fact]
            public void Settles_once()
            {
                var copy = Dying with
                {
                    SinceDeath = new Seconds(5f),
                    Settled = true,
                };

                var settles = CopyStateRules.Settles(copy);

                settles.ShouldBeFalse();
            }

            [Fact]
            public void Settles_no_living_copy()
            {
                var copy = Dying with
                {
                    SinceDeath = new Seconds(5f),
                    Dead = false,
                };

                var settles = CopyStateRules.Settles(copy);

                settles.ShouldBeFalse();
            }

            [Fact]
            public void Settles_no_copy_that_is_gone()
            {
                var copy = Dying with
                {
                    SinceDeath = new Seconds(5f),
                    Exists = false,
                };

                var settles = CopyStateRules.Settles(copy);

                settles.ShouldBeFalse();
            }
        }

        public class Draws
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Draws_a_copy_once_a_state_came(
                bool stated,
                bool exists,
                bool draws)
            {
                var result = CopyStateRules.Draws(stated, exists);

                result.ShouldBe(draws);
            }
        }

        public class SwitchesOn
        {
            [Theory]
            [InlineData(false, false, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Switches_on_a_copy_that_is_not_a_corpse_yet(
                bool activeSelf,
                bool settled,
                bool switches)
            {
                var result = CopyStateRules.SwitchesOn(activeSelf, settled);

                result.ShouldBe(switches);
            }
        }

        public class SettleAs
        {
            [Theory]
            [InlineData(true, true, SettleStep.Cut)]
            [InlineData(true, false, SettleStep.Cut)]
            [InlineData(false, false, SettleStep.Collider)]
            [InlineData(false, true, SettleStep.Corpse)]
            public void Settles_by_how_it_died(
                bool cut,
                bool animated,
                SettleStep settle)
            {
                var step = CopyStateRules.SettleAs(cut, animated);

                step.ShouldBe(settle);
            }
        }

        public class Clip
        {
            [Fact]
            public void Keeps_a_clip_exactly_two_frames_off()
            {
                var step = CopyStateRules.Clip(Behind);

                step.ShouldBe(ClipStep.Keep);
            }

            [Fact]
            public void Replays_a_clip_three_frames_behind()
            {
                var a = Behind with { Current = 4 };

                var step = CopyStateRules.Clip(a);

                step.ShouldBe(ClipStep.Replay);
            }

            [Fact]
            public void Replays_a_clip_three_frames_ahead()
            {
                var a = Behind with { Current = 10 };

                var step = CopyStateRules.Clip(a);

                step.ShouldBe(ClipStep.Replay);
            }

            [Fact]
            public void Keeps_a_clip_when_the_host_frame_is_past_its_end()
            {
                var a = Behind with { Current = 0, Frame = 12 };

                var step = CopyStateRules.Clip(a);

                step.ShouldBe(ClipStep.Keep);
            }

            [Fact]
            public void Keeps_a_clip_with_no_frames()
            {
                var a = Behind with { Current = 0, HasFrames = false };

                var step = CopyStateRules.Clip(a);

                step.ShouldBe(ClipStep.Keep);
            }

            [Fact]
            public void Switches_to_another_clip()
            {
                var a = Behind with { Same = false };

                var step = CopyStateRules.Clip(a);

                step.ShouldBe(ClipStep.Switch);
            }

            [Fact]
            public void Skips_a_state_with_no_clip()
            {
                var a = Behind with { Clip = 0, Same = false };

                var step = CopyStateRules.Clip(a);

                step.ShouldBe(ClipStep.Skip);
            }

            [Fact]
            public void Skips_an_animator_with_no_library()
            {
                var a = Behind with { Library = false, Same = false };

                var step = CopyStateRules.Clip(a);

                step.ShouldBe(ClipStep.Skip);
            }
        }

        public class StartFrame
        {
            [Theory]
            [InlineData(3, 10, 3)]
            [InlineData(9, 10, 9)]
            [InlineData(10, 10, 9)]
            [InlineData(-1, 10, 0)]
            [InlineData(5, 0, 0)]
            public void Starts_from_the_host_frame_within_the_clip(
                int frame,
                int frames,
                int start)
            {
                var result = CopyStateRules.StartFrame(frame, frames);

                result.ShouldBe(start);
            }
        }

        public class Play
        {
            [Theory]
            [InlineData(false, true, false, PlayStep.Pause)]
            [InlineData(true, false, true, PlayStep.Resume)]
            [InlineData(true, true, false, PlayStep.None)]
            [InlineData(false, false, true, PlayStep.None)]
            public void Pauses_and_resumes_as_the_host_s(
                bool playing,
                bool isPlaying,
                bool paused,
                PlayStep play)
            {
                var step = CopyStateRules.Play(playing, isPlaying, paused);

                step.ShouldBe(play);
            }
        }

        public class PlaysEvent
        {
            [Theory]
            [InlineData("TweenShadows", true)]
            [InlineData("attack", false)]
            [InlineData("playSound", false)]
            [InlineData(null, false)]
            public void Plays_only_tween_shadows(string info, bool plays)
            {
                var result = CopyStateRules.PlaysEvent(info);

                result.ShouldBe(plays);
            }
        }

        public class HidesShadows
        {
            [Theory]
            [InlineData(0, true)]
            [InlineData(1, false)]
            public void Hides_the_shadows_for_zero(int value, bool hides)
            {
                var result = CopyStateRules.HidesShadows(value);

                result.ShouldBe(hides);
            }
        }

        public class KeepsSound
        {
            [Theory]
            [InlineData(true, "dog_death", "dog_death", true)]
            [InlineData(false, "dog_death", "dog_death", false)]
            [InlineData(true, "dog_death", "dog_bark", false)]
            [InlineData(true, "", "", false)]
            [InlineData(true, null, null, false)]
            public void Keeps_only_the_death_cry_when_asked(
                bool keepDeath,
                string death,
                string audio,
                bool keeps)
            {
                var result = CopyStateRules.KeepsSound(keepDeath, death, audio);

                result.ShouldBe(keeps);
            }
        }

        public class IdleLoopFor
        {
            [Theory]
            [InlineData(true, false, IdleLoop.Idle)]
            [InlineData(true, true, IdleLoop.Sleeping)]
            [InlineData(false, true, IdleLoop.None)]
            public void Starts_the_loop_for_how_it_is(
                bool alive,
                bool asleep,
                IdleLoop loop)
            {
                var result = CopyStateRules.IdleLoopFor(alive, asleep);

                result.ShouldBe(loop);
            }
        }

        public class SwitchesWithLocation
        {
            [Fact]
            public void Switches_a_dead_copy_made_here_inside_it()
            {
                var result = CopyStateRules.SwitchesWithLocation(Corpse);

                result.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_our_copy_from_the_save()
            {
                var c = Corpse with { MadeHere = false };

                var result = CopyStateRules.SwitchesWithLocation(c);

                result.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_living_copy()
            {
                var c = Corpse with { Alive = true };

                var result = CopyStateRules.SwitchesWithLocation(c);

                result.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_one_switched_already()
            {
                var c = Corpse with { Already = true };

                var result = CopyStateRules.SwitchesWithLocation(c);

                result.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_one_outside_the_location()
            {
                var c = Corpse with { Within = false };

                var result = CopyStateRules.SwitchesWithLocation(c);

                result.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_one_that_is_gone()
            {
                var c = Corpse with { Exists = false };

                var result = CopyStateRules.SwitchesWithLocation(c);

                result.ShouldBeFalse();
            }
        }
    }
}
