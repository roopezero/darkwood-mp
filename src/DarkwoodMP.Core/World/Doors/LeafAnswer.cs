namespace DarkwoodMP.World.Doors
{
    /// What this game does with another game's leaf angle.
    public enum LeafAnswer
    {
        /// Ignored: no world, a client's message not from the host, or an
        /// angle that isn't a number.
        Dropped,

        /// The door or its leaf isn't here (logged with --dwmp-debug-world).
        NotFound,

        /// The door is shut, broken or barricaded here: the angle is late
        /// or wrong, and the leaf is left as it is.
        NotOpen,

        /// Host: the pusher is too far from the leaf, or the angle past
        /// MaxTurn + PushSlack. The host's own angle goes back to the
        /// pusher; nothing else changes.
        PutBack,

        /// Host: send the angle to everyone (the pusher too: the host's
        /// last word), then glide our leaf there.
        Relay,

        /// Client: glide our leaf there, unless it's the echo of an angle
        /// we sent (LeafEchoes).
        Take,
    }
}
