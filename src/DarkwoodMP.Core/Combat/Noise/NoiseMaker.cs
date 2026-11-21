namespace DarkwoodMP.Combat.Noise
{
    /// The game's code that makes noise only for its own player, outside
    /// Player's code (PlayerNoiseSourcePatch's targets).
    public enum NoiseMaker : byte
    {
        /// Anything else: never counted.
        Other = 0,

        /// Door.open(Vector3, Transform, float) or Door.close(Transform).
        Door,

        /// MeleeSensor.OnTriggerEnter.
        Sensor,

        /// ItemSounds.alertCharactersInArea, which checks Player.Instance
        /// itself.
        Furniture,
    }
}
