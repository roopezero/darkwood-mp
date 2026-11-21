using DarkwoodMP.Combat.Enemies;

namespace DarkwoodMP.Tests.Combat.Enemies
{
    public class EnemyHitRulesTests
    {
        /// A hit on a tracked enemy in play, from a player with a stand-in.
        static readonly HitFacts Hit = new HitFacts
        {
            Tracked = true,
            InPlay = true,
            StandIn = true,
        };

        static readonly FiringFacts BareHands = default;

        static readonly FiringFacts Axe = new FiringFacts
        {
            Armed = true,
            Melee = true,
            Type = "axe",
        };

        /// A one-shot gun that fires every quarter of a second.
        static readonly FiringFacts Rifle = new FiringFacts
        {
            Armed = true,
            Firearm = true,
            FireRate = 0.25f,
            Projectiles = 1,
            Type = "rifle",
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_reach_and_the_rate()
            {
                var limits = new[]
                {
                    EnemyHitRules.MeleeReach,
                    EnemyHitRules.MaxHitsPerSecond,
                    EnemyHitRules.AutoGap,
                    EnemyHitRules.ClickGap,
                    EnemyHitRules.RateMargin,
                };

                limits.ShouldBe(new[] { 500f, 24f, 0.05f, 0.15f, 1.5f });
            }
        }

        public class MayHit
        {
            [Fact]
            public void Allows_a_hit_on_an_enemy_in_play()
            {
                var verdict = EnemyHitRules.MayHit(Hit);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_a_hit_on_an_enemy_it_does_not_track()
            {
                var hit = Hit with { Tracked = false };

                var verdict = EnemyHitRules.MayHit(hit);

                verdict.Reason.ShouldBe(Refusal.EnemyNotAlive);
            }

            [Fact]
            public void Refuses_a_hit_on_an_enemy_out_of_play()
            {
                var hit = Hit with { InPlay = false };

                var verdict = EnemyHitRules.MayHit(hit);

                verdict.Reason.ShouldBe(Refusal.EnemyNotAlive);
            }

            [Fact]
            public void Refuses_a_hit_from_a_player_with_no_stand_in()
            {
                var hit = Hit with { StandIn = false };

                var verdict = EnemyHitRules.MayHit(hit);

                verdict.Reason.ShouldBe(Refusal.NoStandIn);
            }

            [Fact]
            public void Refuses_a_dead_enemy_before_a_missing_stand_in()
            {
                var hit = new HitFacts();

                var verdict = EnemyHitRules.MayHit(hit);

                verdict.Reason.ShouldBe(Refusal.EnemyNotAlive);
            }
        }

        public class ReachFor
        {
            [Fact]
            public void Gives_bare_hands_the_melee_reach()
            {
                var reach = EnemyHitRules.ReachFor(BareHands);

                reach.ShouldBe(500f);
            }

            [Fact]
            public void Gives_a_melee_weapon_the_melee_reach()
            {
                var reach = EnemyHitRules.ReachFor(Axe);

                reach.ShouldBe(500f);
            }

            [Fact]
            public void Gives_a_weapon_that_is_not_melee_the_relevant_range()
            {
                var reach = EnemyHitRules.ReachFor(Rifle);

                reach.ShouldBe(3000f);
            }
        }

        public class MaxHits
        {
            [Fact]
            public void Gives_bare_hands_24()
            {
                var most = EnemyHitRules.MaxHits(BareHands);

                most.ShouldBe(24);
            }

            [Fact]
            public void Gives_a_melee_weapon_24()
            {
                var most = EnemyHitRules.MaxHits(Axe);

                most.ShouldBe(24);
            }

            [Fact]
            public void Never_gives_a_slow_gun_under_24()
            {
                var most = EnemyHitRules.MaxHits(Rifle);

                most.ShouldBe(24);
            }

            [Fact]
            public void Counts_every_pellet_of_a_shot()
            {
                var gun = Rifle with { Projectiles = 8 };

                var most = EnemyHitRules.MaxHits(gun);

                most.ShouldBe(48);
            }

            [Fact]
            public void Rounds_a_part_hit_up()
            {
                var gun = Rifle with { Projectiles = 11, FireRate = 0.625f };

                var most = EnemyHitRules.MaxHits(gun);

                most.ShouldBe(27);
            }

            [Fact]
            public void Counts_an_automatic_gun_at_the_auto_gap_at_most()
            {
                var gun = Rifle with { Auto = true, FireRate = 0f };

                var most = EnemyHitRules.MaxHits(gun);

                most.ShouldBe(30);
            }

            [Fact]
            public void Counts_a_click_gun_at_the_click_gap_at_most()
            {
                var gun = Rifle with { Projectiles = 4, FireRate = 0f };

                var most = EnemyHitRules.MaxHits(gun);

                most.ShouldBe(40);
            }

            [Fact]
            public void Counts_a_gun_of_no_pellets_as_one()
            {
                var gun = Rifle with
                {
                    Auto = true,
                    FireRate = 0f,
                    Projectiles = 0,
                };

                var most = EnemyHitRules.MaxHits(gun);

                most.ShouldBe(30);
            }

            [Fact]
            public void Takes_a_slower_fire_rate_over_the_gap()
            {
                var gun = Rifle with
                {
                    Auto = true,
                    FireRate = 0.0625f,
                    Projectiles = 2,
                };

                var most = EnemyHitRules.MaxHits(gun);

                most.ShouldBe(48);
            }
        }

        public class Lands
        {
            [Theory]
            [InlineData(0f, false)]
            [InlineData(0.01f, true)]
            [InlineData(float.NaN, false)]
            public void Applies_only_damage_over_zero(float applied, bool lands)
            {
                var result = EnemyHitRules.Lands(applied);

                result.ShouldBe(lands);
            }
        }

        public class Effects
        {
            /// A weapon that bleeds (1) twice and burns (2) once.
            static readonly EffectFacts Knife = new EffectFacts
            {
                Armed = true,
                Own = new[] { 1, 2, 1 },
                Sent = new[] { 1 },
            };

            [Fact]
            public void Takes_the_weapon_own_effect_of_the_sent_type()
            {
                var used = EnemyHitRules.Effects(Knife);

                used.ShouldBe(new[] { 0 });
            }

            [Fact]
            public void Uses_each_own_effect_once()
            {
                var hit = Knife with { Sent = new[] { 1, 1, 1 } };

                var used = EnemyHitRules.Effects(hit);

                used.ShouldBe(new[] { 0, 2, -1 });
            }

            [Fact]
            public void Drops_an_effect_the_weapon_does_not_cause()
            {
                var hit = Knife with { Sent = new[] { 5, 2 } };

                var used = EnemyHitRules.Effects(hit);

                used.ShouldBe(new[] { -1, 1 });
            }

            [Fact]
            public void Gives_bare_hands_none()
            {
                var hit = Knife with { Armed = false };

                var used = EnemyHitRules.Effects(hit);

                used.ShouldBeEmpty();
            }

            [Fact]
            public void Gives_none_with_none_sent()
            {
                var hit = Knife with { Sent = new int[0] };

                var used = EnemyHitRules.Effects(hit);

                used.ShouldBeEmpty();
            }

            [Fact]
            public void Gives_none_with_no_list_sent()
            {
                var hit = Knife with { Sent = null };

                var used = EnemyHitRules.Effects(hit);

                used.ShouldBeEmpty();
            }
        }
    }
}
