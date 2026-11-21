namespace DarkwoodMP.World.Scripted
{
    /// What to do as a trigger's events start running.
    public enum FiringShare : byte
    {
        /// Nothing to share: the game runs them, and the trigger stays the
        /// one firing.
        Pass = 0,

        /// The trigger is no longer the one firing, and the game runs them
        /// unshared.
        Game,

        /// Host: a world trigger a client caused. Send it as theirs
        /// (ScriptedEvents.SendCaused) and run the host's and the shared
        /// part here; if it couldn't be sent, Send.
        Caused,

        /// Send the trigger (ScriptedEvents.SendTrigger).
        Send,
    }
}
