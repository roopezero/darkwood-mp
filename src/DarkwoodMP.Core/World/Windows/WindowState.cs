using System;

namespace DarkwoodMP.World.Windows
{
    /// A window before or after one of its game calls (the plugin's
    /// WorldActions.WindowState): if any of it differs, the call changed
    /// the window and is sent.
    public readonly struct WindowState : IEquatable<WindowState>
    {
        /// Window.barricaded.
        public bool Barricaded { get; init; }

        /// Window.barricadeHealth.
        public int BarricadeHealth { get; init; }

        public bool Equals(WindowState other) =>
            Barricaded == other.Barricaded
            && BarricadeHealth == other.BarricadeHealth;

        public override bool Equals(object obj) =>
            obj is WindowState other && Equals(other);

        public override int GetHashCode() => BarricadeHealth;
    }
}
