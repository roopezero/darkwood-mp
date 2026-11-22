using DarkwoodMP.World;
using DarkwoodMP.World.Fire;

namespace DarkwoodMP.Tests.World.Fire
{
    public partial class FireRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_reach_and_the_burn_times()
            {
                var limits = new[]
                {
                    FireRules.FlameReach,
                    FireRules.ShortestBurn,
                    FireRules.LongestBurn,
                    FireRules.UnknownBurn,
                };

                limits.ShouldBe(new[] { 1500f, 1f, 20f, 20f });
            }

            [Fact]
            public void Pins_the_kinds_on_the_wire()
            {
                var kinds = new[]
                {
                    (byte)FireKind.None,
                    (byte)FireKind.Enemy,
                    (byte)FireKind.Door,
                    (byte)FireKind.Window,
                    (byte)FireKind.Item,
                };

                kinds.ShouldBe(new byte[] { 0, 1, 2, 3, 4 });
            }
        }

        public class KindOf
        {
            static readonly KindFacts Everything = new KindFacts
            {
                Character = true,
                Door = true,
                Window = true,
                Item = true,
            };

            [Fact]
            public void Takes_a_character_before_anything_else()
            {
                var kind = FireRules.KindOf(Everything);

                kind.ShouldBe(FireKind.Enemy);
            }

            [Fact]
            public void Takes_a_door_before_a_window()
            {
                var thing = Everything with { Character = false };

                var kind = FireRules.KindOf(thing);

                kind.ShouldBe(FireKind.Door);
            }

            [Fact]
            public void Takes_a_window_before_an_item()
            {
                var thing = new KindFacts { Window = true, Item = true };

                var kind = FireRules.KindOf(thing);

                kind.ShouldBe(FireKind.Window);
            }

            [Fact]
            public void Takes_an_item()
            {
                var kind = FireRules.KindOf(new KindFacts { Item = true });

                kind.ShouldBe(FireKind.Item);
            }

            [Fact]
            public void Never_sends_a_player()
            {
                var thing = Everything with { Player = true };

                var kind = FireRules.KindOf(thing);

                kind.ShouldBe(FireKind.None);
            }

            [Fact]
            public void Never_sends_a_stand_in()
            {
                var thing = Everything with { Proxy = true };

                var kind = FireRules.KindOf(thing);

                kind.ShouldBe(FireKind.None);
            }

            [Fact]
            public void Sends_nothing_for_anything_else()
            {
                var kind = FireRules.KindOf(new KindFacts());

                kind.ShouldBe(FireKind.None);
            }
        }

        public class Flammable
        {
            /// A flame touching a collider with everything on it.
            static readonly FlameFacts Touch = new FlameFacts
            {
                Collider = true,
                ColliderItem = true,
                ColliderItemDestructible = true,
                TransformItem = true,
                TransformItemDestructible = true,
                Door = true,
                Window = true,
            };

            [Fact]
            public void Lights_the_colliders_destructible_item_first()
            {
                var target = FireRules.Flammable(Touch);

                target.ShouldBe(FlameTarget.ColliderItem);
            }

            [Fact]
            public void Lights_the_transforms_item_when_the_collider_has_none()
            {
                var touch = Touch with { ColliderItem = false };

                var target = FireRules.Flammable(touch);

                target.ShouldBe(FlameTarget.TransformItem);
            }

            [Fact]
            public void Skips_to_the_door_past_a_lasting_item_on_the_collider()
            {
                var touch = Touch with { ColliderItemDestructible = false };

                var target = FireRules.Flammable(touch);

                target.ShouldBe(FlameTarget.Door);
            }

            [Fact]
            public void Skips_to_the_door_past_a_lasting_item_on_the_transform()
            {
                var touch = Touch with
                {
                    ColliderItem = false,
                    TransformItemDestructible = false,
                };

                var target = FireRules.Flammable(touch);

                target.ShouldBe(FlameTarget.Door);
            }

            [Fact]
            public void Lights_a_window_with_no_item_or_door()
            {
                var touch = new FlameFacts { Collider = true, Window = true };

                var target = FireRules.Flammable(touch);

                target.ShouldBe(FlameTarget.Window);
            }

            [Fact]
            public void Lights_nothing_on_a_character()
            {
                var touch = Touch with { CharOnCollider = true };

                var target = FireRules.Flammable(touch);

                target.ShouldBe(FlameTarget.Nothing);
            }

            [Fact]
            public void Lights_nothing_through_a_characters_transform()
            {
                var touch = Touch with { CharOnTransform = true };

                var target = FireRules.Flammable(touch);

                target.ShouldBe(FlameTarget.Nothing);
            }

            [Fact]
            public void Lights_nothing_with_no_collider()
            {
                var touch = Touch with { Collider = false };

                var target = FireRules.Flammable(touch);

                target.ShouldBe(FlameTarget.Nothing);
            }

            [Fact]
            public void Lights_nothing_else()
            {
                var touch = new FlameFacts { Collider = true };

                var target = FireRules.Flammable(touch);

                target.ShouldBe(FlameTarget.Nothing);
            }
        }

        public class SendsChange
        {
            /// The host's door catching fire with a player joined.
            static readonly ChangeFacts Door = new ChangeFacts
            {
                Exists = true,
                Host = true,
                Peers = 1,
                InWorld = true,
                Kind = FireKind.Door,
                Named = true,
            };

            [Fact]
            public void Sends_the_hosts_fire()
            {
                var sent = FireRules.SendsChange(Door);

                sent.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_a_fire_with_nobody_joined()
            {
                var fire = Door with { Peers = 0 };

                var sent = FireRules.SendsChange(fire);

                sent.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_replayed_fire()
            {
                var fire = Door with { Replaying = true };

                var sent = FireRules.SendsChange(fire);

                sent.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_clients_fire()
            {
                var fire = Door with { Host = false };

                var sent = FireRules.SendsChange(fire);

                sent.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_fire_out_of_a_world()
            {
                var fire = Door with { InWorld = false };

                var sent = FireRules.SendsChange(fire);

                sent.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_fire_on_nothing_shared()
            {
                var fire = Door with { Kind = FireKind.None };

                var sent = FireRules.SendsChange(fire);

                sent.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_fire_it_cant_name()
            {
                var fire = Door with { Named = false };

                var sent = FireRules.SendsChange(fire);

                sent.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_burn_that_isnt_there()
            {
                var fire = Door with { Exists = false };

                var sent = FireRules.SendsChange(fire);

                sent.ShouldBeFalse();
            }
        }

        public class MayAskToIgnite
        {
            /// A client's player lit a window.
            static readonly AskFacts Window = new AskFacts
            {
                Exists = true,
                ClientMode = true,
                Kind = FireKind.Window,
                Named = true,
            };

            [Theory]
            [InlineData(FireKind.Door)]
            [InlineData(FireKind.Window)]
            [InlineData(FireKind.Item)]
            public void Asks_for_doors_windows_and_items(FireKind kind)
            {
                var thing = Window with { Kind = kind };

                var asks = FireRules.MayAskToIgnite(thing);

                asks.ShouldBeTrue();
            }

            [Theory]
            [InlineData(FireKind.Enemy)]
            [InlineData(FireKind.None)]
            public void Leaves_enemies_and_the_rest(FireKind kind)
            {
                var thing = Window with { Kind = kind };

                var asks = FireRules.MayAskToIgnite(thing);

                asks.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_it_outside_a_client()
            {
                var thing = Window with { ClientMode = false };

                var asks = FireRules.MayAskToIgnite(thing);

                asks.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_replayed_fire()
            {
                var thing = Window with { Replaying = true };

                var asks = FireRules.MayAskToIgnite(thing);

                asks.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_thing_in_a_place_of_our_own()
            {
                var thing = Window with { OursAlone = true };

                var asks = FireRules.MayAskToIgnite(thing);

                asks.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_thing_it_cant_name()
            {
                var thing = Window with { Named = false };

                var asks = FireRules.MayAskToIgnite(thing);

                asks.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_a_thing_that_isnt_there()
            {
                var thing = Window with { Exists = false };

                var asks = FireRules.MayAskToIgnite(thing);

                asks.ShouldBeFalse();
            }
        }

        public class AskedTime
        {
            [Theory]
            [InlineData(true, 7f, 7f)]
            [InlineData(false, 7f, 20f)]
            public void Asks_for_its_own_burn_time_or_20(
                bool burning,
                float burnTime,
                float asked)
            {
                var time = FireRules.AskedTime(burning, burnTime);

                time.ShouldBe(asked);
            }
        }

        public class PutsOut
        {
            [Theory]
            [InlineData(false, true, true, true)]
            [InlineData(true, true, true, false)]
            [InlineData(false, false, true, false)]
            [InlineData(false, true, false, false)]
            public void Puts_out_only_a_refused_fire_still_burning(
                bool allowed,
                bool exists,
                bool burning,
                bool putsOut)
            {
                var put = FireRules.PutsOut(allowed, exists, burning);

                put.ShouldBe(putsOut);
            }
        }
    }
}
