using DarkwoodMP.World.Inventories;

namespace DarkwoodMP.Tests.World.Inventories
{
    public class ContainerLocksTests
    {
        const string Chest = "Saved:42";
        const ulong Host = 1;
        const ulong Ann = 2;
        const ulong Bob = 3;

        static ContainerLocks HeldByAnn()
        {
            var locks = new ContainerLocks();
            locks.Open(Chest, Ann, out _);
            return locks;
        }

        public class Open
        {
            [Fact]
            public void Gives_a_free_container_to_the_opener()
            {
                var locks = new ContainerLocks();

                var opened = locks.Open(Chest, Ann, out _);

                opened.ShouldBeTrue();
            }

            [Fact]
            public void Refuses_a_container_another_player_holds()
            {
                var locks = HeldByAnn();

                var opened = locks.Open(Chest, Bob, out _);

                opened.ShouldBeFalse();
            }

            [Fact]
            public void Names_who_holds_it_when_refused()
            {
                var locks = HeldByAnn();

                locks.Open(Chest, Host, out var holder);

                holder.ShouldBe(Ann);
            }

            [Fact]
            public void Keeps_it_for_its_holder_opening_it_again()
            {
                var locks = HeldByAnn();

                var opened = locks.Open(Chest, Ann, out _);

                opened.ShouldBeTrue();
            }

            [Fact]
            public void Leaves_the_holder_when_refused()
            {
                var locks = HeldByAnn();

                locks.Open(Chest, Bob, out _);

                locks.HeldByOther(Chest, Bob, out var holder);
                holder.ShouldBe(Ann);
            }
        }

        public class Close
        {
            [Fact]
            public void Frees_a_container_its_holder_closes()
            {
                var locks = HeldByAnn();

                locks.Close(Chest, Ann);

                locks.Count.ShouldBe(0);
            }

            [Fact]
            public void Leaves_the_lock_when_someone_else_closes_it()
            {
                var locks = HeldByAnn();

                locks.Close(Chest, Bob);

                locks.HeldByOther(Chest, Bob, out _).ShouldBeTrue();
            }
        }

        public class HeldByOther
        {
            [Fact]
            public void Says_a_free_container_isnt_held()
            {
                var locks = new ContainerLocks();

                var held = locks.HeldByOther(Chest, Bob, out _);

                held.ShouldBeFalse();
            }

            [Fact]
            public void Says_ones_own_lock_isnt_anothers()
            {
                var locks = HeldByAnn();

                var held = locks.HeldByOther(Chest, Ann, out _);

                held.ShouldBeFalse();
            }

            [Fact]
            public void Names_the_other_holder()
            {
                var locks = HeldByAnn();

                locks.HeldByOther(Chest, Bob, out var holder);

                holder.ShouldBe(Ann);
            }
        }

        public class Leave
        {
            [Fact]
            public void Frees_everything_a_leaving_player_held()
            {
                var locks = HeldByAnn();
                locks.Open("Saved:43", Ann, out _);

                locks.Leave(Ann);

                locks.Count.ShouldBe(0);
            }

            [Fact]
            public void Keeps_what_the_others_hold()
            {
                var locks = HeldByAnn();
                locks.Open("Saved:43", Bob, out _);

                locks.Leave(Ann);

                locks.HeldByOther("Saved:43", Ann, out _).ShouldBeTrue();
            }
        }

        public class Clear
        {
            [Fact]
            public void Frees_every_container()
            {
                var locks = HeldByAnn();
                locks.Open("Saved:43", Bob, out _);

                locks.Clear();

                locks.Count.ShouldBe(0);
            }
        }
    }
}
