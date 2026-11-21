using System.Collections.Generic;
using DarkwoodMP.Combat.Joiners;
using DarkwoodMP.Sync;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// On the host, a remote player as the game's AI sees them: a CharBase of
    /// the player's faction, with a collider on the player's layer, standing
    /// where the remote player is. Enemies find it with the same sight checks
    /// they use for the host's own player
    /// (Character.checkForCharactersInViewRange and canSeeEnemy look for any
    /// living CharBase of a faction they attack), chase it, and hit it; their
    /// hits are sent on to that player's game (see CombatPatches).
    ///
    /// Not a Player: that's the 9000-line singleton tied to input, UI and
    /// saving.
    /// </summary>
    public sealed class NetPlayerProxy : CharBase
    {
        public ulong Owner;
        public string OwnerName;

        /// <summary>
        /// From the player's own game (their PlayerState): their shadow ward
        /// and forest spirit ward effects and their Friend / Enemy of the
        /// Forest skills, which the host's enemy checks read from the proxy of
        /// the player they look at instead of from the host's player (see
        /// WardPatches). Invisible is CharBase.invisible.
        /// </summary>
        public bool ShadowWard,
            ForestSpiritWard,
            FriendOfTheForest,
            EnemyOfTheForest;
        /// <summary>
        /// The player is somewhere the host's game doesn't run: a dream (each
        /// player's own), or a separate location the host hasn't got (or has
        /// somewhere else). Not in the host's world at all.
        /// </summary>
        public bool Away;
        /// <summary>
        /// The separate location the player is in, when the host has it at the
        /// same spot and runs it for them (World.Locations.HostRunsAt,
        /// KeepRunning): the host's enemies there see, chase and hit this proxy
        /// as in the world.
        /// </summary>
        public Location InLocation;
        /// <summary>
        /// The player's health in percent, from their PlayerState.
        /// </summary>
        public int HealthPercent = 100;

        /// <summary>
        /// The host's stand-ins by their player's id (ModHost.Proxies).
        /// </summary>
        public static Dictionary<ulong, NetPlayerProxy> All =>
            Plugin.Instance.Host.Proxies.All;

        public static NetPlayerProxy Create(ulong owner, string name)
        {
            var player = Player.Instance;
            var go = new GameObject($"DarkwoodMP_Proxy_{name}");
            if (player != null)
            {
                go.layer = player.gameObject.layer;
            }
            else
            {
                go.layer = 0;
            }

            Collider src = null;
            if (player != null)
            {
                src = player.GetComponent<Collider>();
            }

            CopyCollider(src, go);
            var rb = go.AddComponent<Rigidbody>();
            // moved by the network, not physics; still gets trigger hits
            rb.isKinematic = true;
            rb.useGravity = false;

            var p = go.AddComponent<NetPlayerProxy>();
            p.Owner = owner;
            p.OwnerName = name;
            p.faction = Faction.player;
            p.alive = true;
            p.isActive = true;
            All[owner] = p;
            Plugin.Log.LogInfo($"Enemies can now see and attack {name}");
            return p;
        }

        /// <summary>
        /// A thing another player moves (drags) shouldn't be shoved here by
        /// their stand-in, which is solid for the AI and stands right against
        /// it: physics would push it away once it's let go, in this game only.
        /// </summary>
        public static void IgnoreWith(GameObject thing)
        {
            if (thing == null || All.Count == 0)
            {
                return;
            }

            var theirs = thing.GetComponentsInChildren<Collider>(true);
            foreach (var p in All.Values)
            {
                if (p == null)
                {
                    continue;
                }

                foreach (var c in theirs)
                {
                    p.Ignore(c);
                }
            }
        }

        // Loose things (an Item with a rigidbody: furniture, placed objects,
        // dropped items) this stand-in no longer collides with, by collider
        // instance id.
        private readonly HashSet<int> _ignored = new HashSet<int>();
        private Collider _body;
        private Collider Body
        {
            get
            {
                if (_body != null)
                {
                    return _body;
                }

                return _body = GetComponent<Collider>();
            }
        }

        private readonly Collider[] _near = new Collider[ProxyRules.MaxLoose];

        private void Ignore(Collider c)
        {
            if (c == null || c.isTrigger || Body == null
                || !_ignored.Add(c.GetInstanceID()))
            {
                return;
            }

            Physics.IgnoreCollision(Body, c, true);
        }

        /// <summary>
        /// The stand-in is moved to where its player is by the network, as a
        /// kinematic body: anything loose it overlaps, physics shoves away
        /// hard, in the host's game only, where the player's own game never saw
        /// it move. Its player's own game sends where they pushed things
        /// (DragSync), so it doesn't touch loose things at all. Enemies still
        /// bump into it. Called each frame after it's moved, before the physics
        /// step.
        /// </summary>
        public void IgnoreLooseNear()
        {
            int n = Physics.OverlapSphereNonAlloc(
                transform.position, ProxyRules.LooseRadius, _near, ~0,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = _near[i];
                if (c == null || _ignored.Contains(c.GetInstanceID()))
                {
                    continue;
                }

                var b = c.attachedRigidbody;
                if (b == null || b.gameObject == gameObject)
                {
                    continue;
                }

                if (b.GetComponent<Item>() == null
                    || b.GetComponent<CharBase>() != null)
                {
                    continue;
                }

                Ignore(c);
            }
        }

        private Player _ignoredPlayer;

        /// <summary>
        /// Set the ignores up again on the next frame: our player's here, the
        /// loose things' as they're met.
        /// </summary>
        public void ForgetIgnores()
        {
            _ignoredPlayer = null;
            _ignored.Clear();
        }

        /// <summary>
        /// Players don't push each other. The stand-in is a kinematic body
        /// moved to where its player is, and our player's body is pushed out of
        /// anything solid it overlaps: a player who joined or walked into ours
        /// shoved ours away, often into a corner, while their own game never
        /// saw it. So our player's body and the stand-in don't collide at all
        /// (the stand-in stays solid for enemies, and its hit triggers work as
        /// before). Done again when our player is a new object (a world loaded
        /// again). Called each frame.
        /// </summary>
        public void IgnoreOurPlayer()
        {
            var player = Player.Instance;
            var body = Body;
            if (player == null || player == _ignoredPlayer || body == null)
            {
                return;
            }

            foreach (var c in player.GetComponentsInChildren<Collider>(true))
            {
                if (c != null && !c.isTrigger)
                {
                    Physics.IgnoreCollision(body, c, true);
                }
            }

            _ignoredPlayer = player;
        }

        private static void CopyCollider(Collider src, GameObject dst)
        {
            switch (src)
            {
                case CapsuleCollider c:
                    var cc = dst.AddComponent<CapsuleCollider>();
                    cc.center = c.center;
                    cc.radius = c.radius;
                    cc.height = c.height;
                    cc.direction = c.direction;
                    break;
                case SphereCollider s:
                    var sc = dst.AddComponent<SphereCollider>();
                    sc.center = s.center;
                    sc.radius = s.radius;
                    break;
                case BoxCollider b:
                    var bc = dst.AddComponent<BoxCollider>();
                    bc.center = b.center;
                    bc.size = b.size;
                    break;
                default:
                    dst.AddComponent<SphereCollider>().radius = 15f;
                    break;
            }
        }

        public override void OnDestroy()
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || plugin.Host == null)
            {
                return;
            }

            if (All.TryGetValue(Owner, out var p) && p == this)
            {
                All.Remove(Owner);
            }
        }

        /// <summary>
        /// Hits on a proxy go to its player (CombatPatches routes enemies'
        /// sensors here).
        /// </summary>
        public override void getHit(float damage)
        {
        }

        public override void getHit(
            float damage, Transform attackerTransform, bool CanCutInHalf,
            bool byPlayer, bool canInterrupt,
            bool normalHit = true, bool showRedScreen = false,
            bool force = false, bool dontShowHealthBar = false)
        {
            if (byPlayer)
            {
                return; // no friendly fire
            }

            PlayerDamage.Send(
                Owner, damage, attackerTransform, shadow: false, effects: null);
        }

        public override void updateHealthBar()
        {
        }

        public override CharacterMessage displayMessage(
            string message, bool verticallyCentered = false,
            bool isEpilogueMessage = false) => null;
    }
}
