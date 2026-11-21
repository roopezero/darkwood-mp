namespace DarkwoodMP.World.Doors
{
    /// A door as the host has it, sent in an ObjState so a client can put
    /// its copy back after a refused request (DoorObjState).
    public readonly struct HostDoor
    {
        /// What a rollback compares (DoorRules.RollBack).
        public DoorStatus Status { get; init; }

        /// Door.blocked: only the story sets or clears it.
        public bool Blocked { get; init; }

        /// Door.health.
        public int Health { get; init; }

        /// Door.barricadeHealth.
        public int BarricadeHealth { get; init; }

        /// The lock's key type, or "" for a door with no lock.
        public string KeyType { get; init; }
    }
}
