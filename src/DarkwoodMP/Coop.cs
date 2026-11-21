namespace DarkwoodMP
{
    /// <summary>
    /// The running co-op session, or null when there's none. Harmony patches
    /// are static, so this is how they reach the session; it's the one
    /// static the subsystems may use. A patch that finds it null does
    /// nothing, so playing alone is unaffected.
    /// </summary>
    internal static class Coop
    {
        public static CoopSession Current { get; private set; }

        /// ModHost, when a session starts. Ends one still running, if any.
        internal static void Begin(CoopSession session)
        {
            End();
            Current = session;
        }

        /// ModHost, when the session ends or the game closes. Current is
        /// null before the session is disposed, so nothing its Dispose
        /// causes reaches it again.
        internal static void End()
        {
            CoopSession session = Current;
            if (session == null)
            {
                return;
            }

            Current = null;
            session.Dispose();
        }
    }
}
