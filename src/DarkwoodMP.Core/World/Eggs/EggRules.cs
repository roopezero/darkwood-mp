using System;

namespace DarkwoodMP.World.Eggs
{
    /// Larva eggs and other items broken open to get at what's inside
    /// (the plugin's Eggs). Breaking one is the one thing shared:
    /// EggDestroyed, instead of the ItemHit or ItemBroken BreakSync sends.
    public static class EggRules
    {
        /// What an egg's name holds, in any case (Objects/larva_egg_big).
        public const string Word = "egg";

        /// How far from where the sender saw it a copy found by its name may
        /// be (ObjRef.PlacedSlack).
        public const float FindRadius = ObjRefRules.PlacedSlack;

        /// How far from an egg a player may break it: eggs are shot too
        /// (WorldActions.ShotReach).
        public const float Reach = ReachRules.ShotReach;

        /// Whether an item is an egg: destructible, and broken open to get
        /// at its inventory or named so (Eggs.IsEgg). The name is only
        /// looked at when the item doesn't say.
        public static bool IsEgg(
            bool destructible,
            bool opensByBreaking,
            string name)
        {
            if (!destructible)
            {
                return false;
            }

            if (opensByBreaking)
            {
                return true;
            }

            return name.IndexOf(Word, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// Whether this game sends the egg it broke (Eggs.Send): not in a
        /// place of our own (WorldActions.OursAlone), and named by a valid
        /// ObjRef.
        public static bool Sends(bool oursAlone, bool named)
        {
            return !oursAlone && named;
        }

        /// The health the sender leaves its broken egg at: a hit can take it
        /// below 0, and the others' copies are broken at 0.
        public static int SentHealth(int health)
        {
            if (health < 0)
            {
                return 0;
            }

            return health;
        }

        /// Host: a client's EggDestroyed. Every refusal sends the client
        /// the egg's state to roll back to (WorldActions.SendState) before
        /// the verdict. An egg broken here already is allowed from any
        /// distance, and Breaks then says nothing is done.
        public static Verdict MayBreak(in EggFacts egg)
        {
            if (!egg.Found)
            {
                return Verdict.Refuse(Refusal.NoSuchEgg);
            }

            if (!IsEgg(egg.Destructible, egg.OpensByBreaking, egg.Name))
            {
                return Verdict.Refuse(Refusal.NotAnEgg, egg.Name);
            }

            if (egg.Destroyed)
            {
                return Verdict.Allow;
            }

            var reach = egg.Reach with { Reach = Reach, What = egg.Name };
            return ReachRules.InReach(reach);
        }

        /// Either side, once the message is taken: whether to break this
        /// game's copy and, on the host, pass it on. Only an egg that is
        /// here and not broken yet.
        public static bool Breaks(bool found, bool destroyed)
        {
            return found && !destroyed;
        }
    }
}
