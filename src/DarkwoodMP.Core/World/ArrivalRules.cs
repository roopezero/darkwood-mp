namespace DarkwoodMP.World
{
    /// Whether a game takes one of the world's messages at all, before it
    /// reads what the message names. A host's handler that doesn't take a
    /// request accepts it with nothing done: it never refuses.
    public static class ArrivalRules
    {
        /// Taken from anyone on the host, and only from the host on a
        /// client, while in a world (Eggs.OnDestroyed,
        /// Infections.OnCleared, Fuel.OnFuel, Fire.OnBurning's host side,
        /// Construction.OnConstruct).
        public static bool Takes(in ArrivalFacts message)
        {
            if (!message.Session || !message.InWorld)
            {
                return false;
            }

            return !message.Client || message.FromHost;
        }

        /// Taken only by a client, from the host, while in a world
        /// (Infections.OnSpawned, Fire.OnBurning's client side).
        public static bool TakesFromHost(in ArrivalFacts message)
        {
            if (!message.Session || !message.InWorld)
            {
                return false;
            }

            return message.Client && message.FromHost;
        }
    }
}
