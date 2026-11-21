using DarkwoodMP.Combat.Cues;

namespace DarkwoodMP.Tests.Combat.Cues
{
    public class CueRulesTests
    {
        /// A swing from a living joiner, on the host.
        static readonly CueFacts Swing = new CueFacts
        {
            Hosting = true,
            Kind = 0,
            StandInAlive = true,
        };

        public class Limits
        {
            [Fact]
            public void Pins_the_cue_kinds()
            {
                var kinds = new[] { CueRules.Dodge, CueRules.Scare };

                kinds.ShouldBe(new byte[] { 0, 1 });
            }

            [Fact]
            public void Pins_the_cue_limits()
            {
                var limits = new[]
                {
                    CueRules.DodgeRadius,
                    CueRules.ScareRadius,
                    CueRules.EnemyLayers,
                    CueRules.MaxNear,
                };

                limits.ShouldBe(new[] { 150f, 350f, 2099200f, 128f });
            }
        }

        public class Sends
        {
            [Theory]
            [InlineData(true, true, true, true)]
            [InlineData(false, true, true, false)]
            [InlineData(true, false, true, false)]
            [InlineData(true, true, false, false)]
            public void Sends_from_a_client_running_copies(
                bool session,
                bool client,
                bool copies,
                bool sends)
            {
                CueRules.Sends(session, client, copies).ShouldBe(sends);
            }
        }

        public class Reaction
        {
            [Fact]
            public void Makes_them_dodge_a_swing()
            {
                CueRules.Reaction(Swing).ShouldBe(CueReaction.Dodge);
            }

            [Fact]
            public void Scares_them_with_an_aim()
            {
                var cue = Swing with { Kind = 1 };

                CueRules.Reaction(cue).ShouldBe(CueReaction.Scare);
            }

            [Fact]
            public void Drops_an_unknown_kind()
            {
                var cue = Swing with { Kind = 2 };

                CueRules.Reaction(cue).ShouldBe(CueReaction.None);
            }

            [Fact]
            public void Drops_a_cue_when_not_hosting()
            {
                var cue = Swing with { Hosting = false };

                CueRules.Reaction(cue).ShouldBe(CueReaction.None);
            }

            [Fact]
            public void Drops_a_cue_with_no_living_stand_in()
            {
                var cue = Swing with { StandInAlive = false };

                CueRules.Reaction(cue).ShouldBe(CueReaction.None);
            }
        }

        public class Reacts
        {
            [Theory]
            [InlineData(true, true, false)]
            [InlineData(true, false, true)]
            [InlineData(false, true, true)]
            public void Freezes_only_a_client_s_copies(
                bool copies,
                bool frozen,
                bool reacts)
            {
                CueRules.Reacts(copies, frozen).ShouldBe(reacts);
            }
        }
    }
}
