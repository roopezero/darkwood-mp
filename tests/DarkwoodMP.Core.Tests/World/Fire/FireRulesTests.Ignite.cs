using DarkwoodMP.World;
using DarkwoodMP.World.Fire;

namespace DarkwoodMP.Tests.World.Fire
{
    public partial class FireRulesTests
    {
        public class MayIgnite
        {
            /// A client's request to light a door exactly at the reach.
            static readonly IgniteFacts Door = new IgniteFacts
            {
                On = true,
                Kind = FireKind.Door,
                Found = true,
                Time = 8f,
                Name = "Wooden door",
                Reach = new ReachFacts
                {
                    Handling = true,
                    Known = true,
                    StandIn = new Vec2(0f, 0f),
                    Target = new Vec2(1500f, 0f),
                },
            };

            static readonly IgniteFacts Item = Door with
            {
                Kind = FireKind.Item,
                Destructible = true,
                Name = "crate",
            };

            static IgniteFacts At(float x)
            {
                return Door with
                {
                    Reach = Door.Reach with { Target = new Vec2(x, 0f) },
                };
            }

            [Fact]
            public void Lights_a_door_exactly_at_the_reach()
            {
                var fire = FireRules.MayIgnite(Door);

                fire.ShouldBe(new Ignition { Lights = true, Time = 8f });
            }

            [Fact]
            public void Refuses_a_door_just_past_the_reach()
            {
                var fire = FireRules.MayIgnite(At(1500.5f));

                fire.Verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Words_the_reach_refusal_with_the_things_name()
            {
                var fire = FireRules.MayIgnite(At(4041f));

                var text = Refusals.Text(fire.Verdict);

                text.ShouldBe(
                    "Wooden door is 4041 away from them (reach 1500)");
            }

            [Fact]
            public void Lights_nothing_when_refused()
            {
                var fire = FireRules.MayIgnite(At(4041f));

                fire.Lights.ShouldBeFalse();
            }

            [Fact]
            public void Ignores_a_burning_message_outside_a_request()
            {
                var door = Door with
                {
                    On = false,
                    Reach = Door.Reach with { Handling = false },
                };

                var fire = FireRules.MayIgnite(door);

                fire.ShouldBe(new Ignition());
            }

            [Fact]
            public void Refuses_putting_a_fire_out()
            {
                var fire = FireRules.MayIgnite(Door with { On = false });

                fire.Verdict.Reason.ShouldBe(Refusal.OnlyLighting);
            }

            [Theory]
            [InlineData(FireKind.None)]
            [InlineData(FireKind.Enemy)]
            [InlineData((FireKind)5)]
            public void Refuses_an_enemy_or_an_unknown_kind(FireKind kind)
            {
                var fire = FireRules.MayIgnite(Door with { Kind = kind });

                fire.Verdict.Reason.ShouldBe(Refusal.NothingToBurn);
            }

            [Fact]
            public void Refuses_a_thing_that_isnt_here()
            {
                var fire = FireRules.MayIgnite(Door with { Found = false });

                fire.Verdict.Reason.ShouldBe(Refusal.NothingToBurn);
            }

            [Fact]
            public void Refuses_an_item_that_isnt_destructible()
            {
                var item = Item with { Destructible = false };

                var fire = FireRules.MayIgnite(item);

                fire.Verdict.ShouldBe(
                    Verdict.Refuse(Refusal.CantBurn, "crate"));
            }

            [Fact]
            public void Refuses_a_broken_item()
            {
                var fire = FireRules.MayIgnite(Item with { Destroyed = true });

                fire.Verdict.Reason.ShouldBe(Refusal.CantBurn);
            }

            [Fact]
            public void Refuses_a_broken_door()
            {
                var fire = FireRules.MayIgnite(Door with { Destroyed = true });

                fire.Verdict.ShouldBe(
                    Verdict.Refuse(Refusal.Broken, "Wooden door"));
            }

            [Fact]
            public void Lights_a_broken_window()
            {
                var window = Door with
                {
                    Kind = FireKind.Window,
                    Destroyed = true,
                };

                var fire = FireRules.MayIgnite(window);

                fire.Lights.ShouldBeTrue();
            }

            [Fact]
            public void Allows_a_thing_burning_already_with_nothing_done()
            {
                var fire = FireRules.MayIgnite(Door with { Burning = true });

                fire.ShouldBe(new Ignition());
            }

            [Fact]
            public void Refuses_putting_out_before_the_kind()
            {
                var door = Door with { On = false, Kind = FireKind.Enemy };

                var fire = FireRules.MayIgnite(door);

                fire.Verdict.Reason.ShouldBe(Refusal.OnlyLighting);
            }

            [Fact]
            public void Refuses_a_missing_thing_before_its_reach()
            {
                var door = At(4041f) with { Found = false };

                var fire = FireRules.MayIgnite(door);

                fire.Verdict.Reason.ShouldBe(Refusal.NothingToBurn);
            }

            [Fact]
            public void Refuses_the_reach_before_a_broken_door()
            {
                var door = At(4041f) with { Destroyed = true };

                var fire = FireRules.MayIgnite(door);

                fire.Verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }

            [Fact]
            public void Refuses_a_broken_door_even_if_burning()
            {
                var door = Door with { Destroyed = true, Burning = true };

                var fire = FireRules.MayIgnite(door);

                fire.Verdict.Reason.ShouldBe(Refusal.Broken);
            }

            [Theory]
            [InlineData(0.5f, 1f)]
            [InlineData(1f, 1f)]
            [InlineData(20f, 20f)]
            [InlineData(20.5f, 20f)]
            [InlineData(-3f, 1f)]
            public void Keeps_the_burn_time_from_1_to_20(float sent, float time)
            {
                var fire = FireRules.MayIgnite(Door with { Time = sent });

                fire.Time.ShouldBe(time);
            }

            [Fact]
            public void Takes_a_burn_time_that_isnt_a_number_as_it_is()
            {
                var door = Door with { Time = float.NaN };

                var fire = FireRules.MayIgnite(door);

                float.IsNaN(fire.Time).ShouldBeTrue();
            }
        }

