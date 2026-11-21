namespace DarkwoodMP.World.Dreaming
{
    /// What of the game's own may hold our player
    /// (DreamSync.InputsHeldByNothing).
    public readonly struct HoldFacts
    {
        /// Player.Instance is there.
        public bool HasPlayer { get; init; }

        /// Core.cantChangeForbidInputs.
        public bool Locked { get; init; }

        /// Core.loadingGame.
        public bool LoadingGame { get; init; }

        /// Controller.playingCutscene (false with no Controller).
        public bool Cutscene { get; init; }

        /// OutsideLocations.loading (false with no OutsideLocations).
        public bool OutsideLoading { get; init; }

        /// Dreams.switchingDream (false with no Dreams).
        public bool Switching { get; init; }

        /// Core.EnteringDream.
        public bool EnteringDream { get; init; }

        /// Player.inDialogue.
        public bool InDialogue { get; init; }
    }
}
