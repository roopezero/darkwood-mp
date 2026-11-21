namespace DarkwoodMP.Sync.Players
{
    /// What our game puts in its PlayerState about our player's look:
    /// which sprites, the state bits, the lights and the health
    /// (PlayerCapture.Capture and LocalAway).
    public static class PlayerLookRules
    {
        /// The most sprites one snapshot holds: their count is one byte.
        public const int MaxSprites = 255;

        /// Seconds (unscaled) between two scans of the player's sprites.
        public const float RescanEvery = 1f;

        /// The player's own art: the torso (the root sprite, with the held
        /// weapon's frames), the legs and the drop shadow. The rest of the
        /// player object (the FOV and light rigs, the cursor, the prompts
        /// and bars) is drawn by the lighting or is this game's own UI.
        public static bool IsCharacterArt(string path) =>
            path == "."
            || path == "PlayerLegs"
            || path.StartsWith("PlayerLegs/")
            || path == "Shadow";

        /// The sprite list is scanned again when the player object is
        /// another one, or once RescanEvery has passed (unscaled).
        public static bool Rescans(bool sameRoot, Seconds now, Seconds next)
        {
            if (!sameRoot)
            {
                return true;
            }

            return now.Value >= next.Value;
        }

        /// When the next scan is due, after one at now.
        public static Seconds NextRescan(Seconds now) =>
            new Seconds(now.Value + RescanEvery);

        /// A sprite goes in the snapshot when it's there, active in the
        /// hierarchy, has a collection, and its renderer is there and
        /// enabled. The shell stops at MaxSprites.
        public static bool SendsSprite(in SpriteFacts sprite)
        {
            if (!sprite.Exists || !sprite.Active || !sprite.HasCollection)
            {
                return false;
            }

            return sprite.HasRenderer && sprite.RendererEnabled;
        }

        /// Alive is "alive and not dying".
        public static bool Alive(bool alive, bool dying) => alive && !dying;

        /// The item type of a torch or lamp in hand, or "" when the player
        /// has no item light, holds nothing, or the item has no base class.
        public static string HeldLight(in HeldFacts held)
        {
            if (!held.HasLightEmitter || !held.HeldValid)
            {
                return "";
            }

            if (!held.HasBaseClass || held.HeldType == null)
            {
                return "";
            }

            return held.HeldType;
        }

        /// Where the held light's flames are, from the player's centre in
        /// the root's rotation: only with a held light and a flames
        /// emitter, otherwise zero.
        public static Vec3 Flames(string held, bool hasEmitter, Vec3 offset)
        {
            if (held != "" && hasEmitter)
            {
                return offset;
            }

            return default;
        }

        /// The lantern's radius: the light dot's while it lights the
        /// player and is one of the controller's logic lights, else 0.
        public static float Lantern(in LanternFacts dot)
        {
            if (!dot.HasDot || !dot.LightsPlayer || !dot.HasController)
            {
                return 0f;
            }

            if (!dot.IsLogicLight)
            {
                return 0f;
            }

            return dot.Radius;
        }

        /// The beam's radius while the flashlight is on and has a light,
        /// else 0.
        public static float BeamRadius(in BeamFacts beam)
        {
            if (beam.On && beam.HasLight)
            {
                return beam.Radius;
            }

            return 0f;
        }

        /// The beam's grey (its colour's red) while the flashlight is on
        /// and has a light, else 0.
        public static float BeamGrey(in BeamFacts beam)
        {
            if (beam.On && beam.HasLight)
            {
                return beam.Grey;
            }

            return 0f;
        }

        /// Health as a percent of the most, rounded (halves to even) and
        /// held to 0 to 100; 0 when the most is 0 or less.
        public static byte HealthPercent(float health, float max)
        {
            if (!(max > 0f))
            {
                return 0;
            }

            int percent = GameMath.RoundToInt(health / max * 100f);
            return (byte)GameMath.Clamp(percent, 0, 100);
        }

        /// Our player is away: in a separate location or in a dream.
        public static bool Away(bool outside, bool dreaming) =>
            outside || dreaming;
    }
}
