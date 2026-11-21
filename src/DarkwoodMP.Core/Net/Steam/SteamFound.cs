namespace DarkwoodMP.Net.Steam
{
    /// What one look for the game's SteamManager found.
    public enum SteamFound : byte
    {
        /// Not there yet, or reading it threw: look again in a second.
        NotYet,

        /// Steam is up: make the backend.
        Ready,

        /// This build has no SteamManager: Direct only, for good. Log "No
        /// SteamManager in this build: direct IP only".
        Missing,
    }
}
