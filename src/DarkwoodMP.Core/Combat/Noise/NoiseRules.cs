namespace DarkwoodMP.Combat.Noise
{
    /// Players' noise for the host's enemies (PlayerNoisePatch and
    /// PlayerNoiseSourcePatch). These run with no session too.
    public static class NoiseRules
    {
        /// The most range a client's noise may have.
        public const float MostRange = 2000f;

        /// The most volume a client's noise may have.
        public const float MostVolume = 5f;

        /// How far from its stand-in a client's noise may be (it's meant
        /// to be where they are). Compared squared, in 3D.
        public const float MostDrift = 500f;

        /// Whose noise this is, in this order: a source set already wins;
        /// then ours when inside an own-noise method or called from
        /// Player's code; a client also sends it to the host.
        public static NoiseOwner Owner(in NoiseFacts noise)
        {
            if (noise.Set)
            {
                return NoiseOwner.Set;
            }

            if (noise.OwnNoise <= 0 && !noise.FromPlayer)
            {
                return NoiseOwner.NotOurs;
            }

            if (noise.Client)
            {
                return NoiseOwner.OursToHost;
            }

            return NoiseOwner.Ours;
        }

        /// Whether a noise maker counts as our player's (OwnNoise goes up
        /// for it): a door our player opens or closes, our player's melee
        /// sensor, or furniture; never with no Player.Instance.
        public static bool CountsAsOurs(in OwnNoiseFacts maker)
        {
            if (!maker.Player)
            {
                return false;
            }

            switch (maker.Maker)
            {
                case NoiseMaker.Door:
                    return maker.PlayerOpener;
                case NoiseMaker.Sensor:
                    return maker.PlayerSensor;
                case NoiseMaker.Furniture:
                    return true;
                default:
                    return false;
            }
        }

        /// Which of a door method's arguments is the opener: the second
        /// when it has more than one (open), else the first (close); -1
        /// when it has none.
        public static int OpenerAt(int count)
        {
            if (count <= 0)
            {
                return -1;
            }

            if (count > 1)
            {
                return 1;
            }

            return 0;
        }

        /// A client's noise range, between 0 and MostRange.
        public static float Range(float range) =>
            Floats.Clamp(range, 0f, MostRange);

        /// A client's noise volume, between 0 and MostVolume.
        public static float Volume(float volume) =>
            Floats.Clamp(volume, 0f, MostVolume);

        /// Host: whether a client's noise is raised for its stand-in: it
        /// has one, and the noise is no more than MostDrift from it. Out
        /// of reach it's dropped, never refused (it isn't a request).
        public static bool Raises(in RemoteNoiseFacts noise)
        {
            if (!noise.StandIn)
            {
                return false;
            }

            float x = noise.At.X - noise.Noise.X;
            float y = noise.At.Y - noise.Noise.Y;
            float z = noise.At.Z - noise.Noise.Z;
            return !(x * x + y * y + z * z > MostDrift * MostDrift);
        }
    }
}
