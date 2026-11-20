using DarkwoodMP.Combat.Damage;

namespace DarkwoodMP.Tests.Combat.Damage
{
    public class CutInHalfRulesTests
    {
        /// A chomper being cut in half whose body is gone throws.
        static readonly CutFacts Cut = new CutFacts
        {
            NullReference = true,
            Character = true,
            Cutting = true,
            Body = false,
        };

        public class Drops
        {
            [Fact]
            public void Drops_the_cut_s_null_reference()
            {
                CutInHalfRules.Drops(Cut).ShouldBeTrue();
            }

            [Fact]
            public void Keeps_another_exception()
            {
                var cut = Cut with { NullReference = false };

                CutInHalfRules.Drops(cut).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_it_with_no_character()
            {
                var cut = Cut with { Character = false };

                CutInHalfRules.Drops(cut).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_it_when_not_cutting()
            {
                var cut = Cut with { Cutting = false };

                CutInHalfRules.Drops(cut).ShouldBeFalse();
            }

            [Fact]
            public void Keeps_it_with_a_body()
            {
                var cut = Cut with { Body = true };

                CutInHalfRules.Drops(cut).ShouldBeFalse();
            }
        }
    }
}
