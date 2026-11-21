namespace DarkwoodMP.World.Objects
{
    /// What a game knows when spilt gasoline catches fire
    /// (Liquid.startBurning), to say whether it tells the others.
    public readonly struct LitFacts
    {
        /// The liquid wasn't burning before the call.
        public bool WasOut { get; init; }

        /// A burning liquid is lighting its neighbours
        /// (ObjectActions.Spreading): a spread.
        public bool Spreading { get; init; }

        /// Liquid.burning, after the call.
        public bool Burning { get; init; }

        /// Coop.Guards.Sharing.
        public bool Sharing { get; init; }

        /// This game is a client (Session.IsClient).
        public bool Client { get; init; }

        /// Our player's melee sensor is touching something
        /// (ObjectActions.OwnSwing).
        public bool OwnSwing { get; init; }
    }
}
