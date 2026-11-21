namespace DarkwoodMP.World.Fire
{
    /// What a flame touching a collider sets alight
    /// (FireRules.Flammable).
    public enum FlameTarget : byte
    {
        Nothing = 0,

        /// The destructible Item on the collider's transform.
        ColliderItem,

        /// The destructible Item on the flame's transform: the collider's
        /// transform has none.
        TransformItem,

        /// The door Door.getDoorScript finds from the collider.
        Door,

        /// The Window on the collider.
        Window,
    }
}
