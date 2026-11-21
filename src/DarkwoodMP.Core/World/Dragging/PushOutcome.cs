namespace DarkwoodMP.World.Dragging
{
    /// What a push check does with one watched loose thing.
    public enum PushOutcome : byte
    {
        /// Not away from where it is known to be: nothing.
        Still = 0,

        /// Its body or Item is gone: no longer watched, and the step's
        /// state is empty.
        Drop,

        /// Held by a drag, a settle, another game's move or kinematic:
        /// known to be where it is now (Rebase), which is sent or heard.
        Rebase,

        /// Away and still moving: where it is goes out (not last).
        Slide,

        /// Away and still moving, but not this game's word: wait.
        Wait,

        /// Client, not our player's push: put back where it is known to
        /// be, with no velocity and asleep.
        PutBack,

        /// Come to rest away from there (or moving for too long): sent as
        /// it is, last, which notes it there; a push counted.
        Settle,
    }
}
