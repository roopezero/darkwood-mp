using System.Collections.Generic;
using DarkwoodMP.Combat.Nights;

namespace DarkwoodMP.Tests.Combat.Nights
{
    public class NightRulesTests
    {
        /// The night's spawner on, the host's player at hideout 7.
        static readonly NightFacts Night = new NightFacts
        {
            HasSpawner = true,
            SpawnsNocturnal = true,
            HostAtPlace = true,
            HostPlace = 7,
            HostBase = true,
        };

        /// A living joiner at hideout 9.
        static readonly NightPlayer Joiner = new NightPlayer
        {
            Alive = true,
            AtPlace = true,
            Place = 9,
            Base = true,
        };

        /// A hard night, the host's player awake and without a ward.
        static readonly WormFacts Hard = new WormFacts
        {
            HasSpawner = true,
            HasHost = true,
            HardNight = true,
        };

        static readonly NightEntry Dogs = new NightEntry
        {
            Name = "dog",
            Amount = 2,
            Out = 1,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_night_limits()
            {
                var limits = new[]
                {
                    NightRules.AttackerAround,
                    NightRules.WormNear,
                    NightRules.WormFar,
                    NightRules.WormEvery,
                    NightRules.PlaceRadius,
                    NightRules.ArmorRoll,
                };

                limits.ShouldBe(new[] { 1500f, 1500f, 2000f, 5f, 300f, 0.5f });
            }

            [Fact]
            public void Pins_the_layers_and_the_first_chapter()
            {
                var limits = new[]
                {
                    NightRules.PlaceLayers,
                    NightRules.FirstChapter,
                };

                limits.ShouldBe(new[] { 73402368, 1 });
            }
        }

        public class WhoSpawns
        {
            [Theory]
            [InlineData(true, true, NightWay.None)]
            [InlineData(true, false, NightWay.None)]
            [InlineData(false, false, NightWay.Game)]
            [InlineData(false, true, NightWay.Ours)]
            public void Picks_who_sends_the_attackers(
                bool client,
                bool proxies,
                NightWay way)
            {
                NightRules.WhoSpawns(client, proxies).ShouldBe(way);
            }
        }

        public class Hideouts
        {
            static List<NightTarget> Of(
                NightFacts night,
                params NightPlayer[] proxies) =>
                NightRules.Hideouts(night, proxies);

            [Fact]
            public void Lists_the_host_s_hideout_first()
            {
                var hideouts = Of(Night, Joiner);

                hideouts.Count.ShouldBe(2);
                var host = new NightTarget { Place = 7, Host = true };
                hideouts[0].ShouldBe(host);
                hideouts[1].ShouldBe(new NightTarget { Place = 9, Proxy = 0 });
            }

            [Fact]
            public void Aims_a_shared_hideout_at_the_host()
            {
                var there = Joiner with { Place = 7 };

                var hideouts = Of(Night, there);

                hideouts.Count.ShouldBe(1);
                hideouts[0].Host.ShouldBeTrue();
            }

            [Fact]
            public void Aims_a_hideout_two_joiners_share_at_the_first()
            {
                var away = Night with { HostBase = false };

                var hideouts = Of(away, Joiner, Joiner);

                hideouts.Count.ShouldBe(1);
                hideouts[0].Proxy.ShouldBe(0);
            }

            [Fact]
            public void Keeps_each_stand_in_s_place_in_the_list()
            {
                var dead = Joiner with { Alive = false };
                var other = Joiner with { Place = 11 };

                var hideouts = Of(Night, dead, other);

                hideouts[1].ShouldBe(new NightTarget { Place = 11, Proxy = 1 });
            }

            [Theory]
            [InlineData(true, false, true)]
            [InlineData(false, false, true)]
            [InlineData(false, true, false)]
            public void Leaves_out_the_host_away_from_a_hideout(
                bool outside,
                bool atPlace,
                bool hideout)
            {
                var night = Night with
                {
                    HostOutside = outside,
                    HostAtPlace = atPlace,
                    HostBase = hideout,
                };

                var hideouts = Of(night);

                hideouts.ShouldBeEmpty();
            }

            [Theory]
            [InlineData(false, false, true, true)]
            [InlineData(true, true, true, true)]
            [InlineData(true, false, false, true)]
            [InlineData(true, false, true, false)]
            public void Leaves_out_a_stand_in_away_from_a_hideout(
                bool alive,
                bool separate,
                bool atPlace,
                bool hideout)
            {
                var joiner = Joiner with
                {
                    Alive = alive,
                    Separate = separate,
                    AtPlace = atPlace,
                    Base = hideout,
                };

                var hideouts = Of(Night, joiner);

                hideouts.Count.ShouldBe(1);
                hideouts[0].Host.ShouldBeTrue();
            }

            [Theory]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            [InlineData(true, true, true)]
            public void Sends_nobody_with_the_spawner_off_or_a_dream(
                bool spawner,
                bool nocturnal,
                bool dreaming)
            {
                var night = Night with
                {
                    HasSpawner = spawner,
                    SpawnsNocturnal = nocturnal,
                    Dreaming = dreaming,
                };

                var hideouts = Of(night, Joiner);

                hideouts.ShouldBeEmpty();
            }
        }

        public class PickAttacker
        {
            static int Pick(params NightEntry[] entries) =>
                NightRules.PickAttacker(entries);

