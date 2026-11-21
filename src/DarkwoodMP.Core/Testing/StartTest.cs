namespace DarkwoodMP.Testing
{
    /// Which of the two window tests the arguments start, if either.
    public enum StartTest : byte
    {
        /// Neither.
        None,

        /// --dwmp-test-start-window: the Start Game window's test.
        StartWindow,

        /// --dwmp-screen WxH: the menus at that size.
        Screen,
    }
}
