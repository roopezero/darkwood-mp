namespace DarkwoodMP.World.Story
{
    /// What became of the Talking Tree, in a game started at the epilogue.
    public enum TreeFate : byte
    {
        /// It stands: no flag is set.
        Standing = 0,

        /// It was burned: the story flag "talkingTree_burnt" is set.
        Burned = 1,
    }
}
