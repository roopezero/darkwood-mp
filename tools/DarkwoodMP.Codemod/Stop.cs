namespace DarkwoodMP.Codemod;

/// Stops the current command with a message for the person running it.
/// Program.Main prints the message and exits with code 2; nothing has been
/// written by then.
sealed class Stop : Exception
{
    public Stop(string message) : base(message)
    {
    }
}
