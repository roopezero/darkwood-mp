using System;
using DarkwoodMP.Net;

namespace DarkwoodMP.World
{
    /// What an object is named by in every game: a kind and an id (the
    /// plugin's ObjRef value). The zero value names nothing. On the wire:
    /// u8 kind, i64 id.
    public readonly struct ObjName : IEquatable<ObjName>
    {
        public ObjKind Kind { get; }

        public long Id { get; }

        public ObjName(ObjKind kind, long id)
        {
            Kind = kind;
            Id = id;
        }

        /// Read a name as it came: an unknown kind byte is kept.
        public static ObjName Read(PacketReader r)
        {
            var kind = (ObjKind)r.R.ReadByte();
            return new ObjName(kind, r.R.ReadInt64());
        }

        public void Write(PacketWriter w)
        {
            w.W.Write((byte)Kind);
            w.W.Write(Id);
        }

        public bool Equals(ObjName other) =>
            Kind == other.Kind && Id == other.Id;

        public override bool Equals(object obj) =>
            obj is ObjName other && Equals(other);

        public override int GetHashCode() => Id.GetHashCode() ^ (int)Kind;

        public override string ToString() => Kind + ":" + Id;
    }
}
