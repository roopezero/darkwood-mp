using DarkwoodMP.Sync.Players;

namespace DarkwoodMP.Tests.Sync.Players
{
    public partial class PlayerFxRulesTests
    {
        public class Hears
        {
            /// Our player 100 from their shown puppet, in a world.
            static readonly HearFacts Near = new HearFacts
            {
                HasPuppet = true,
                InWorld = true,
                HasPlayer = true,
                Ours = new Vec3(100f, 0f, 0f),
                Puppet = new Vec3(0f, 0f, 0f),
            };

            [Fact]
            public void Hears_a_puppet_nearby()
            {
                var hears = PlayerFxRules.Hears(Near);

                hears.ShouldBeTrue();
            }

            [Fact]
            public void Hears_nothing_at_exactly_the_range()
            {
                var hear = Near with { Ours = new Vec3(3000f, 0f, 0f) };

                var hears = PlayerFxRules.Hears(hear);

                hears.ShouldBeFalse();
            }

            [Fact]
            public void Hears_a_puppet_just_inside_the_range()
            {
                var hear = Near with { Ours = new Vec3(2999f, 0f, 0f) };

                var hears = PlayerFxRules.Hears(hear);

                hears.ShouldBeTrue();
            }

            [Fact]
            public void Counts_the_height_too()
            {
                var hear = Near with { Ours = new Vec3(0f, 3001f, 0f) };

                var hears = PlayerFxRules.Hears(hear);

                hears.ShouldBeFalse();
            }

            [Fact]
            public void Hears_nothing_without_a_puppet()
            {
                var hear = Near with { HasPuppet = false };

                var hears = PlayerFxRules.Hears(hear);

                hears.ShouldBeFalse();
            }

            [Fact]
            public void Hears_nothing_out_of_a_world()
            {
                var hear = Near with { InWorld = false };

                var hears = PlayerFxRules.Hears(hear);

                hears.ShouldBeFalse();
            }

            [Fact]
            public void Hears_nothing_without_our_player()
            {
                var hear = Near with { HasPlayer = false };

                var hears = PlayerFxRules.Hears(hear);

                hears.ShouldBeFalse();
            }
        }

        public class TakesShot
        {
            [Theory]
            [InlineData(0UL, FxDrop.NoOwner)]
            [InlineData(3UL, FxDrop.None)]
            public void Drops_a_shot_with_no_owner(ulong owner, FxDrop drop)
            {
                var result = PlayerFxRules.TakesShot(owner);

                result.ShouldBe(drop);
            }
        }

        public class TakesHurt
        {
            [Theory]
            [InlineData(1)]
            [InlineData(2)]
            [InlineData(3)]
            public void Takes_each_known_kind(byte kind)
            {
                var drop = PlayerFxRules.TakesHurt(kind, 3);

                drop.ShouldBe(FxDrop.None);
            }

            [Theory]
            [InlineData(0)]
            [InlineData(4)]
            public void Drops_an_unknown_kind(byte kind)
            {
                var drop = PlayerFxRules.TakesHurt(kind, 3);

                drop.ShouldBe(FxDrop.UnknownKind);
            }

            [Fact]
            public void Drops_a_wound_with_no_owner()
            {
                var drop = PlayerFxRules.TakesHurt(1, 0);

                drop.ShouldBe(FxDrop.NoOwner);
            }

            [Fact]
            public void Prefers_an_unknown_kind_to_no_owner()
            {
                var drop = PlayerFxRules.TakesHurt(9, 0);

                drop.ShouldBe(FxDrop.UnknownKind);
            }
        }

        public class RelaysReliably
        {
            [Theory]
            [InlineData(HurtKind.Blood, false)]
            [InlineData(HurtKind.Burning, true)]
            [InlineData(HurtKind.Stopped, true)]
            public void Relays_only_blood_unreliably(HurtKind kind, bool sure)
            {
                var result = PlayerFxRules.RelaysReliably(kind);

                result.ShouldBe(sure);
            }
        }

        public class ShowsBlood
        {
            [Fact]
            public void Shows_a_blood_splat()
            {
                var shows = PlayerFxRules.ShowsBlood(
                    "FX/Bloodsplats/Shotsplat_stay");

                shows.ShouldBeTrue();
            }

            [Theory]
            [InlineData("FX/Explosion")]
            [InlineData("FX/Bloodsplats")]
            [InlineData("")]
            public void Spawns_no_other_prefab_a_peer_names(string prefab)
            {
                var shows = PlayerFxRules.ShowsBlood(prefab);

                shows.ShouldBeFalse();
            }
        }

