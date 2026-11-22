using DarkwoodMP.World.Npcs;

namespace DarkwoodMP.Tests.World.Npcs
{
    public class NpcDeathRulesTests
    {
        public class Limits
        {
            [Fact]
            public void Pins_the_death_limits()
            {
                var limits = new[]
                {
                    NpcDeathRules.DeathReach,
                    NpcDeathRules.FindRadius,
                };

                limits.ShouldBe(new[] { 2500f, 400f });
            }
        }

        public class Pick
        {
            /// A death of "trader" at the origin, whose ObjRef found
            /// nothing.
            static readonly DyingFacts Trader = new DyingFacts
            {
                Name = "trader",
                At = new Vec2(0f, 0f),
            };

            static NpcCandidate Living(string name, float x)
            {
                return new NpcCandidate
                {
                    Name = name,
                    HasCharacter = true,
                    Alive = true,
                    At = new Vec2(x, 0f),
                };
            }

            [Fact]
            public void Takes_the_ref_npc_of_that_name()
            {
                var death = Trader with { RefIsNpc = true, RefName = "trader" };
                var npcs = new[] { Living("trader", 1f) };

                var pick = NpcDeathRules.Pick(death, npcs);

                pick.ShouldBe(NpcDeathRules.ByRef);
            }

            [Fact]
            public void Takes_the_ref_npc_of_a_death_with_no_name()
            {
                var death = Trader with { RefIsNpc = true, Name = "" };
                var npcs = new[] { Living("", 1f) };

                var pick = NpcDeathRules.Pick(death, npcs);

                pick.ShouldBe(NpcDeathRules.ByRef);
            }

            [Fact]
            public void Takes_the_ref_result_of_a_death_with_no_name()
            {
                var death = Trader with { Name = null };
                var npcs = new[] { Living("trader", 1f) };

                var pick = NpcDeathRules.Pick(death, npcs);

                pick.ShouldBe(NpcDeathRules.ByRef);
            }

            [Fact]
            public void Takes_the_nearest_living_npc_of_that_name()
            {
                var npcs = new[]
                {
                    Living("trader", 300f),
                    Living("wolf", 10f),
                    Living("trader", 100f),
                    Living("trader", 200f),
                };

                var pick = NpcDeathRules.Pick(Trader, npcs);

                pick.ShouldBe(2);
            }

            [Fact]
            public void Looks_past_a_ref_npc_with_another_name()
            {
                var death = Trader with { RefIsNpc = true, RefName = "wolf" };
                var npcs = new[] { Living("trader", 1f) };

                var pick = NpcDeathRules.Pick(death, npcs);

                pick.ShouldBe(0);
            }

            [Fact]
            public void Leaves_out_a_dead_npc()
            {
                var npc = Living("trader", 1f) with { Alive = false };

                var pick = NpcDeathRules.Pick(Trader, new[] { npc });

                pick.ShouldBe(NpcDeathRules.ByRef);
            }

            [Fact]
            public void Leaves_out_an_npc_with_no_character()
            {
                var npc = Living("trader", 1f) with { HasCharacter = false };

                var pick = NpcDeathRules.Pick(Trader, new[] { npc });

                pick.ShouldBe(NpcDeathRules.ByRef);
            }

            [Fact]
            public void Takes_an_npc_just_inside_the_radius()
            {
                var npcs = new[] { Living("trader", 399.9f) };

                var pick = NpcDeathRules.Pick(Trader, npcs);

                pick.ShouldBe(0);
            }

            [Fact]
            public void Leaves_out_an_npc_exactly_at_the_radius()
            {
                var npcs = new[] { Living("trader", 400f) };

                var pick = NpcDeathRules.Pick(Trader, npcs);

                pick.ShouldBe(NpcDeathRules.ByRef);
            }

