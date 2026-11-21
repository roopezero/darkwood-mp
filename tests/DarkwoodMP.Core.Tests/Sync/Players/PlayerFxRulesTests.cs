using DarkwoodMP.Sync.Players;

namespace DarkwoodMP.Tests.Sync.Players
{
    public partial class PlayerFxRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_effect_limits()
            {
                var limits = new[]
                {
                    PlayerFxRules.SoundInterval,
                    PlayerFxRules.MaxSounds,
                    PlayerFxRules.HearingRange,
                    PlayerFxRules.MaxVolume,
                    PlayerFxRules.MaxBloodHeight,
                };

                var pinned = new[] { 0.1f, 16f, 3000f, 2f, 50f };

                limits.ShouldBe(pinned);
            }

            [Fact]
            public void Pins_the_names()
            {
                var names = new[]
                {
                    PlayerFxRules.BloodPrefabs,
                    PlayerFxRules.DefaultFlames,
                    PlayerFxRules.DefaultFireSound,
                };

                var pinned = new[]
                {
                    "FX/Bloodsplats/",
                    "fire_flamesV2_burningEnemy_01",
                    "fire_loop_01",
                };

                names.ShouldBe(pinned);
            }

            [Fact]
            public void Pins_the_hurt_kinds()
            {
                var kinds = new[]
                {
                    (byte)HurtKind.Blood,
                    (byte)HurtKind.Burning,
                    (byte)HurtKind.Stopped,
                };

                kinds.ShouldBe(new byte[] { 1, 2, 3 });
            }
        }

        public class Sharing
        {
            /// The host, connected, with one client, in a world.
            static readonly SharingFacts Hosting = new SharingFacts
            {
                Active = true,
                Connected = true,
                Peers = 1,
                InWorld = true,
            };

            [Fact]
            public void Shares_with_a_client()
            {
                var shares = PlayerFxRules.Sharing(Hosting);

                shares.ShouldBeTrue();
            }

            [Fact]
            public void Shares_nothing_hosting_alone()
            {
                var session = Hosting with { Peers = 0 };

                var shares = PlayerFxRules.Sharing(session);

                shares.ShouldBeFalse();
            }

            [Fact]
            public void Shares_with_the_host_on_a_client()
            {
                var session = Hosting with { Peers = 0, Client = true };

                var shares = PlayerFxRules.Sharing(session);

                shares.ShouldBeTrue();
            }

            [Fact]
            public void Shares_nothing_out_of_an_active_session()
            {
                var session = Hosting with { Active = false };

                var shares = PlayerFxRules.Sharing(session);

                shares.ShouldBeFalse();
            }

            [Fact]
            public void Shares_nothing_while_not_connected()
            {
                var session = Hosting with { Connected = false };

                var shares = PlayerFxRules.Sharing(session);

                shares.ShouldBeFalse();
            }

            [Fact]
            public void Shares_nothing_out_of_a_world()
            {
                var session = Hosting with { InWorld = false };

                var shares = PlayerFxRules.Sharing(session);

                shares.ShouldBeFalse();
            }
        }

        public class Queues
        {
            [Theory]
            [InlineData(15, true)]
            [InlineData(16, false)]
            public void Queues_up_to_16_sounds(int queued, bool queues)
            {
                var result = PlayerFxRules.Queues(queued);

                result.ShouldBe(queues);
            }
        }

        public class SendsSounds
        {
            [Fact]
            public void Sends_a_batch_exactly_when_due()
            {
                var sends = PlayerFxRules.SendsSounds(
                    1, new Seconds(2f), new Seconds(2f));

                sends.ShouldBeTrue();
            }

            [Fact]
            public void Waits_until_due()
            {
                var sends = PlayerFxRules.SendsSounds(
                    1, new Seconds(1.99f), new Seconds(2f));

                sends.ShouldBeFalse();
            }

            [Fact]
            public void Sends_no_empty_batch()
            {
                var sends = PlayerFxRules.SendsSounds(
                    0, new Seconds(5f), new Seconds(2f));

                sends.ShouldBeFalse();
            }
        }

        public class NextSounds
        {
            [Fact]
            public void Waits_a_tenth_of_a_second()
            {
                var next = PlayerFxRules.NextSounds(new Seconds(2f));

                next.Value.ShouldBe(2.1f, 1e-6f);
            }
        }

        public class BatchSize
        {
            [Theory]
            [InlineData(3, 3)]
            [InlineData(16, 16)]
            [InlineData(20, 16)]
            public void Holds_at_most_16(int queued, int size)
            {
                var result = PlayerFxRules.BatchSize(queued);

                result.ShouldBe(size);
            }
        }

        public class Burning
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Burns_only_alive(bool alive, bool burn, bool burning)
            {
                var result = PlayerFxRules.Burning(alive, burn);

                result.ShouldBe(burning);
            }
        }

        public class SendsBurning
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(false, true, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Sends_a_change(bool burning, bool sent, bool sends)
            {
                var result = PlayerFxRules.SendsBurning(burning, sent);

                result.ShouldBe(sends);
            }
        }

        public class BurnFlames
        {
            [Fact]
            public void Sends_no_flames_for_a_burn_without_particles()
            {
                var flames = PlayerFxRules.BurnFlames(true, "fire_big");

                flames.ShouldBe("");
            }

            [Fact]
            public void Sends_the_players_burn_prefab()
            {
                var flames = PlayerFxRules.BurnFlames(false, "fire_big");

                flames.ShouldBe("fire_big");
            }

            [Fact]
            public void Sends_the_default_without_a_prefab()
            {
                var flames = PlayerFxRules.BurnFlames(false, null);

                flames.ShouldBe("fire_flamesV2_burningEnemy_01");
            }
        }

        public class BurnSound
        {
            [Fact]
            public void Sends_no_sound_for_a_silent_burn()
            {
                var sound = PlayerFxRules.BurnSound(true, "hiss");

                sound.ShouldBe("");
            }

            [Fact]
            public void Sends_the_burns_own_sound()
            {
                var sound = PlayerFxRules.BurnSound(false, "hiss");

                sound.ShouldBe("hiss");
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            public void Sends_the_default_without_one(string own)
            {
                var sound = PlayerFxRules.BurnSound(false, own);

                sound.ShouldBe("fire_loop_01");
            }
        }

        public class Owner
        {
            /// A client gets the host's relay of another client's effect.
            static readonly OwnerFacts Relayed = new OwnerFacts
            {
                From = 1,
                Said = 3,
                HostId = 1,
                LocalId = 2,
            };

            [Fact]
            public void Takes_the_sender_on_the_host()
            {
                var message = Relayed with { Host = true, From = 3, Said = 9 };

                var owner = PlayerFxRules.Owner(message);

                owner.ShouldBe(3UL);
            }

            [Fact]
            public void Takes_the_owner_said_from_the_host()
            {
                var owner = PlayerFxRules.Owner(Relayed);

                owner.ShouldBe(3UL);
            }

            [Fact]
            public void Takes_none_not_from_the_host()
            {
                var message = Relayed with { From = 3 };

                var owner = PlayerFxRules.Owner(message);

                owner.ShouldBe(0UL);
            }

            [Fact]
            public void Takes_none_about_us()
            {
                var message = Relayed with { Said = 2 };

                var owner = PlayerFxRules.Owner(message);

                owner.ShouldBe(0UL);
            }
        }

        public class TakesSounds
        {
            [Fact]
            public void Takes_16_sounds()
            {
                var drop = PlayerFxRules.TakesSounds(3, 16);

                drop.ShouldBe(FxDrop.None);
            }

            [Fact]
            public void Drops_17_sounds()
            {
                var drop = PlayerFxRules.TakesSounds(3, 17);

                drop.ShouldBe(FxDrop.TooManySounds);
            }

            [Fact]
            public void Drops_sounds_with_no_owner()
            {
                var drop = PlayerFxRules.TakesSounds(0, 1);

                drop.ShouldBe(FxDrop.NoOwner);
            }

            [Fact]
            public void Prefers_no_owner_to_too_many()
            {
                var drop = PlayerFxRules.TakesSounds(0, 17);

                drop.ShouldBe(FxDrop.NoOwner);
            }
        }

        public class Volume
        {
            [Theory]
            [InlineData(1f, 1f)]
            [InlineData(2f, 2f)]
            [InlineData(2.5f, 2f)]
            [InlineData(-1f, 0f)]
            public void Holds_the_volume_to_0_to_2(float volume, float held)
            {
                var result = PlayerFxRules.Volume(volume);

                result.ShouldBe(held);
            }

            [Fact]
            public void Leaves_nan_as_it_is()
            {
                var result = PlayerFxRules.Volume(float.NaN);

                float.IsNaN(result).ShouldBeTrue();
            }
        }
    }
}
