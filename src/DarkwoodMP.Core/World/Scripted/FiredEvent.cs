namespace DarkwoodMP.World.Scripted
{
    /// What an EventFired message carries (EventFiredMessage).
    public struct FiredEvent
    {
        public EventKind Kind;

        /// The EventTriggers (a world event: none, 0 and 0).
        public ObjName Trigger;

        /// The trigger's index in its EventTriggers (sent as one byte).
        public int Index;

        /// The world events' names, one per line, or "".
        public string Name;
    }
}
