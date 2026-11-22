using DarkwoodMP.World;
using DarkwoodMP.World.Infections;

namespace DarkwoodMP.Tests.World.Infections
{
    public class InfectionRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_find_radius_and_the_reach()
            {
                var limits = new[]
                {
                    InfectionRules.FindRadius,
                    InfectionRules.Reach,
                };

                limits.ShouldBe(new[] { 20f, 600f });
            }
        }

        public class SendsClear
        {
            /// The host's patch that started to fade just now.
            static readonly ClearFacts OnHost = new ClearFacts
            {
                Fading = true,
                Sharing = true,
            };

            static readonly ClearFacts OwnSwing = OnHost with
            {
                ClientMode = true,
                OwnSwing = true,
            };

            [Fact]
            public void Sends_every_clear_on_the_host()
            {
                var sent = InfectionRules.SendsClear(OnHost);

                sent.ShouldBeTrue();
            }

            [Fact]
            public void Sends_a_clients_own_swing()
            {
                var sent = InfectionRules.SendsClear(OwnSwing);

                sent.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_clients_copy_of_the_hosts_fire()
            {
                var patch = OwnSwing with { OwnSwing = false };

                var sent = InfectionRules.SendsClear(patch);

                sent.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_patch_that_was_fading_already()
            {
                var patch = OnHost with { WasFading = true };

                var sent = InfectionRules.SendsClear(patch);

                sent.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_patch_that_didnt_start_fading()
            {
                var patch = OnHost with { Fading = false };

                var sent = InfectionRules.SendsClear(patch);

                sent.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_every_clear_while_not_sharing()
            {
                var patch = OnHost with { Sharing = false };

                var sent = InfectionRules.SendsClear(patch);

                sent.ShouldBeFalse();
            }
        }

        public class Clears
        {
            /// A client's patch on the host, exactly at the reach.
            static readonly PatchFacts AtReach = new PatchFacts
            {
                Found = true,
                Host = true,
                Reach = new ReachFacts
                {
                    Known = true,
                    StandIn = new Vec2(0f, 0f),
                    Target = new Vec2(600f, 0f),
                },
            };

            static readonly PatchFacts PastReach = AtReach with
            {
                Reach = AtReach.Reach with { Target = new Vec2(600.5f, 0f) },
            };

            [Fact]
            public void Clears_a_patch_exactly_at_the_reach()
            {
                var cleared = InfectionRules.Clears(AtReach);

                cleared.ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_patch_just_past_the_reach()
            {
                var cleared = InfectionRules.Clears(PastReach);

                cleared.ShouldBeFalse();
            }

            [Fact]
            public void Clears_any_patch_for_a_sender_with_no_stand_in()
            {
                var patch = PastReach with
                {
                    Reach = PastReach.Reach with { Known = false },
                };

                var cleared = InfectionRules.Clears(patch);

                cleared.ShouldBeTrue();
            }

            [Fact]
            public void Clears_the_hosts_patch_at_any_distance_on_a_client()
            {
                var patch = PastReach with { Host = false };

                var cleared = InfectionRules.Clears(patch);

                cleared.ShouldBeTrue();
            }

            [Fact]
            public void Skips_a_patch_that_isnt_here()
            {
                var patch = AtReach with { Found = false };

                var cleared = InfectionRules.Clears(patch);

                cleared.ShouldBeFalse();
            }

            [Fact]
            public void Skips_a_patch_fading_already()
            {
                var patch = AtReach with { Host = false, Fading = true };

                var cleared = InfectionRules.Clears(patch);

                cleared.ShouldBeFalse();
            }
        }

        public class MayClear
        {
            [Fact]
            public void Refuses_a_list_of_which_none_was_cleared()
            {
                var verdict = InfectionRules.MayClear(3, 0);

                verdict.Reason.ShouldBe(Refusal.InfectionOutOfReach);
            }

            [Theory]
            [InlineData(3, 1)]
            [InlineData(3, 3)]
            [InlineData(0, 0)]
            public void Allows_a_list_with_one_cleared_or_an_empty_one(
                int asked,
                int cleared)
            {
                var verdict = InfectionRules.MayClear(asked, cleared);

                verdict.ShouldBe(Verdict.Allow);
            }
        }

        public class Relays
        {
            [Theory]
            [InlineData(0, false)]
            [InlineData(1, true)]
            public void Relays_only_what_was_cleared(int cleared, bool relays)
            {
                var relayed = InfectionRules.Relays(cleared);

                relayed.ShouldBe(relays);
            }
        }

        public class MaySpread
        {
            [Theory]
            [InlineData(false, true)]
            [InlineData(true, false)]
            public void Spreads_only_outside_a_client(bool client, bool spreads)
            {
                var spread = InfectionRules.MaySpread(client);

                spread.ShouldBe(spreads);
            }
        }

        public class Left
        {
            [Theory]
            [InlineData(2, 1)]
            [InlineData(1, 0)]
            [InlineData(0, 0)]
            public void Takes_one_off_but_never_below_0(int growing, int left)
            {
                var after = InfectionRules.Left(growing);

                after.ShouldBe(left);
            }
        }

        public class SendsSpawned
        {
            /// The host's world spreading while players are in.
            static readonly SpawnFacts Spreading = new SpawnFacts
            {
                Growing = 1,
                Exists = true,
                Host = true,
                Sharing = true,
            };

            [Fact]
            public void Sends_a_patch_the_world_made()
            {
                var sent = InfectionRules.SendsSpawned(Spreading);

                sent.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_patch_made_outside_a_spread()
            {
                var patch = Spreading with { Growing = 0 };

                var sent = InfectionRules.SendsSpawned(patch);

                sent.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_patch_on_a_client()
            {
                var patch = Spreading with { Host = false };

                var sent = InfectionRules.SendsSpawned(patch);

                sent.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_patch_while_not_sharing()
            {
                var patch = Spreading with { Sharing = false };

                var sent = InfectionRules.SendsSpawned(patch);

                sent.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_patch_that_isnt_there()
            {
                var patch = Spreading with { Exists = false };

                var sent = InfectionRules.SendsSpawned(patch);

                sent.ShouldBeFalse();
            }
        }

        public class MakesSpawned
        {
            [Theory]
            [InlineData(false, false, true)]
            [InlineData(true, false, true)]
            [InlineData(true, true, false)]
            public void Makes_a_patch_unless_its_id_is_alive_here(
                bool known,
                bool alive,
                bool makes)
            {
                var made = InfectionRules.MakesSpawned(known, alive);

                made.ShouldBe(makes);
            }
        }
    }
}
