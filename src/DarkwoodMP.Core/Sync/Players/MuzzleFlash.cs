namespace DarkwoodMP.Sync.Players
{
    /// What a PlayerShot shows at the gun's muzzle.
    public readonly struct MuzzleFlash
    {
        /// The muzzle prefab in the FX pool; "" for none.
        public string Muzzle { get; init; }

        /// The muzzle particles in the FX pool; "" for none.
        public string Particles { get; init; }

        /// Not 0: FX/Muzzle/PistolFlash too. Relayed as it came.
        public byte Flash { get; init; }

        /// Where the muzzle is from the player (x right, y up).
        public Vec3 Offset { get; init; }
    }
}
