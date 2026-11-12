using System.Collections.Generic;
using DarkwoodMP.Sync;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// On the host, a remote player as the game's AI sees them: a CharBase of the player's faction, with a
    /// collider on the player's layer, standing where the remote player is. Enemies find it with the same
    /// sight checks they use for the host's own player (Character.checkForCharactersInViewRange and
    /// canSeeEnemy look for any living CharBase of a faction they attack), chase it, and hit it; their hits are
    /// sent on to that player's game (see CombatPatches).
    ///
    /// Not a Player: that's the 9000-line singleton tied to input, UI and saving.
    /// </summary>
    public sealed class NetPlayerProxy : CharBase
    {
        public ulong Owner;
        public string OwnerName;

        /// <summary>
        /// From the player's own game (their PlayerState): their shadow ward and forest spirit ward effects and their
        /// Friend / Enemy of the Forest skills, which the host's enemy checks read from the proxy of the player they
        /// look at instead of from the host's player (see WardPatches). Invisible is CharBase.invisible.
        /// </summary>
        public bool ShadowWard, ForestSpiritWard, FriendOfTheForest, EnemyOfTheForest;
        /// <summary>
        /// The player is somewhere the host's game doesn't run: a dream (each player's own), or a separate location the
        /// host hasn't got (or has somewhere else). Not in the host's world at all.
        /// </summary>
        public bool Away;
        /// <summary>
        /// The separate location the player is in, when the host has it at the same spot and runs it for them
        /// (World.Locations.HostRunsAt, KeepRunning): the host's enemies there see, chase and hit this proxy as in the world.
        /// </summary>
        public Location InLocation;
        /// <summary>The player's health in percent, from their PlayerState.</summary>
        public int HealthPercent = 100;

        public static readonly Dictionary<ulong, NetPlayerProxy> All = new Dictionary<ulong, NetPlayerProxy>();

        public static NetPlayerProxy Create(ulong owner, string name)
        {
            var player = Player.Instance;
            var go = new GameObject($"DarkwoodMP_Proxy_{name}");
            go.layer = player != null ? player.gameObject.layer : 0;
            CopyCollider(player != null ? player.GetComponent<Collider>() : null, go);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; // moved by the network, not physics; still gets trigger hits
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
        /// A thing another player moves (drags) shouldn't be shoved here by their stand-in, which is solid for the AI
        /// and stands right against it: physics would push it away once it's let go, in this game only.
        /// </summary>
        public static void IgnoreWith(GameObject thing)
        {
            if (thing == null || All.Count == 0) return;
            var theirs = thing.GetComponentsInChildren<Collider>(true);
            foreach (var p in All.Values)
            {
                if (p == null) continue;
                foreach (var c in theirs) p.Ignore(c);
            }
        }

        // Loose things (an Item with a rigidbody: furniture, placed objects, dropped items) this stand-in no longer
        // collides with, by collider instance id.
        private readonly HashSet<int> _ignored = new HashSet<int>();
        private Collider _body;
        private static readonly Collider[] Near = new Collider[128];
        private const float LooseRadius = 150f; // more than a stand-in moves in a frame, and its own size

        private void Ignore(Collider c)
        {
            if (c == null || c.isTrigger) return;
            if (_body == null) _body = GetComponent<Collider>();
            if (_body == null || !_ignored.Add(c.GetInstanceID())) return;
            Physics.IgnoreCollision(_body, c, true);
        }

        /// <summary>
        /// The stand-in is moved to where its player is by the network, as a kinematic body: anything loose it
        /// overlaps, physics shoves away hard, in the host's game only, where the player's own game never saw it
        /// move. Its player's own game sends where they pushed things (DragSync), so it doesn't touch loose things
        /// at all. Enemies still bump into it. Called each frame after it's moved, before the physics step.
        /// </summary>
        public void IgnoreLooseNear()
        {
            int n = Physics.OverlapSphereNonAlloc(transform.position, LooseRadius, Near, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = Near[i];
                if (c == null || _ignored.Contains(c.GetInstanceID())) continue;
                var b = c.attachedRigidbody;
                if (b == null || b.gameObject == gameObject) continue;
                if (b.GetComponent<Item>() == null || b.GetComponent<CharBase>() != null) continue;
                Ignore(c);
            }
        }

        private Player _ignoredPlayer;

        /// <summary>Set the ignores up again on the next frame: our player's here, the loose things' as they're met.</summary>
        public void ForgetIgnores()
        {
            _ignoredPlayer = null;
            _ignored.Clear();
        }

        /// <summary>
        /// Players don't push each other. The stand-in is a kinematic body moved to where its player is, and our
        /// player's body is pushed out of anything solid it overlaps: a player who joined or walked into ours shoved
        /// ours away, often into a corner, while their own game never saw it. So our player's body and the stand-in
        /// don't collide at all (the stand-in stays solid for enemies, and its hit triggers work as before). Done
        /// again when our player is a new object (a world loaded again). Called each frame.
        /// </summary>
        public void IgnoreOurPlayer()
        {
            var player = Player.Instance;
            if (player == null || player == _ignoredPlayer) return;
            if (_body == null) _body = GetComponent<Collider>();
            if (_body == null) return;
            foreach (var c in player.GetComponentsInChildren<Collider>(true))
                if (c != null && !c.isTrigger) Physics.IgnoreCollision(_body, c, true);
            _ignoredPlayer = player;
        }

        private static void CopyCollider(Collider src, GameObject dst)
        {
            switch (src)
            {
                case CapsuleCollider c:
                    var cc = dst.AddComponent<CapsuleCollider>();
                    cc.center = c.center; cc.radius = c.radius; cc.height = c.height; cc.direction = c.direction;
                    break;
                case SphereCollider s:
                    var sc = dst.AddComponent<SphereCollider>();
                    sc.center = s.center; sc.radius = s.radius;
                    break;
                case BoxCollider b:
                    var bc = dst.AddComponent<BoxCollider>();
                    bc.center = b.center; bc.size = b.size;
                    break;
                default:
                    dst.AddComponent<SphereCollider>().radius = 15f;
                    break;
            }
        }

        public override void OnDestroy()
        {
            if (All.TryGetValue(Owner, out var p) && p == this) All.Remove(Owner);
        }

        /// <summary>Hits on a proxy go to its player (CombatPatches routes enemies' sensors here).</summary>
        public override void getHit(float damage) { }
        public override void getHit(float damage, Transform attackerTransform, bool CanCutInHalf, bool byPlayer, bool canInterrupt,
            bool normalHit = true, bool showRedScreen = false, bool force = false, bool dontShowHealthBar = false)
        {
            if (byPlayer) return; // no friendly fire
            PlayerDamage.Send(Owner, damage, attackerTransform, shadow: false, effects: null);
        }

        public override void updateHealthBar() { }
        public override CharacterMessage displayMessage(string message, bool verticallyCentered = false, bool isEpilogueMessage = false) => null;
    }

    /// <summary>Host: keeps a proxy for every remote player (and the test dummy) where their puppet stands.</summary>
    public static class Proxies
    {
        /// <summary>True when the host has other players for the AI to consider: threat and shared culling apply.</summary>
        public static bool Any => NetPlayerProxy.All.Count > 0;

        public static void Update(PlayerSync sync, bool hosting)
        {
            bool wanted = hosting || sync.HasDummy;
            // Remove proxies whose player is gone (or everything, when not hosting).
            var gone = new List<ulong>();
            foreach (var kv in NetPlayerProxy.All)
                if (kv.Value == null || !wanted || !sync.Remotes.ContainsKey(kv.Key) || (!hosting && kv.Key != PlayerSync.DummyId)) gone.Add(kv.Key);
            foreach (var id in gone)
            {
                if (NetPlayerProxy.All.TryGetValue(id, out var p) && p != null) Object.Destroy(p.gameObject);
                NetPlayerProxy.All.Remove(id);
            }
            if (!wanted || Player.Instance == null) return;

            foreach (var r in sync.Remotes.Values)
            {
                if (r.Root == null) continue;
                if (!hosting && r.Owner != PlayerSync.DummyId) continue;
                if (!NetPlayerProxy.All.TryGetValue(r.Owner, out var proxy) || proxy == null)
                    proxy = NetPlayerProxy.Create(r.Owner, r.Name);
                var t = proxy.transform;
                t.SetPositionAndRotation(r.Root.transform.position, r.Root.transform.rotation);
                proxy.IgnoreLooseNear();
                proxy.IgnoreOurPlayer();
                // The AI only looks for living characters. A joiner in a dream, or in a separate location we haven't got,
                // isn't in the host's world at all (their own game runs that area), so they count as not there, as dead
                // ones do. One in a location we have at the same spot is in our world: our enemies there go for them.
                var snap = r.Last;
                var loc = hosting && r.Away && snap != null ? World.Locations.HostRunsAt(snap.Place, t.position) : null;
                bool away = r.Away && loc == null;
                bool here = r.Alive && !away;
                if (proxy.Away != away || proxy.InLocation != loc)
                {
                    Plugin.Log.LogInfo(away ? $"{proxy.OwnerName} is away ({(snap != null && snap.Place.StartsWith("dream:") ? "a dream" : "a separate location we don't have")}): the host's enemies leave them be"
                                     : loc != null ? $"{proxy.OwnerName} is in {snap.Place}, which we run for them: the enemies there are ours and go for them"
                                     : $"{proxy.OwnerName} is back in the world");
                    proxy.Away = away;
                    proxy.InLocation = loc;
                }
                proxy.alive = here;
                if (snap != null) Apply(proxy, snap);
                var col = proxy.GetComponent<Collider>();
                if (col != null && col.enabled != here)
                {
                    col.enabled = here; // a dead or away player (or a spectator) sets nothing off
                    proxy.ForgetIgnores(); // a collider switched off and on can lose what it ignored
                }
            }
        }

        /// <summary>
        /// The player's state bits onto their proxy. Turning invisible does here what Player.setInvisible does in the
        /// player's own game: everything attacking them stops (and the sight checks skip CharBase.invisible).
        /// </summary>
        private static void Apply(NetPlayerProxy proxy, PlayerSnapshot snap)
        {
            bool turnedInvisible = snap.Invisible && !proxy.invisible;
            bool changed = proxy.invisible != snap.Invisible || proxy.ShadowWard != snap.ShadowWard || proxy.ForestSpiritWard != snap.ForestSpiritWard
                           || proxy.FriendOfTheForest != snap.FriendOfTheForest || proxy.EnemyOfTheForest != snap.EnemyOfTheForest;
            proxy.invisible = snap.Invisible;
            proxy.ShadowWard = snap.ShadowWard;
            proxy.ForestSpiritWard = snap.ForestSpiritWard;
            proxy.FriendOfTheForest = snap.FriendOfTheForest;
            proxy.EnemyOfTheForest = snap.EnemyOfTheForest;
            proxy.HealthPercent = snap.Health;
            int stopped = 0;
            if (turnedInvisible)
            {
                var t = proxy.transform;
                foreach (var c in Object.FindObjectsOfType<Character>())
                {
                    if (c == null || (c.target != t && c.superTarget != t)) continue;
                    try { c.stopAttacking(t); stopped++; }
                    catch (System.Exception e) { Plugin.Log.LogWarning($"{c.name} couldn't stop attacking {proxy.OwnerName}: {e.Message}"); }
                }
            }
            if (changed && Threat.Debug)
                Plugin.Log.LogInfo($"[threat] {proxy.OwnerName}: invisible {proxy.invisible}, shadow ward {proxy.ShadowWard}, forest spirit ward {proxy.ForestSpiritWard}, " +
                                   $"friend of the forest {proxy.FriendOfTheForest}, enemy of the forest {proxy.EnemyOfTheForest}" +
                                   (turnedInvisible ? $"; {stopped} enemies stop attacking them" : "") +
                                   $"; night worms sent at them: {NightSpawns.WormWanted(proxy)}; in light here: {Core.isInLight(proxy.transform.position, mustBeWalkable: false)}");
        }

        public static IEnumerable<Vector3> Positions()
        {
            foreach (var p in NetPlayerProxy.All.Values) if (p != null) yield return p.transform.position;
        }

        /// <summary>A CharBase that is a player: the host's own, or a remote one's proxy.</summary>
        public static bool IsPlayer(CharBase c) => c != null && (c is Player || c is NetPlayerProxy);
    }
}
