using System.Collections.Generic;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.Sync.Players
{
    /// <summary>
    /// What a player looks like right now: root transform plus every visible
    /// tk2d sprite in the player's hierarchy, expressed relative to the root.
    /// Mirroring sprite ids directly means weapons, clothing and every
    /// animation come across without syncing the animation state machine.
    /// </summary>
    public sealed class PlayerSnapshot
    {
        public struct SpriteState
        {
            public int PathHash;
            public int CollectionHash;
            public int SpriteId;
            public int SortingOrder;
            public int Layer;
            public Vector3 Pos;     // relative to player root
            public Quaternion Rot;  // relative to player root
            public Vector3 Scale;
            // tk2d mesh scale, separate from the transform
            public Vector3 SpriteScale;
            public Color Color;
        }

        public ulong Owner;
        public float Time;
        public Vector3 Position;
        public Quaternion Rotation;
        /// <summary>
        /// Alive and not dying: enemies leave a dead or dying player be.
        /// </summary>
        public bool Alive = true;
        /// <summary>
        /// Dead for good and watching the others (World.Spectator): not shown.
        /// </summary>
        public bool Spectating;
        /// <summary>
        /// The body's animation library (a weapon's, a held torch's...). Its
        /// sprites come from collections a game loads only when its own player
        /// uses that item, so the others load it by this name to show them.
        /// </summary>
        public string Library = "";
        /// <summary>
        /// The item type of the lit thing in hand (a torch, a lamp), whose
        /// light and flames the others show.
        /// </summary>
        public string HeldLight = "";
        /// <summary>
        /// Where the held light's flames are, relative to the root (as the
        /// sprites' Pos). The game moves them to the item's emitter position
        /// for each animation clip and frame (Player.ProcessAnims,
        /// InvItem.emitterPositions), so a torch's flame sits on the torch in
        /// every facing; zero when nothing lit is in hand.
        /// </summary>
        public Vector3 HeldFlames;
        /// <summary>
        /// The radius of the light around the player from a lantern worn on the
        /// hotbar (InvItemClass.checkForActiveSwitches puts Player.lightDot in
        /// Controller.logicLights at the lantern's lightRadius); 0 when there's
        /// none. The others light the area around the puppet with it, so the
        /// host's proxy counts as lit (Core.isInLight).
        /// </summary>
        public float Lantern;
        /// <summary>The flashlight's beam is on.</summary>
        public bool Flashlight;
        /// <summary>
        /// The beam's radius and brightness (the grey of its colour), which
        /// Player.processFlashlight works out each frame from how far the mouse
        /// is; the others draw their copy of the beam with them. Zero when it's
        /// off.
        /// </summary>
        public float BeamRadius, BeamBright;
        /// <summary>
        /// What the host's enemies check about the player they look at, which
        /// only the player's own game knows: invisible (the Chameleon skill,
        /// Player.setInvisible), the shadow ward and forest spirit ward
        /// effects, and the Friend / Enemy of the Forest skills. The host puts
        /// them on the player's proxy.
        /// </summary>
        public bool Invisible,
            ShadowWard,
            ForestSpiritWard,
            FriendOfTheForest,
            EnemyOfTheForest;
        /// <summary>
        /// In a separate location or a dream
        /// (OutsideLocations.playerInOutsideLocation, Dreams.dreaming): an area
        /// loaded only in their own game, so the others don't show them where
        /// their coordinates are.
        /// </summary>
        public bool Away;
        /// <summary>
        /// In an after-night: in their hideout after dawn, with their game's
        /// clock stopped (Controller.isAfterNight).
        /// </summary>
        public bool AfterNight;
        /// <summary>
        /// Where, when Away: the location's name, or "dream:" and the dream's
        /// (World.Places.Whereabouts.Current).
        /// </summary>
        public string Place = "";
        /// <summary>
        /// Health as a percentage of the maximum (0 to 100), for the name tag.
        /// </summary>
        public byte Health = 100;
        public readonly List<SpriteState> Sprites = new List<SpriteState>(32);

        public void Write(PacketWriter pw)
        {
            var w = pw.W;
            w.Write(Owner);
            pw.Write(Position);
            WriteRot(w, Rotation);
            int flags = 0;
            if (Alive)
            {
                flags |= 1;
            }

            if (Spectating)
            {
                flags |= 2;
            }

            if (Flashlight)
            {
                flags |= 4;
            }

            if (Invisible)
            {
                flags |= 8;
            }

            if (ShadowWard)
            {
                flags |= 16;
            }

            if (ForestSpiritWard)
            {
                flags |= 32;
            }

            if (FriendOfTheForest)
            {
                flags |= 64;
            }

            if (EnemyOfTheForest)
            {
                flags |= 128;
            }

            w.Write((byte)flags);
            string library = Library;
            if (library == null)
            {
                library = "";
            }

            pw.WriteStr(library);
            string heldLight = HeldLight;
            if (heldLight == null)
            {
                heldLight = "";
            }

            pw.WriteStr(heldLight);
            w.Write((byte)Sprites.Count);
            foreach (var s in Sprites)
            {
                w.Write(s.PathHash);
                w.Write(s.CollectionHash);
                w.Write((ushort)s.SpriteId);
                w.Write((short)s.SortingOrder);
                w.Write((byte)s.Layer);
                pw.Write(s.Pos);
                WriteRot(w, s.Rot);
                pw.Write(s.Scale);
                pw.Write(s.SpriteScale);
                Color32 c = s.Color;
                w.Write(c.r);
                w.Write(c.g);
                w.Write(c.b);
                w.Write(c.a);
            }

            int stateFlags = 0;
            if (Away)
            {
                stateFlags |= 1;
            }

            if (AfterNight)
            {
                stateFlags |= 2;
            }

            w.Write((byte)stateFlags);
            w.Write(Health);
            string place = Place;
            if (place == null)
            {
                place = "";
            }

            pw.WriteStr(place);
            pw.Write(HeldFlames);
            w.Write(Lantern);
            w.Write(BeamRadius);
            w.Write(BeamBright);
        }

        public void Read(PacketReader pr)
        {
            var r = pr.R;
            Owner = r.ReadUInt64();
            Position = pr.ReadVector3();
            Rotation = ReadRot(r);
            byte state = r.ReadByte();
            Alive = (state & 1) != 0;
            Spectating = (state & 2) != 0;
            Flashlight = (state & 4) != 0;
            Invisible = (state & 8) != 0;
            ShadowWard = (state & 16) != 0;
            ForestSpiritWard = (state & 32) != 0;
            FriendOfTheForest = (state & 64) != 0;
            EnemyOfTheForest = (state & 128) != 0;
            Library = pr.ReadStr();
            HeldLight = pr.ReadStr();
            int n = r.ReadByte();
            Sprites.Clear();
            for (int i = 0; i < n; i++)
            {
                var s = new SpriteState
                {
                    PathHash = r.ReadInt32(),
                    CollectionHash = r.ReadInt32(),
                    SpriteId = r.ReadUInt16(),
                    SortingOrder = r.ReadInt16(),
                    Layer = r.ReadByte(),
                    Pos = pr.ReadVector3(),
                    Rot = ReadRot(r),
                    Scale = pr.ReadVector3(),
                    SpriteScale = pr.ReadVector3(),
                };

                s.Color = new Color32(
                    r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                Sprites.Add(s);
            }

            byte more = r.ReadByte();
            Away = (more & 1) != 0;
            AfterNight = (more & 2) != 0;
            Health = r.ReadByte();
            Place = pr.ReadStr();
            HeldFlames = pr.ReadVector3();
            Lantern = r.ReadSingle();
            BeamRadius = r.ReadSingle();
            BeamBright = r.ReadSingle();
        }

        private static void WriteRot(System.IO.BinaryWriter w, Quaternion q)
        {
            w.Write(q.x);
            w.Write(q.y);
            w.Write(q.z);
            w.Write(q.w);
        }

        private static Quaternion ReadRot(System.IO.BinaryReader r) =>
            new Quaternion(
                r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        public void CopyFrom(PlayerSnapshot o)
        {
            Owner = o.Owner;
            Time = o.Time;
            Position = o.Position;
            Rotation = o.Rotation;
            Alive = o.Alive;
            Spectating = o.Spectating;
            Library = o.Library;
            HeldLight = o.HeldLight;
            HeldFlames = o.HeldFlames;
            Flashlight = o.Flashlight;
            Lantern = o.Lantern;
            BeamRadius = o.BeamRadius;
            BeamBright = o.BeamBright;
            Invisible = o.Invisible;
            ShadowWard = o.ShadowWard;
            ForestSpiritWard = o.ForestSpiritWard;
            FriendOfTheForest = o.FriendOfTheForest;
            EnemyOfTheForest = o.EnemyOfTheForest;
            Away = o.Away;
            AfterNight = o.AfterNight;
            Place = o.Place;
            Health = o.Health;
            Sprites.Clear();
            Sprites.AddRange(o.Sprites);
        }
    }
}
