using DarkwoodMP.Combat.Damage;

namespace DarkwoodMP.Tests.Combat.Damage
{
    public class DamageRulesTests
    {
        /// The host's enemy hits a joiner's stand-in.
        static readonly ProxyHitFacts EnemyHit = new ProxyHitFacts
        {
            ByNpc = false,
            NpcIsEnemy = false,
            ToDummy = false,
            Host = true,
        };

        public class Send
        {
            [Fact]
            public void Sends_an_enemy_hit_as_the_host()
            {
                var outcome = DamageRules.Send(EnemyHit);

                outcome.ShouldBe(ProxyHit.Sent);
            }

            [Fact]
            public void Sends_a_hostile_npc_hit()
            {
                var hit = EnemyHit with { ByNpc = true, NpcIsEnemy = true };

                var outcome = DamageRules.Send(hit);

                outcome.ShouldBe(ProxyHit.Sent);
            }

            [Fact]
            public void Leaves_a_friendly_npc_hit_to_their_copy()
            {
                var hit = EnemyHit with { ByNpc = true };

                var outcome = DamageRules.Send(hit);

                outcome.ShouldBe(ProxyHit.LeftToTheirCopy);
            }

            [Fact]
            public void Hits_the_dummy_without_a_session()
            {
                var hit = EnemyHit with { ToDummy = true, Host = false };

                var outcome = DamageRules.Send(hit);

                outcome.ShouldBe(ProxyHit.ToDummy);
            }

            [Fact]
            public void Sends_nothing_unless_hosting()
            {
                var hit = EnemyHit with { Host = false };

                var outcome = DamageRules.Send(hit);

                outcome.ShouldBe(ProxyHit.NotSent);
            }

            [Fact]
            public void Drops_a_friendly_npc_hit_on_the_dummy_first()
            {
                var hit = EnemyHit with { ByNpc = true, ToDummy = true };

                var outcome = DamageRules.Send(hit);

                outcome.ShouldBe(ProxyHit.LeftToTheirCopy);
            }
        }

        public class EffectsSent
        {
            [Fact]
            public void Pins_the_most_effects()
            {
                DamageRules.MostEffects.ShouldBe(16);
            }

            [Theory]
            [InlineData(0, 0)]
            [InlineData(16, 16)]
            [InlineData(17, 16)]
            public void Sends_at_most_16(int count, int expected)
            {
                var sent = DamageRules.EffectsSent(count);

                sent.ShouldBe(expected);
            }
        }

        public class Reads
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Reads_only_the_host_s_hit_on_a_client(
                bool client, bool fromHost, bool expected)
            {
                var reads = DamageRules.Reads(client, fromHost);

                reads.ShouldBe(expected);
            }
        }

        public class Takes
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Takes_it_only_on_a_living_player(
                bool player, bool alive, bool expected)
            {
                var takes = DamageRules.Takes(player, alive);

                takes.ShouldBe(expected);
            }
        }
    }
}
