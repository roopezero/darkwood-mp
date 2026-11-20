namespace DarkwoodMP.World.Switches
{
    /// What the host knows about something a client switched: an Item
    /// turned on or off (a lamp, a generator, a radio), or an
    /// InteractiveItem switched in its window.
    public readonly struct SwitchFacts
    {
        /// It is in the host's world (ObjRef.Resolve<Item> for ItemOn and
        /// ItemOff, ObjRef.Resolve<Transform> for InteractiveSwitch).
        public bool Exists { get; init; }

        /// It has an InteractiveItem.
        public bool IsSwitch { get; init; }

        /// InteractiveItem.isOn, in the host's world.
        public bool IsOn { get; init; }

        /// The action's flag 1: the client's switch is on now.
        public bool On { get; init; }

        /// The requester and the thing. The rule sets the reach.
        public ReachFacts Requester { get; init; }
    }
}
