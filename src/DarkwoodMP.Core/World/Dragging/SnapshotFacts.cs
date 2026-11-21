namespace DarkwoodMP.World.Dragging
{
    /// Host: one loose thing where the last snapshot for joiners had it,
    /// as a joiner comes into the world (DragSync.JoinerInWorld).
    /// Distances are 3D, from the snapshot's place.
    public readonly struct SnapshotFacts
    {
        /// Its body is gone.
        public bool Gone { get; init; }

        /// How far it moved since the snapshot.
        public float Moved { get; init; }

        /// Degrees it turned since (Quaternion.Angle).
        public float Turned { get; init; }

        /// It is loose (PushRules.IsLoose).
        public bool Loose { get; init; }

        /// Its body is kinematic.
        public bool Kinematic { get; init; }

        /// Our player drags it.
        public bool Dragged { get; init; }

        /// It is shown following another game's move.
        public bool Shown { get; init; }

        /// Its body is at rest (PushRules.AtRest).
        public bool AtRest { get; init; }

        /// It is watched for pushes (_loose).
        public bool Watched { get; init; }
    }
}
