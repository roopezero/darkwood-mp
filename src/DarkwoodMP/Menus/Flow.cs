using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkwoodMP.Menus
{
    /// <summary>
    /// A top-to-bottom layout for a window's body: each call takes the next
    /// strip of it.
    /// </summary>
    public sealed class Flow
    {
        public readonly Rect Area;
        public float Y;
        public float Gap => 4 * UiWindow.S;

        public Flow(Rect area)
        {
            Area = area;
            Y = area.y;
        }

        public float Width => Area.width;

        /// <summary>The next strip, h pixels (already scaled) high.</summary>
        public Rect Next(float h)
        {
            var r = new Rect(Area.x, Y, Area.width, h);
            Y += h + Gap;
            return r;
        }

        public void Space(float unscaled) => Y += unscaled * UiWindow.S;

        /// <summary>
        /// The next strip cut into columns: a width above 0 is fixed (pixels at
        /// scale 1), 0 or below shares what's left.
        /// </summary>
        public Rect[] Row(float h, params float[] widths) =>
            Ui.Split(Next(h), widths);
    }
}
