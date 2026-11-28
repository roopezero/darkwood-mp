namespace DarkwoodMP.World.Dreaming
{
    /// What DreamSync does with a story dream every player dreams together.
    public enum StoryStep
    {
        /// Nothing: it isn't one, or it is shared already.
        None,
        /// Host: our player goes in; the dream is shared and sent to
        /// everyone.
        Share,
        /// Client: our player goes in at once; the host is asked to share
        /// it.
        Ask,
        /// Host: a joiner went in; it is shared, sent to everyone and our
        /// player follows.
        Follow,
        /// Host: a joiner went into the one shared already: told again, so
        /// its game knows it is shared.
        Confirm,
    }
}
