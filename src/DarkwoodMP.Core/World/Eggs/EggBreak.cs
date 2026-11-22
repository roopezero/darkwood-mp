namespace DarkwoodMP.World.Eggs
{
    /// What MsgType.EggDestroyed carries: the egg, where the sender saw it
    /// and its name, to find it by when its name isn't known here.
    public readonly struct EggBreak
    {
        /// The egg's ObjRef.
        public ObjName Egg { get; init; }

        /// Where the sender saw it, on the ground plane.
        public Vec2 At { get; init; }

        /// Its GameObject's name.
        public string Name { get; init; }
    }
}
