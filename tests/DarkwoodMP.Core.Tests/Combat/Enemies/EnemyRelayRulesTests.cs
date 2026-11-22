using DarkwoodMP.Combat.Enemies;

namespace DarkwoodMP.Tests.Combat.Enemies
{
    public class EnemyRelayRulesTests
    {
        /// A living enemy the client has, 1000 away from its stand-in.
        static readonly RelayFacts Known = new RelayFacts
        {
            Exists = true,
            Active = true,
            Enemy = new Vec3(1000f, 0f, 0f),
            StandIn = new Vec3(0f, 0f, 0f),
            Known = true,
        };

        /// A corpse the client never had, dead longer than CorpseLinger,
        /// near it.
        static readonly RelayFacts Corpse = Known with
        {
            Known = false,
            Dead = true,
            SinceDeath = new Seconds(7f),
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_range_times_and_corpses()
            {
                var limits = new[]
                {
                    EnemyRelayRules.RelevantRange,
                    EnemyRelayRules.StateInterval,
                    EnemyRelayRules.CorpseLinger,
                    EnemyRelayRules.CorpsesPerPass,
                };

                limits.ShouldBe(new[] { 3000f, 0.05f, 6f, 4f });
            }
        }

        public class HostRuns
        {
            [Theory]
            [InlineData(true, 1, true, true)]
            [InlineData(false, 1, true, false)]
            [InlineData(true, 0, true, false)]
            [InlineData(true, 1, false, false)]
            public void Runs_on_a_host_with_peers_in_its_world(
                bool host,
                int peers,
                bool inWorld,
                bool runs)
            {
                var result = EnemyRelayRules.HostRuns(host, peers, inWorld);

                result.ShouldBe(runs);
            }
        }

