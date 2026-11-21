namespace DarkwoodMP.Sync.Players
{
    /// What our player holds, for the light their puppet shows in hand
    /// (PlayerCapture.Capture).
    public readonly struct HeldFacts
    {
        /// Player.itemLightEmitter isn't null.
        public bool HasLightEmitter { get; init; }

        /// The item in hand isn't empty (!InvItemClass.isNull(currentItem)).
        public bool HeldValid { get; init; }

        /// currentItem.baseClass isn't null.
        public bool HasBaseClass { get; init; }

        /// currentItem.type; null is sent as "".
        public string HeldType { get; init; }
    }
}
