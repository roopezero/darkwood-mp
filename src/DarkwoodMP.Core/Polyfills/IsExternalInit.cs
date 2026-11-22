namespace System.Runtime.CompilerServices
{
    /// The compiler marks init setters with this type, which netstandard2.0
    /// lacks. Internal, so it can't clash with the real one in net8.0.
    internal static class IsExternalInit
    {
    }
}
