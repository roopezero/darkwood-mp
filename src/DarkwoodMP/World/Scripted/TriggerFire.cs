namespace DarkwoodMP.World.Scripted
{
    /// <summary>
    /// An EventTrigger.fire call's arguments: the trigger, the
    /// EventTriggers it is in, the type it was set off as and its value.
    /// </summary>
    internal struct TriggerFire
    {
        public global::EventTrigger Trigger;

        public EventTriggers Triggers;

        public global::EventTrigger.Type Type;

        public string Value;
    }
}
