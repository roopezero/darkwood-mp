namespace DarkwoodMP.Tests
{
    public class SecondsTests
    {
        public class Minus
        {
            [Fact]
            public void Gives_the_time_between_two_moments()
            {
                var early = new Seconds(2f);
                var late = new Seconds(5.5f);

                var span = late - early;

                span.Value.ShouldBe(3.5f);
            }
        }

        public class GreaterThan
        {
            [Fact]
            public void Holds_for_a_later_time()
            {
                var early = new Seconds(2f);
                var late = new Seconds(3f);

                var later = late > early;

                later.ShouldBeTrue();
            }

            [Fact]
            public void Fails_for_the_same_time()
            {
                var time = new Seconds(2f);

                var later = time > new Seconds(2f);

                later.ShouldBeFalse();
            }
        }

        public class LessThan
        {
            [Fact]
            public void Holds_for_an_earlier_time()
            {
                var early = new Seconds(2f);
                var late = new Seconds(3f);

                var earlier = early < late;

                earlier.ShouldBeTrue();
            }

            [Fact]
            public void Fails_for_the_same_time()
            {
                var time = new Seconds(2f);

                var earlier = time < new Seconds(2f);

                earlier.ShouldBeFalse();
            }
        }
    }
}
