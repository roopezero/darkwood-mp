using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Traders;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// TraderSync: the host's copies of joiners' morning traders.
    /// </summary>
    internal sealed partial class TraderSync
    {
        /// <summary>
        /// Host: the traders we put in a joiner's hideout for their morning,
        /// and when.
        /// </summary>
        private readonly List<(Location loc, NPC npc, float since)> _copies =
            new List<(Location, NPC, float)>();
        private Seconds _nextCheck;

        private bool SpawnHostCopy(string name, Vector3 at, ulong from)
        {
            Flags instance = Singleton<Flags>.Instance;
            Flags.NPCState st = null;
            if (instance != null)
            {
                st = instance.getNPCState(name);
            }

            // The wolf comes to a hideout in the morning too
            // (Location.spawnWolf), but he isn't one to trade with.
            if (!TraderCopyRules.MayCopy(name, st != null && st.dead))
            {
                return false;
            }

            var locs = new List<Location>();
            var bases = new List<HideoutBase>();
            foreach (var loc in Object.FindObjectsOfType<Location>(true))
            {
                if (loc != null)
                {
                    locs.Add(loc);
                    bases.Add(FactsOf(loc));
                }
            }

            int nearest = TraderCopyRules.NearestBase(
                bases, new Vec2(at.x, at.z));
            if (nearest < 0)
            {
                return false;
            }

            Location best = locs[nearest];
            bool had = best.trader != null;
            NPC npc;
            try
            {
                npc = best.spawnTrader();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't put {name} in {best.name}: {e.Message}");
                return false;
            }

            string spawned = null;
            if (npc != null)
            {
                spawned = npc.name;
            }

            var result = TraderCopyRules.AfterSpawn(name, spawned, had);
            if (result == SpawnResult.NoTrader)
            {
                return false;
            }

            if (result != SpawnResult.Kept)
            {
                Plugin.Log.LogInfo(
                    $"[world] {_session.NameOf(from)} asked about {name}, but "
                        + $"{best.name}'s morning trader here is {npc.name}");
                if (result == SpawnResult.OtherGoes)
                {
                    best.despawnTrader();
                }

                return false;
            }

            _copies.Add((best, npc, Time.unscaledTime));
            Plugin.Log.LogInfo(
                $"[world] {_session.NameOf(from)}'s morning: our own {name} is "
                    + $"in {best.name} too");
            return true;
        }

        /// <summary>
        /// Host, every frame: a copy we put in a joiner's hideout goes when no
        /// joiner is in a morning any more (their endAfterNight took theirs
        /// away), unless our own player's morning is there. A dead one stays,
        /// as a body does.
        /// </summary>
        internal void HostUpdate()
        {
            var now = new Seconds(Time.unscaledTime);
            if (!TraderCopyRules.Due(_copies.Count, now, _nextCheck))
            {
                return;
            }

            _nextCheck = TraderCopyRules.Next(now);
            var remotes = new List<RemoteMorning>();
            var sync = Sync.PlayerSync.Instance;
            if (sync != null)
            {
                foreach (var kv in sync.Remotes)
                {
                    remotes.Add(FactsOf(kv.Key, kv.Value));
                }
            }

            bool someone = TraderCopyRules.SomeoneInMorning(remotes);
            for (int i = _copies.Count - 1; i >= 0; i--)
            {
                var (loc, npc, since) = _copies[i];
                var facts = FactsOf(loc, npc) with
                {
                    Someone = someone,
                    Now = now,
                    Since = new Seconds(since),
                };

                var fate = TraderCopyRules.Fate(facts);
                if (fate == CopyFate.Keep)
                {
                    continue;
                }

                if (fate == CopyFate.TakeAway)
                {
                    Plugin.Log.LogInfo(
                        $"[world] no joiner is in a morning: our {npc.name} "
                            + $"leaves {loc.name}");
                    if (loc.trader == npc.gameObject)
                    {
                        loc.despawnTrader();
                    }
                    else
                    {
                        Object.Destroy(npc.gameObject);
                    }
                }

                _copies.RemoveAt(i);
            }
        }

        /// <summary>
        /// What the copy rules know of a hideout: the only code that reads a
        /// Location for them.
        /// </summary>
        private static HideoutBase FactsOf(Location loc)
        {
            var spot = loc.traderPosition;
            var facts = new HideoutBase
            {
                PlayerBase = loc.playerBase,
                HasTraderSpot = spot != null,
            };

            if (spot != null)
            {
                var at = spot.transform.position;
                facts = facts with { TraderSpot = new Vec2(at.x, at.z) };
            }

            return facts;
        }

        /// <summary>
        /// Whether a remote player is in a morning, as their last state says.
        /// </summary>
        private static RemoteMorning FactsOf(ulong id, Sync.RemotePlayer p)
        {
            var last = p.Last;
            var facts = new RemoteMorning
            {
                Dummy = id == Sync.PlayerSync.DummyId,
                HasState = last != null,
            };

            if (last != null)
            {
                facts = facts with { AfterNight = last.AfterNight };
            }

            return facts;
        }

        /// <summary>
        /// What the copy rules know of one of our copies: gone, dead, and
        /// whether our own player's morning is in that hideout (ours now).
        /// </summary>
        private static TraderCopyFacts FactsOf(Location loc, NPC npc)
        {
            if (npc == null || loc == null)
            {
                return new TraderCopyFacts { Gone = true };
            }

            var ch = npc.GetComponent<Character>();
            var c = Singleton<Controller>.Instance;
            var p = Player.Instance;
            return new TraderCopyFacts
            {
                Dead = ch != null && !ch.alive,
                OursNow = c != null && c.isAfterNight && p != null
                    && p.whereAmI != null && p.whereAmI.bigLocation == loc,
            };
        }
    }
}
