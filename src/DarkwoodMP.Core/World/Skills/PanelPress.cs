namespace DarkwoodMP.World.Skills
{
    /// The mouse buttons pressed over the Skills panel and still held, read
    /// once a frame (SkillsWindow.MouseState). One instance for our game.
    public sealed class PanelPress
    {
        /// How many mouse buttons are tracked: left, right and middle.
        public const int Buttons = 3;

        readonly bool[] _pressed = new bool[Buttons];
        int _frame = -1;
        bool _over;

        /// Whether this frame still has to be read. A frame is read once.
        public bool Due(int frame)
        {
            return _frame != frame;
        }

        /// Takes in a frame's mouse: a button pressed this frame over the
        /// panel is marked; a button not held is cleared. A second read in
        /// the same frame changes nothing.
        public void Note(in MouseFacts mouse)
        {
            if (!Due(mouse.Frame))
            {
                return;
            }

            _frame = mouse.Frame;
            _over = mouse.Over;
            for (int b = 0; b < Buttons; b++)
            {
                int bit = 1 << b;
                if ((mouse.Down & bit) != 0 && _over)
                {
                    _pressed[b] = true;
                }
                else if ((mouse.Held & bit) == 0)
                {
                    _pressed[b] = false;
                }
            }
        }

        /// Whether the game's read of a mouse action is "not pressed": a
        /// button marked is held, or the read is of a press (down) over the
        /// panel. A press that began in the world goes on as it was.
        public bool Eats(bool down)
        {
            bool held = _pressed[0] || _pressed[1] || _pressed[2];
            return held || (down && _over);
        }
    }
}
