namespace DarkwoodMP.Tests
{
    public class TickTests
    {
        public class At
        {
            [Fact]
            public void Runs_exactly_at_the_time()
            {
                var tick = Tick.At(
                    new Seconds(2f), new Seconds(2f), new Seconds(0.5f));

                tick.Due.ShouldBeTrue();
                tick.Next.Value.ShouldBe(2.5f);
            }

            [Fact]
            public void Waits_before_it_and_keeps_the_time()
            {
                var tick = Tick.At(
                    new Seconds(1.9f), new Seconds(2f), new Seconds(0.5f));

                tick.Due.ShouldBeFalse();
                tick.Next.Value.ShouldBe(2f);
            }

            [Fact]
            public void Counts_from_now_when_late()
            {
                var tick = Tick.At(
                    new Seconds(5f), new Seconds(2f), new Seconds(0.5f));

                tick.Next.Value.ShouldBe(5.5f);
            }
        }
    }
}
