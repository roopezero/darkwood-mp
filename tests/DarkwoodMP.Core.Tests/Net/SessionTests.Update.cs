using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.Net
{
    /// Time while joining (a Hello sent again, a host gone quiet), and the
    /// session following its backend in and out.
    public partial class SessionTests
    {
        private const string Silence =
            "The host hasn't answered for 60 s, so joining stopped.";

        /// A client of a raw host that never answers; the host's list of
        /// what it got starts empty after the first Hello.
        private static (RawPeer host, Session client) Unanswered(
            LoopbackGame game)
        {
            var host = RawHost(game);
            var client = game.Join("Ann");
            game.Run();
            host.Got.Clear();
            return (host, client);
        }

        /// The raw host says something to its client (any message counts as
        /// an answer).
        private static void Answer(LoopbackGame game, RawPeer host)
        {
            var notice = new byte[] { (byte)MsgType.JoinNotice, 0, 0, 0 };
            foreach (ulong id in host.Backend.Links)
            {
                host.SendTo(id, notice);
            }

            game.Run();
        }

        [Collection(SessionCollection.Name)]
        public class Update
        {
            [Fact]
            public void Sends_hello_again_after_5_seconds_unanswered()
            {
                var game = new LoopbackGame();
                var (host, _) = Unanswered(game);

                game.Wait(5.1f);

                TypesOf(host).ShouldBe(new[] { MsgType.Hello });
            }

            [Fact]
            public void Waits_the_full_5_seconds_before_sending_hello_again()
            {
                var game = new LoopbackGame();
                var (host, _) = Unanswered(game);

                game.Wait(5f);

                host.Got.ShouldBeEmpty();
            }

            [Fact]
            public void Stops_sending_hello_once_the_host_answers()
            {
                var game = new LoopbackGame();
                var (host, _) = Unanswered(game);
                Answer(game, host);

                game.Wait(30f);

                host.Got.ShouldBeEmpty();
            }

            [Fact]
            public void Gives_up_joining_after_60_seconds_of_silence()
            {
                var game = new LoopbackGame();
                var (_, client) = Unanswered(game);
                var why = new List<string>();
                client.OnJoinTimedOut += why.Add;

                game.Wait(60.5f);

                why.ShouldBe(new[] { Silence });
            }

            [Fact]
            public void Leaves_after_giving_up()
            {
                var game = new LoopbackGame();
                var (_, client) = Unanswered(game);

                game.Wait(60.5f);

                client.Active.ShouldBeFalse();
            }

            [Fact]
            public void Waits_the_full_60_seconds_before_giving_up()
            {
                var game = new LoopbackGame();
                var (_, client) = Unanswered(game);

                game.Wait(60f);

                client.Active.ShouldBeTrue();
            }

            [Fact]
            public void Counts_the_silence_from_the_last_word_from_the_host()
            {
                var game = new LoopbackGame();
                var (host, client) = Unanswered(game);
                game.Wait(50f);
                Answer(game, host);

                game.Wait(50f);

                client.Active.ShouldBeTrue();
            }

            [Fact]
            public void Stops_the_silence_timer_once_welcomed()
            {
                var game = new LoopbackGame();
                game.Host();
                var client = game.Join("Ann");
                game.Run();

                game.Wait(600f);

                client.Connected.ShouldBeTrue();
            }
        }

        [Collection(SessionCollection.Name)]
        public class Add
        {
            [Fact]
            public void Tells_a_client_the_host_went_away()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                var client = game.Join("Ann");
                game.Run();
                var lost = new List<string>();
                client.OnHostLost += lost.Add;

                game.Backend(host).Leave();
                game.Run();

                lost.ShouldBe(new[]
                {
                    "Disconnected from host: connection closed",
                });
            }

            [Fact]
            public void Says_nothing_of_the_host_to_a_client_that_left()
            {
                var game = new LoopbackGame();
                game.Host();
                var client = game.Join("Ann");
                game.Run();
                var lost = new List<string>();
                client.OnHostLost += lost.Add;

                game.Backend(client).Leave();
                game.Run();

                lost.ShouldBeEmpty();
            }

            [Fact]
            public void Ends_the_session_when_the_backend_leaves()
            {
                var game = new LoopbackGame();
                game.Host();
                var client = game.Join("Ann");
                game.Run();
                int ended = 0;
                client.OnSessionEnded += () => ended++;

                game.Backend(client).Leave();

                ended.ShouldBe(1);
            }

            [Fact]
            public void Leaves_the_old_backend_when_another_starts_a_session()
            {
                var session = new Session(
                    new FakeClock(), new RecordingLog(), "1.2.3");
                var first = new LoopbackHub().NewBackend();
                var second = new LoopbackHub().NewBackend();
                session.Add(first);
                session.Add(second);
                first.Host("", Protocol.MaxPlayers);

                second.Host("", Protocol.MaxPlayers);

                first.InSession.ShouldBeFalse();
            }

            [Fact]
            public void Follows_the_backend_that_started_a_session()
            {
                var session = new Session(
                    new FakeClock(), new RecordingLog(), "1.2.3");
                var first = new LoopbackHub().NewBackend();
                var second = new LoopbackHub().NewBackend();
                session.Add(first);
                session.Add(second);
                first.Host("", Protocol.MaxPlayers);

                second.Host("", Protocol.MaxPlayers);

                session.Backend.ShouldBeSameAs(second);
            }
        }
    }
}
