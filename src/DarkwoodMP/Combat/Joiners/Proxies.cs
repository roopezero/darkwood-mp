using System.Collections.Generic;
using DarkwoodMP.Combat.Joiners;
using DarkwoodMP.Sync;
using DarkwoodMP.Sync.Players;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Host: keeps a proxy for every remote player (and the test dummy) where
    /// their puppet stands. One lives as long as the game, in ModHost.Proxies:
    /// the test dummy has one in a game played alone too. The core's
    /// ProxyRules decide which stay and where their players are.
    /// </summary>
    internal sealed class Proxies
    {
        /// <summary>
        /// The stand-ins by their player's id.
        /// </summary>
        public readonly Dictionary<ulong, NetPlayerProxy> All =
            new Dictionary<ulong, NetPlayerProxy>();

        /// <summary>
        /// True when the host has other players for the AI to consider: threat
        /// and shared culling apply.
        /// </summary>
        public static bool Any => NetPlayerProxy.All.Count > 0;

        /// <summary>
        /// A CharBase that is a player: the host's own, or a remote one's
        /// proxy.
        /// </summary>
        public static bool IsPlayer(CharBase c) =>
            c != null && (c is Player || c is NetPlayerProxy);

        public void Update(PlayerSync sync, bool hosting)
        {
            bool wanted = ProxyRules.Wanted(hosting, sync.HasDummy);
            // Remove proxies whose player is gone (or everything, when not
            // hosting).
            var gone = new List<ulong>();
            foreach (var kv in All)
            {
                if (!ProxyRules.Keeps(KeepOf(kv, sync, wanted, hosting)))
                {
                    gone.Add(kv.Key);
                }
            }

            foreach (var id in gone)
            {
                if (All.TryGetValue(id, out var p) && p != null)
                {
                    Object.Destroy(p.gameObject);
                }

                All.Remove(id);
            }

            if (!ProxyRules.Places(wanted, Player.Instance != null))
            {
                return;
            }

            foreach (var r in sync.Remotes.Values)
            {
                bool dummy = r.Owner == PlayerSync.DummyId;
                if (ProxyRules.Stands(r.Root != null, hosting, dummy))
                {
                    Place(r, hosting);
                }
            }
        }

        private static KeepFacts KeepOf(
            KeyValuePair<ulong, NetPlayerProxy> kv,
            PlayerSync sync,
            bool wanted,
            bool hosting)
        {
            return new KeepFacts
            {
                Exists = kv.Value != null,
                Wanted = wanted,
                Known = sync.Remotes.ContainsKey(kv.Key),
                Hosting = hosting,
                Dummy = kv.Key == PlayerSync.DummyId,
            };
        }

        private void Place(RemotePlayer r, bool hosting)
        {
            if (!All.TryGetValue(r.Owner, out var proxy) || proxy == null)
            {
                proxy = NetPlayerProxy.Create(r.Owner, r.Name);
            }

            var t = proxy.transform;
            t.SetPositionAndRotation(
                r.Root.transform.position, r.Root.transform.rotation);
            proxy.IgnoreLooseNear();
            proxy.IgnoreOurPlayer();
            // The AI only looks for living characters. A joiner in a dream
            // of their own, or in a separate location we haven't got, isn't
            // in the host's world at all (their own game runs that area),
            // so they count as not there, as dead ones do. One in a
            // location we have at the same spot, or in the shared dream
            // with our player, is in our world: our enemies there go for
            // them.
            var snap = r.Last;
            Location loc = null;
            if (hosting && r.Away && snap != null)
            {
                loc = World.Places.Whereabouts.HostRunsAt(
                    snap.Place, t.position);
            }

            Presence presence = ProxyRules.PresenceOf(new PresenceFacts
            {
                Hosting = hosting,
                Away = r.Away,
                Alive = r.Alive,
                HasSnapshot = snap != null,
                HostRuns = loc != null,
            });

            bool away = presence.Away;
            bool here = presence.Here;
            if (proxy.Away != away || proxy.InLocation != loc)
            {
                Plugin.Log.LogInfo(NoteOf(proxy, presence, snap));
                proxy.Away = away;
                proxy.InLocation = loc;
            }

            proxy.alive = here;
            if (snap != null)
            {
                Apply(proxy, snap);
            }

            var col = proxy.GetComponent<Collider>();
            if (col != null && col.enabled != here)
            {
                // a dead or away player (or a spectator) sets nothing off
                col.enabled = here;
                // a collider switched off and on can lose what it ignored
                proxy.ForgetIgnores();
            }
        }

        private static string NoteOf(
            NetPlayerProxy proxy,
            Presence presence,
            PlayerSnapshot snap)
        {
            PresenceNote note = ProxyRules.NoteOf(presence);
            if (note == PresenceNote.Away)
            {
                string text = "a separate location we don't have";
                if (ProxyRules.InDream(snap != null, PlaceOf(snap)))
                {
                    text = "a dream";
                }

                return $"{proxy.OwnerName} is away ({text}): the host's "
                    + "enemies leave them be";
            }

            if (note == PresenceNote.InLocation)
            {
                return $"{proxy.OwnerName} is in {snap.Place}, which we "
                    + "run for them: the enemies there are ours "
                    + "and go for them";
            }

            return $"{proxy.OwnerName} is back in the world";
        }

        private static string PlaceOf(PlayerSnapshot snap)
        {
            if (snap == null)
            {
                return null;
            }

            return snap.Place;
        }

        /// <summary>
        /// The player's state bits onto their proxy. Turning invisible does
        /// here what Player.setInvisible does in the player's own game:
        /// everything attacking them stops (and the sight checks skip
        /// CharBase.invisible).
        /// </summary>
        private static void Apply(NetPlayerProxy proxy, PlayerSnapshot snap)
        {
            bool turnedInvisible =
                ProxyRules.TurnedInvisible(snap.Invisible, proxy.invisible);
            bool changed = proxy.invisible != snap.Invisible
                || proxy.ShadowWard != snap.ShadowWard
                || proxy.ForestSpiritWard != snap.ForestSpiritWard
                || proxy.FriendOfTheForest
                    != snap.FriendOfTheForest
                || proxy.EnemyOfTheForest != snap.EnemyOfTheForest;
            proxy.invisible = snap.Invisible;
            proxy.ShadowWard = snap.ShadowWard;
            proxy.ForestSpiritWard = snap.ForestSpiritWard;
            proxy.FriendOfTheForest = snap.FriendOfTheForest;
            proxy.EnemyOfTheForest = snap.EnemyOfTheForest;
            proxy.HealthPercent = snap.Health;
            int stopped = 0;
            if (turnedInvisible)
            {
                stopped = StopAttacking(proxy);
            }

            if (changed && Threat.Debug)
            {
                LogState(proxy, turnedInvisible, stopped);
            }
        }

        private static int StopAttacking(NetPlayerProxy proxy)
        {
            int stopped = 0;
            var t = proxy.transform;
            foreach (var c in Object.FindObjectsOfType<Character>())
            {
                if (c == null || (c.target != t && c.superTarget != t))
                {
                    continue;
                }

                try
                {
                    c.stopAttacking(t);
                    stopped++;
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"{c.name} couldn't stop attacking "
                            + $"{proxy.OwnerName}: {e.Message}");
                }
            }

            return stopped;
        }

        private static void LogState(
            NetPlayerProxy proxy,
            bool turnedInvisible,
            int stopped)
        {
            string stopNote = "";
            if (turnedInvisible)
            {
                stopNote = $"; {stopped} enemies stop attacking them";
            }

            bool lit = Core.isInLight(
                proxy.transform.position, mustBeWalkable: false);
            Plugin.Log.LogInfo(
                $"[threat] {proxy.OwnerName}: invisible {proxy.invisible}, "
                    + $"shadow ward {proxy.ShadowWard}, forest spirit ward "
                    + $"{proxy.ForestSpiritWard}, "
                    + "friend of the forest "
                    + $"{proxy.FriendOfTheForest}, enemy of the forest "
                    + $"{proxy.EnemyOfTheForest}"
                    + stopNote
                    + "; night worms sent at them: "
                    + $"{NightSpawns.WormWanted(proxy)}; in light here: "
                    + $"{lit}");
        }
    }
}
