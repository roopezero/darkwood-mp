using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class SlotKeyRulesTests
    {
        /// Raw meat that rots, half fresh.
        static readonly SlotItem Meat = new SlotItem
        {
            Filled = true,
            Type = "meat",
            Amount = 2,
            Durability = 0.5f,
            HasBase = true,
            Rots = true,
            MaxDurability = 1f,
        };

        /// A plank, which doesn't drift.
        static readonly SlotItem Plank = new SlotItem
        {
            Filled = true,
            Type = "plank",
            Amount = 3,
            Durability = 1f,
            HasBase = true,
            MaxDurability = 1f,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_drift_tolerance()
            {
                var tolerance = SlotKeyRules.DriftTolerance;

                tolerance.ShouldBe(0.25f);
            }
        }

        public class Of
        {
            [Fact]
            public void Keys_an_empty_slot_as_nothing()
            {
                var key = SlotKeyRules.Of(new SlotItem());

                key.ShouldBe("");
            }

            [Fact]
            public void Keys_type_amount_durability_recipe_and_upgrades()
            {
                var gun = Plank with
                {
                    Type = "pistol",
                    Amount = 1,
                    Durability = 0.75f,
                    Recipe = true,
                    Upgrades = 2,
                };

                var key = SlotKeyRules.Of(gun);

                key.ShouldBe("pistol|1|750|1|2");
            }

            [Theory]
            [InlineData(0.0005f, "0")]
            [InlineData(0.0015f, "2")]
            [InlineData(0.0025f, "2")]
            [InlineData(0.0026f, "3")]
            public void Rounds_thousandths_half_to_even(
                float durability,
                string expected)
            {
                var item = Plank with { Durability = durability };

                var key = SlotKeyRules.Of(item);

                key.Split('|')[2].ShouldBe(expected);
            }
        }

        public class Drifts
        {
            [Fact]
            public void Says_food_that_rots_drifts()
            {
                var drifts = SlotKeyRules.Drifts(Meat);

                drifts.ShouldBeTrue();
            }

            [Fact]
            public void Says_an_item_that_recharges_drifts()
            {
                var lamp = Plank with { Regenerates = true };

                var drifts = SlotKeyRules.Drifts(lamp);

                drifts.ShouldBeTrue();
            }

            [Fact]
            public void Says_an_item_that_drains_drifts()
            {
                var lamp = Plank with { Drain = 0.01f };

                var drifts = SlotKeyRules.Drifts(lamp);

                drifts.ShouldBeTrue();
            }

            [Fact]
            public void Says_a_plank_doesnt_drift()
            {
                var drifts = SlotKeyRules.Drifts(Plank);

                drifts.ShouldBeFalse();
            }

            [Fact]
            public void Says_an_item_with_no_base_class_doesnt_drift()
            {
                var odd = Meat with { HasBase = false };

                var drifts = SlotKeyRules.Drifts(odd);

                drifts.ShouldBeFalse();
            }

            [Fact]
            public void Says_an_empty_slot_doesnt_drift()
            {
                var empty = Meat with { Filled = false };

                var drifts = SlotKeyRules.Drifts(empty);

                drifts.ShouldBeFalse();
            }
        }

        public class RotOnly
        {
            [Fact]
            public void Matches_equal_keys_of_any_item()
            {
                var same = SlotKeyRules.RotOnly(
                    Plank,
                    "plank|3|1000|0|0",
                    "plank|3|1000|0|0");

                same.ShouldBeTrue();
            }

            [Fact]
            public void Matches_rot_exactly_at_the_tolerance()
            {
                var same = SlotKeyRules.RotOnly(
                    Meat,
                    "meat|2|500|0|0",
                    "meat|2|250|0|0");

                same.ShouldBeTrue();
            }

            [Fact]
            public void Refuses_rot_just_past_the_tolerance()
            {
                var same = SlotKeyRules.RotOnly(
                    Meat,
                    "meat|2|500|0|0",
                    "meat|2|249|0|0");

                same.ShouldBeFalse();
            }

            [Fact]
            public void Measures_the_tolerance_of_the_items_maximum()
            {
                var big = Meat with { MaxDurability = 4f };

                var same = SlotKeyRules.RotOnly(
                    big,
                    "meat|2|3000|0|0",
                    "meat|2|2000|0|0");

                same.ShouldBeTrue();
            }

            [Fact]
            public void Measures_a_maximum_under_one_as_one()
            {
                var small = Meat with { MaxDurability = 0.1f };

                var same = SlotKeyRules.RotOnly(
                    small,
                    "meat|2|500|0|0",
                    "meat|2|250|0|0");

                same.ShouldBeTrue();
            }

            [Fact]
            public void Refuses_wear_on_an_item_that_doesnt_drift()
            {
                var same = SlotKeyRules.RotOnly(
                    Plank,
                    "plank|3|1000|0|0",
                    "plank|3|999|0|0");

                same.ShouldBeFalse();
            }

            [Theory]
            [InlineData("fish|2|400|0|0")]
            [InlineData("meat|1|400|0|0")]
            [InlineData("meat|2|400|1|0")]
            [InlineData("meat|2|400|0|1")]
            public void Refuses_another_part_that_changed(string other)
            {
                var same = SlotKeyRules.RotOnly(Meat, "meat|2|500|0|0", other);

                same.ShouldBeFalse();
            }

            [Theory]
            [InlineData("")]
            [InlineData(null)]
            [InlineData("meat|2|400|0")]
            [InlineData("meat|2|x|0|0")]
            public void Refuses_a_key_that_isnt_one(string other)
            {
                var same = SlotKeyRules.RotOnly(Meat, "meat|2|500|0|0", other);

                same.ShouldBeFalse();
            }
        }
    }
}