            [Fact]
            public void Picks_the_first_with_room_at_the_hideout()
            {
                Pick(Dogs, Dogs).ShouldBe(0);
            }

            [Fact]
            public void Skips_one_whose_amount_is_out_there()
            {
                var full = Dogs with { Out = 2 };

                Pick(full, Dogs).ShouldBe(1);
            }

            [Fact]
            public void Picks_one_just_under_its_amount()
            {
                Pick(Dogs).ShouldBe(0);
            }

            [Theory]
            [InlineData(null, 2)]
            [InlineData("", 2)]
            [InlineData("dog", 0)]
            public void Skips_an_entry_with_no_name_or_amount(
                string name,
                int amount)
            {
                var entry = new NightEntry { Name = name, Amount = amount };

                Pick(entry, Dogs).ShouldBe(1);
            }

            [Fact]
            public void Says_none_when_every_kind_is_out()
            {
                var full = Dogs with { Out = 3 };

                Pick(full).ShouldBe(-1);
            }
        }

        public class LaterChapter
        {
            [Theory]
            [InlineData(true, 2, false, 0, true)]
            [InlineData(true, 1, true, 3, false)]
            [InlineData(false, 3, true, 2, true)]
            [InlineData(false, 3, true, 1, false)]
            [InlineData(false, 3, false, 2, false)]
            public void Reads_the_chapter_the_world_is_made_from(
                bool random,
                int generated,
                bool scene,
                int sceneChapter,
                bool later)
            {
                var world = new ChapterFacts
                {
                    RandomGeneration = random,
                    GeneratedChapter = generated,
                    HasScene = scene,
                    SceneChapter = sceneChapter,
                };

                NightRules.LaterChapter(world).ShouldBe(later);
            }
        }

        public class GetsShadowArmor
        {
            [Theory]
            [InlineData(0.5f, false)]
            [InlineData(0.5001f, true)]
            [InlineData(0f, false)]
            [InlineData(1f, true)]
            public void Arms_an_attacker_on_a_roll_over_a_half(
                float roll,
                bool armed)
            {
                NightRules.GetsShadowArmor(roll).ShouldBe(armed);
            }
        }

        public class CountsGone
        {
            [Fact]
            public void Counts_an_attacker_gone_at_night()
            {
                NightRules.CountsGone(false).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_an_attacker_gone_by_day_counted()
            {
                NightRules.CountsGone(true).ShouldBeFalse();
            }
        }

        public class WormsFor
        {
            [Fact]
            public void Sends_worms_at_everyone_on_a_hard_night()
            {
                NightRules.WormsFor(Hard).ShouldBe(WormDue.HostAndProxies);
            }

            [Fact]
            public void Spares_a_host_with_a_shadow_ward()
            {
                var tick = Hard with { HostWard = true };

                NightRules.WormsFor(tick).ShouldBe(WormDue.Proxies);
            }

            [Theory]
            [InlineData(true, true, true, true, false, false, false)]
            [InlineData(false, false, true, true, false, false, false)]
            [InlineData(false, true, false, true, false, false, false)]
            [InlineData(false, true, true, false, false, false, false)]
            [InlineData(false, true, true, true, true, false, false)]
            [InlineData(false, true, true, true, false, true, false)]
            [InlineData(false, true, true, true, false, false, true)]
            public void Sends_nobody_a_worm(
                bool client,
                bool spawner,
                bool host,
                bool hard,
                bool day,
                bool dreaming,
                bool immune)
            {
                var tick = new WormFacts
                {
                    ClientMode = client,
                    HasSpawner = spawner,
                    HasHost = host,
                    HardNight = hard,
                    Day = day,
                    Dreaming = dreaming,
                    IgnoresSickness = immune,
                };

                NightRules.WormsFor(tick).ShouldBe(WormDue.None);
            }
        }

        public class WormWanted
        {
            [Theory]
            [InlineData(true, false, false, true)]
            [InlineData(false, false, false, false)]
            [InlineData(true, true, false, false)]
            [InlineData(true, false, true, false)]
            public void Sends_a_worm_at_a_stand_in_out_in_the_world(
                bool alive,
                bool separate,
                bool ward,
                bool wanted)
            {
                var worm = NightRules.WormWanted(alive, separate, ward);

                worm.ShouldBe(wanted);
            }
        }

        public class WormOnSight
        {
            [Theory]
            [InlineData(false, true)]
            [InlineData(true, false)]
            public void Keeps_a_defensive_worm_defensive(
                bool defensive,
                bool onSight)
            {
                NightRules.WormOnSight(defensive).ShouldBe(onSight);
            }
        }

        public class NearestPlace
        {
            [Fact]
            public void Picks_the_nearest()
            {
                var at = NightRules.NearestPlace(new[] { 50f, 20f, 30f });

                at.ShouldBe(1);
            }

            [Fact]
            public void Gives_a_tie_to_the_first()
            {
                var at = NightRules.NearestPlace(new[] { 20f, 20f });

                at.ShouldBe(0);
            }

            [Fact]
            public void Says_none_with_no_location()
            {
                NightRules.NearestPlace(new float[0]).ShouldBe(-1);
            }

            [Fact]
            public void Never_picks_a_distance_that_is_not_a_number()
            {
                var at = NightRules.NearestPlace(new[] { float.NaN });

                at.ShouldBe(-1);
            }
        }
    }
}
