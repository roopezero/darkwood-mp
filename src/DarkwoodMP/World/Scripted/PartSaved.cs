namespace DarkwoodMP.World.Scripted
{
    /// <summary>
    /// EventParts.Current as it was before a patch set it (Set), to put back
    /// after the game's call (EventParts.PutBack).
    /// </summary>
    internal struct PartSaved
    {
        public bool Set;

        public EventPart Was;
    }
}
