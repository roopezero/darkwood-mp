using System;

namespace DarkwoodMP.World
{
    /// What an object is named by in every game: a kind and an id (the
    /// plugin's ObjRef value). The zero value names nothing.
    public readonly struct ObjName : IEquatable<ObjName>
    {
        public ObjKind Kind { get; }

        public long Id { get; }

        public ObjName(ObjKind kind, long id)
        {
            Kind = kind;
            Id = id;
        }

        public bool Equals(ObjName other) =>
            Kind == other.Kind && Id == other.Id;

        public override bool Equals(object obj) =>
            obj is ObjName other && Equals(other);

        public override int GetHashCode() => Id.GetHashCode() ^ (int)Kind;

        public override string ToString() => Kind + ":" + Id;
    }
}
