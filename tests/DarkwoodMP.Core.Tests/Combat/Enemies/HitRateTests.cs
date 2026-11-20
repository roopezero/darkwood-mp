using DarkwoodMP.Combat.Enemies;

namespace DarkwoodMP.Tests.Combat.Enemies
{
    public class HitRateTests
    {
        const ulong Player = 7;

        static readonly FiringFacts BareHands = default;

        static readonly FiringFacts Axe = new FiringFacts
        {
            Armed = true,
            Melee = true,
            Type = "axe",
        };

        /// Hits at 0.0, 0.01, ... seconds: 24 of them by 0.23 s.
        static HitRate Landed(int hits)
        {
            var rate = new HitRate();
            for (int i = 0; i < hits; i++)
            {
                rate.Take(Player, new Seconds(i * 0.01f), BareHands);
            }

            return rate;
        }

        public class Limits
        {
            [Fact]
            public void Pins_the_window()
            {
                var window = HitRate.Window;

                window.ShouldBe(1f);
            }
        }

        public class Take
        {
            [Fact]
            public void Allows_the_24th_hit_in_a_second()
            {
                var rate = Landed(23);

                var verdict = rate.Take(Player, new Seconds(0.5f), BareHands);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Refuses_the_25th_hit_in_a_second()
            {
                var rate = Landed(24);

                var verdict = rate.Take(Player, new Seconds(0.5f), BareHands);

                verdict.Reason.ShouldBe(Refusal.HitsTooFast);
            }

            [Fact]
            public void Names_bare_hands_in_the_refusal()
            {
                var rate = Landed(24);

                var verdict = rate.Take(Player, new Seconds(0.5f), BareHands);

                verdict.Detail.ShouldBe("bare hands");
            }

            [Fact]
            public void Names_the_weapon_type_in_the_refusal()
            {
                var rate = Landed(24);

                var verdict = rate.Take(Player, new Seconds(0.5f), Axe);

                verdict.Detail.ShouldBe("axe");
            }

            [Fact]
            public void Names_bare_hands_for_a_weapon_of_no_type()
            {
                var rate = Landed(24);
                var weapon = Axe with { Type = null };

                var verdict = rate.Take(Player, new Seconds(0.5f), weapon);

                verdict.Detail.ShouldBe("bare hands");
            }

            [Fact]
            public void Words_the_refusal_as_the_host_does()
            {
                var rate = Landed(24);
                var verdict = rate.Take(Player, new Seconds(0.5f), Axe);

                var text = Refusals.Text(verdict);

                text.ShouldBe("more hits a second than axe makes");
            }

            [Fact]
            public void Does_not_count_a_refused_hit()
            {
                var rate = Landed(24);
                rate.Take(Player, new Seconds(0.5f), BareHands);

                var verdict = rate.Take(Player, new Seconds(1.005f), BareHands);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Still_counts_a_hit_exactly_a_second_old()
            {
                var rate = Landed(24);

                var verdict = rate.Take(Player, new Seconds(1f), BareHands);

                verdict.Reason.ShouldBe(Refusal.HitsTooFast);
            }

            [Fact]
            public void Forgets_a_hit_just_over_a_second_old()
            {
                var rate = Landed(24);

                var verdict = rate.Take(Player, new Seconds(1.001f), BareHands);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Counts_each_player_apart()
            {
                var rate = Landed(24);

                var verdict = rate.Take(8, new Seconds(0.5f), BareHands);

                verdict.ShouldBe(Verdict.Allow);
            }

            [Fact]
            public void Allows_more_hits_for_a_gun_with_pellets()
            {
                var rate = Landed(24);
                var gun = new FiringFacts
                {
                    Armed = true,
                    Firearm = true,
                    FireRate = 0.25f,
                    Projectiles = 8,
                };

                var verdict = rate.Take(Player, new Seconds(0.5f), gun);

                verdict.ShouldBe(Verdict.Allow);
            }
        }
    }
}
