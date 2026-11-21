namespace DarkwoodMP.World.Inventories
{
    /// What a client does with the host's answer to its ask for a
    /// container's contents (SlotShare.OnContents).
    public enum HostContents : byte
    {
        /// The container isn't here: logged.
        NotFound = 0,

        /// The host hasn't got it (its answer is ""): logged.
        HostHasNone,

        /// Our changes to it went already, against what we saw: the answer
        /// is older than them, so it's not used.
        NotUsed,

        /// Closed and not watched: loaded whole.
        LoadWhole,

        /// Open or watched: taken slot by slot (SlotShare.Merge).
        Merge,
    }
}
