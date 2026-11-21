namespace DarkwoodMP.Sync.Players
{
    /// What a PlayerHurt shows on the player's puppet.
    public readonly struct HurtLook
    {
        /// The HurtKind byte as it came; an unknown one has nothing after
        /// it.
        public byte Kind { get; init; }

        /// Blood: the splat's prefab path. Burning: the flames prefab.
        public string Prefab { get; init; }

        /// Burning: the fire sound.
        public string Sound { get; init; }

        /// Blood: the splat's height above their feet.
        public float Height { get; init; }

        /// Blood: the splat's yaw.
        public float Yaw { get; init; }
    }
}
