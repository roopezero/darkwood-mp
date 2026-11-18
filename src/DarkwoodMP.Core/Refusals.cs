namespace DarkwoodMP
{
    /// The sentence for each refusal, in one table. The host logs it and
    /// sends it to the client, and tools/tests/authtest.py prints it, so a
    /// text that moves here keeps its exact words.
    public static class Refusals
    {
        /// What to say about a verdict, with its detail in the sentence.
        /// Empty when nothing was refused.
        public static string Text(Verdict verdict)
        {
            switch (verdict.Reason)
            {
                case Refusal.None:
                    return "";
                case Refusal.NotHere:
                    return "not here";
                case Refusal.NoKey:
                    return "nobody has the key (" + verdict.Detail + ")";
                case Refusal.HitTooLarge:
                    return "a hit of " + verdict.Detail
                        + " is more than any weapon does";
                default:
                    return verdict.Reason.ToString();
            }
        }
    }
}
