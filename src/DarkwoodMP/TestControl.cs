using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP
{
    /// <summary>
    /// "--dwmp-test-control PORT": one game runs a whole test run, and tools/run-test.sh tells it what to do before
    /// each test, over a TCP connection on 127.0.0.1 (never from another machine). One command per line, its
    /// words separated by tabs; each gets one line back.
    ///
    ///   title TEXT        show TEXT at the top of the screen ("Test (3/15): host-world - ...")      -> ok
    ///   reset             stop the last test, leave its session and world, wait for the main menu   -> ready
    ///   args ARG...       apply --dwmp-* arguments, as on the command line (host, join, test hooks)  -> ok
    ///   quit              close the game                                                           -> ok
    ///   do STEP ARG...    run a scenario step (World/MapScenario.cs) to its end              -> done [INFO] / fail REASON
    ///   dump PATH [X Z R] write the shared world's state as JSON (World/StateDump.cs), around X Z within R -> ok N entries
    ///   wait EVENT SECS [N]  world-ready (our world loaded and settled), player-joined (host: a player, or N
    ///                     players, in and out of loading)                                     -> ok / fail timeout
    /// </summary>
    public static class TestControl
    {
        public static string Title { get; private set; }

        private static Plugin _plugin;
        private static readonly ConcurrentQueue<(string line, StreamWriter reply)> _commands = new ConcurrentQueue<(string, StreamWriter)>();
        private static bool _busy;

        public static void Start(Plugin plugin, int port)
        {
            _plugin = plugin;
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            Plugin.Log.LogInfo($"Test control on 127.0.0.1:{port}");
            new Thread(() =>
            {
                while (true)
                {
                    TcpClient c;
                    try { c = listener.AcceptTcpClient(); }
                    catch { return; }
                    new Thread(() => Serve(c)) { IsBackground = true, Name = "DarkwoodMP test control" }.Start();
                }
            }) { IsBackground = true, Name = "DarkwoodMP test control" }.Start();
        }

        private static void Serve(TcpClient c)
        {
            try
            {
                using (c)
                using (var s = c.GetStream())
                using (var reader = new StreamReader(s, Encoding.UTF8))
                using (var writer = new StreamWriter(s, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var done = new ManualResetEventSlim();
                        var w = new ReplyWriter(writer, done);
                        _commands.Enqueue((line, w));
                        done.Wait();
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Test control: {e.Message}"); }
        }

        /// <summary>Replies from the main thread, then lets the connection's thread read the next command.</summary>
        private sealed class ReplyWriter : StreamWriter
        {
            private readonly StreamWriter _to;
            private readonly ManualResetEventSlim _done;
            public ReplyWriter(StreamWriter to, ManualResetEventSlim done) : base(Stream.Null) { _to = to; _done = done; }
            public override void WriteLine(string value) { try { _to.WriteLine(value); } catch { } _done.Set(); }
        }

        /// <summary>Called every frame (Plugin.Update): runs the commands that came in, one at a time.</summary>
        public static void Update()
        {
            if (_busy || !_commands.TryDequeue(out var cmd)) return;
            var words = cmd.line.Split('\t');
            Plugin.Log.LogInfo($"[test control] {cmd.line.Replace('\t', ' ')}");
            switch (words[0])
            {
                case "title":
                    Title = words.Length > 1 ? string.Join(" ", words, 1, words.Length - 1) : null;
                    cmd.reply.WriteLine("ok");
                    break;
                case "reset":
                    _busy = true;
                    _plugin.StartCoroutine(Reset(cmd.reply));
                    break;
                case "args":
                    var args = new string[words.Length - 1];
                    Array.Copy(words, 1, args, 0, args.Length);
                    _plugin.ApplyTestArgs(args);
                    cmd.reply.WriteLine("ok");
                    break;
                case "do":
                    if (words.Length < 2) { cmd.reply.WriteLine("fail usage: do STEP ARG..."); break; }
                    _busy = true;
                    var stepArgs = new string[words.Length - 2];
                    Array.Copy(words, 2, stepArgs, 0, stepArgs.Length);
                    var doReply = cmd.reply;
                    _plugin.StartCoroutine(MapScenario.Run(words[1], stepArgs, line => { _busy = false; doReply.WriteLine(line); }));
                    break;
                case "dump":
                    try
                    {
                        if (words.Length < 2) throw new ArgumentException("usage: dump PATH [X Z R]");
                        Vector3? at = null; float radius = StateDump.DefaultRadius;
                        if (words.Length >= 4) at = new Vector3(F(words[2]), 0f, F(words[3]));
                        if (words.Length >= 5) radius = F(words[4]);
                        cmd.reply.WriteLine("ok " + StateDump.Write(words[1], at, radius));
                    }
                    catch (Exception e) { Plugin.Log.LogWarning($"[test control] dump: {e}"); cmd.reply.WriteLine($"fail {e.GetType().Name}: {e.Message}"); }
                    break;
                case "wait":
                    _busy = true;
                    _plugin.StartCoroutine(Wait(words.Length > 1 ? words[1] : "", words.Length > 2 ? F(words[2]) : 60f,
                        words.Length > 3 && int.TryParse(words[3], out int n) ? n : 1, cmd.reply));
                    break;
                case "quit":
                    cmd.reply.WriteLine("ok");
                    Application.Quit();
                    break;
                default:
                    cmd.reply.WriteLine($"error: unknown command {words[0]}");
                    break;
            }
        }

        private static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Wait for something to happen in this game ("wait EVENT SECS").</summary>
        private static IEnumerator Wait(string what, float secs, int count, StreamWriter reply)
        {
            Func<bool> done;
            switch (what)
            {
                case "world-ready": done = () => WorldShare.WorldReady; break;
                case "player-joined": done = () => PlayersIn() >= count; break;
                default: _busy = false; reply.WriteLine($"fail unknown event {what} (world-ready, player-joined)"); yield break;
            }
            float until = Time.realtimeSinceStartup + secs;
            while (!done() && Time.realtimeSinceStartup < until) yield return null;
            _busy = false;
            reply.WriteLine(done() ? "ok" : "fail timeout");
        }

        /// <summary>Host: how many players have joined and are out of loading (their PlayerState no longer comes from
        /// the loading spot at x = 75000, and they aren't away).</summary>
        private static int PlayersIn()
        {
            var s = Net.Session.Instance;
            if (s == null || !s.IsHost || s.Peers.Count == 0 || !WorldShare.WorldReady) return 0;
            var ps = Sync.PlayerSync.Instance;
            if (ps == null) return 0;
            int n = 0;
            foreach (var kv in ps.Remotes)
            {
                if (kv.Key == Sync.PlayerSync.DummyId || !s.Peers.Contains(kv.Key)) continue;
                var last = kv.Value.Last;
                if (last != null && Mathf.Abs(last.Position.x) < 50000f && !last.Away) n++;
            }
            return n;
        }

        /// <summary>Back to a clean main menu: the last test stopped, its session left, its world closed.</summary>
        private static IEnumerator Reset(StreamWriter reply)
        {
            _plugin.StopTests();
            // A world still loading is let finish first: leaving in the middle of it isn't something the game does.
            float until = Time.realtimeSinceStartup + 90f;
            while (!WorldShare.InWorld && !WorldShare.MainMenuReady() && Time.realtimeSinceStartup < until) yield return null;
            if (WorldShare.InWorld)
            {
                if (Core.mainMenu) Singleton<MainMenu>.Instance.close(); // the pause menu
                Core.returnToMainMenu();
                yield return null;
            }
            until = Time.realtimeSinceStartup + 90f;
            while ((!WorldShare.MainMenuReady() || SaveRedirect.Active) && Time.realtimeSinceStartup < until) yield return null;
            Plugin.Instance.SetPanel(false);
            _busy = false;
            reply.WriteLine(WorldShare.MainMenuReady() ? "ready" : "error: no main menu after 90 s");
        }

        /// <summary>Called from Plugin.OnGUI: the test's title along the top of the screen.</summary>
        public static void OnGUI()
        {
            if (string.IsNullOrEmpty(Title)) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                _style.normal.textColor = Color.white;
                _back = new Texture2D(1, 1);
                _back.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.75f));
                _back.Apply();
            }
            var r = new Rect(0, 0, Screen.width, 44);
            GUI.DrawTexture(r, _back);
            GUI.Label(r, Title, _style);
        }

        private static GUIStyle _style;
        private static Texture2D _back;
    }
}
