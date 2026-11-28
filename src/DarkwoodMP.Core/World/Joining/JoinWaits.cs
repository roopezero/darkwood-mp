namespace DarkwoodMP.World.Joining
{
    /// The words of each JoinWait, as WorldShare sends them. The map test
    /// looks for "Waiting to join: The host is watching the prologue".
    public static class JoinWaits
    {
        public static string Text(JoinWait why)
        {
            switch (why)
            {
                case JoinWait.Intro:
                    return "The host is watching the prologue's intro."
                        + " You'll join when it's over.";
                case JoinWait.BetweenPrologueParts:
                    return "The host is between parts of the prologue."
                        + " You'll join in a moment.";
                case JoinWait.Dreaming:
                    return "The host is in a dream. You'll join when they"
                        + " wake up.";
                case JoinWait.Away:
                    return "The host is away in another place. You'll join"
                        + " when they're back.";
                case JoinWait.BetweenEpilogueParts:
                    return "The host is between parts of the epilogue."
                        + " You'll join them in a moment.";
                case JoinWait.Loading:
                    return "The host's world is loading.";
                case JoinWait.Preparing:
                    return "The host is saving its world for you...";
                case JoinWait.WorldChanged:
                    return "The host's world changed while you were getting"
                        + " it: starting again...";
                default:
                    return null;
            }
        }
    }
}
