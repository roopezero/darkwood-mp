using System.Collections.Generic;

namespace DarkwoodMP.World.Rolls
{
    /// A MsgType.RandomObjects as read (RandomObjectsMessage.Read). Only
    /// the fields of its Kind are set.
    public readonly struct RandomObjectsBody
    {
        /// RandomObjectsMessage.Dawn or RandomObjectsMessage.Night; any
        /// other byte is kept, with nothing after it read.
        public byte Kind { get; init; }

        /// Dawn: every one of the dawn's random objects.
        public List<DawnObject> Dawn { get; init; }

        /// Night: the night mushroom.
        public NightMushroom Night { get; init; }
    }
}
