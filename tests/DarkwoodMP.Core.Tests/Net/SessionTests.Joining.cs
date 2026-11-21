using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.Net
{
    /// The client's Hello, and the host holding a joiner back while it gets
    /// its world (BeforeWelcome).
    public partial class SessionTests
    {
        /// A raw peer hosting, for reading what a client sends.
        private static RawPeer RawHost(LoopbackGame game)
        {
            var raw = game.Raw();
            raw.Backend.Host("", Protocol.MaxPlayers);
            return raw;
        }

        [Collection(SessionCollection.Name)]
        public class SendHello
        {
            [Fact]
            public void Writes_the_hello_bytes()
            {
                var game = new LoopbackGame();
                var host = RawHost(game);
                game.Join("Ann", "pw");

                game.Run();

                host.Got.ShouldHaveSingleItem().ShouldBe(Message(
                    MsgType.Hello,
                    U16(Protocol.Version),
                    Str("darkwood"),
                    Str(LoopbackGame.KeyOf("Ann")),
                    Str("pw"),
                    Str(LoopbackGame.ModVersion),
                    Str("Ann"),
                    new byte[32]));
            }

            [Fact]
            public void Sends_the_hash_of_the_world_it_has()
            {
                var game = new LoopbackGame();
                var host = RawHost(game);
                var hash = Enumerable.Range(1, 32).Select(i => (byte)i)
                    .ToArray();
                var client = game.Player("Ann");
                client.ClientWorldHash = () => hash;

                game.Backend(client).Join("");
                game.Run();

                host.Got[0].Skip(host.Got[0].Length - 32).ShouldBe(hash);
            }

            [Fact]
            public void Sends_zeros_for_a_hash_that_isnt_32_bytes()
            {
                var game = new LoopbackGame();
                var host = RawHost(game);
                var client = game.Player("Ann");
                client.ClientWorldHash = () => new byte[] { 1, 2, 3 };

                game.Backend(client).Join("");
                game.Run();

                host.Got[0].Skip(host.Got[0].Length - 32)
                    .ShouldBe(new byte[32]);
            }
        }

        [Collection(SessionCollection.Name)]
        public class BeforeWelcome
        {
            [Fact]
            public void Hands_over_the_clients_world_hash()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                byte[] got = null;
                host.BeforeWelcome = (id, hash) =>
                {
                    got = hash;
                    return true;
                };

                var hash = Enumerable.Repeat((byte)7, 32).ToArray();
                var client = game.Player("Ann");
                client.ClientWorldHash = () => hash;

                game.Backend(client).Join("");
                game.Run();

                got.ShouldBe(hash);
            }

            [Fact]
            public void Holds_back_the_welcome_when_it_says_no()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                host.BeforeWelcome = (id, hash) => false;
                var client = game.Join("Ann");

                game.Run();

                client.Connected.ShouldBeFalse();
            }

            [Fact]
            public void Tells_the_others_someone_is_joining()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                var ann = game.Join("Ann");
                game.Run();
                host.BeforeWelcome = (id, hash) => false;
                var joining = new List<string>();
                ann.OnPlayerJoining += joining.Add;

                game.Join("Bea");
                game.Run();

                joining.ShouldBe(new[] { "Bea" });
            }

            [Fact]
            public void Sends_the_join_notice_bytes()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                var raw = Admitted(game, "Raw");
                host.BeforeWelcome = (id, hash) => false;

                game.Join("Bea");
                game.Run();

                raw.Got.ShouldHaveSingleItem().ShouldBe(Message(
                    MsgType.JoinNotice, Str("Bea"), new byte[] { 0 }));
            }

            [Fact]
            public void Tells_the_others_when_a_joiner_is_refused()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                var ann = game.Join("Ann");
                game.Run();
                host.BeforeWelcome = (id, hash) =>
                {
                    host.Refuse(id, "No world for you.");
                    return false;
                };

                var stopped = new List<string>();
                ann.OnPlayerStoppedJoining += stopped.Add;

                game.Join("Bea");
                game.Run();

                stopped.ShouldBe(new[] { "Bea" });
            }

            [Fact]
            public void Tells_the_others_when_a_joiner_goes()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                var ann = game.Join("Ann");
                game.Run();
                host.BeforeWelcome = (id, hash) => false;
                var bea = game.Join("Bea");
                game.Run();
                var stopped = new List<string>();
                ann.OnPlayerStoppedJoining += stopped.Add;

                game.Backend(bea).Drop();
                game.Run();

                stopped.ShouldBe(new[] { "Bea" });
            }

            [Fact]
            public void Welcomes_the_joiner_once_it_says_yes()
            {
                var game = new LoopbackGame();
                var host = game.Host();
                bool ready = false;
                host.BeforeWelcome = (id, hash) => ready;
                var client = game.Join("Ann");
                game.Run();
                ready = true;

                client.SendHello();
                game.Run();

                client.Connected.ShouldBeTrue();
            }
        }

        [Collection(SessionCollection.Name)]
        public class Refused
        {
            [Fact]
            public void Keeps_the_reason_for_the_panel()
            {
                var game = new LoopbackGame();
                game.Host("secret", Protocol.MaxPlayers);
                var client = game.Join("Ann", "guess");

                game.Run();

                client.LastRefusal.ShouldBe("Wrong password.");
            }

            [Fact]
            public void Raises_OnRefused_with_the_reason()
            {
                var game = new LoopbackGame();
                game.Host("secret", Protocol.MaxPlayers);
                var client = game.Join("Ann", "guess");
                var refused = new List<string>();
                client.OnRefused += refused.Add;

                game.Run();

                refused.ShouldBe(new[] { "Wrong password." });
            }

            [Fact]
            public void Leaves_the_session()
            {
                var game = new LoopbackGame();
                game.Host("secret", Protocol.MaxPlayers);
                var client = game.Join("Ann", "guess");

                game.Run();

                client.Active.ShouldBeFalse();
            }

            [Fact]
            public void Leaves_the_host_with_no_one_in()
            {
                var game = new LoopbackGame();
                var host = game.Host("secret", Protocol.MaxPlayers);
                game.Join("Ann", "guess");

                game.Run();

                host.PlayerCount.ShouldBe(1);
            }
        }
    }
}
