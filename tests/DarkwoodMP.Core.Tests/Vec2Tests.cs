namespace DarkwoodMP.Tests
{
    public class Vec2Tests
    {
        public class Distance
        {
            [Fact]
            public void Measures_across_x_and_z()
            {
                var from = new Vec2(10f, 20f);
                var to = new Vec2(13f, 24f);

                var distance = Vec2.Distance(from, to);

                distance.ShouldBe(5f);
            }
        }

        public class Equal
        {
            [Fact]
            public void Matches_the_same_x_and_z()
            {
                var point = new Vec2(1f, 2f);

                var same = point.Equals(new Vec2(1f, 2f));

                same.ShouldBeTrue();
            }

            [Fact]
            public void Tells_apart_a_different_x()
            {
                var point = new Vec2(1f, 2f);

                var same = point.Equals(new Vec2(9f, 2f));

                same.ShouldBeFalse();
            }

            [Fact]
            public void Tells_apart_a_different_z()
            {
                var point = new Vec2(1f, 2f);

                var same = point.Equals(new Vec2(1f, 9f));

                same.ShouldBeFalse();
            }
        }

        public class HashCode
        {
            [Fact]
            public void Gives_equal_points_the_same_hash()
            {
                var point = new Vec2(1f, 2f);

                var hash = point.GetHashCode();

                hash.ShouldBe(new Vec2(1f, 2f).GetHashCode());
            }
        }
    }
}
