using DarkwoodMP.World.Stacks;

namespace DarkwoodMP.Tests.World.Stacks
{
    public class StackRulesTests
    {
        /// A plain stackable item that stacks to 10 in the game.
        static readonly StackItemFacts Planks = new StackItemFacts
        {
            Stackable = true,
            Max = 10,
        };

        /// Seven of them in one slot.
        static readonly StackSlotFacts Slot = new StackSlotFacts
        {
            Exists = true,
            Item = Planks,
            Amount = 7,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_stack_limits()
            {
                var limits = new[]
                {
                    StackRules.NoHostSize,
                    StackRules.Single,
                    StackRules.MostOnWire,
                };

                limits.ShouldBe(new[] { -1, 1, 65535 });
            }
        }

        public class Eligible
        {
            [Fact]
            public void Takes_a_stackable_item()
            {
                var eligible = StackRules.Eligible(Planks);

                eligible.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_an_item_that_does_not_stack()
            {
                var item = Planks with { Stackable = false };

                var eligible = StackRules.Eligible(item);

                eligible.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_an_item_whose_amount_is_fuel()
            {
                var item = Planks with { StacksFuel = true };

                var eligible = StackRules.Eligible(item);

                eligible.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_an_item_that_holds_ammo()
            {
                var item = Planks with { HoldsAmmo = true };

                var eligible = StackRules.Eligible(item);

                eligible.ShouldBeFalse();
            }

            [Fact]
            public void Takes_an_item_that_stacks_to_two()
            {
                var item = Planks with { Max = 2 };

                var eligible = StackRules.Eligible(item);

                eligible.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_an_item_that_stacks_to_one()
            {
                var item = Planks with { Max = 1 };

                var eligible = StackRules.Eligible(item);

                eligible.ShouldBeFalse();
            }
        }

        public class SizeFor
        {
            [Theory]
            [InlineData(10, 50, 50)]
            [InlineData(50, 30, 50)]
            [InlineData(10, 10, 10)]
            [InlineData(10, 0, 10)]
            [InlineData(10, -5, 10)]
            [InlineData(2, 1, 2)]
            public void Only_ever_raises_a_stack(
                int original,
                int size,
                int max)
            {
                var result = StackRules.SizeFor(original, size);

                result.ShouldBe(max);
            }
        }

        public class Fits
        {
            [Fact]
            public void Fits_a_stack_under_its_size()
            {
                var fits = StackRules.Fits(Slot);

                fits.ShouldBeTrue();
            }

            [Fact]
            public void Fits_a_stack_exactly_at_its_size()
            {
                var slot = Slot with { Amount = 10 };

                var fits = StackRules.Fits(slot);

                fits.ShouldBeTrue();
            }

            [Fact]
            public void Refuses_a_stack_one_past_its_size()
            {
                var slot = Slot with { Amount = 11 };

                var fits = StackRules.Fits(slot);

                fits.ShouldBeFalse();
            }

            [Fact]
            public void Fits_anything_with_no_item()
            {
                var slot = Slot with { Exists = false, Amount = 999 };

                var fits = StackRules.Fits(slot);

                fits.ShouldBeTrue();
            }

            [Fact]
            public void Fits_any_amount_of_an_item_that_is_fuel()
            {
                var slot = Slot with
                {
                    Item = Planks with { StacksFuel = true },
                    Amount = 999,
                };

                var fits = StackRules.Fits(slot);

                fits.ShouldBeTrue();
            }

            [Fact]
            public void Fits_any_amount_of_an_item_that_stacks_to_one()
            {
                var slot = Slot with
                {
                    Item = Planks with { Max = 1 },
                    Amount = 5,
                };

                var fits = StackRules.Fits(slot);

                fits.ShouldBeTrue();
            }
        }

        public class Current
        {
            [Theory]
            [InlineData(-1, 50, 50)]
            [InlineData(-1, 0, 0)]
            [InlineData(-1, -3, 0)]
            [InlineData(30, 50, 30)]
            [InlineData(0, 50, 0)]
            public void Takes_the_host_size_over_our_setting(
                int host,
                int setting,
                int size)
            {
                var result = StackRules.Current(host, setting);

                result.ShouldBe(size);
            }
        }

        public class FollowsHost
        {
            [Theory]
            [InlineData(-1, false)]
            [InlineData(0, true)]
            [InlineData(30, true)]
            public void Follows_any_host_size_of_zero_or_more(
                int host,
                bool follows)
            {
                var result = StackRules.FollowsHost(host);

                result.ShouldBe(follows);
            }
        }

        public class OnSettingChanged
        {
            [Fact]
            public void Ignores_it_as_a_client_in_a_session()
            {
                var change = StackRules.OnSettingChanged(30, false);

                change.ShouldBe(StackChange.Ignore);
            }

            [Fact]
            public void Applies_it_playing_alone()
            {
                var change = StackRules.OnSettingChanged(-1, false);

                change.ShouldBe(StackChange.Apply);
            }

            [Fact]
            public void Sends_it_to_everyone_as_the_host()
            {
                var change = StackRules.OnSettingChanged(-1, true);

                change.ShouldBe(StackChange.ApplyAndSend);
            }
        }

        public class TakesSize
        {
            [Theory]
            [InlineData(true, true, true)]
            [InlineData(true, false, false)]
            [InlineData(false, true, false)]
            public void Takes_it_from_the_host_as_a_client(
                bool client,
                bool fromHost,
                bool takes)
            {
                var result = StackRules.TakesSize(client, fromHost);

                result.ShouldBe(takes);
            }
        }

        public class OnWire
        {
            [Theory]
            [InlineData(0, 0)]
            [InlineData(999, 999)]
            [InlineData(65535, 65535)]
            [InlineData(65536, 65535)]
            [InlineData(-1, 0)]
            public void Clamps_the_size_to_sixteen_bits(int size, int wire)
            {
                var result = StackRules.OnWire(size);

                result.ShouldBe((ushort)wire);
            }
        }
    }
}