        public class BloodHeight
        {
            [Theory]
            [InlineData(10f, 10f)]
            [InlineData(50f, 50f)]
            [InlineData(51f, 50f)]
            [InlineData(-50f, -50f)]
            [InlineData(-51f, -50f)]
            public void Holds_the_height_to_50(float height, float held)
            {
                var result = PlayerFxRules.BloodHeight(height);

                result.ShouldBe(held);
            }
        }

        public class IsOurs
        {
            /// Played from our player's own code, with no parent.
            static readonly SoundSourceFacts InOurCode = new SoundSourceFacts
            {
                HasPlayer = true,
                InPlayerCode = 1,
            };

            static readonly SoundSourceFacts OnUs = InOurCode with
            {
                InPlayerCode = 0,
                HasParent = true,
                ParentIsOurs = true,
            };

            [Fact]
            public void Takes_a_sound_from_our_players_code()
            {
                var ours = PlayerFxRules.IsOurs(InOurCode);

                ours.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_one_inside_a_shared_world_action()
            {
                var sound = InOurCode with { Depth = 1 };

                var ours = PlayerFxRules.IsOurs(sound);

                ours.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_one_from_other_code()
            {
                var sound = InOurCode with { InPlayerCode = 0 };

                var ours = PlayerFxRules.IsOurs(sound);

                ours.ShouldBeFalse();
            }

            [Fact]
            public void Takes_a_sound_on_our_player()
            {
                var ours = PlayerFxRules.IsOurs(OnUs);

                ours.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_one_on_something_else_even_in_our_code()
            {
                var sound = OnUs with
                {
                    ParentIsOurs = false,
                    InPlayerCode = 1,
                };

                var ours = PlayerFxRules.IsOurs(sound);

                ours.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_every_sound_while_replaying()
            {
                var sound = OnUs with { Replaying = true };

                var ours = PlayerFxRules.IsOurs(sound);

                ours.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_every_sound_with_no_player()
            {
                var sound = OnUs with { HasPlayer = false };

                var ours = PlayerFxRules.IsOurs(sound);

                ours.ShouldBeFalse();
            }
        }

        public class SendsSound
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Sends_only_sounds_that_dont_loop(
                bool item,
                bool loops,
                bool sends)
            {
                var result = PlayerFxRules.SendsSound(item, loops);

                result.ShouldBe(sends);
            }
        }

        public class ShotIsOurs
        {
            [Theory]
            [InlineData(true, true, true, true)]
            [InlineData(false, true, true, false)]
            [InlineData(true, false, true, false)]
            [InlineData(true, true, false, false)]
            public void Sends_our_players_shots_with_a_gun(
                bool ourPlayer,
                bool sharing,
                bool gun,
                bool ours)
            {
                var result = PlayerFxRules.ShotIsOurs(ourPlayer, sharing, gun);

                result.ShouldBe(ours);
            }
        }

        public class BloodIsOurs
        {
            static readonly BloodFacts Bled = new BloodFacts
            {
                InGetHit = 1,
                HasResult = true,
                Prefab = "FX/Bloodsplats/Shotsplat_stay",
                Sharing = true,
            };

            [Fact]
            public void Sends_our_players_blood()
            {
                var ours = PlayerFxRules.BloodIsOurs(Bled);

                ours.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_prefab_outside_get_hit()
            {
                var blood = Bled with { InGetHit = 0 };

                var ours = PlayerFxRules.BloodIsOurs(blood);

                ours.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_prefab_that_wasnt_made()
            {
                var blood = Bled with { HasResult = false };

                var ours = PlayerFxRules.BloodIsOurs(blood);

                ours.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_prefab_with_no_name()
            {
                var blood = Bled with { Prefab = null };

                var ours = PlayerFxRules.BloodIsOurs(blood);

                ours.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_prefab_that_isnt_blood()
            {
                var blood = Bled with { Prefab = "FX/Sparks" };

                var ours = PlayerFxRules.BloodIsOurs(blood);

                ours.ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_while_not_sharing()
            {
                var blood = Bled with { Sharing = false };

                var ours = PlayerFxRules.BloodIsOurs(blood);

                ours.ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_while_replaying()
            {
                var blood = Bled with { Replaying = true };

                var ours = PlayerFxRules.BloodIsOurs(blood);

                ours.ShouldBeFalse();
            }
        }

        public class Leave
        {
            [Theory]
            [InlineData(2, 1)]
            [InlineData(1, 0)]
            [InlineData(0, 0)]
            public void Counts_down_to_0(int depth, int left)
            {
                var result = PlayerFxRules.Leave(depth);

                result.ShouldBe(left);
            }
        }
    }
}
