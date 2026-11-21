namespace DarkwoodMP.Combat.Enemies
{
    /// Whether the client's own copy from the save shows the host's enemy
    /// (CopyRules.FromSave). Anything but Use goes on to HostileNpcs'
    /// OurCopy, then the prefab.
    public enum SavedCopy
    {
        /// No character of ours under that save id.
        None = 0,

        /// Ours, but it already shows another of the host's enemies.
        Driven,

        /// Ours, but another kind (logged).
        OtherKind,

        /// Ours shows it.
        Use,
    }
}