            [Fact]
            public void Leaves_out_an_npc_exactly_at_the_radius_on_z()
            {
                var at = new Vec2(0f, 400f);
                var npc = Living("trader", 0f) with { At = at };

                var pick = NpcDeathRules.Pick(Trader, new[] { npc });

                pick.ShouldBe(NpcDeathRules.ByRef);
            }

            [Fact]
            public void Leaves_out_an_npc_past_the_radius_on_a_diagonal()
            {
                var at = new Vec2(300f, 300f);
                var npc = Living("trader", 0f) with { At = at };

                var pick = NpcDeathRules.Pick(Trader, new[] { npc });

                pick.ShouldBe(NpcDeathRules.ByRef);
            }

            [Fact]
            public void Takes_an_npc_just_inside_the_radius_on_z()
            {
                var at = new Vec2(0f, 399.9f);
                var npc = Living("trader", 0f) with { At = at };

                var pick = NpcDeathRules.Pick(Trader, new[] { npc });

                pick.ShouldBe(0);
            }

            [Fact]
            public void Takes_the_first_of_two_equally_near()
            {
                var npcs = new[]
                {
                    Living("trader", 100f),
                    Living("trader", 100f),
                };

                var pick = NpcDeathRules.Pick(Trader, npcs);

                pick.ShouldBe(0);
            }

            [Fact]
            public void Looks_past_a_ref_that_isnt_an_npc()
            {
                var death = Trader with { RefName = "trader" };
                var npcs = new[] { Living("trader", 1f) };

                var pick = NpcDeathRules.Pick(death, npcs);

                pick.ShouldBe(0);
            }

            [Fact]
            public void Falls_back_to_a_ref_npc_with_another_name()
            {
                var death = Trader with { RefIsNpc = true, RefName = "wolf" };
                var npcs = new[] { Living("trader", 500f) };

                var pick = NpcDeathRules.Pick(death, npcs);

                pick.ShouldBe(NpcDeathRules.ByRef);
            }
        }

        public class MayDie
        {
            /// A found NPC exactly at the death's reach.
            static readonly NpcDeathFacts Found = new NpcDeathFacts
            {
                Found = true,
                Requester = Requesters.At(NpcDeathRules.DeathReach),
            };

            [Fact]
            public void Allows_an_npc_at_the_death_reach()
            {
                var verdict = NpcDeathRules.MayDie(Found);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_an_npc_that_isnt_here()
            {
                var npc = Found with { Found = false };

                var verdict = NpcDeathRules.MayDie(npc);

                verdict.Reason.ShouldBe(Refusal.NoSuchNpc);
            }

            [Fact]
            public void Refuses_an_npc_just_past_the_death_reach()
            {
                var far = Requesters.At(NpcDeathRules.DeathReach + 0.5f);
                var npc = Found with { Requester = far };

                var verdict = NpcDeathRules.MayDie(npc);

                verdict.Reason.ShouldBe(Refusal.OutOfReach);
            }
        }

        public class SendsDeath
        {
            /// A living NPC's death at the outermost call.
            static readonly DeathSentFacts Wolf = new DeathSentFacts
            {
                Outermost = true,
                Sharing = true,
                HasNpc = true,
                WasAlive = true,
            };

            [Fact]
            public void Sends_the_death_of_a_living_npc()
            {
                NpcDeathRules.SendsDeath(Wolf).ShouldBeTrue();
            }

            [Fact]
            public void Sends_nothing_from_a_nested_call()
            {
                var npc = Wolf with { Outermost = false };

                NpcDeathRules.SendsDeath(npc).ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_while_not_sharing()
            {
                var npc = Wolf with { Sharing = false };

                NpcDeathRules.SendsDeath(npc).ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_for_a_character_with_no_npc()
            {
                var npc = Wolf with { HasNpc = false };

                NpcDeathRules.SendsDeath(npc).ShouldBeFalse();
            }

            [Fact]
            public void Sends_nothing_for_one_already_dead()
            {
                var npc = Wolf with { WasAlive = false };

                NpcDeathRules.SendsDeath(npc).ShouldBeFalse();
            }
        }
    }
}
