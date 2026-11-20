using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DarkwoodMP.Combat;
using DarkwoodMP.Sync;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for a joiner waiting out the host's prologue, the LAN search, the crouch while disarming,
    /// and watching the others after dying for good.
    /// The list is tools/tests/map_scenario_status.py.
    /// </summary>
    public static class MapScenarioStatus
    {
        private static Player P => Player.Instance;

        public static void Register()
        {
            // "hold-joiners SECS" (host, before the join): answer joiners as if watching the prologue's intro for SECS from the first.
            Add("hold-joiners", (a, r) =>
            {
                if (a.Length > 0)
                {
                    WorldShare.TestHoldSecs = F(a[0]);
                }
                else
                {
                    WorldShare.TestHoldSecs = 15f;
                }
                r.Info = $"joiners wait {WorldShare.TestHoldSecs:0} s from the first";
                return Nothing();
            });
            Add("join-waited", JoinWaited);
            // "join-stop" (host, after the join): how long the snapshot for the joiner stopped the host's game.
            Add("join-stop", (a, r) =>
            {
                string text = "the game's own save";
                if (WorldShare.LastSnapshotOffThread)
                {
                    text = "sav.dat written on a worker thread";
                }
                r.Info = $"snapshot stopped the game for {WorldShare.LastSnapshotStopMs} ms ({text}), longest host frame while joining {WorldShare.LastJoinLongestFrameMs:0} ms";
                if (WorldShare.LastSnapshotStopMs < 0)
                {
                    r.Fail("no snapshot was made for the joiner");
                }
                else if (!WorldShare.LastSnapshotOffThread)
                {
                    r.Fail("the snapshot was saved the game's way, not on a worker thread: " + r.Info);
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
        /// "story-at-join" (host, before the join): once the joiner's snapshot is read, while the joiner downloads and
        /// loads it, change the story (the first NPC's reputation +1, a story flag set) as a player could meanwhile.
        /// </summary>
        private static IEnumerator StoryAtJoin(string[] a, Result r)
        {
            int noted = FlagsSync.SnapshotNoted;
            Singleton<Controller>.Instance.StartCoroutine(ChangeStoryAfter(noted));
            r.Info = "the story changes once the next snapshot is read";
            yield break;
        }

        private static IEnumerator ChangeStoryAfter(int noted)
        {
            float until = Time.realtimeSinceStartup + 120f;
            while (FlagsSync.SnapshotNoted == noted && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            if (FlagsSync.SnapshotNoted == noted)
            {
                Plugin.Log.LogWarning("[map] story-at-join: no snapshot was made");
                yield break;
            }
            yield return null; // after the frame the world was read in
            var f = Singleton<Flags>.Instance;
            var npc = f.npcStates.FirstOrDefault(n => n != null);
            if (npc != null)
            {
                npc.reputation += 1;
            }
            f.setFlag("player_firstOvenInteraction", activeModifier: true); // a shared one, not read by the game's code
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
            Plugin.Log.LogInfo($"[map] story-at-join: after the snapshot, {name}'s reputation {reputation}, player_firstOvenInteraction set");
        }

        /// <summary>"join-story" (host, after the join): the story changes made while the joiner loaded were sent to it.</summary>
        private static IEnumerator JoinStory(string[] a, Result r)
        {
            var lines = MapScenarioIssues.LogLines();
            string changed = lines.LastOrDefault(l => l.Contains("[map] story-at-join: after the snapshot"));
            string sent = lines.LastOrDefault(l => l.Contains("story changes since their snapshot sent to them"));
            if (changed == null)
            {
                r.Fail("the story wasn't changed during the join (story-at-join)");
            }
            else if (sent == null)
            {
                r.Fail("no 'story changes since their snapshot sent to them' in the log");
            }
            else
            {
                r.Info = sent.Substring(sent.IndexOf("] ") + 2);
            }
            yield break;
        }

        /// <summary>"join-waited" (client): the log has the host's prologue wait, then our world came in.</summary>
        private static IEnumerator JoinWaited(string[] a, Result r)
        {
            var lines = MapScenarioIssues.LogLines();
            int wait = lines.FindIndex(l => l.Contains("Waiting to join: The host is watching the prologue"));
            int ready = lines.FindLastIndex(l => l.Contains("World ready"));
            if (wait < 0)
            {
                r.Fail("no \"Waiting to join: The host is watching the prologue\" in the log");
            }
            else if (ready < wait)
            {
                r.Fail("the world wasn't ready after the wait");
            }
            else
            {
                r.Info = $"waited ({lines.Skip(wait).Count(l => l.Contains("Sent Hello") || l.Contains("sending it again"))} Hellos), then joined";
            }
            yield break;
        }

        /// <summary>"lan-search" (client): the server browser's LAN tab finds the host (its info answer, our protocol).</summary>
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
            yield return null; // the browser's update hands over the last answers
            var found = b.List(Net.ServerBrowser.Tab.Lan).Select(addr => (addr, e: b.Get(addr))).ToList();
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
                    Net.ServerInfo info2 = f.e.Info;
                    int? players = null;
                    if (info2 != null)
                    {
                        players = info2.Players;
                    }
                    Net.ServerInfo info3 = f.e.Info;
                    int? maxPlayers = null;
                    if (info3 != null)
                    {
                        maxPlayers = info3.MaxPlayers;
                    }
                    Net.ServerInfo answer = f.e.Info;
                    int? proto = null;
                    if (answer != null)
                    {
                        proto = answer.Proto;
                    }
                    return $"{f.addr} \"{name}\" {players}/{maxPlayers} proto {proto} {f.e.PingMs} ms";
                }));
            }
            if (!found.Any(f => f.e.Info != null && f.e.Info.Proto == Net.Protocol.Version && f.e.Info.Players >= 2))
            {
                r.Fail($"the LAN tab didn't find the host (2 players, protocol {Net.Protocol.Version}): {r.Info}");
            }
        }

        private static Trigger FindTrap(string spec) =>
            MapScenarioWorld.Find<Trigger>(spec, t => t.active && !t.triggered && t.canDisarm && t.GetComponent<Item>() != null);

        /// <summary>The other player's puppet (not the test dummy).</summary>
        private static RemotePlayer Other()
        {
            PlayerSync instance = PlayerSync.Instance;
            if (instance != null)
            {
                return instance.Remotes.Values.FirstOrDefault(x => x.Owner != PlayerSync.DummyId);
            }
            return null;
        }

        private static string Clip(Player p)
        {
            if (p.torsoAnimator != null && p.torsoAnimator.CurrentClip != null)
            {
                return p.torsoAnimator.CurrentClip.name;
            }
            return "-";
        }

        /// <summary>"disarm-crouch TRAP[@DX,DZ]": start disarming it as the player does (Player.doDisarm), with the
        /// progress bar held, and check our player crouches (InventoryGet, then InventoryLoop).</summary>
        private static IEnumerator DisarmCrouch(string[] a, Result r)
        {
            var t = FindTrap(a[0]);
            if (t == null)
            {
                r.Fail($"no armed trap {a[0]}");
                yield break;
            }
            yield return Walk(t.transform.position, 90f);
            var item = t.GetComponent<Item>();
            AsPlayer(() => P.doDisarm(item));
            if (P.progressBar != null)
            {
                P.progressBar.time = 60f; // held until disarm-finish
            }
            float until = Time.realtimeSinceStartup + 3f;
            while (Clip(P) != "InventoryLoop" && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            r.Info = $"{t.name} at {Fmt(t.transform.position)} (the game's own crouch: {t.bendDownToDisarm}): clip {Clip(P)}, disarming {P.disarmingTrap}";
            if (!P.disarmingTrap)
            {
                r.Fail($"{r.Info}: not disarming");
            }
            else if (Clip(P) != "InventoryLoop")
            {
                r.Fail($"{r.Info}: not crouching");
            }
        }

        /// <summary>"puppet-crouch": the other player's puppet here shows the crouch (its sprites are frames of
        /// InventoryGet or InventoryLoop in the library it says it uses).</summary>
        private static IEnumerator PuppetCrouch(string[] a, Result r)
        {
            var rp = Other();
            if (rp == null)
            {
                r.Fail("no other player");
                yield break;
            }
            float until = Time.realtimeSinceStartup + 4f;
            string seen = "";
            while (Time.realtimeSinceStartup < until)
            {
                var last = rp.Last;
                if (last != null)
                {
                    tk2dSpriteAnimation lib = Resources.FindObjectsOfTypeAll<tk2dSpriteAnimation>().FirstOrDefault(l => l.name == last.Library);
                    if (lib == null)
                    {
                        lib = Resources.Load(last.Library, typeof(tk2dSpriteAnimation)) as tk2dSpriteAnimation;
                    }
                    var frames = new HashSet<(int, int)>();
                    if (lib != null)
                    {
                        foreach (var clip in new[] { "InventoryGet", "InventoryLoop" })
                        {
                            var c = lib.GetClipByName(clip);
                            if (c == null || c.frames == null)
                            {
                                continue;
                            }
                            foreach (var f in c.frames)
                            {
                                if (f.spriteCollection != null)
                                {
                                    frames.Add((Net.StableHash.Of(f.spriteCollection.spriteCollectionName), f.spriteId));
                                }
                            }
                        }
                    }
                    int match = last.Sprites.Count(s => frames.Contains((s.CollectionHash, s.SpriteId)));
                    string text = "not found here";
                    if (lib != null)
                    {
                        text = "found";
                    }
                    seen = $"library {last.Library} ({text}), {match} of {last.Sprites.Count} sprites from the crouch, shown {rp.Root != null && rp.Root.activeInHierarchy}";
                    if (match > 0 && rp.Root != null && rp.Root.activeInHierarchy)
                    {
                        r.Info = $"{rp.Name}: {seen}";
                        yield break;
                    }
                }
                yield return null;
            }
            r.Fail($"{rp.Name} isn't seen crouching: {seen}");
        }

        /// <summary>"disarm-finish TRAP[@DX,DZ]": let the held progress bar end; the trap is disarmed or sprung
        /// (the host's roll) and our player stands up again.</summary>
        private static IEnumerator DisarmFinish(string[] a, Result r)
        {
            var t = FindTrap(a[0]);
            string what;
            if (t != null)
            {
                what = $"{t.name} at {Fmt(t.transform.position)}";
            }
            else
            {
                what = a[0];
            }
            if (P.progressBar == null || !P.disarmingTrap)
            {
                r.Fail($"not disarming {what} (bar {P.progressBar != null})");
                yield break;
            }
            WorldActions.TestAsPlayer = true; // the bar ends in its own Update, as the player's action
            P.progressBar.time = 0.01f;
            float until = Time.realtimeSinceStartup + 4f;
            while (t != null && !t.triggered && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            WorldActions.TestAsPlayer = false;
            until = Time.realtimeSinceStartup + 4f;
            while ((P.gettingInventory || P.performingAction) && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            string text = "still armed";
            if (t == null)
            {
                text = "disarmed (gone)";
            }
            else if (t.triggered)
            {
                text = "sprung (the roll failed)";
            }
            r.Info = $"{what}: {text}; clip {Clip(P)}, performingAction {P.performingAction}, gettingInventory {P.gettingInventory}";
            if (t != null && !t.triggered)
            {
                r.Fail($"{r.Info}: nothing happened");
            }
            else if (P.gettingInventory || P.performingAction)
            {
                r.Fail($"{r.Info}: still crouched");
            }
            if (P.inBearTrap)
            {
                P.stopAllPerformingActionAnims();
            }
            yield return Walk(Centre, 60f);
        }

        /// <summary>"permadeath" (after the dumps): our player dies on Nightmare, which is for good; with the host
        /// still playing, we watch them instead of the permadeath screen.</summary>
        private static IEnumerator Permadeath(string[] a, Result r)
        {
            if (Core.currentProfile == null)
            {
                r.Fail("no profile");
                yield break;
            }
            var was = Core.currentProfile.difficulty;
            Core.currentProfile.difficulty = GameProfile.Difficulty.nightmare;
            P.die();
            float until = Time.realtimeSinceStartup + 15f;
            while (!Spectator.Active && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
            if (!Spectator.Active)
            {
                Core.currentProfile.difficulty = was;
                r.Fail($"not spectating (alive {P.alive}, permadeath box {Singleton<UI>.Instance.permadeathBox != null})");
                yield break;
            }
            yield return new WaitForSeconds(2f);
            var cam = Singleton<CamMain>.Instance;
            var host = Other();
            bool follows = cam != null && host != null && host.Root != null && cam.followTarget == host.Root.transform;
            float dist = -1f;
            if (host != null && host.Root != null)
            {
                dist = StateDump.Flat(P.transform.position - host.Root.transform.position);
            }
            bool touching = P.GetComponentsInChildren<Collider>().Any(c => c.enabled);
            string name = null;
            if (host != null)
            {
                name = host.Name;
            }
            r.Info = $"watching {name}: camera follows {follows}, our body {dist:0} from them, colliders on {touching}, profile dead {Core.currentProfile.dead}, time scale {Time.timeScale}";
            if (!follows || dist < 0f || dist > 50f || touching || !Core.currentProfile.dead || Time.timeScale == 0f)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"spectator-view" [DIR] (client, spectating): the view cone is open all around (both lights at 360
        /// degrees, currentFOV 370), and stays so after the watched player turns; with DIR, a screenshot
        /// DIR/spectator-view.png.</summary>
        private static IEnumerator SpectatorView(string[] a, Result r)
        {
            if (!Spectator.Active)
            {
                r.Fail("not spectating");
                yield break;
            }
            var first = Spectator.ConeAngles();
            yield return new WaitForSeconds(2f);
            var later = Spectator.ConeAngles();
            if (a.Length > 0)
            {
                Directory.CreateDirectory(a[0]);
                string path = Path.Combine(a[0], "spectator-view.png");
                ScreenCapture.CaptureScreenshot(path);
                yield return new WaitForSecondsRealtime(1f);
                Plugin.Log.LogInfo($"[shots] {path}");
            }
            r.Info = $"cone: light {first.light:0}/{later.light:0}, logic {first.logic:0}/{later.logic:0}, currentFOV {first.current:0}/{later.current:0} (2 s apart)";
            if (first.light < 360f || first.logic < 360f || first.current < 360f || later.light < 360f || later.logic < 360f || later.current < 360f)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"spectate-end" (client, spectating, last): spectating ends as it does when the world is replaced,
        /// and the view cone is back at the angles it had when it began (not 360).</summary>
        private static IEnumerator SpectateEnd(string[] a, Result r)
        {
            if (!Spectator.Active)
            {
                r.Fail("not spectating");
                yield break;
            }
            var saved = Spectator.SavedCone;
            Spectator.TestEnd();
            yield return null;
            var now = Spectator.ConeAngles();
            r.Info = $"saved light {saved.light:0}, logic {saved.logic:0}, currentFOV {saved.current:0}; now light {now.light:0}, logic {now.logic:0}, currentFOV {now.current:0}; spectating {Spectator.Active}";
            if (Spectator.Active || float.IsNaN(saved.light) || now.light != saved.light || now.logic != saved.logic || now.current != saved.current)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"sees-spectator" (host): the other player says it's spectating, isn't shown, and its proxy
        /// touches nothing; no movement corrections were sent for it.</summary>
        private static IEnumerator SeesSpectator(string[] a, Result r)
        {
            int from = MapScenarioIssues.LogMark();
            yield return new WaitForSeconds(3f);
            var rp = Other();
            if (rp == null)
            {
                r.Fail("no other player");
                yield break;
            }
            NetPlayerProxy.All.TryGetValue(rp.Owner, out var proxy);
            Collider col = null;
            if (proxy != null)
            {
                col = proxy.GetComponent<Collider>();
            }
            int corrections = MapScenarioIssues.LogLines().Skip(from).Count(l => l.Contains("put back"));
            bool spectating = rp.Spectating;
            bool shown = rp.Root != null && rp.Root.activeSelf;
            bool? alive = null;
            if (proxy != null)
            {
                alive = proxy.alive;
            }
            r.Info = $"{rp.Name}: spectating {spectating}, shown {shown}, proxy alive {alive}, collider on {col != null && col.enabled}, corrections {corrections}";
            if (!rp.Spectating || (rp.Root != null && rp.Root.activeSelf) || (col != null && col.enabled) || corrections > 0)
            {
                r.Fail(r.Info);
            }
        }
    }
}
