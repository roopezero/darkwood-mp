using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat.Enemies;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The client side: our frozen characters and the host's copies. See
    /// NetEnemies.cs.
    /// </summary>
    internal sealed partial class NetEnemies
    {
        // =====================================================================
        // Client
        // =====================================================================

        /// <summary>
        /// A client in the host's world: its own enemies don't run, the host's
        /// are shown.
        /// </summary>
        public static bool ClientMode
        {
            get
            {
                return FreezeRules.ClientMode(ClientOf(Session.Instance));
            }
        }

        /// <summary>
        /// The session as FreezeRules.ClientMode sees it.
        /// </summary>
        private static ClientFacts ClientOf(Session s)
        {
            if (s == null)
            {
                return default;
            }

            return new ClientFacts
            {
                HasSession = true,
                Client = s.IsClient,
                Connected = s.Connected,
                Redirected = SaveRedirect.Active,
            };
        }

        private sealed class Puppet
        {
            public uint Id;
            public Character C;
            // made from the prefab here, rather than our copy from the save
            public bool Ours;
            public readonly Dictionary<int, tk2dSpriteAnimator> Animators =
                new Dictionary<int, tk2dSpriteAnimator>();
            public bool Has; // it has had a state
            // dead: the corpse made lootable once its death has played
            public float DiedAt = -1f;
            public bool Settled;
            // cut in half on the host (Character.cutMeInHalf): its lower half
            // is a new enemy, this part no corpse
            public bool Cut;
            // taken out of our culling while the host sends it (Uncull)
            public bool Unculled;
            public Location HomeLoc;
            public readonly DarkwoodMP.Combat.Motion.MotionTrack Motion =
                new DarkwoodMP.Combat.Motion.MotionTrack();
        }

        /// <summary>
        /// Client: take a shown copy out of our own culling. Our WorldGrid
        /// culls only around our player, so a copy 1600-3000 away (still within
        /// the host's RelevantRange) would be switched off by its node being
        /// left (Cullable.hide, tryToHideMoveable) or by its location's
        /// characters being switched off (Location.activateCharacters), and
        /// nothing would switch it on again while the host moves it: we'd be
        /// hit by an enemy we can't see. While the host sends it, its
        /// EnemyDespawn decides when it goes.
        /// </summary>
        private static void Uncull(Puppet p)
        {
            var c = p.C;
            if (c == null || p.Unculled)
            {
                return;
            }

            p.Unculled = true;
            var go = c.gameObject;
            var co = c.GetComponent<CullableObject>();
            if (co != null)
            {
                WorldGrid.Node node = co.node;
                if (node != null)
                {
                    node.removeCullable(go);
                }

                foreach (var n in co.nodes)
                {
                    if (n != null)
                    {
                        n.removeCullable(go);
                    }
                }

                co.node = null; // checkForNewNode then leaves it alone
                co.nodes.Clear();
            }

            var loc = c.baseLocation;
            if (loc != null)
            {
                if (loc.charactersList.Remove(c))
                {
                    p.HomeLoc = loc;
                }

                loc.objects.Remove(go);
            }

            // Under a location's characters holder it would go off with the
            // location's children: put it where the game puts characters that
            // left their location (a holder that's never switched off).
            CharacterSpawner instance = Singleton<CharacterSpawner>.Instance;
            GameObject holder = null;
            if (instance != null)
            {
                holder = instance.charactersEscapedFromLocationHolder;
            }

            if (holder != null && go.transform.parent != null
                && go.transform.parent != holder.transform)
            {
                c.disconnectFromLocation();
            }

            if (!go.activeSelf)
            {
                go.SetActive(true);
            }

            if (!go.activeInHierarchy)
            {
                Transform parent = go.transform.parent;
                string name = null;
                if (parent != null)
                {
                    name = parent.name;
                }

                Plugin.Log.LogWarning(
                    $"[enemy] the host's {c.name} #{p.Id} is still switched "
                        + $"off here (under {name})");
            }
        }

        /// <summary>
        /// Client: the host stopped sending a copy: our culling looks after it
        /// again, at the spot it's at now.
        /// </summary>
        private static void Recull(Puppet p)
        {
            var c = p.C;
            if (c == null || !p.Unculled)
            {
                return;
            }

            p.Unculled = false;
            if (p.HomeLoc != null && !p.HomeLoc.charactersList.Contains(c))
            {
                p.HomeLoc.charactersList.Add(c);
            }

            p.HomeLoc = null;
            var o = Singleton<OutsideLocations>.Instance;
            var grid = Singleton<WorldGrid>.Instance;
            if (grid == null || (o != null && o.playerInOutsideLocation))
            {
                return; // our current grid isn't the world's
            }

            try
            {
                grid.registerToNode(c.gameObject);
                CullableObject cullable = c.GetComponent<CullableObject>();
                WorldGrid.Node node = null;
                if (cullable != null)
                {
                    node = cullable.node;
                }

                if (node != null && !node.entered && c.gameObject.activeSelf)
                {
                    c.gameObject.SetActive(false);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't put {c.name} back into the culling: "
                        + $"{e.Message}");
            }
        }

        private readonly Dictionary<uint, Puppet> _puppets =
            new Dictionary<uint, Puppet>();
        private readonly Dictionary<Character, Puppet> _byChar =
            new Dictionary<Character, Puppet>();
        private float _nextSweep;

        // Corpses the host told us to leave where they fell: still known by id,
        // for their loot.
        private readonly Dictionary<uint, Puppet> _corpses =
            new Dictionary<uint, Puppet>();
        private readonly Dictionary<Character, uint> _corpseIds =
            new Dictionary<Character, uint>();

        /// <summary>
        /// The net id of one of the host's enemies (host: tracked; client:
        /// shown, or its corpse).
        /// </summary>
        public bool NetIdOf(Character c, out uint id)
        {
            id = 0;
            if (c == null)
            {
                return false;
            }

            if (_session.IsHost && _byCharHost.TryGetValue(c, out var t))
            {
                id = t.Id;
                return true;
            }

            if (_byChar.TryGetValue(c, out var p))
            {
                id = p.Id;
                return true;
            }

            return _corpseIds.TryGetValue(c, out id);
        }

        /// <summary>Client: this is a copy of one of the host's enemies that we
        /// made from its prefab (one spawned while playing), rather than our
        /// own copy from the save.</summary>
        public bool MadeHere(Character c) =>
            c != null && _byChar.TryGetValue(c, out var p) && p.Ours;

        /// <summary>
        /// Client: a corpse of one of the host's enemies that the host told us
        /// to keep (EnemyDespawn with keep corpse).
        /// </summary>
        internal bool KeptCorpse(Character c) =>
            c != null && _corpseIds.ContainsKey(c);

        public Character CharacterOf(uint id)
        {
            if (_session.IsHost)
            {
                if (_byIdHost.TryGetValue(id, out var t))
                {
                    return t.C;
                }

                return null;
            }

            if (_puppets.TryGetValue(id, out var p))
            {
                return p.C;
            }

            if (_corpses.TryGetValue(id, out var c))
            {
                return c.C;
            }

            return null;
        }

        /// <summary>
        /// Host: enemies the clients know of that died at least this long ago.
        /// </summary>
        public IEnumerable<Character> HostDeadSince(float seconds)
        {
            foreach (var t in _byCharHost.Values)
            {
                if (t.C != null && EnemyRelayRules.DeadSince(
                    t.DiedAt >= 0, new Seconds(Time.time - t.DiedAt),
                    new Seconds(seconds)))
                {
                    yield return t.C;
                }
            }
        }

        public bool IsPuppet(Character c, out uint id)
        {
            id = 0;
            if (c != null && _byChar.TryGetValue(c, out var p))
            {
                id = p.Id;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Client: stop one of our own characters from acting, and hide it
        /// until the host says it's there.
        /// </summary>
        public void Freeze(Character c, bool hide)
        {
            if (c == null)
            {
                return;
            }

            c.enabled = false;
            if (c.AIpath != null)
            {
                c.AIpath.enabled = false;
            }

            foreach (var mb in c.GetComponents<MonoBehaviour>())
            {
                if (mb is AILerp)
                {
                    mb.enabled = false;
                }
            }

            var rb = c.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.velocity = Vector3.zero;
            }

            foreach (var a in c.GetComponentsInChildren<tk2dSpriteAnimator>(
                true))
            {
                a.AnimationEventTriggered = null;
                a.AnimationCompleted = null;
            }

            // a corpse the host sent stays shown; no barking from a dog that
            // isn't shown
            if (FreezeRules.Hides(hide, _corpseIds.ContainsKey(c)))
            {
                Hide(c);
                StopSounds(c, keepDeathSound: false);
            }

            _frozen.Add(c);
        }

        private readonly HashSet<Character> _frozen = new HashSet<Character>();

        /// <summary>
        /// Client: our own characters frozen (Freeze), the session's; with no
        /// session an empty set that keeps nothing.
        /// </summary>
        public static HashSet<Character> Frozen
        {
            get
            {
                NetEnemies enemies = Instance;
                if (enemies == null)
                {
                    return new HashSet<Character>();
                }

                return enemies._frozen;
            }
        }

        /// <summary>
        /// Client: our own characters hidden while the host runs the world
        /// there (frozen ones, and our save's copies of host enemies the host
        /// stopped sending). They stay in their location's lists, so the game
        /// switches them off and on with their area, and each time
        /// CharacterSounds.OnEnable starts their idle loop again
        /// (CharacterSoundsPlayIdleLoopHidden keeps them silent): a dead dog
        /// camp's dogs barked on in a joiner's game.
        /// The session's; with no session an empty set that keeps nothing.
        /// </summary>
        public static HashSet<Character> Hidden
        {
            get
            {
                NetEnemies enemies = Instance;
                if (enemies == null)
                {
                    return new HashSet<Character>();
                }

                return enemies._hidden;
            }
        }

        private readonly HashSet<Character> _hidden = new HashSet<Character>();

        private void Hide(Character c)
        {
            SetVisible(c, false);
            _hidden.Add(c);
        }

        private static readonly System.Reflection.FieldInfo FovLightField =
            HarmonyLib.AccessTools.Field(typeof(Player), "FOVLight");

        /// <summary>
        /// The layers our player's view (Player.FOVLight, a Light2D) is cut by:
        /// its rays stop at colliders on these.
        /// </summary>
        internal static int FovShadowMask()
        {
            Light2D l = null;
            if (Player.Instance != null)
            {
                System.Reflection.FieldInfo fovLightField = FovLightField;
                object fovLight = null;
                if (fovLightField != null)
                {
                    fovLight = fovLightField.GetValue(Player.Instance);
                }

                l = fovLight as Light2D;
            }

            if (l != null)
            {
                return (int)l.ShadowLayer;
            }

            return 0;
        }

        /// <summary>
        /// A character's switched-on colliders that cut our player's view (on
        /// FovShadowMask's layers), by path under it.
        /// </summary>
        internal static List<string> FovBlockers(Character c, int mask)
        {
            var list = new List<string>();
            if (c == null || mask == 0)
            {
                return list;
            }

            foreach (var col in c.GetComponentsInChildren<Collider>(true))
            {
                if (col == null || !col.enabled
                    || !col.gameObject.activeInHierarchy
                    || (mask & (1 << col.gameObject.layer)) == 0)
                {
                    continue;
                }

                string path;
                if (col.transform == c.transform)
                {
                    path = ".";
                }
                else
                {
                    path = col.transform.name;
                }

                for (var t = col.transform.parent; t != null
                    && t != c.transform; t = t.parent)
                {
                    path = t.name + "/" + path;
                }

                string text = "";
                if (col.isTrigger)
                {
                    text = ":trigger";
                }

                list.Add(
                    $"{path}:{col.GetType().Name}:"
                        + $"{col.gameObject.layer}{text}");
            }

            list.Sort(System.StringComparer.Ordinal);
            return list;
        }

        private static void LogFovBlockers(Character c, string when)
        {
            int mask = FovShadowMask();
            var b = FovBlockers(c, mask);
            if (b.Count == 0)
            {
                return;
            }

            var sizes = c.GetComponentsInChildren<Collider>(true).Where(
                x => x != null && x.enabled
                    && (mask & (1 << x.gameObject.layer)) != 0)
                .Select(
                    x => $"{x.name} {x.bounds.size.x:0}x{x.bounds.size.z:0}");
            Plugin.Log.LogInfo(
                $"[enemy] {c.name} {when}: colliders on the view's shadow "
                    + $"layers (mask {mask}): {string.Join(", ", b)}; sizes "
                    + $"{string.Join(", ", sizes)}");
        }
    }
}
