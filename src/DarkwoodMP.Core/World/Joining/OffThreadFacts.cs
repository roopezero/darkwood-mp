namespace DarkwoodMP.World.Joining
{
    /// What decides where a snapshot's sav.dat is written.
    public readonly struct OffThreadFacts
    {
        /// savs.dat was copied from the host's own save.
        public bool HaveStatic { get; init; }

        /// OutsideLocations.addedNewLocation.
        public bool NewLocation { get; init; }

        /// The SnapshotOffThread setting.
        public bool Setting { get; init; }

        /// SnapshotSave couldn't read this game's world before.
        public bool Broken { get; init; }
    }
}