        public class Serves
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(false, false, false)]
            [InlineData(true, true, false)]
            public void Serves_a_client_with_a_stand_in_that_is_not_away(
                bool standIn,
                bool away,
                bool serves)
            {
                var result = EnemyRelayRules.Serves(standIn, away);

                result.ShouldBe(serves);
            }
        }

        public class Dies
        {
            [Theory]
            [InlineData(false, false, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Times_a_death_once(bool inPlay, bool timed, bool dies)
            {
                var result = EnemyRelayRules.Dies(inPlay, timed);

                result.ShouldBe(dies);
            }
        }

        public class Choose
        {
            [Fact]
            public void Sends_the_state_of_a_known_enemy_in_range()
            {
                var step = EnemyRelayRules.Choose(Known);

                step.ShouldBe(RelayStep.State);
            }

            [Fact]
            public void Spawns_an_enemy_new_to_the_client()
            {
                var e = Known with { Known = false };

                var step = EnemyRelayRules.Choose(e);

                step.ShouldBe(RelayStep.Spawn);
            }

            [Fact]
            public void Skips_a_destroyed_enemy()
            {
                var e = Known with { Exists = false };

                var step = EnemyRelayRules.Choose(e);

                step.ShouldBe(RelayStep.Skip);
            }

            [Fact]
            public void Drops_a_known_enemy_exactly_at_the_range()
            {
                var e = Known with { Enemy = new Vec3(3000f, 0f, 0f) };

                var step = EnemyRelayRules.Choose(e);

                step.ShouldBe(RelayStep.Drop);
            }

            [Fact]
            public void Keeps_a_known_enemy_just_inside_the_range()
            {
                var e = Known with { Enemy = new Vec3(2999.9f, 0f, 0f) };

                var step = EnemyRelayRules.Choose(e);

                step.ShouldBe(RelayStep.State);
            }

            [Fact]
            public void Measures_the_range_in_3d()
            {
                var e = Known with { Enemy = new Vec3(2000f, 2300f, 0f) };

                var step = EnemyRelayRules.Choose(e);

                step.ShouldBe(RelayStep.Drop);
            }

            [Fact]
            public void Drops_a_known_enemy_switched_off_on_the_host()
            {
                var e = Known with { Active = false };

                var step = EnemyRelayRules.Choose(e);

                step.ShouldBe(RelayStep.Drop);
            }

            [Fact]
            public void Skips_an_unknown_enemy_out_of_range()
            {
                var e = Known with
                {
                    Known = false,
                    Enemy = new Vec3(5000f, 0f, 0f),
                };

                var step = EnemyRelayRules.Choose(e);

                step.ShouldBe(RelayStep.Skip);
            }

            [Fact]
            public void Sends_the_state_of_one_dead_for_exactly_the_linger()
            {
                var e = Known with
                {
                    Dead = true,
                    SinceDeath = new Seconds(6f),
                };

                var step = EnemyRelayRules.Choose(e);

                step.ShouldBe(RelayStep.State);
            }

            [Fact]
            public void Drops_one_dead_just_past_the_linger_as_a_corpse()
            {
                var e = Known with
                {
                    Dead = true,
                    SinceDeath = new Seconds(6.01f),
                };

                var step = EnemyRelayRules.Choose(e);

                step.ShouldBe(RelayStep.DropCorpse);
            }

            [Fact]
            public void Drops_a_known_dead_one_out_of_range_as_a_corpse()
            {
                var e = Known with
                {
                    Dead = true,
                    Enemy = new Vec3(5000f, 0f, 0f),
                };

                var step = EnemyRelayRules.Choose(e);

                step.ShouldBe(RelayStep.DropCorpse);
            }

            [Fact]
            public void Skips_a_corpse_the_client_keeps()
            {
                var e = Corpse with { CorpseKept = true };

                var step = EnemyRelayRules.Choose(e);

                step.ShouldBe(RelayStep.Skip);
            }

            [Fact]
            public void Skips_a_kept_corpse_before_sending_its_state()
            {
                var e = Known with { Dead = true, CorpseKept = true };

                var step = EnemyRelayRules.Choose(e);

                step.ShouldBe(RelayStep.Skip);
            }

            [Fact]
            public void Sends_a_corpse_the_client_never_saw_settle()
            {
                var step = EnemyRelayRules.Choose(Corpse);

                step.ShouldBe(RelayStep.SendCorpse);
            }

            [Fact]
            public void Sends_a_corpse_while_under_the_pass_limit()
            {
                var e = Corpse with { CorpsesSent = 3 };

                var step = EnemyRelayRules.Choose(e);

                step.ShouldBe(RelayStep.SendCorpse);
            }

            [Fact]
            public void Waits_with_a_corpse_once_the_pass_sent_four()
            {
                var e = Corpse with { CorpsesSent = 4 };

                var step = EnemyRelayRules.Choose(e);

                step.ShouldBe(RelayStep.Skip);
            }

            [Fact]
            public void Waits_with_a_corpse_out_of_range()
            {
                var e = Corpse with { Enemy = new Vec3(5000f, 0f, 0f) };

                var step = EnemyRelayRules.Choose(e);

                step.ShouldBe(RelayStep.Skip);
            }
        }

        public class SendsBatch
        {
            [Theory]
            [InlineData(0, false)]
            [InlineData(1, true)]
            public void Sends_a_batch_that_holds_a_state(int count, bool sends)
            {
                var result = EnemyRelayRules.SendsBatch(count);

                result.ShouldBe(sends);
            }
        }

        public class SendsLoot
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            [InlineData(false, true, false)]
            public void Sends_the_loot_of_a_corpse_new_to_the_client(
                bool corpse,
                bool added,
                bool sends)
            {
                var result = EnemyRelayRules.SendsLoot(corpse, added);

                result.ShouldBe(sends);
            }
        }

        public class DeadSince
        {
            [Theory]
            [InlineData(true, 1.99f, false)]
            [InlineData(true, 2f, true)]
            [InlineData(false, 9f, false)]
            public void Counts_one_dead_at_least_that_long(
                bool dead,
                float since,
                bool counts)
            {
                var result = EnemyRelayRules.DeadSince(
                    dead,
                    new Seconds(since),
                    new Seconds(2f));

                result.ShouldBe(counts);
            }
        }

        public class ArmorChanged
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(false, true, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Tells_the_clients_when_the_armour_differs(
                bool wears,
                bool told,
                bool changed)
            {
                var result = EnemyRelayRules.ArmorChanged(wears, told);

                result.ShouldBe(changed);
            }
        }
    }
}