        public class FromHost
        {
            /// The host's door caught fire; it isn't burning here yet.
            static readonly HostFireFacts Lit = new HostFireFacts
            {
                Kind = FireKind.Door,
                Found = true,
                On = true,
            };

            [Fact]
            public void Lights_a_thing_not_burning_here()
            {
                var change = FireRules.FromHost(Lit);

                change.ShouldBe(FireChange.Light);
            }

            [Fact]
            public void Lights_an_enemy_too()
            {
                var fire = Lit with { Kind = FireKind.Enemy };

                var change = FireRules.FromHost(fire);

                change.ShouldBe(FireChange.Light);
            }

            [Fact]
            public void Puts_out_a_thing_burning_here()
            {
                var fire = Lit with { On = false, Burning = true };

                var change = FireRules.FromHost(fire);

                change.ShouldBe(FireChange.PutOut);
            }

            [Fact]
            public void Leaves_a_thing_burning_here_already()
            {
                var fire = Lit with { Burning = true };

                var change = FireRules.FromHost(fire);

                change.ShouldBe(FireChange.Nothing);
            }

            [Fact]
            public void Leaves_a_thing_out_already()
            {
                var fire = Lit with { On = false };

                var change = FireRules.FromHost(fire);

                change.ShouldBe(FireChange.Nothing);
            }

            [Theory]
            [InlineData(FireKind.None)]
            [InlineData((FireKind)5)]
            public void Leaves_an_unknown_kind(FireKind kind)
            {
                var fire = Lit with { Kind = kind };

                var change = FireRules.FromHost(fire);

                change.ShouldBe(FireChange.Nothing);
            }

            [Fact]
            public void Leaves_a_thing_that_isnt_here()
            {
                var fire = Lit with { Found = false };

                var change = FireRules.FromHost(fire);

                change.ShouldBe(FireChange.Nothing);
            }
        }

        public class BurnHurts
        {
            [Theory]
            [InlineData(false, false, true)]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            public void Hurts_on_a_client_only_its_own_player(
                bool client,
                bool onPlayer,
                bool hurts)
            {
                var hurt = FireRules.BurnHurts(client, onPlayer);

                hurt.ShouldBe(hurts);
            }
        }
    }
}
