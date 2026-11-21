using System;
using System.Globalization;

namespace DarkwoodMP.World.Fuel
{
    /// The host's side of the generators' and saws' fuel (the plugin's
    /// Fuel): a client's pours and log cuts, and the levels it sends.
    public static class FuelRules
    {
        /// How far from a tank a client's player may pour into it
        /// (WorldActions.Reach, through Authority.Near).
        public const float Reach = ReachRules.Reach;

        /// What a can holds when the game's gasoline item doesn't say.
        public const float FallbackCan = 100f;

        /// How much more than a can a pour may be, for rounding.
        public const float CanMargin = 0.01f;

        /// How near the top a tank counts as full.
        public const float FullMargin = 0.01f;

        /// The fewest logs a cut may be.
        public const float FewestLogs = 0.99f;

        /// How far from a whole number of logs a cut may be.
        public const float LogTolerance = 0.02f;

        /// How much more than the saw has a cut may take.
        public const float SawSlack = 0.5f;

        /// How much a generator's level must change for the 5-second check
        /// to send it.
        public const float LevelChange = 0.5f;

        /// How often, in seconds, the host checks its generators.
        public const float CheckInterval = 5f;

        /// The tank the message's what byte names: 1 is a generator, any
        /// other value a saw.
        public static FuelTank TankOf(byte what)
        {
            if (what == (byte)FuelTank.Generator)
            {
                return FuelTank.Generator;
            }

            return FuelTank.Saw;
        }

        /// What a can holds: the gasoline item's maxDurability, when there
        /// is one above 0 (pass 0 when there's no item).
        public static float CanHolds(float maxDurability)
        {
            if (maxDurability > 0f)
            {
                return maxDurability;
            }

            return FallbackCan;
        }

        /// Host: a client's Fuel, taken only inside a Request. A pour is
        /// clamped to what fits; a cut is taken whole. The checks run in
        /// this order, and the first that fails wins.
        public static FuelAnswer MayAdd(in FuelFacts fuel)
        {
            if (!fuel.Found)
            {
                return new FuelAnswer { Verdict = NotHere(fuel) };
            }

            if (!ReachRules.Near(fuel.Reach with { Reach = Reach }))
            {
                return Back(Verdict.Refuse(Refusal.TooFar));
            }

            float value = fuel.Value;
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return Back(Verdict.Refuse(Refusal.NotANumber));
            }

            float took = value;
            if (value > 0f)
            {
                if (value > fuel.CanHolds + CanMargin)
                {
                    return Back(MoreThanACan(fuel));
                }

                if (fuel.Have >= fuel.Max - FullMargin)
                {
                    return Back(TankFull(fuel));
                }

                took = Min(value, fuel.Max - fuel.Have);
            }
            else
            {
                var cut = MayCut(fuel);
                if (cut.Refused)
                {
                    return Back(cut);
                }
            }

            return new FuelAnswer
            {
                Took = took,
                Level = Clamp(fuel.Have + took, 0f, fuel.Max),
            };
        }

        /// The request's Result for an allowed Fuel: the amount taken, in
        /// the invariant culture (the refusals' numbers are in the host's
        /// own).
        public static string ResultOf(float took)
        {
            return took.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// Host, every CheckInterval: whether a generator's level goes out.
        /// It does when it was never sent, moved by LevelChange or more, or
        /// its running state changed.
        public static bool Changed(in SentFacts generator)
        {
            if (!generator.Sent)
            {
                return true;
            }

            float moved = Math.Abs(generator.SentFuel - generator.Fuel);
            if (moved < LevelChange && generator.SentOn == generator.On)
            {
                return false;
            }

            return true;
        }

        /// Whether the host checks its generators now.
        public static bool CheckDue(bool host, Seconds now, Seconds next)
        {
            return host && now.Value >= next.Value;
        }

        /// When the host checks its generators next.
        public static Seconds NextCheck(Seconds now)
        {
            return new Seconds(now.Value + CheckInterval);
        }

        /// A cut: only at a saw, a whole number of logs, and no more than
        /// it has (with SawSlack).
        static Verdict MayCut(in FuelFacts fuel)
        {
            if (fuel.Tank == FuelTank.Generator)
            {
                return Verdict.Refuse(Refusal.GeneratorFuelTaken);
            }

            float used = -fuel.Value;
            float logs = used / MaxInt(1, fuel.FuelToConvert);
            float whole = (float)Math.Round(logs);
            if (logs < FewestLogs || Math.Abs(logs - whole) > LogTolerance)
            {
                var detail = Refusals.Detail(
                    used.ToString("0.0"),
                    fuel.FuelToConvert.ToString());
                return Verdict.Refuse(Refusal.NotWholeLogs, detail);
            }

            if (used > fuel.Have + SawSlack)
            {
                var have = fuel.Have.ToString("0.0");
                return Verdict.Refuse(Refusal.NotEnoughFuel, have);
            }

            return Verdict.Allow;
        }

        static Verdict NotHere(in FuelFacts fuel)
        {
            if (fuel.Tank == FuelTank.Generator)
            {
                return Verdict.Refuse(
                    Refusal.NoSuchGenerator,
                    fuel.Ref.ToString());
            }

            return Verdict.Refuse(Refusal.NoSuchSaw, fuel.Ref.ToString());
        }

        static Verdict MoreThanACan(in FuelFacts fuel)
        {
            var detail = Refusals.Detail(
                fuel.Value.ToString("0.0"),
                fuel.CanHolds.ToString("0"));
            return Verdict.Refuse(Refusal.MoreThanACan, detail);
        }

        static Verdict TankFull(in FuelFacts fuel)
        {
            var detail = Refusals.Detail(
                fuel.Have.ToString("0.0"),
                fuel.Max.ToString("0"));
            return Verdict.Refuse(Refusal.TankFull, detail);
        }

        static FuelAnswer Back(Verdict refusal)
        {
            return new FuelAnswer { Verdict = refusal, SendsBack = true };
        }

        /// As Mathf.Min.
        static float Min(float a, float b)
        {
            if (a < b)
            {
                return a;
            }

            return b;
        }

        /// As Mathf.Max for ints.
        static int MaxInt(int a, int b)
        {
            if (a > b)
            {
                return a;
            }

            return b;
        }

        /// As Mathf.Clamp: NaN stays NaN.
        internal static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }
    }
}
