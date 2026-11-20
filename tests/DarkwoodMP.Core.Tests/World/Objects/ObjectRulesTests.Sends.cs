using DarkwoodMP.World.Objects;

namespace DarkwoodMP.Tests.World.Objects
{
    /// What each game sends of the smaller objects, waits for or puts back.
    public partial class ObjectRulesTests
    {
        public class VineHealthBefore
        {
            [Fact]
            public void Adds_the_hit_back_to_the_health_it_left()
            {
                var health = ObjectRules.VineHealthBefore(12.5f, 30f);

                health.ShouldBe(42.5f);
            }
        }

        public class SendsLit
        {
            /// The host's gasoline catching fire, not from a spread.
            static readonly LitFacts Lit = new LitFacts
            {
                WasOut = true,
                Burning = true,
                Sharing = true,
            };

            [Fact]
            public void Sends_a_fire_the_host_saw_start()
            {
                ObjectRules.SendsLit(Lit).ShouldBeTrue();
            }

            [Fact]
            public void Sends_a_fire_a_client_swing_lit()
            {
                var lit = Lit with { Client = true, OwnSwing = true };

                ObjectRules.SendsLit(lit).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_client_fire_its_swing_didnt_light()
            {
                var lit = Lit with { Client = true };

                ObjectRules.SendsLit(lit).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_liquid_that_burned_already()
            {
                var lit = Lit with { WasOut = false };

                ObjectRules.SendsLit(lit).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_spread()
            {
                var lit = Lit with { Spreading = true };

                ObjectRules.SendsLit(lit).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_liquid_that_didnt_catch()
            {
                var lit = Lit with { Burning = false };

                ObjectRules.SendsLit(lit).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_when_not_sharing()
            {
                var lit = Lit with { Sharing = false };

                ObjectRules.SendsLit(lit).ShouldBeFalse();
            }
        }

        public class WatchesPickup
        {
            [Theory]
            [InlineData(true, false, false, true)]
            [InlineData(true, true, false, false)]
            [InlineData(false, false, true, true)]
            [InlineData(true, true, true, true)]
            [InlineData(false, false, false, false)]
            public void Watches_a_note_read_away_or_a_death_drop(
                bool note,
                bool kept,
                bool drop,
                bool watches)
            {
                var answer = ObjectRules.WatchesPickup(note, kept, drop);

                answer.ShouldBe(watches);
            }
        }

        public class OnShadowDied
        {
            /// A client's own shadow dying in a session, in the world.
            static readonly ShadowFacts Client = new ShadowFacts
            {
                OnItem = true,
                InSession = true,
                InWorld = true,
                Client = true,
            };

            static readonly ShadowFacts Host = Client with
            {
                Client = false,
                Host = true,
            };

            [Fact]
            public void Keeps_a_client_shadow_until_the_host_says()
            {
                var death = ObjectRules.OnShadowDied(Client);

                death.ShouldBe(ShadowDeath.Waits);
            }

            [Fact]
            public void Lets_the_host_replay_kill_a_client_shadow()
            {
                var shadow = Client with { Replaying = true };

                var death = ObjectRules.OnShadowDied(shadow);

                death.ShouldBe(ShadowDeath.Dies);
            }

            [Fact]
            public void Shares_a_host_shadow_death()
            {
                var death = ObjectRules.OnShadowDied(Host);

                death.ShouldBe(ShadowDeath.DiesForAll);
            }

            [Fact]
            public void Lets_a_save_kill_it_as_alone()
            {
                var shadow = Client with { Instant = true };

                var death = ObjectRules.OnShadowDied(shadow);

                death.ShouldBe(ShadowDeath.Dies);
            }

            [Fact]
            public void Lets_a_shadow_on_a_character_die_as_alone()
            {
                var shadow = Host with { OnItem = false };

                var death = ObjectRules.OnShadowDied(shadow);

                death.ShouldBe(ShadowDeath.Dies);
            }

            [Fact]
            public void Lets_it_die_as_alone_outside_a_session()
            {
                var shadow = Client with { InSession = false };

                var death = ObjectRules.OnShadowDied(shadow);

                death.ShouldBe(ShadowDeath.Dies);
            }

            [Fact]
            public void Lets_it_die_as_alone_outside_the_world()
            {
                var shadow = Host with { InWorld = false };

                var death = ObjectRules.OnShadowDied(shadow);

                death.ShouldBe(ShadowDeath.Dies);
            }
        }

        public class AsksForFeeder
        {
            /// A client's player using a shared feeder.
            static readonly FeederAskFacts Shared = new FeederAskFacts
            {
                Sharing = true,
                ClientMode = true,
                Named = true,
            };

            [Fact]
            public void Asks_the_host_for_a_shared_feeder()
            {
                ObjectRules.AsksForFeeder(Shared).ShouldBeTrue();
            }

            [Fact]
            public void Leaves_it_when_not_sharing()
            {
                var feeder = Shared with { Sharing = false };

                ObjectRules.AsksForFeeder(feeder).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_outside_client_mode()
            {
                var feeder = Shared with { ClientMode = false };

                ObjectRules.AsksForFeeder(feeder).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_feeder_that_isnt_shared()
            {
                var feeder = Shared with { Named = false };

                ObjectRules.AsksForFeeder(feeder).ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_feeder_ours_alone()
            {
                var feeder = Shared with { OursAlone = true };

                ObjectRules.AsksForFeeder(feeder).ShouldBeFalse();
            }
        }

        public class OnFeederAnswer
        {
            [Fact]
            public void Activates_the_feeder_on_a_yes()
            {
                var ans = new FeederAnswerFacts { Here = true, Ok = true };

                var outcome = ObjectRules.OnFeederAnswer(ans);

                outcome.ShouldBe(FeederOutcome.Activates);
            }

            [Fact]
            public void Says_the_feeder_is_inactive_on_a_refusal()
            {
                var ans = new FeederAnswerFacts { Here = true };

                var outcome = ObjectRules.OnFeederAnswer(ans);

                outcome.ShouldBe(FeederOutcome.Inactive);
            }

            [Fact]
            public void Does_nothing_when_the_feeder_is_gone()
            {
                var ans = new FeederAnswerFacts { Ok = true };

                var outcome = ObjectRules.OnFeederAnswer(ans);

                outcome.ShouldBe(FeederOutcome.Nothing);
            }
        }
    }
}
