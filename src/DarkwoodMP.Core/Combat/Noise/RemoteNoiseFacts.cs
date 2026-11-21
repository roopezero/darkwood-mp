namespace DarkwoodMP.Combat.Noise
{
    /// A client's PlayerNoise on the host (CharacterAlertInArea.OnRemoteNoise).
    public readonly struct RemoteNoiseFacts
    {
        /// NetPlayerProxy.All has a live stand-in for the sender.
        public bool StandIn { get; init; }

        /// Where the stand-in is (transform.position).
        public Vec3 At { get; init; }

        /// Where the noise is, as sent.
        public Vec3 Noise { get; init; }
    }
}
