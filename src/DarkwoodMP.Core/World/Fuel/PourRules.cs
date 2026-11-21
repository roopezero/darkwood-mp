using System;
using System.Globalization;

namespace DarkwoodMP.World.Fuel
{
    /// The pourer's side of the fuel (the plugin's Fuel): which changes
    /// it records, when they go out, what it gives back when the host takes
    /// less, and the level it shows.
    public static class PourRules
    {
        /// A change smaller than this isn't a pour.
        public const float Smallest = 0.0001f;

        /// How often, in seconds, pending pours go out.
        public const float FlushInterval = 0.25f;

        /// How much less than asked the host must take for some to come
        /// back.
        public const float TookTolerance = 0.001f;

        /// The least fuel worth giving back.
        public const float LeastBack = 0.001f;

        /// The item type of a gasoline can.
        public const string Gasoline = "gasoline";

        /// Whether a change of a tank's fuel is recorded to send (Poured).
        /// Not while not sharing or replaying, nor a change too small to
        /// be a pour.
        public static bool Records(bool sharing, bool replaying, float delta)
        {
            if (!sharing || replaying)
            {
                return false;
            }

            return Math.Abs(delta) >= Smallest;
        }

        /// Whether a change is a log cut, kept apart from pours: the host
        /// checks the two differently.
        public static bool IsCut(float delta)
        {
            return delta < 0f;
        }

        /// Whether a pour came from the gasoline can the local player holds
        /// (Player.currentItem): it then counts as one drained pour.
        public static bool FromCan(float delta, bool holds, string type)
        {
            return delta > 0f && holds && type == Gasoline;
        }

        /// Whether pending pours go out now.
        public static bool FlushDue(int pending, Seconds now, Seconds next)
        {
            return pending > 0 && now.Value >= next.Value;
        }

        /// When pending pours go out next.
        public static Seconds NextFlush(Seconds now)
        {
            return new Seconds(now.Value + FlushInterval);
        }

        /// Whether pending pours go out before an Item.turnOn, so a
        /// generator's fuel arrives before it is switched on.
        public static bool FlushesBeforeOn(bool sharing, bool replaying)
        {
            return sharing && !replaying;
        }

        /// Client, when the host answers: how many drained pours go back to
        /// the can. Refused: all of them. Allowed, but the host took less of
        /// a pour than asked: the part it didn't take. A result that doesn't
        /// read as a number gives nothing back.
        public static float Back(in RefundFacts pour)
        {
            if (!pour.Allowed)
            {
                return pour.Drained;
            }

            if (pour.Amount <= 0f)
            {
                return 0f;
            }

            if (!float.TryParse(
                pour.Result,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float took))
            {
                return 0f;
            }

            if (took >= pour.Amount - TookTolerance)
            {
                return 0f;
            }

            return pour.Drained * (1f - Clamp01(took / pour.Amount));
        }

        /// Whether that much is worth giving back.
        public static bool Refunds(float back)
        {
            return back > LeastBack;
        }

        /// How it is given back: nothing with no player or no can; a new
        /// can when the can was used up (amount 0 or less); else into it.
        public static RefundKind RefundOf(bool player, bool can, float amount)
        {
            if (!player || !can)
            {
                return RefundKind.Nothing;
            }

            if (amount <= 0f)
            {
                return RefundKind.NewCan;
            }

            return RefundKind.Refill;
        }

        /// The can's durability after the fuel goes back, up to its most.
        public static float Refilled(float durability, float back, float max)
        {
            float full = durability + back;
            if (max < full)
            {
                return max;
            }

            return full;
        }

        /// Client: the fuel a tank shows with the host's level: that level
        /// plus what is pending for it here (pours and cuts), from 0 to the
        /// most it holds.
        public static float Shown(float level, float pending, float max)
        {
            return FuelRules.Clamp(level + pending, 0f, max);
        }

        /// Client: what to do to its generator with the host's running
        /// state. Switching on goes through its Item when it has one.
        public static GeneratorSwitch Switch(bool on, bool running, bool item)
        {
            if (on && !running)
            {
                if (item)
                {
                    return GeneratorSwitch.TurnOnItem;
                }

                return GeneratorSwitch.TurnOnGenerator;
            }

            if (!on && running)
            {
                return GeneratorSwitch.PowerDown;
            }

            return GeneratorSwitch.Leave;
        }

        /// As Mathf.Clamp01.
        static float Clamp01(float value)
        {
            return FuelRules.Clamp(value, 0f, 1f);
        }
    }
}
