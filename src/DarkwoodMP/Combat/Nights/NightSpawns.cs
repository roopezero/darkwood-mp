using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Combat.Nights;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// Night attackers and night worms, for every player. The game spawns both
    /// around Player.Instance only: CharacterSpawner.spawnNightChar sends one
    /// of the night scenario's attackers at the player while they are at a
    /// hideout (a Location with playerBase), and waitToSpawnWorm sends a night
    /// worm at them every five seconds of a hard night. The host does the same
    /// around each remote player's proxy, and joiners spawn neither (anything
    /// they spawned would only be frozen and hidden there, see
    /// NetEnemies.LocalOnly).
    ///
    /// A joiner is at a hideout when the game's own WhereAmI check, run at
    /// their proxy's position on the host, finds a hideout location: the
    /// nearest Location collider within 300, or the big location it belongs to.
    /// Each occupied hideout has the night's attackers to itself, as the one
    /// hideout of a game played alone: every spawnInterval, one attacker goes
    /// to each hideout that has a player in it (the host's own player, or a
    /// joiner's proxy), aimed at the host's player if they are there, else the
    /// first joiner there, and the scenario's numbers (how many of each kind
    /// may be out at once, CharacterToSpawn.amount) are counted per hideout.
    /// Players who share a hideout share its budget: three players in one
    /// hideout get the attackers one player would, not three each interval. A
    /// player out of any hideout gets no night attackers (the game sends none
    /// at a player outside), only the worms. The scenario's own count
    /// (CharacterToSpawn.spawned) still goes up and down with every attacker,
    /// as the game keeps it; it just no longer decides. Alone (no other
    /// players), the game's own spawnNightChar runs unchanged.
    /// A shadow ward stops the worms of the player who has it: the host's own,
    /// and a joiner's from their PlayerState (NetPlayerProxy.ShadowWard), as
    /// the game's own loop checks Player.Instance's.
    /// The decisions are the core's NightRules; the host's budgets and the
    /// attackers it sent are ModHost.Nights (NightSpawner), which callers use
    /// directly.
    /// </summary>
    public static class NightSpawns
    {
        public static bool AtHideout(Vector3 pos, out Location loc)
        {
            loc = Plugin.Instance.Host.Nights.BigLocationAt(pos);
            return loc != null && loc.playerBase;
        }

        internal static IEnumerable<NetPlayerProxy> LivingProxies()
        {
            foreach (var p in new List<NetPlayerProxy>(
                NetPlayerProxy.All.Values))
            {
                if (p != null && p.alive && p.InLocation == null)
                {
                    // nobody is sent at night into a separate location
                    yield return p;
                }
            }
        }

        /// <summary>
        /// The worm loop sends worms at this joiner: alive, in the world (not
        /// in a separate location the host runs for them), and without a shadow
        /// ward.
        /// </summary>
        public static bool WormWanted(NetPlayerProxy proxy) => proxy != null
            && NightRules.WormWanted(
                proxy.alive, proxy.InLocation != null, proxy.ShadowWard);

        /// <summary>
        /// "--dwmp-test-night": once a remote player (or the test dummy) is in,
        /// check their hideout and send one night worm and one night attacker
        /// at them, whatever the time, logging where each was spawned.
        /// </summary>
        public static IEnumerator TestAfterJoin()
        {
            while (NetPlayerProxy.All.Count == 0)
            {
                yield return new WaitForSeconds(0.5f);
            }

            yield return new WaitForSeconds(2f);
            var spawner = Singleton<CharacterSpawner>.Instance;
            var nights = Plugin.Instance.Host.Nights;
            foreach (var proxy in LivingProxies())
            {
                bool home = AtHideout(proxy.transform.position, out var loc);
                string locationName = "none";
                if (loc != null)
                {
                    locationName = loc.name;
                }

                Plugin.Log.LogInfo(
                    $"[night test] {proxy.OwnerName} at "
                        + $"{proxy.transform.position}: location "
                        + $"{locationName}, at a hideout: {home}");
                var worm = nights.SpawnWorm(spawner, proxy);
                var log = Plugin.Log;
                string line;
                if (worm != null)
                {
                    var owner = proxy.OwnerName;
                    var position = worm.transform.position;
                    var distance = Core.trueDistance(
                        worm.transform.position, proxy.transform.position);
                    string target = "nobody";
                    if (worm.target != null)
                    {
                        target = worm.target.name;
                    }

                    line =
                        $"[night test] worm for {owner} at {position}, "
                            + $"{distance:0} from them, going for {target}";
                }
                else
                {
                    line = $"[night test] no worm for {proxy.OwnerName}";
                }

                log.LogInfo(line);
                // A night attacker as at a hideout: the host's own location if
                // the proxy isn't at one.
                Location at = loc;
                if (at == null)
                {
                    if (Player.Instance.whereAmI != null)
                    {
                        at = Player.Instance.whereAmI.bigLocation;
                    }
                    else
                    {
                        at = null;
                    }
                }

                if (at != null)
                {
                    var c = nights.SpawnNightChar(spawner, proxy, at);
                    string data;
                    if (c != null)
                    {
                        data =
                            $"[night test] night attacker {c.name} for "
                                + $"{proxy.OwnerName} at "
                                + $"{c.transform.position}";
                    }
                    else
                    {
                        data =
                            "[night test] no night attacker for "
                                + $"{proxy.OwnerName} (the scenario has none "
                                + "left)";
                    }

                    Plugin.Log.LogInfo(data);
                    JoinerAi.Test(c, proxy);
                }
                else
                {
                    Plugin.Log.LogInfo(
                        "[night test] no location to send a night attacker "
                            + $"from for {proxy.OwnerName}");
                }
            }

            Plugin.Log.LogInfo("[night test] done");
        }
    }
}
