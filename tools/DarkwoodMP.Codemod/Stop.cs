namespace DarkwoodMP.Codemod;

/// Stops the current command with a message for the person running it.
/// Program.Main prints the message and exits with code 2. Nothing has been
/// written by then, unless the message says so and how to undo it (see
/// Stages/OutputFiles.cs).
sealed class Stop : Exception
{
    public Stop(string message) : base(message)
    {
    }
}
