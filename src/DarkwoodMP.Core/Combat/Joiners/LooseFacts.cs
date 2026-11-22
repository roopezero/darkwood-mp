namespace DarkwoodMP.Combat.Joiners
{
    /// A collider found near a stand-in (NetPlayerProxy.IgnoreLooseNear).
    public readonly struct LooseFacts
    {
        /// The collider isn't null.
        public bool Exists { get; init; }

        /// The stand-in already ignores it (by collider instance id).
        public bool Ignored { get; init; }

        /// Collider.attachedRigidbody isn't null.
        public bool HasBody { get; init; }

        /// That rigidbody is on the stand-in's own object.
        public bool OwnBody { get; init; }

        /// The rigidbody has an Item.
        public bool Item { get; init; }

        /// The rigidbody has a CharBase.
        public bool Character { get; init; }
    }
}
