using DarkwoodMP.Combat.Noise;

namespace DarkwoodMP.Tests.Combat.Noise
{
    public class NoiseRulesTests
    {
        /// Our player's step on the host: from Player's code, no source.
        static readonly NoiseFacts Step = new NoiseFacts
        {
            Set = false,
            OwnNoise = 0,
            FromPlayer = true,
            Client = false,
        };

        /// Our player opens a door.
        static readonly OwnNoiseFacts Door = new OwnNoiseFacts
        {
            Player = true,
            Maker = NoiseMaker.Door,
            PlayerOpener = true,
            PlayerSensor = false,
        };

        /// A client's noise 300 along x from its stand-in.
        static readonly RemoteNoiseFacts Remote = new RemoteNoiseFacts
        {
            StandIn = true,
            At = new Vec3(0f, 0f, 0f),
            Noise = new Vec3(300f, 0f, 0f),
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_noise_limits()
            {
                var limits = new[]
                {
                    NoiseRules.MostRange,
                    NoiseRules.MostVolume,
                    NoiseRules.MostDrift,
                };

                limits.ShouldBe(new[] { 2000f, 5f, 500f });
            }
        }

        public class Owner
        {
            [Fact]
            public void Marks_our_player_s_step_as_ours()
            {
                NoiseRules.Owner(Step).ShouldBe(NoiseOwner.Ours);
            }

            [Fact]
            public void Leaves_a_source_set_already()
            {
                var noise = Step with { Set = true, Client = true };

                NoiseRules.Owner(noise).ShouldBe(NoiseOwner.Set);
            }

            [Fact]
            public void Ignores_a_noise_not_from_our_player()
            {
                var noise = Step with { FromPlayer = false, Client = true };

                NoiseRules.Owner(noise).ShouldBe(NoiseOwner.NotOurs);
            }

            [Theory]
            [InlineData(1, NoiseOwner.Ours)]
            [InlineData(0, NoiseOwner.NotOurs)]
            [InlineData(-1, NoiseOwner.NotOurs)]
            public void Counts_a_noise_inside_an_own_noise_method(
                int own, NoiseOwner expected)
            {
                var noise = Step with { OwnNoise = own, FromPlayer = false };

                NoiseRules.Owner(noise).ShouldBe(expected);
            }

            [Fact]
            public void Sends_a_client_s_noise_to_the_host()
            {
                var noise = Step with { Client = true };

                NoiseRules.Owner(noise).ShouldBe(NoiseOwner.OursToHost);
            }
        }

        public class CountsAsOurs
        {
            [Fact]
            public void Counts_a_door_our_player_opens()
            {
                NoiseRules.CountsAsOurs(Door).ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_door_someone_else_opens()
            {
                var maker = Door with { PlayerOpener = false };

                NoiseRules.CountsAsOurs(maker).ShouldBeFalse();
            }

            [Theory]
            [InlineData(true, true)]
            [InlineData(false, false)]
            public void Counts_our_player_s_sensor(bool player, bool expected)
            {
                var maker = Door with
                {
                    Maker = NoiseMaker.Sensor,
                    PlayerSensor = player,
                };

                NoiseRules.CountsAsOurs(maker).ShouldBe(expected);
            }

            [Fact]
            public void Counts_furniture_always()
            {
                var maker = Door with
                {
                    Maker = NoiseMaker.Furniture,
                    PlayerOpener = false,
                };

                NoiseRules.CountsAsOurs(maker).ShouldBeTrue();
            }

            [Fact]
            public void Skips_anything_else()
            {
                var maker = Door with { Maker = NoiseMaker.Other };

                NoiseRules.CountsAsOurs(maker).ShouldBeFalse();
            }

            [Fact]
            public void Skips_everything_with_no_player()
            {
                var maker = Door with
                {
                    Player = false,
                    Maker = NoiseMaker.Furniture,
                };

                NoiseRules.CountsAsOurs(maker).ShouldBeFalse();
            }
        }

        public class OpenerAt
        {
            [Theory]
            [InlineData(0, -1)]
            [InlineData(1, 0)]
            [InlineData(2, 1)]
            [InlineData(3, 1)]
            public void Takes_the_second_argument_of_two_or_more(
                int count, int expected)
            {
                NoiseRules.OpenerAt(count).ShouldBe(expected);
            }
        }

        public class Range
        {
            [Theory]
            [InlineData(-1f, 0f)]
            [InlineData(150f, 150f)]
            [InlineData(2000f, 2000f)]
            [InlineData(2001f, 2000f)]
            public void Keeps_the_range_within_2000(float range, float at)
            {
                NoiseRules.Range(range).ShouldBe(at);
            }
        }

        public class Volume
        {
            [Theory]
            [InlineData(-1f, 0f)]
            [InlineData(5f, 5f)]
            [InlineData(5.5f, 5f)]
            public void Keeps_the_volume_within_5(float volume, float at)
            {
                NoiseRules.Volume(volume).ShouldBe(at);
            }
        }

        public class Raises
        {
            [Fact]
            public void Raises_a_noise_near_the_stand_in()
            {
                NoiseRules.Raises(Remote).ShouldBeTrue();
            }

            [Theory]
            [InlineData(500f, true)]
            [InlineData(500.1f, false)]
            public void Drops_a_noise_past_500(float x, bool expected)
            {
                var noise = Remote with { Noise = new Vec3(x, 0f, 0f) };

                NoiseRules.Raises(noise).ShouldBe(expected);
            }

            [Fact]
            public void Measures_the_height_too()
            {
                var noise = Remote with { Noise = new Vec3(300f, 401f, 0f) };

                NoiseRules.Raises(noise).ShouldBeFalse();
            }

            [Fact]
            public void Drops_a_noise_with_no_stand_in()
            {
                var noise = Remote with { StandIn = false };

                NoiseRules.Raises(noise).ShouldBeFalse();
            }
        }
    }
}
