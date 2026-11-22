using System;
using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.Net
{
    /// Messages while playing: relaying, which ones are handled, and
    /// players leaving.
    public partial class SessionTests
    {
        /// Counts the PlayerState messages a session handles, by sender.
        private static List<ulong> Hear(Session session)
        {
            var from = new List<ulong>();
            session.Register(MsgType.PlayerState, (id, r) => from.Add(id));
            return from;
        }

        /// Makes the host send each client's PlayerState on to the others.
        private static void Relay(Session host)
        {
            host.Register(MsgType.PlayerState, (from, r) =>
            {
                var w = host.Begin(MsgType.PlayerState);
                host.Broadcast(w, Channel.State, false, from);
            });
        }

        private static void SendState(Session client)
        {
            var w = client.Begin(MsgType.PlayerState);
            client.SendToHost(w, Channel.State, false);
        }

        [Collection(SessionCollection.Name)]
        public class Broadcast
        {
            [Fact]
            public void Relays_to_the_other_clients()
            {
                var game = new LoopbackGame();
                Relay(game.Host());
                var ann = game.Join("Ann");
                var bea = game.Join("Bea");
                var cid = game.Join("Cid");
                game.Run();
                var heardByBea = Hear(bea);
                var heardByCid = Hear(cid);

                SendState(ann);
                game.Run();

                new[] { heardByBea.Count, heardByCid.Count }
                    .ShouldBe(new[] { 1, 1 });
            }

            [Fact]
            public void Leaves_out_the_sender()
            {
                var game = new LoopbackGame();
                Relay(game.Host());
                var ann = game.Join("Ann");
                game.Join("Bea");
                game.Run();
                var heardByAnn = Hear(ann);

                SendState(ann);
                game.Run();

                heardByAnn.ShouldBeEmpty();
            }

            [Fact]
            public void Sends_nothing_from_a_client()
            {
                var game = new LoopbackGame();
                var heardByHost = Hear(game.Host());
                var ann = game.Join("Ann");
                game.Run();

                ann.Broadcast(
                    ann.Begin(MsgType.PlayerState), Channel.State, false);
                game.Run();

                heardByHost.ShouldBeEmpty();
            }
        }

        [Collection(SessionCollection.Name)]
        public class Dispatch
        {
            [Fact]
            public void Drops_a_message_from_someone_not_let_in()
            {
                var game = new LoopbackGame();
                var heardByHost = Hear(game.Host());
                var raw = Connected(game);

                raw.Send(new[] { (byte)MsgType.PlayerState });
                game.Run();

                heardByHost.ShouldBeEmpty();
            }

            [Fact]
            public void Lets_a_world_download_through_before_welcome()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                var asked = new List<ulong>();
                host.Register(MsgType.NeedSave, (id, r) => asked.Add(id));
                var raw = Connected(game);

                raw.Send(new[] { (byte)MsgType.NeedSave });
                game.Run();

                asked.ShouldBe(new[] { raw.Backend.Id });
            }

            [Fact]
            public void Drops_what_AllowBare_turns_down()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                var heardByHost = Hear(host);
                var ann = game.Join("Ann");
                game.Run();
                host.AllowBare = (id, type) => type != MsgType.PlayerState;

                SendState(ann);
                game.Run();

                heardByHost.ShouldBeEmpty();
            }

            [Fact]
            public void Logs_a_message_nothing_handles()
            {
                var game = new LoopbackGame();
                game.Host();
                var ann = game.Join("Ann");
                game.Run();

                SendState(ann);
                game.Run();

                game.Log.Lines.ShouldContain(
                    $"warning: Unhandled message PlayerState from "
                        + $"#{ann.LocalId}");
            }

            [Fact]
            public void Logs_a_handler_that_throws()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                host.Register(MsgType.PlayerState, (id, r) =>
                    throw new InvalidOperationException("boom"));
                var ann = game.Join("Ann");
                game.Run();

                SendState(ann);
                game.Run();

                game.Log.Lines.ShouldContain(line => line.StartsWith(
                    $"error: Error handling PlayerState from #{ann.LocalId}"));
            }

            [Fact]
            public void Ignores_player_info_from_a_client()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                var raw = Admitted(game, "Raw");
                var w = new PacketWriter(new RecordingLog());
                w.Begin(MsgType.PlayerInfo).W.Write(77UL);
                w.WriteStr("Fake");

                raw.Send(w);
                game.Run();

                host.Names.ContainsKey(77).ShouldBeFalse();
            }
        }

        [Collection(SessionCollection.Name)]
        public class Handle
        {
            [Fact]
            public void Handles_a_message_as_if_it_came_from_that_peer()
            {
                var session = new Session(
                    new FakeClock(), new RecordingLog(), "1.2.3");
                var heard = Hear(session);
                var bytes = new[] { (byte)MsgType.PlayerState };

                session.Handle(42, bytes, bytes.Length);

                heard.ShouldBe(new[] { 42UL });
            }
        }

        [Collection(SessionCollection.Name)]
        public class PeerLost
        {
            [Fact]
            public void Tells_the_others_who_left()
            {
                var game = new LoopbackGame();
                game.Host();
                var ann = game.Join("Ann");
                var bea = game.Join("Bea");
                game.Run();
                var left = new List<string>();
                bea.OnPlayerLeft += left.Add;

                game.Backend(ann).Leave();
                game.Run();

                left.ShouldBe(new[] { "Ann" });
            }

            [Fact]
            public void Sends_the_player_left_bytes()
            {
                var game = new LoopbackGame();
                game.Host();
                var ann = game.Join("Ann");
                game.Run();
                var raw = Admitted(game, "Raw");

                game.Backend(ann).Drop();
                game.Run();

                raw.Got.ShouldHaveSingleItem().ShouldBe(
                    Message(MsgType.PlayerLeft, U64(ann.LocalId)));
            }

            [Fact]
            public void Forgets_the_players_name_on_the_host()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                var ann = game.Join("Ann");
                game.Run();
                ulong id = ann.LocalId;

                game.Backend(ann).Leave();
                game.Run();

                host.Names.ContainsKey(id).ShouldBeFalse();
            }

            [Fact]
            public void Raises_OnPeerDisconnected_on_the_host()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                var ann = game.Join("Ann");
                game.Run();
                var gone = new List<ulong>();
                host.OnPeerDisconnected += gone.Add;

                game.Backend(ann).Leave();
                game.Run();

                gone.ShouldBe(new[] { game.Backend(ann).Id });
            }

            [Fact]
            public void Frees_the_place_for_another_player()
            {
                var game = new LoopbackGame();
                game.Host("", 2);
                var ann = game.Join("Ann");
                game.Run();
                game.Backend(ann).Leave();
                game.Run();
                var bea = game.Join("Bea");

                game.Run();

                bea.Connected.ShouldBeTrue();
            }
        }
    }
}
