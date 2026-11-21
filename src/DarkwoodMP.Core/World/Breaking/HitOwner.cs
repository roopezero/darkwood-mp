namespace DarkwoodMP.World.Breaking
{
    /// Whose change to the world this is: ours to send, the others', or
    /// for the stack to say (WorldActions.ByLocalPlayer, which only the
    /// shell can call, at the same place as today).
    public enum HitOwner : byte
    {
        Ours = 0,

        Others,

        AskStack,
    }
}
