namespace DarkwoodMP.Sync.Players
{
    /// When the other players' names are drawn, and what they say
    /// (PlayerSync.DrawNameTags, FlashDummy).
    public static class NameTagRules
    {
        /// Seconds (scaled) the dummy shows the damage it took.
        public const float FlashTime = 1.5f;

        /// Not over a menu; on the map beside the markers; not when the
        /// world's overlays are hidden; otherwise above the puppets.
        public static TagLayer Where(in TagFacts screen)
        {
            if (screen.MenuOver)
            {
                return TagLayer.None;
            }

            if (screen.MapOpen)
            {
                return TagLayer.Map;
            }

            if (screen.OverlaysHidden)
            {
                return TagLayer.None;
            }

            return TagLayer.World;
        }

        /// A puppet's tag is drawn while its root is there and active, and
        /// not behind the camera (the screen point's z below 0).
        public static bool Draws(bool shown, float depth)
        {
            if (!shown)
            {
                return false;
            }

            return !(depth < 0f);
        }

        /// The name is read again at each draw, since names can arrive
        /// after the first snapshot; the dummy keeps its own.
        public static bool RefreshesName(ulong owner) =>
            owner != PuppetRules.DummyId;

        /// When the dummy's damage stops showing, after a hit at now.
        public static Seconds FlashUntil(Seconds now) =>
            new Seconds(now.Value + FlashTime);

        /// The name, then "  {health}%" when there is a snapshot and the
        /// puppet is alive; for the dummy, "  -{damage:0}" while its flash
        /// lasts, and once it's over its total goes back to 0.
        public static NameTag Label(in LabelFacts tag)
        {
            string text = tag.Name;
            if (tag.HasLast && tag.Alive)
            {
                text += "  " + tag.Health + "%";
            }

            if (!tag.Dummy)
            {
                return new NameTag { Text = text };
            }

            if (tag.Now.Value < tag.FlashUntil.Value)
            {
                text += "  -" + tag.Damage.ToString("0");
                return new NameTag { Text = text };
            }

            return new NameTag { Text = text, ResetsDamage = true };
        }
    }
}
