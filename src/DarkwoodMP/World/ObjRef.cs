using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Names a world object the same way in every player's game, so a change to
    /// it can be sent.
    ///
    ///   Spawned  made while playing together (a dropped item...): a random id
    ///            its maker gave it, sent with it. Also what the world made by
    ///            itself after a player joined (a location's characters, a
    ///            delayed event's spawn): the host gives it an id and sends it
    ///            with its name and position (HostIds), and each client ties
    ///            its own copy to that id. A client that hasn't tied one finds
    ///            it by that name and position, as Placed.
    ///   Enemy    one of the host's enemies (its corpse's loot): the host's net
    ///            id for it
    ///   Saved    anything from the shared save: its save id
    ///            (SaveableObject.uniqueId), the same for everyone, since
    ///            everyone loaded the host's save. Only ids below
    ///            SharedIds.SendLimit: a game gives later ids out itself, to
    ///            whatever it makes while it's played, so those differ between
    ///            games and the object is named as Placed instead. A Saved ref
    ///            above SharedIds.ResolveLimit names nothing.
    ///   Placed   anything else that stands where the level put it: its name
    ///            and position. The id holds a 24-bit hash of the name and the
    ///            position (x and z, rounded, 20 bits each), and the object is
    ///            found as the one of that name standing nearest to it, within
    ///            PlacedSlack. So it's still found when it lies a little
    ///            differently in another game (physics settling after loading,
    ///            a nudge).
    ///
    /// Written as u8 kind, i64 id.
    /// </summary>
    public struct ObjRef
    {
        public enum Kind : byte
        {
            None = 0,
            Saved = 1,
            Spawned = 2,
            Enemy = 3,
            Placed = 4
        }

        public Kind K;
        public long Id;

        public bool Valid => K != Kind.None;
        public override string ToString() => $"{K}:{Id}";

        public void Write(PacketWriter w)
        {
            NameOf(this).Write(w);
        }

        public static ObjRef Read(PacketReader r)
        {
            return RefOf(ObjName.Read(r));
        }

        /// The core's name for a ref: the same kind values and id.
        public static ObjName NameOf(ObjRef r)
        {
            return new ObjName((ObjKind)r.K, r.Id);
        }

        public static ObjRef RefOf(ObjName name)
        {
            return new ObjRef { K = (Kind)name.Kind, Id = name.Id };
        }

        /// <summary>
        /// How far from its named spot a Placed object is still found.
        /// </summary>
        public const float PlacedSlack = ObjRefRules.PlacedSlack;

        // The session's tables, or the empty ones used with no session.
        private static ObjNames Names
        {
            get
            {
                CoopSession coop = Coop.Current;
                if (coop != null)
                {
                    return coop.Names;
                }

                return Plugin.Instance.Host.AloneNames;
            }
        }

        /// <summary>
        /// Objects made while playing together, by id, in every game. Empty
        /// with no session.
        /// </summary>
        public static Dictionary<long, GameObject> Spawned => Names.Spawned;

        /// <summary>
        /// Name go by id in every game. Nothing is kept with no session.
        /// </summary>
        public static void RegisterSpawned(long id, GameObject go)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Names.RegisterSpawned(id, go);
            }
        }

        /// <summary>
        /// Keep this object's current name for it, even after it moves. Call
        /// before moving it. Nothing is kept with no session.
        /// </summary>
        public static ObjRef Pin(GameObject go)
        {
            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                return coop.Names.Pin(go);
            }

            return Of(go);
        }

        /// <summary>
        /// go is tied to an id the host gave out (HostIds).
        /// </summary>
        public static bool IsShared(GameObject go)
        {
            return Names.IsShared(go);
        }

        public static ObjRef Of(GameObject go)
        {
            return Names.Of(go);
        }

        /// <summary>
        /// Find the object in this game; T is the component it's expected to
        /// have.
        /// </summary>
        public static T Resolve<T>(ObjRef r) where T : Component
        {
            return Names.Resolve<T>(r);
        }
    }
}
