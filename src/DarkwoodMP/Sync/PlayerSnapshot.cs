using System.Collections.Generic;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.Sync
{
    /// <summary>
    /// What a player looks like right now: root transform plus every visible tk2d sprite in the
    /// player's hierarchy, expressed relative to the root. Mirroring sprite ids directly means
    /// weapons, clothing and every animation come across without syncing the animation state machine.
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
            public Vector3 SpriteScale; // tk2d mesh scale, separate from the transform
            public Color Color;
        }

        public ulong Owner;
        public float Time;
        public Vector3 Position;
        public Quaternion Rotation;
        /// <summary>Alive and not dying: enemies leave a dead or dying player be.</summary>
        public bool Alive = true;
        /// <summary>Dead for good and watching the others (World.Spectator): not shown.</summary>
        public bool Spectating;
        /// <summary>
        /// The body's animation library (a weapon's, a held torch's...). Its sprites come from collections a game loads
        /// only when its own player uses that item, so the others load it by this name to show them.
        /// </summary>
        public string Library = "";
        /// <summary>The item type of the lit thing in hand (a torch, a lamp), whose light and flames the others show.</summary>
        public string HeldLight = "";
        /// <summary>
        /// Where the held light's flames are, relative to the root (as the sprites' Pos). The game moves them to the
        /// item's emitter position for each animation clip and frame (Player.ProcessAnims, InvItem.emitterPositions),
        /// so a torch's flame sits on the torch in every facing; zero when nothing lit is in hand.
        /// </summary>
        public Vector3 HeldFlames;
        /// <summary>
        /// The radius of the light around the player from a lantern worn on the hotbar (InvItemClass.checkForActiveSwitches
        /// puts Player.lightDot in Controller.logicLights at the lantern's lightRadius); 0 when there's none. The others
        /// light the area around the puppet with it, so the host's proxy counts as lit (Core.isInLight).
        /// </summary>
        public float Lantern;
        /// <summary>The flashlight's beam is on.</summary>
        public bool Flashlight;
        /// <summary>
        /// What the host's enemies check about the player they look at, which only the player's own game knows:
        /// invisible (the Chameleon skill, Player.setInvisible), the shadow ward and forest spirit ward effects, and the
        /// Friend / Enemy of the Forest skills. The host puts them on the player's proxy.
        /// </summary>
        public bool Invisible, ShadowWard, ForestSpiritWard, FriendOfTheForest, EnemyOfTheForest;
        /// <summary>
        /// In a separate location or a dream (OutsideLocations.playerInOutsideLocation, Dreams.dreaming): an area loaded
        /// only in their own game, so the others don't show them where their coordinates are.
        /// </summary>
        public bool Away;
        /// <summary>In an after-night: in their hideout after dawn, with their game's clock stopped (Controller.isAfterNight).</summary>
        public bool AfterNight;
        /// <summary>Where, when Away: the location's name, or "dream:" and the dream's (World.Locations.Current).</summary>
        public string Place = "";
        /// <summary>Health as a percentage of the maximum (0 to 100), for the name tag.</summary>
        public byte Health = 100;
        public readonly List<SpriteState> Sprites = new List<SpriteState>(32);

        public void Write(PacketWriter pw)
        {
            var w = pw.W;
            w.Write(Owner);
            pw.Write(Position);
            WriteRot(w, Rotation);
            w.Write((byte)((Alive ? 1 : 0) | (Spectating ? 2 : 0) | (Flashlight ? 4 : 0) | (Invisible ? 8 : 0) | (ShadowWard ? 16 : 0)
                           | (ForestSpiritWard ? 32 : 0) | (FriendOfTheForest ? 64 : 0) | (EnemyOfTheForest ? 128 : 0)));
            pw.WriteStr(Library ?? "");
            pw.WriteStr(HeldLight ?? "");
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
                w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a);
            }
            w.Write((byte)((Away ? 1 : 0) | (AfterNight ? 2 : 0)));
            w.Write(Health);
            pw.WriteStr(Place ?? "");
            pw.Write(HeldFlames);
            w.Write(Lantern);
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
                s.Color = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                Sprites.Add(s);
            }
            byte more = r.ReadByte();
            Away = (more & 1) != 0;
            AfterNight = (more & 2) != 0;
            Health = r.ReadByte();
            Place = pr.ReadStr();
            HeldFlames = pr.ReadVector3();
            Lantern = r.ReadSingle();
        }

        private static void WriteRot(System.IO.BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
        private static Quaternion ReadRot(System.IO.BinaryReader r) => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        public void CopyFrom(PlayerSnapshot o)
        {
            Owner = o.Owner; Time = o.Time; Position = o.Position; Rotation = o.Rotation; Alive = o.Alive; Spectating = o.Spectating; Library = o.Library; HeldLight = o.HeldLight; HeldFlames = o.HeldFlames; Flashlight = o.Flashlight; Lantern = o.Lantern;
            Invisible = o.Invisible; ShadowWard = o.ShadowWard; ForestSpiritWard = o.ForestSpiritWard; FriendOfTheForest = o.FriendOfTheForest; EnemyOfTheForest = o.EnemyOfTheForest;
            Away = o.Away; AfterNight = o.AfterNight; Place = o.Place; Health = o.Health;
            Sprites.Clear(); Sprites.AddRange(o.Sprites);
        }
    }

    /// <summary>Captures the local player's look into a snapshot.</summary>
    public sealed class PlayerCapture
    {
        private GameObject _cachedRoot;
        private GameObject _flashlight;
        private Player _flashlightOf;
        private tk2dBaseSprite[] _sprites;
        private int[] _pathHashes;
        private float _nextRescan;
        private bool _dumped;

        /// <summary>
        /// The player object also carries the FOV/light rigs, the cursor, interaction prompts and progress bars.
        /// Those are rendered by the lighting system or are local-only UI, so only the character's own art is mirrored:
        /// torso (root sprite, includes held weapon frames), legs, and the drop shadow.
        /// </summary>
        private static bool IsCharacterArt(string path) =>
            path == "." || path == "PlayerLegs" || path.StartsWith("PlayerLegs/") || path == "Shadow";

        public bool Capture(PlayerSnapshot snap)
        {
            var player = Player.Instance;
            if (player == null) return false;
            var root = player.transform;

            // Children (held items, effects) come and go; rescan periodically.
            if (_cachedRoot != player.gameObject || UnityEngine.Time.unscaledTime >= _nextRescan)
            {
                _cachedRoot = player.gameObject;
                var all = player.GetComponentsInChildren<tk2dBaseSprite>(true);
                var keep = new System.Collections.Generic.List<tk2dBaseSprite>();
                var hashes = new System.Collections.Generic.List<int>();
                bool dump = Plugin.LogPlayerSprites.Value && !_dumped;
                foreach (var s in all)
                {
                    string path = PathOf(s.transform, root);
                    bool ok = IsCharacterArt(path);
                    if (dump)
                        Plugin.Log.LogInfo($"[sprite] {(ok ? "SYNC" : "skip")} path={path} layer={LayerMask.LayerToName(s.gameObject.layer)} " +
                                           $"active={s.gameObject.activeInHierarchy} coll={s.Collection?.spriteCollectionName} sprite={s.CurrentSprite?.name}");
                    if (!ok) continue;
                    keep.Add(s);
                    hashes.Add(StableHash.Of(path));
                }
                _dumped |= dump;
                _sprites = keep.ToArray();
                _pathHashes = hashes.ToArray();
                _nextRescan = UnityEngine.Time.unscaledTime + 1f;
            }

            snap.Position = root.position;
            snap.Rotation = root.rotation;
            snap.Alive = player.alive && !player.dying;
            snap.Spectating = World.Spectator.Active;
            snap.Library = player.torsoAnimator != null && player.torsoAnimator.Library != null ? player.torsoAnimator.Library.name : "";
            var held = player.currentItem;
            snap.HeldLight = player.itemLightEmitter != null && !InvItemClass.isNull(held) && held.baseClass != null ? held.type ?? "" : "";
            var flames = player.itemParticleEmitter;
            snap.HeldFlames = snap.HeldLight != "" && flames != null ? Quaternion.Inverse(root.rotation) * (flames.position - root.position) : Vector3.zero;
            var dot = player.lightDot;
            var ctl = Singleton<Controller>.Instance;
            snap.Lantern = dot != null && dot.lightsPlayer && ctl != null && ctl.logicLights.Contains(dot) ? dot.LightRadius : 0f;
            if (_flashlight == null || _flashlightOf != player) { _flashlightOf = player; _flashlight = root.Find("Flashlight")?.gameObject; }
            snap.Flashlight = _flashlight != null && _flashlight.activeSelf;
            snap.Invisible = player.invisible;
            var fx = player.effects;
            snap.ShadowWard = fx != null && fx.hasEffectType(CharacterEffectType.shadowWard);
            snap.ForestSpiritWard = fx != null && fx.hasEffectType(CharacterEffectType.forestSpiritWard);
            snap.FriendOfTheForest = player.skills != null && player.skills.FriendOfTheForest;
            snap.EnemyOfTheForest = player.skills != null && player.skills.EnemyOfTheForest;
            snap.Away = LocalAway();
            snap.AfterNight = World.AfterNight.Local;
            snap.Place = World.Locations.Current();
            snap.Health = (byte)(player.maxHealth > 0f ? Mathf.Clamp(Mathf.RoundToInt(player.health / player.maxHealth * 100f), 0, 100) : 0);
            snap.Sprites.Clear();
            var invRot = Quaternion.Inverse(root.rotation);
            for (int i = 0; i < _sprites.Length && snap.Sprites.Count < 255; i++)
            {
                var s = _sprites[i];
                if (s == null || !s.gameObject.activeInHierarchy || s.Collection == null) continue;
                var mr = s.GetComponent<Renderer>();
                if (mr == null || !mr.enabled) continue;
                var t = s.transform;
                snap.Sprites.Add(new PlayerSnapshot.SpriteState
                {
                    PathHash = _pathHashes[i],
                    CollectionHash = StableHash.Of(s.Collection.spriteCollectionName),
                    SpriteId = s.spriteId,
                    SortingOrder = mr.sortingOrder,
                    Layer = s.gameObject.layer,
                    Pos = invRot * (t.position - root.position),
                    Rot = invRot * t.rotation,
                    Scale = t.lossyScale,
                    SpriteScale = s.scale,
                    Color = s.color,
                });
            }
            return true;
        }

        /// <summary>This game's player is in a separate location or a dream.</summary>
        public static bool LocalAway()
        {
            var o = Singleton<OutsideLocations>.Instance;
            var d = Singleton<Dreams>.Instance;
            return (o != null && o.playerInOutsideLocation) || (d != null && d.dreaming);
        }

        private static string PathOf(Transform t, Transform root)
        {
            if (t == root) return ".";
            string p = t.name;
            for (var c = t.parent; c != null && c != root; c = c.parent) p = c.name + "/" + p;
            return p;
        }
    }
}
