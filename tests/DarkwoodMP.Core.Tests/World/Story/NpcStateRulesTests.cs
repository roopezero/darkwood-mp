using DarkwoodMP.World.Story;

namespace DarkwoodMP.Tests.World.Story
{
    public class NpcStateRulesTests
    {
        /// An alive state for an NPC the host has alive.
        static readonly NpcStateFacts Alive = new NpcStateFacts
        {
            Readable = true,
            OursKnown = true,
        };

        /// A dead state for an NPC the host has alive, with no kill
        /// allowed.
        static readonly NpcStateFacts Killed = Alive with
        {
            TheirsDead = true,
        };

        /// An NPC called Wolf with save id 7, alive, one of several.
        static readonly NpcCopyFacts Wolf = new NpcCopyFacts
        {
            SameName = true,
            ById = true,
            Saved = true,
            SaveId = 7,
            DeadId = 7,
            Alive = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_kill_window()
            {
                NpcStateRules.KillWindow.ShouldBe(30f);
            }
        }

        public class Judge
        {
            [Fact]
            public void Takes_an_alive_state()
            {
                NpcStateRules.Judge(Alive).ShouldBe(NpcStateOutcome.Taken);
            }

            [Fact]
            public void Takes_an_alive_state_for_an_npc_we_dont_know()
            {
                var npc = Alive with { OursKnown = false };

                NpcStateRules.Judge(npc).ShouldBe(NpcStateOutcome.Taken);
            }

            [Fact]
            public void Refuses_a_death_with_no_kill_allowed()
            {
                var outcome = NpcStateRules.Judge(Killed);

                outcome.ShouldBe(NpcStateOutcome.NotAllowed);
            }

            [Fact]
            public void Refuses_a_death_of_an_npc_we_dont_know()
            {
                var npc = Killed with { OursKnown = false };

                var outcome = NpcStateRules.Judge(npc);

                outcome.ShouldBe(NpcStateOutcome.NotAllowed);
            }

            [Fact]
            public void Takes_a_death_with_a_kill_allowed()
            {
                var npc = Killed with { KillAllowed = true };

                NpcStateRules.Judge(npc).ShouldBe(NpcStateOutcome.Taken);
            }

            [Fact]
            public void Takes_a_death_over_our_dead_npc()
            {
                var npc = Killed with { OursDead = true };

                NpcStateRules.Judge(npc).ShouldBe(NpcStateOutcome.Taken);
            }

            [Fact]
            public void Calls_alive_over_our_dead_npc_stale()
            {
                var npc = Alive with { OursDead = true };

                NpcStateRules.Judge(npc).ShouldBe(NpcStateOutcome.Stale);
            }

            [Fact]
            public void Calls_it_stale_before_a_kill_is_looked_at()
            {
                var npc = Alive with { OursDead = true, KillAllowed = true };

                NpcStateRules.Judge(npc).ShouldBe(NpcStateOutcome.Stale);
            }

            [Fact]
            public void Takes_an_unreadable_state()
            {
                var npc = Killed with { Readable = false };

                NpcStateRules.Judge(npc).ShouldBe(NpcStateOutcome.Taken);
            }

            [Fact]
            public void Takes_an_unreadable_state_over_our_dead_npc()
            {
                var npc = Alive with { Readable = false, OursDead = true };

                NpcStateRules.Judge(npc).ShouldBe(NpcStateOutcome.Taken);
            }

            [Fact]
            public void Ignores_a_dead_flag_with_no_state_of_ours()
            {
                var npc = Alive with { OursKnown = false, OursDead = true };

                NpcStateRules.Judge(npc).ShouldBe(NpcStateOutcome.Taken);
            }
        }

        public class ById
        {
            [Theory]
            [InlineData(0, 100, false)]
            [InlineData(1, 100, true)]
            [InlineData(99, 100, true)]
            [InlineData(100, 100, false)]
            [InlineData(-3, 100, false)]
            public void Names_the_dead_one_by_a_shared_save_id(
                int deadId,
                int sendLimit,
                bool byId)
            {
                NpcStateRules.ById(deadId, sendLimit).ShouldBe(byId);
            }
        }

        public class Kills
        {
            [Fact]
            public void Kills_the_copy_with_the_dead_ones_id()
            {
                NpcStateRules.Kills(Wolf).ShouldBeTrue();
            }

            [Fact]
            public void Spares_a_copy_with_another_id()
            {
                var npc = Wolf with { SaveId = 8 };

                NpcStateRules.Kills(npc).ShouldBeFalse();
            }

            [Fact]
            public void Spares_a_copy_with_no_save_by_id()
            {
                var npc = Wolf with { Saved = false };

                NpcStateRules.Kills(npc).ShouldBeFalse();
            }

            [Fact]
            public void Kills_every_copy_when_not_by_id()
            {
                var npc = Wolf with { ById = false, Saved = false };

                NpcStateRules.Kills(npc).ShouldBeTrue();
            }

            [Fact]
            public void Spares_another_name()
            {
                var npc = Wolf with { SameName = false };

                NpcStateRules.Kills(npc).ShouldBeFalse();
            }

            [Fact]
            public void Spares_one_that_is_dead()
            {
                var npc = Wolf with { Alive = false };

                NpcStateRules.Kills(npc).ShouldBeFalse();
            }

            [Fact]
            public void Spares_a_copy_of_the_hosts_enemy()
            {
                var npc = Wolf with { Puppet = true };

                NpcStateRules.Kills(npc).ShouldBeFalse();
            }
        }
    }
}
