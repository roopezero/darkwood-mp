using DarkwoodMP.Combat.Damage;

namespace DarkwoodMP.Tests.Combat.Damage
{
    public class ClientHitRulesTests
    {
        /// Our player swings at the host's enemy.
        static readonly ClientHitFacts Swing = new ClientHitFacts
        {
            Handled = true,
            Puppet = true,
            Frozen = false,
            Damage = 25f,
            Effects = 0,
            Thrown = false,
            Flame = false,
            OurFlame = false,
            ByPlayer = true,
            PlayerAttacker = false,
        };

        static readonly HostEffectsFacts OnPuppet = new HostEffectsFacts
        {
            Handled = true,
            Character = true,
            Puppet = true,
            Frozen = false,
            Forwarded = false,
        };

        public class Forward
        {
            [Fact]
            public void Sends_our_player_s_hit_to_the_host()
            {
                var outcome = ClientHitRules.Forward(Swing);

                outcome.ShouldBe(ClientHit.ToHost);
            }

            [Fact]
            public void Leaves_the_hit_to_the_game_outside_ClientMode()
            {
                var hit = Swing with { Handled = false, Puppet = false };

                var outcome = ClientHitRules.Forward(hit);

                outcome.ShouldBe(ClientHit.Game);
            }

            [Fact]
            public void Drops_a_hit_on_a_frozen_copy()
            {
                var hit = Swing with { Puppet = false, Frozen = true };

                var outcome = ClientHitRules.Forward(hit);

                outcome.ShouldBe(ClientHit.FrozenCopy);
            }

            [Fact]
            public void Asks_the_npcs_about_any_other_character()
            {
                var hit = Swing with { Puppet = false, ByPlayer = false };

                var outcome = ClientHitRules.Forward(hit);

                outcome.ShouldBe(ClientHit.AskNpcs);
            }

            [Theory]
            [InlineData(0f, 0, ClientHit.NoDamage)]
            [InlineData(-1f, 0, ClientHit.NoDamage)]
            [InlineData(0f, 1, ClientHit.ToHost)]
            [InlineData(0.01f, 0, ClientHit.ToHost)]
            public void Drops_a_hit_of_no_damage_and_no_effects(
                float damage, int effects, ClientHit expected)
            {
                var hit = Swing with { Damage = damage, Effects = effects };

                var outcome = ClientHitRules.Forward(hit);

                outcome.ShouldBe(expected);
            }

            [Fact]
            public void Drops_no_damage_before_a_throw()
            {
                var hit = Swing with { Damage = 0f, Thrown = true };

                var outcome = ClientHitRules.Forward(hit);

                outcome.ShouldBe(ClientHit.NoDamage);
            }

            [Fact]
            public void Drops_a_thrown_thing_s_hit()
            {
                var hit = Swing with { Thrown = true, Flame = true };

                var outcome = ClientHitRules.Forward(hit);

                outcome.ShouldBe(ClientHit.Thrown);
            }

            [Fact]
            public void Drops_a_flame_that_isn_t_ours()
            {
                var hit = Swing with { Flame = true, ByPlayer = false };

                var outcome = ClientHitRules.Forward(hit);

                outcome.ShouldBe(ClientHit.OthersFlame);
            }

            [Fact]
            public void Sends_our_weapon_s_flame()
            {
                var hit = Swing with { Flame = true, OurFlame = true };

                var outcome = ClientHitRules.Forward(hit);

                outcome.ShouldBe(ClientHit.ToHost);
            }

            [Fact]
            public void Drops_a_hit_not_by_our_player()
            {
                var hit = Swing with { ByPlayer = false };

                var outcome = ClientHitRules.Forward(hit);

                outcome.ShouldBe(ClientHit.NotOurs);
            }

            [Fact]
            public void Sends_a_hit_whose_attacker_is_our_player()
            {
                var hit = Swing with
                {
                    ByPlayer = false,
                    PlayerAttacker = true,
                };

                var outcome = ClientHitRules.Forward(hit);

                outcome.ShouldBe(ClientHit.ToHost);
            }
        }

        public class NotesEffects
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Notes_our_player_s_sensor_in_ClientMode(
                bool client, bool playerSensor, bool expected)
            {
                var notes = ClientHitRules.NotesEffects(client, playerSensor);

                notes.ShouldBe(expected);
            }
        }

        public class RunsEffects
        {
            [Fact]
            public void Stops_effects_on_the_host_s_enemy()
            {
                var runs = ClientHitRules.RunsEffects(OnPuppet);

                runs.ShouldBeFalse();
            }

            [Fact]
            public void Runs_them_outside_ClientMode()
            {
                var c = OnPuppet with { Handled = false };

                var runs = ClientHitRules.RunsEffects(c);

                runs.ShouldBeTrue();
            }

            [Fact]
            public void Runs_them_on_what_is_no_character()
            {
                var c = OnPuppet with { Character = false };

                var runs = ClientHitRules.RunsEffects(c);

                runs.ShouldBeTrue();
            }

            [Fact]
            public void Runs_them_on_our_own_character()
            {
                var c = OnPuppet with { Puppet = false };

                var runs = ClientHitRules.RunsEffects(c);

                runs.ShouldBeTrue();
            }

            [Theory]
            [InlineData(true, false)]
            [InlineData(false, true)]
            public void Stops_them_on_frozen_or_forwarded_ones(
                bool frozen, bool forwarded)
            {
                var c = OnPuppet with
                {
                    Puppet = false,
                    Frozen = frozen,
                    Forwarded = forwarded,
                };

                var runs = ClientHitRules.RunsEffects(c);

                runs.ShouldBeFalse();
            }
        }
    }
}
