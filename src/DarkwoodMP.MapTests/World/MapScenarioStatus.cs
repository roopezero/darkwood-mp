using System.Collections;
using System.Linq;
using DarkwoodMP.Sync;
using DarkwoodMP.Sync.Players;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for a joiner waiting out the host's prologue, the LAN
    /// search, the crouch while disarming, and watching the others after dying
    /// for good.
    /// The list is tools/tests/map_scenario_status.py.
    /// </summary>
    public static partial class MapScenarioStatus
    {
        private static Player P => Player.Instance;

        public static void Register()
        {
            // "hold-joiners SECS" (host, before the join): answer joiners as if
            // watching the prologue's intro for SECS from the first.
            Add("hold-joiners", (a, r) =>
            {
                WorldShare share = Plugin.Instance.Host.Share;
                if (a.Length > 0)
                {
                    share.TestHoldSecs = F(a[0]);
                }
                else
                {
                    share.TestHoldSecs = 15f;
                }

                r.Info =
                    $"joiners wait {share.TestHoldSecs:0} s from the "
                        + "first";
                return Nothing();
            });

            Add("join-waited", JoinWaited);
            // "join-stop" (host, after the join): how long the snapshot for the
            // joiner stopped the host's game.
            Add("join-stop", (a, r) =>
            {
                WorldShare share = Plugin.Instance.Host.Share;
                string text = "the game's own save";
                if (share.LastSnapshotOffThread)
                {
                    text = "sav.dat written on a worker thread";
                }

                r.Info =
                    "snapshot stopped the game for "
                        + $"{share.LastSnapshotStopMs} ms ({text}), "
                        + "longest host frame while joining "
                        + $"{share.LastJoinLongestFrameMs:0} ms";
                if (share.LastSnapshotStopMs < 0)
                {
                    r.Fail("no snapshot was made for the joiner");
                }
                else if (!share.LastSnapshotOffThread)
                {
                    r.Fail(
                        "the snapshot was saved the game's way, not on a "
                            + "worker thread: " + r.Info);
                }

                return Nothing();
            });

            Add("story-at-join", StoryAtJoin);
            Add("join-story", JoinStory);
            Add("lan-search", LanSearch, timeout: 30f);
            Add("disarm-crouch", DisarmCrouch);
            Add("puppet-crouch", PuppetCrouch);
            Add("disarm-finish", DisarmFinish);
            Add("permadeath", Permadeath, timeout: 30f);
            Add("sees-spectator", SeesSpectator);
            Add("spectator-view", SpectatorView, timeout: 20f);
            Add("spectate-end", SpectateEnd);
        }

        private static IEnumerator Nothing()
        {
            yield break;
        }

        /// <summary>
        /// "story-at-join" (host, before the join): once the joiner's snapshot
        /// is read, while the joiner downloads and loads it, change the story
        /// (the first NPC's reputation +1, a story flag set) as a player could
        /// meanwhile.
        /// </summary>
        private static IEnumerator StoryAtJoin(string[] a, Result r)
        {
            int noted = SnapshotsNoted();
            Singleton<Controller>.Instance.StartCoroutine(
                ChangeStoryAfter(noted));
            r.Info = "the story changes once the next snapshot is read";
            yield break;
        }

        /// <summary>
        /// How many snapshots' story the host noted in this session.
        /// </summary>
        private static int SnapshotsNoted()
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return 0;
            }

            return coop.Story.SnapshotNoted;
        }

        private static IEnumerator ChangeStoryAfter(int noted)
        {
            float until = Time.realtimeSinceStartup + 120f;
            while (SnapshotsNoted() == noted
                && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            if (SnapshotsNoted() == noted)
            {
                Plugin.Log.LogWarning(
                    "[map] story-at-join: no snapshot was made");
                yield break;
            }

            yield return null; // after the frame the world was read in
            var f = Singleton<Flags>.Instance;
            var npc = f.npcStates.FirstOrDefault(n => n != null);
            if (npc != null)
            {
                npc.reputation += 1;
            }

            // a shared one, not read by the game's code
            f.setFlag("player_firstOvenInteraction", activeModifier: true);
            string name = null;
            if (npc != null)
            {
                name = npc.name;
            }

            int? reputation = null;
            if (npc != null)
            {
                reputation = npc.reputation;
            }

            Plugin.Log.LogInfo(
                $"[map] story-at-join: after the snapshot, {name}'s reputation "
                    + $"{reputation}, player_firstOvenInteraction set");
        }

        /// <summary>
        /// "join-story" (host, after the join): the story changes made while
        /// the joiner loaded were sent to it.
        /// </summary>
        private static IEnumerator JoinStory(string[] a, Result r)
        {
            var lines = MapScenarioIssues.LogLines();
            string changed = lines.LastOrDefault(
                l => l.Contains("[map] story-at-join: after the snapshot"));
            string sent = lines.LastOrDefault(
                l => l.Contains(
                    "story changes since their snapshot sent to them"));
            if (changed == null)
            {
                r.Fail(
                    "the story wasn't changed during the join (story-at-join)");
            }
            else if (sent == null)
            {
                r.Fail(
                    "no 'story changes since their snapshot sent to them' in "
                        + "the log");
            }
            else
            {
                r.Info = sent.Substring(sent.IndexOf("] ") + 2);
            }

            yield break;
        }

        /// <summary>
        /// "join-waited" (client): the log has the host's prologue wait, then
        /// our world came in.
        /// </summary>
        private static IEnumerator JoinWaited(string[] a, Result r)
        {
            var lines = MapScenarioIssues.LogLines();
            int wait = lines.FindIndex(
                l => l.Contains(
                    "Waiting to join: The host is watching the prologue"));
            int ready = lines.FindLastIndex(l => l.Contains("World ready"));
            if (wait < 0)
            {
                r.Fail(
                    "no \"Waiting to join: The host is watching the prologue\" "
                        + "in the log");
            }
            else if (ready < wait)
            {
                r.Fail("the world wasn't ready after the wait");
            }
            else
            {
                int hellos = lines.Skip(wait).Count(
                    l => l.Contains("Sent Hello")
                        || l.Contains("sending it again"));
                r.Info = "waited " + $"({hellos} Hellos), then joined";
            }

            yield break;
        }

        /// <summary>
        /// "lan-search" (client): the server browser's LAN tab finds the host
        /// (its info answer, our protocol).
        /// </summary>
        private static IEnumerator LanSearch(string[] a, Result r)
        {
            var b = Plugin.Browser;
            if (b == null)
            {
                r.Fail("no server browser");
                yield break;
            }

            b.Refresh(Net.ServerBrowser.Tab.Lan);
            yield return null;
            float until = Time.realtimeSinceStartup + 25f;
            while (b.SearchingLan && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            // the browser's update hands over the last answers
            yield return null;
            var found = b.List(Net.ServerBrowser.Tab.Lan)
                .Select(addr => (addr, e: b.Get(addr))).ToList();
            if (found.Count == 0)
            {
                r.Info = "nothing found";
            }
            else
            {
                r.Info = string.Join(", ", found.Select(f =>
                {
                    Net.ServerInfo info = f.e.Info;
                    string name = null;
                    if (info != null)
                    {
                        name = info.Name;
                    }

                    int? players = null;
                    if (info != null)
                    {
                        players = info.Players;
                    }

                    int? maxPlayers = null;
                    if (info != null)
                    {
                        maxPlayers = info.MaxPlayers;
                    }

                    int? proto = null;
                    if (info != null)
                    {
                        proto = info.Proto;
                    }

                    return $"{f.addr} \"{name}\" {players}/{maxPlayers} proto "
                        + $"{proto} {f.e.PingMs} ms";
                }));
            }

            if (!found.Any(
                f => f.e.Info != null
                    && f.e.Info.Proto == Net.Protocol.Version
                    && f.e.Info.Players >= 2))
            {
                r.Fail(
                    "the LAN tab didn't find the host (2 players, protocol "
                        + $"{Net.Protocol.Version}): {r.Info}");
            }
        }

        /// <summary>The other player's puppet (not the test dummy).</summary>
        private static RemotePlayer Other()
        {
            PlayerSync instance = Plugin.Instance.Host.Players;
            if (instance != null)
            {
                return instance.Remotes.Values.FirstOrDefault(
                    x => x.Owner != PlayerSync.DummyId);
            }

            return null;
        }
    }
}
