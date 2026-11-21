using DarkwoodMP.World.Breaking;

namespace DarkwoodMP.Tests.World.Breaking
{
    public class ItemStateTests
    {
        static readonly ItemState Whole = new ItemState
        {
            Health = 5,
            Shadow = 2f,
        };

        public class EqualsOf
        {
            [Fact]
            public void Takes_the_same_state_as_equal()
            {
                var same = Whole.Equals(Whole with { });

                same.ShouldBeTrue();
            }

            [Fact]
            public void Tells_a_change_of_health()
            {
                var same = Whole.Equals(Whole with { Health = 4 });

                same.ShouldBeFalse();
            }

            [Fact]
            public void Tells_a_change_of_destroyed()
            {
                var same = Whole.Equals(Whole with { Destroyed = true });

                same.ShouldBeFalse();
            }

            [Fact]
            public void Tells_the_smallest_change_of_the_shadow()
            {
                var shadow = Whole with { Shadow = 2.0000002f };

                var same = Whole.Equals(shadow);

                same.ShouldBeFalse();
            }
        }
    }
}
