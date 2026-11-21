namespace DarkwoodMP.World.Windows
{
    /// A window as the host has it, sent in an ObjState so a client can
    /// put its copy back after a refused request (WindowObjState).
    public readonly struct HostWindow
    {
        /// Window.barricaded.
        public bool Barricaded { get; init; }

        /// Window.barricadeHealth.
        public int BarricadeHealth { get; init; }
    }
}
