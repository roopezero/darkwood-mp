using System;
using DarkwoodMP.Combat.Joiners;

namespace DarkwoodMP.Tests.Combat.Joiners
{
    public class ProxyRulesTests
    {
        /// A joiner's stand-in on a hosting game, wanted and still there.
        static readonly KeepFacts Kept = new KeepFacts
        {
            Exists = true,
            Wanted = true,
            Known = true,
            Hosting = true,
        };

        /// A living joiner in the host's world, on a hosting game.
        static readonly PresenceFacts Here = new PresenceFacts
        {
            Hosting = true,
            Alive = true,
            HasSnapshot = true,
        };

        /// An item's rigidbody near a stand-in, not ignored yet.
        static readonly LooseFacts Crate = new LooseFacts
        {
            Exists = true,
            HasBody = true,
            Item = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_stand_in_limits()
            {
                var limits = new[]
                {
                    ProxyRules.LooseRadius,
                    ProxyRules.MaxLoose,
                    ProxyRules.BodyRadius,
                };

                limits.ShouldBe(new[] { 150f, 128f, 15f });
            }

            [Fact]
            public void Pins_the_dream_prefix()
            {
                ProxyRules.DreamPrefix.ShouldBe("dream:");
            }
        }

        public class Wanted
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(false, true, true)]
            [InlineData(false, false, false)]
            public void Wants_stand_ins_while_hosting_or_for_the_dummy(
                bool hosting,
                bool dummy,
                bool wanted)
            {
                ProxyRules.Wanted(hosting, dummy).ShouldBe(wanted);
            }
        }

        public class Keeps
        {
            [Fact]
            public void Keeps_a_joiner_s_stand_in_while_hosting()
            {
                ProxyRules.Keeps(Kept).ShouldBeTrue();
            }

            [Fact]
            public void Drops_a_destroyed_stand_in()
            {
                var proxy = Kept with { Exists = false };

                ProxyRules.Keeps(proxy).ShouldBeFalse();
            }

            [Fact]
            public void Drops_every_stand_in_when_none_is_wanted()
            {
                var proxy = Kept with { Wanted = false };

                ProxyRules.Keeps(proxy).ShouldBeFalse();
            }

            [Fact]
            public void Drops_the_stand_in_of_a_player_who_left()
            {
                var proxy = Kept with { Known = false };

                ProxyRules.Keeps(proxy).ShouldBeFalse();
            }

            [Fact]
            public void Drops_a_joiner_s_stand_in_when_not_hosting()
            {
                var proxy = Kept with { Hosting = false };

                ProxyRules.Keeps(proxy).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_the_dummy_s_stand_in_when_not_hosting()
            {
                var proxy = Kept with { Hosting = false, Dummy = true };

                ProxyRules.Keeps(proxy).ShouldBeTrue();
            }
        }

        public class Places
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(false, true, false)]
            [InlineData(true, false, false)]
            public void Places_while_wanted_with_a_host_player(
                bool wanted,
                bool player,
                bool places)
            {
                ProxyRules.Places(wanted, player).ShouldBe(places);
            }
        }

        public class Stands
        {
            [Theory]
            [InlineData(true, true, false, true)]
            [InlineData(false, true, false, false)]
            [InlineData(true, false, false, false)]
            [InlineData(true, false, true, true)]
            [InlineData(false, false, true, false)]
            public void Stands_a_puppet_with_a_root(
                bool root,
                bool hosting,
                bool dummy,
                bool stands)
            {
                ProxyRules.Stands(root, hosting, dummy).ShouldBe(stands);
            }
        }

        public class PresenceOf
        {
            [Fact]
            public void Puts_a_living_player_in_the_world_here()
            {
                var presence = ProxyRules.PresenceOf(Here);

                presence.ShouldBe(new Presence { Here = true });
            }

            [Fact]
            public void Leaves_a_dead_player_in_the_world_not_here()
            {
                var player = Here with { Alive = false };

                var presence = ProxyRules.PresenceOf(player);

                presence.ShouldBe(new Presence());
            }

            [Fact]
            public void Puts_an_away_player_away()
            {
                var player = Here with { Away = true };

                var presence = ProxyRules.PresenceOf(player);

                presence.ShouldBe(new Presence { Away = true });
            }

            [Fact]
            public void Keeps_a_player_in_a_location_the_host_runs_here()
            {
                var player = Here with { Away = true, HostRuns = true };

                var presence = ProxyRules.PresenceOf(player);

                presence.ShouldBe(new Presence
                {
                    InLocation = true,
                    Here = true,
                });
            }

            [Fact]
            public void Puts_them_away_with_no_snapshot()
            {
                var player = Here with
                {
                    Away = true,
                    HostRuns = true,
                    HasSnapshot = false,
                };

                var presence = ProxyRules.PresenceOf(player);

                presence.ShouldBe(new Presence { Away = true });
            }

            [Fact]
            public void Puts_them_away_when_not_hosting()
            {
                var player = Here with
                {
                    Away = true,
                    HostRuns = true,
                    Hosting = false,
                };

                var presence = ProxyRules.PresenceOf(player);

                presence.ShouldBe(new Presence { Away = true });
            }

            [Fact]
            public void Ignores_where_the_host_runs_for_a_player_not_away()
            {
                var player = Here with { HostRuns = true };

                var presence = ProxyRules.PresenceOf(player);

                presence.ShouldBe(new Presence { Here = true });
            }
        }

        public class NoteOf
        {
            [Fact]
            public void Notes_an_away_player()
            {
                var presence = new Presence { Away = true };

                ProxyRules.NoteOf(presence).ShouldBe(PresenceNote.Away);
            }

            [Fact]
            public void Notes_a_player_in_a_location_the_host_runs()
            {
                var presence = new Presence { InLocation = true, Here = true };

                ProxyRules.NoteOf(presence).ShouldBe(PresenceNote.InLocation);
            }

            [Fact]
            public void Notes_a_player_back_in_the_world()
            {
                var presence = new Presence { Here = true };

                ProxyRules.NoteOf(presence).ShouldBe(PresenceNote.Back);
            }
        }

        public class InDream
        {
            [Fact]
            public void Finds_a_dream_place()
            {
                ProxyRules.InDream(true, "dream:forest").ShouldBeTrue();
            }

            [Fact]
            public void Finds_no_dream_in_a_location()
            {
                var place = "outside_test_location";

                ProxyRules.InDream(true, place).ShouldBeFalse();
            }

            [Fact]
            public void Finds_no_dream_with_no_snapshot()
            {
                ProxyRules.InDream(false, null).ShouldBeFalse();
            }

            [Fact]
            public void Throws_on_a_snapshot_with_no_place()
            {
                Should.Throw<NullReferenceException>(
                    () => ProxyRules.InDream(true, null));
            }
        }

        public class TurnedInvisible
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, true, false)]
            [InlineData(false, false, false)]
            public void Notices_only_the_change_to_invisible(
                bool now,
                bool before,
                bool turned)
            {
                ProxyRules.TurnedInvisible(now, before).ShouldBe(turned);
            }
        }

        public class PassesHit
        {
            [Fact]
            public void Passes_an_enemy_s_hit_to_the_player()
            {
                ProxyRules.PassesHit(false).ShouldBeTrue();
            }

            [Fact]
            public void Drops_another_player_s_hit()
            {
                ProxyRules.PassesHit(true).ShouldBeFalse();
            }
        }

        public class IgnoresLoose
        {
            [Fact]
            public void Ignores_an_item_s_rigidbody()
            {
                ProxyRules.IgnoresLoose(Crate).ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_missing_collider()
            {
                var thing = Crate with { Exists = false };

                ProxyRules.IgnoresLoose(thing).ShouldBeFalse();
            }

            [Fact]
            public void Skips_one_already_ignored()
            {
                var thing = Crate with { Ignored = true };

                ProxyRules.IgnoresLoose(thing).ShouldBeFalse();
            }

            [Fact]
            public void Skips_one_with_no_rigidbody()
            {
                var thing = Crate with { HasBody = false };

                ProxyRules.IgnoresLoose(thing).ShouldBeFalse();
            }

            [Fact]
            public void Skips_its_own_rigidbody()
            {
                var thing = Crate with { OwnBody = true };

                ProxyRules.IgnoresLoose(thing).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_rigidbody_that_is_no_item()
            {
                var thing = Crate with { Item = false };

                ProxyRules.IgnoresLoose(thing).ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_character()
            {
                var thing = Crate with { Character = true };

                ProxyRules.IgnoresLoose(thing).ShouldBeFalse();
            }
        }

        public class IgnoresPart
        {
            [Theory]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            [InlineData(false, false, false)]
            public void Ignores_a_solid_part(
                bool exists,
                bool trigger,
                bool ignores)
            {
                ProxyRules.IgnoresPart(exists, trigger).ShouldBe(ignores);
            }
        }
    }
}
