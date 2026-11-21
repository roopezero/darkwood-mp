namespace DarkwoodMP.World.Talk
{
    /// Host: what it looks at before its own player's dialogue opens
    /// (NPC.talkTo).
    public readonly struct HostTalkFacts
    {
        /// NPC.characterDialogue exists.
        public bool HasDialogue { get; init; }

        /// NPC.wantsToTalk.
        public bool WantsToTalk { get; init; }

        /// WorldActions.Sharing.
        public bool Sharing { get; init; }

        /// Session.IsHost.
        public bool IsHost { get; init; }

        /// The NPC's ObjRef is valid.
        public bool RefValid { get; init; }

        /// Someone holds the NPC's lock key, and who (a peer id).
        public bool Held { get; init; }

        public ulong Holder { get; init; }

        /// Our own peer id (Session.LocalId).
        public ulong Local { get; init; }

        /// The holder's name (Session.NameOf), for the refusal's words.
        public string HolderName { get; init; }
    }
}
