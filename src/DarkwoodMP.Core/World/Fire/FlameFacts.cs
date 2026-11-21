namespace DarkwoodMP.World.Fire
{
    /// What a client's flame touched (ThrowSync.FlameTouchPatch): a
    /// collider, and the transform the touch came through.
    public readonly struct FlameFacts
    {
        /// There is a collider.
        public bool Collider { get; init; }

        /// A CharBase is on the collider's transform.
        public bool CharOnCollider { get; init; }

        /// A CharBase is on the transform (false when there is none).
        public bool CharOnTransform { get; init; }

        /// An Item is on the collider's transform.
        public bool ColliderItem { get; init; }

        /// That item is destructible.
        public bool ColliderItemDestructible { get; init; }

        /// An Item is on the transform (false when there is none).
        public bool TransformItem { get; init; }

        /// That item is destructible.
        public bool TransformItemDestructible { get; init; }

        /// Door.getDoorScript finds a door from the collider.
        public bool Door { get; init; }

        /// A Window is on the collider.
        public bool Window { get; init; }
    }
}
