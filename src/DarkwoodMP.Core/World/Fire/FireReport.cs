namespace DarkwoodMP.World.Fire
{
    /// What MsgType.Burning carries: the thing, what it is, its burn time
    /// and whether it is burning now.
    public readonly struct FireReport
    {
        /// The thing's ObjRef.
        public ObjName Thing { get; init; }

        /// What it is, as sent: any byte value.
        public FireKind Kind { get; init; }

        /// Its Burn's burnTime.
        public float Time { get; init; }

        /// It started burning (true) or went out (false).
        public bool On { get; init; }
    }
}
