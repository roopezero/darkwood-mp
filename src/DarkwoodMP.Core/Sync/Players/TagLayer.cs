namespace DarkwoodMP.Sync.Players
{
    /// Where the other players' names are drawn.
    public enum TagLayer : byte
    {
        /// Nowhere.
        None = 0,

        /// Beside their markers on the map (MapMarkers.DrawNames).
        Map,

        /// Above their puppets.
        World,
    }
}
