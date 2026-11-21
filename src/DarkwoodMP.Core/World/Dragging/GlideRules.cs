using System;

namespace DarkwoodMP.World.Dragging
{
    /// How another game's move is shown here (the plugin's
    /// DragSync.ShowOthers): a slide is glided toward, and a rest is glided
    /// to for a moment, then put there and let go of.
    public static class GlideRules
    {
        /// Seconds after a rest was heard that it is glided to at most.
        public const float GlideFor = 0.6f;

        /// Further than this from a rest, it is put there at once.
        public const float GlideMax = 300f;

        /// Closer than this to a rest is there.
        public const float ThereWithin = 0.5f;

        /// Degrees: turned less than this from a rest is there.
        public const float ThereTurn = 1f;

        /// How fast a glide closes the gap, a second.
        public const float Rate = 15f;

        /// A move heard more than DragRules.StaleAfter ago is stale, on
        /// either side: it is treated as a rest, put there and let go of.
        /// A rest that isn't there yet is glided to only while it is near,
        /// fresh and heard under GlideFor ago; then it is put there, and let
        /// go of once it was there already, heard more than GlideFor ago,
        /// or stale. One put there at exactly GlideFor, or from further
        /// than GlideMax, is let go of on the next frame.
        public static GlideAction Step(in GlideFacts glide)
        {
            float ago = glide.Now.Value - glide.Heard.Value;

            // Stale implies past GlideFor, so in the last two tests the
            // stale terms change nothing; they copy the shell's as written.
            bool stale = ago > DragRules.StaleAfter;
            if (!glide.Last && !stale)
            {
                return GlideAction.Glide;
            }

            bool there = glide.Off < ThereWithin && glide.Turn < ThereTurn;
            if (!there && glide.Off < GlideMax && !stale && ago < GlideFor)
            {
                return GlideAction.Glide;
            }

            if (there || ago > GlideFor || stale)
            {
                return GlideAction.Release;
            }

            return GlideAction.Put;
        }

        /// How much of the gap a glide closes in a frame this long:
        /// 1 - e^(-Rate * frame), as Mathf.Exp.
        public static float Blend(Seconds frame)
        {
            return 1f - (float)Math.Exp(-Rate * frame.Value);
        }
    }
}
