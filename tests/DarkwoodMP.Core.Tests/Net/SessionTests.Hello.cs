using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.Net
{
    /// The host's side of the handshake: a Hello let in or refused.
    public partial class SessionTests
    {
        [Collection(SessionCollection.Name)]
        public class Hello
        {
            [Fact]
            public void Welcomes_a_client()
            {
                var game = new LoopbackGame();
                game.Host();
                var client = game.Join("Ann");

                game.Run();

                client.Connected.ShouldBeTrue();
            }

            [Fact]
            public void Gives_the_client_the_id_the_host_knows_it_by()
            {
                var game = new LoopbackGame();
                game.Host();
                var client = game.Join("Ann");

                game.Run();

                client.LocalId.ShouldBe(game.Backend(client).Id);
            }

            [Fact]
            public void Answers_with_welcome_and_the_clients_id()
            {
                var game = new LoopbackGame();
                game.Host();
                var raw = Connected(game);

                raw.Send(Valid.Bytes());
                game.Run();

                raw.Got[0].ShouldBe(
                    Message(MsgType.Welcome, U64(raw.Backend.Id)));
            }

            [Fact]
            public void Sends_the_newcomer_a_player_info_for_the_host()
            {
                var game = new LoopbackGame();
                game.Host();
                var raw = Connected(game);

                raw.Send(Valid.Bytes());
                game.Run();

                raw.Got[1].ShouldBe(
                    Message(MsgType.PlayerInfo, U64(1), Str("Host")));
            }

            [Fact]
            public void Tells_the_newcomer_about_everyone_already_in()
            {
                var game = new LoopbackGame();
                game.Host();
                var ann = game.Join("Ann");
                game.Run();
                var bea = game.Join("Bea");

                game.Run();

                bea.NameOf(ann.LocalId).ShouldBe("Ann");
            }

            [Fact]
            public void Tells_the_others_about_the_newcomer()
            {
                var game = new LoopbackGame();
                game.Host();
                var ann = game.Join("Ann");
                game.Run();
                var bea = game.Join("Bea");

                game.Run();

                ann.NameOf(bea.LocalId).ShouldBe("Bea");
            }

            [Fact]
            public void Tells_the_host_a_player_joined()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                var joined = new List<string>();
                host.OnPlayerJoined += joined.Add;
                game.Join("Ann");

                game.Run();

                joined.ShouldBe(new[] { "Ann" });
            }

            [Fact]
            public void Refuses_another_game()
            {
                var game = new LoopbackGame();
                game.Host();
                var raw = Connected(game);

                raw.Send((Valid with { Game = "other" }).Bytes());
                game.Run();

                RefusalOf(raw).ShouldBe("This server runs a different game.");
            }

            [Fact]
            public void Refuses_another_protocol_version()
            {
                var game = new LoopbackGame();
                game.Host();
                var raw = Connected(game);
                var old = Valid with { Version = Protocol.Version - 1 };

                raw.Send(old.Bytes());
                game.Run();

                RefusalOf(raw).ShouldBe(
                    "The host has DarkwoodMP 1.2.3, you have 0.1.0. Use the "
                        + "same version.");
            }

            [Fact]
            public void Checks_the_game_before_the_version()
            {
                var game = new LoopbackGame();
                game.Host();
                var raw = Connected(game);
                var other = Valid with { Game = "other", Version = 1 };

                raw.Send(other.Bytes());
                game.Run();

                RefusalOf(raw).ShouldBe("This server runs a different game.");
            }

            [Fact]
            public void Refuses_a_hello_without_the_password()
            {
                var game = new LoopbackGame();
                game.Host("secret", Protocol.MaxPlayers);
                var raw = Connected(game);

                raw.Send(Valid.Bytes());
                game.Run();

                RefusalOf(raw).ShouldBe("This server needs a password.");
            }

            [Fact]
            public void Refuses_a_wrong_password()
            {
                var game = new LoopbackGame();
                game.Host("secret", Protocol.MaxPlayers);
                var raw = Connected(game);

                raw.Send((Valid with { Password = "guess" }).Bytes());
                game.Run();

                RefusalOf(raw).ShouldBe("Wrong password.");
            }

            [Fact]
            public void Lets_in_a_client_with_the_password()
            {
                var game = new LoopbackGame();
                game.Host("secret", Protocol.MaxPlayers);
                var client = game.Join("Ann", "secret");

                game.Run();

                client.Connected.ShouldBeTrue();
            }

            [Fact]
            public void Refuses_a_player_key_under_32_characters()
            {
                var game = new LoopbackGame();
                game.Host();
                var raw = Connected(game);
                var key = new string('k', 31);

                raw.Send((Valid with { Key = key }).Bytes());
                game.Run();

                RefusalOf(raw).ShouldBe("Bad player key.");
            }

            [Fact]
            public void Refuses_the_hosts_own_player_key()
            {
                var game = new LoopbackGame();
                game.Host();
                var raw = Connected(game);
                var key = LoopbackGame.KeyOf("Host");

                raw.Send((Valid with { Key = key }).Bytes());
                game.Run();

                RefusalOf(raw).ShouldBe(
                    "You're already playing on this server.");
            }

            [Fact]
            public void Refuses_a_player_past_the_most_players()
            {
                var game = new LoopbackGame();
                game.Host("", 2);
                game.Join("Ann");
                game.Run();
                var bea = game.Join("Bea");

                game.Run();

                bea.LastRefusal.ShouldBe("The server is full.");
            }

            [Fact]
            public void Refuses_a_message_too_short_for_a_hello()
            {
                var game = new LoopbackGame();
                game.Host();
                var raw = Connected(game);

                raw.Send(new byte[] { (byte)MsgType.Hello, 29, 0 });
                game.Run();

                RefusalOf(raw).ShouldBe(
                    "This isn't a DarkwoodMP client, or it's a very "
                        + "different version.");
            }

            [Fact]
            public void Answers_a_refusal_with_refused_and_the_reason()
            {
                var game = new LoopbackGame();
                game.Host();
                var raw = Connected(game);

                raw.Send((Valid with { Key = "short" }).Bytes());
                game.Run();

                raw.Got.ShouldHaveSingleItem().ShouldBe(
                    Message(MsgType.Refused, Str("Bad player key.")));
            }

            [Fact]
            public void Closes_the_connection_after_refusing()
            {
                var game = new LoopbackGame();
                game.Host();
                var raw = Connected(game);

                raw.Send((Valid with { Game = "other" }).Bytes());
                game.Run();

                raw.Backend.InSession.ShouldBeFalse();
            }

            [Fact]
            public void Cuts_a_long_name_to_32_characters()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                var raw = Connected(game);
                var name = "  " + new string('n', 40) + "  ";

                raw.Send((Valid with { Name = name }).Bytes());
                game.Run();

                host.NameOf(raw.Backend.Id).ShouldBe(new string('n', 32));
            }

            [Fact]
            public void Names_a_player_with_a_blank_name_Player()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                var raw = Connected(game);

                raw.Send((Valid with { Name = "   " }).Bytes());
                game.Run();

                host.NameOf(raw.Backend.Id).ShouldBe("Player");
            }

            [Fact]
            public void Drops_the_old_connection_of_a_returning_player()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                Admitted(game, "Raw");
                var again = Connected(game);

                again.Send(Valid.Bytes());
                game.Run();

                host.Peers.ShouldBe(new[] { again.Backend.Id });
            }

            [Fact]
            public void Closes_the_old_connection_of_a_returning_player()
            {
                var game = new LoopbackGame();
                game.Host();
                var old = Admitted(game, "Raw");
                var again = Connected(game);

                again.Send(Valid.Bytes());
                game.Run();

                old.Backend.InSession.ShouldBeFalse();
            }

            [Fact]
            public void Joins_anew_a_player_who_says_hello_again()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                var raw = Admitted(game, "Raw");
                var left = new List<string>();
                host.OnPlayerLeft += left.Add;

                raw.Send(Valid.Bytes());
                game.Run();

                left.ShouldBe(new[] { "Raw" });
            }

            [Fact]
            public void Keeps_a_player_who_says_hello_again()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                var raw = Admitted(game, "Raw");

                raw.Send(Valid.Bytes());
                game.Run();

                host.Peers.ShouldBe(new[] { raw.Backend.Id });
            }
        }
    }
}
