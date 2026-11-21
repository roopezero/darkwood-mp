namespace DarkwoodMP.World.Story
{
    /// What happens to the epilogue's world event, the pan over the
    /// burning forest (EpilogueSync.WorldEventFired).
    public enum PanEventStep : byte
    {
        /// It runs: not the pan, not Playing, or no session.
        Runs = 0,

        /// It runs, the first since the pan was released: mark it run.
        RunsFirst,

        /// It ran already (ours or the game's own): not again.
        Held,

        /// The pan isn't released yet: it waits for the others.
        Waits,
    }
}
