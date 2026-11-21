using DarkwoodMP.Combat.Effects;

namespace DarkwoodMP.Tests.Combat.Effects
{
    public class EffectRulesTests
    {
        /// One of the host's enemies chasing a joiner's stand-in.
        static readonly ChaserFacts Chaser = new ChaserFacts
        {
            Alive = true,
            Enemy = true,
            Chasing = true,
            OnStandIn = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_effect_limits()
            {
                EffectRules.Every.ShouldBe(0.5f);
                EffectRules.ShowRange.ShouldBe(4000f);
            }

            [Fact]
            public void Pins_the_kinds_and_names()
            {
                var kinds = new[]
                {
                    EffectRules.PrefabKind,
                    EffectRules.PooledKind,
                    EffectRules.EmitterKind,
                };

                kinds.ShouldBe(new byte[] { 1, 2, 3 });
                EffectRules.Pool.ShouldBe("FX");
                EffectRules.ForestSpirit.ShouldBe("ForestSpirit_fastSpawnEff");
                EffectRules.BansheeScream.ShouldBe("FX/BansheeScream");
            }
        }

        public class Hosting
        {
            [Theory]
            [InlineData(true, 1, true, true)]
            [InlineData(false, 1, true, false)]
            [InlineData(true, 0, true, false)]
            [InlineData(true, 1, false, false)]
            public void Hosts_with_peers_in_the_world(
                bool host, int peers, bool inWorld, bool expected)
            {
                var hosting = EffectRules.Hosting(host, peers, inWorld);

                hosting.ShouldBe(expected);
            }
        }

        public class Client
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Runs_in_ClientMode_in_the_world(
                bool client, bool inWorld, bool expected)
            {
                EffectRules.Client(client, inWorld).ShouldBe(expected);
            }
        }

        public class Due
        {
            [Theory]
            [InlineData(10f, true, 10.5f)]
            [InlineData(9.9f, false, 10f)]
            public void Ticks_every_half_second(
                float now, bool due, float next)
            {
                var tick = EffectRules.Due(
                    new Seconds(now), new Seconds(10f));

                tick.Due.ShouldBe(due);
                tick.Next.Value.ShouldBe(next);
            }
        }

        public class Chases
        {
            [Fact]
            public void Counts_an_enemy_chasing_the_stand_in()
            {
                EffectRules.Chases(Chaser).ShouldBeTrue();
            }

            public static TheoryData<ChaserFacts> NotChasing => new()
            {
                Chaser with { Alive = false },
                Chaser with { Enemy = false },
                Chaser with { Chasing = false },
                Chaser with { OnStandIn = false },
            };

            [Theory]
            [MemberData(nameof(NotChasing))]
            public void Skips_any_other(ChaserFacts c)
            {
                EffectRules.Chases(c).ShouldBeFalse();
            }
        }

        public class Change
        {
            [Theory]
            [InlineData(true, true, false, CombatChange.Same)]
            [InlineData(false, false, true, CombatChange.Same)]
            [InlineData(true, false, false, CombatChange.Sent)]
            [InlineData(false, true, false, CombatChange.Sent)]
            [InlineData(true, false, true, CombatChange.Kept)]
            [InlineData(false, true, true, CombatChange.Kept)]
            public void Sends_only_a_change_and_never_to_the_dummy(
                bool fight, bool was, bool dummy, CombatChange expected)
            {
                EffectRules.Change(fight, was, dummy).ShouldBe(expected);
            }
        }

        public class Music
        {
            [Theory]
            [InlineData(true, 0, true)]
            [InlineData(false, 1, true)]
            [InlineData(false, 0, false)]
            public void Plays_when_the_host_says_or_one_attacks(
                bool hostSays, int attackers, bool expected)
            {
                EffectRules.Music(hostSays, attackers).ShouldBe(expected);
            }
        }

        public class KeepsMusic
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Keeps_it_while_the_host_says_so(
                bool hostSays, bool client, bool expected)
            {
                EffectRules.KeepsMusic(hostSays, client).ShouldBe(expected);
            }
        }

        public class Sends
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(false, false, false)]
            [InlineData(true, true, false)]
            public void Sends_the_host_s_own_effects(
                bool hosting, bool replaying, bool expected)
            {
                EffectRules.Sends(hosting, replaying).ShouldBe(expected);
            }
        }

        public class Rotates
        {
            [Theory]
            [InlineData(1, true)]
            [InlineData(2, false)]
            [InlineData(3, false)]
            public void Rotates_a_prefab_only(byte kind, bool expected)
            {
                EffectRules.Rotates(kind).ShouldBe(expected);
            }
        }

        public class SpreadsEmitter
        {
            [Theory]
            [InlineData(true, true, false, true)]
            [InlineData(true, false, true, true)]
            [InlineData(true, false, false, false)]
            [InlineData(false, true, true, false)]
            public void Spreads_an_enemy_s_light_or_emitter(
                bool enemy, bool light, bool emitter, bool expected)
            {
                var spreads = EffectRules.SpreadsEmitter(
                    enemy, light, emitter);

                spreads.ShouldBe(expected);
            }
        }

        public class SpreadsPooled
        {
            [Theory]
            [InlineData(true, "FX", "ForestSpirit_fastSpawnEff", true)]
            [InlineData(false, "FX", "ForestSpirit_fastSpawnEff", false)]
            [InlineData(true, "Blood", "ForestSpirit_fastSpawnEff", false)]
            [InlineData(true, "FX", "Smoke", false)]
            [InlineData(true, null, null, false)]
            public void Spreads_only_the_forest_spirit(
                bool made, string pool, string prefab, bool expected)
            {
                var spreads = EffectRules.SpreadsPooled(made, pool, prefab);

                spreads.ShouldBe(expected);
            }
        }

        public class Shown
        {
            [Theory]
            [InlineData(true, false, 4000f, true)]
            [InlineData(true, false, 4000.5f, false)]
            [InlineData(false, false, 0f, false)]
            [InlineData(true, true, 0f, false)]
            [InlineData(true, false, float.NaN, true)]
            public void Shows_it_within_4000_of_our_player(
                bool player, bool away, float d, bool expected)
            {
                EffectRules.Shown(player, away, d).ShouldBe(expected);
            }
        }

        public class Show
        {
            [Theory]
            [InlineData(3, false, EffectShow.Emitter)]
            [InlineData(1, false, EffectShow.NotShown)]
            [InlineData(1, true, EffectShow.Prefab)]
            [InlineData(2, true, EffectShow.Pooled)]
            [InlineData(9, true, EffectShow.Pooled)]
            [InlineData(0, true, EffectShow.Pooled)]
            public void Shows_by_kind(byte kind, bool shown, EffectShow then)
            {
                EffectRules.Show(kind, shown).ShouldBe(then);
            }
        }
    }
}
