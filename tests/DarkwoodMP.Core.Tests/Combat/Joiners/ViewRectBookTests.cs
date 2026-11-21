using DarkwoodMP.Combat.Joiners;

namespace DarkwoodMP.Tests.Combat.Joiners
{
    public class ViewRectBookTests
    {
        static readonly ViewRect Screen = new ViewRect(1200, 800);

        public class Accept
        {
            [Fact]
            public void Keeps_a_new_rect()
            {
                var book = new ViewRectBook();

                book.Accept(7, Screen).ShouldBeTrue();
            }

            [Fact]
            public void Keeps_it_clamped()
            {
                var book = new ViewRectBook();

                book.Accept(7, new ViewRect(100, 9000));

                book.TryGet(7, out var rect);
                rect.ShouldBe(new ViewRect(600, 6000));
            }

            [Fact]
            public void Ignores_the_rect_already_kept()
            {
                var book = new ViewRectBook();
                book.Accept(7, Screen);

                book.Accept(7, Screen).ShouldBeFalse();
            }

            [Fact]
            public void Ignores_a_rect_that_clamps_to_the_one_kept()
            {
                var book = new ViewRectBook();
                book.Accept(7, new ViewRect(100, 800));

                book.Accept(7, new ViewRect(200, 800)).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_each_peer_s_own()
            {
                var book = new ViewRectBook();
                book.Accept(7, Screen);

                book.Accept(8, Screen).ShouldBeTrue();
            }
        }

        public class TryGet
        {
            [Fact]
            public void Finds_none_for_an_unknown_peer()
            {
                var book = new ViewRectBook();

                book.TryGet(7, out _).ShouldBeFalse();
            }
        }

        public class Forget
        {
            [Fact]
            public void Drops_a_peer_s_rect()
            {
                var book = new ViewRectBook();
                book.Accept(7, Screen);

                book.Forget(7);

                book.TryGet(7, out _).ShouldBeFalse();
            }
        }

        public class Clear
        {
            [Fact]
            public void Drops_every_rect()
            {
                var book = new ViewRectBook();
                book.Accept(7, Screen);
                book.Accept(8, Screen);

                book.Clear();

                book.TryGet(8, out _).ShouldBeFalse();
            }
        }
    }
}
