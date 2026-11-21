using System;
using System.Collections.Generic;
using DarkwoodMP.Net;

namespace DarkwoodMP.Tests.Net
{
    /// Session.Register, Unregister and OnSessionStarted: what CoopSession
    /// uses to hook into a session and to let go of it again.
    public class SessionRegisterTests
    {
        private const ulong Peer = 7;
        private const MsgType Info = MsgType.PlayerInfo;
        private const MsgType State = MsgType.PlayerState;

        private static Session NewSession()
        {
            return new Session(new StillClock(), new RecordingLog(), "1.0.0");
        }

        private static void Deliver(Session session, MsgType type)
        {
            var w = new PacketWriter(new RecordingLog());
            w.Begin(type);
            session.Handle(Peer, w.Buffer, w.Length);
        }

        public class Register
        {
            [Fact]
            public void Hands_the_message_to_the_handler()
            {
                var session = NewSession();
                var heard = new List<ulong>();
                session.Register(Info, (from, r) => heard.Add(from));

                Deliver(session, Info);

                heard.ShouldBe(new[] { Peer });
            }

            [Fact]
            public void Replaces_the_handler_of_the_same_type()
            {
                var session = NewSession();
                var heard = new List<string>();
                session.Register(Info, (from, r) => heard.Add("old"));
                session.Register(Info, (from, r) => heard.Add("new"));

                Deliver(session, Info);

                heard.ShouldBe(new[] { "new" });
            }
        }

        public class Unregister
        {
            [Fact]
            public void Stops_the_handler_hearing_its_type()
            {
                var session = NewSession();
                var heard = new List<ulong>();
                Action<ulong, PacketReader> handler =
                    (from, r) => heard.Add(from);
                session.Register(Info, handler);

                session.Unregister(Info, handler);
                Deliver(session, Info);

                heard.ShouldBeEmpty();
            }

            [Fact]
            public void Removes_a_method_registered_as_a_new_delegate()
            {
                var session = NewSession();
                var listener = new Listener();
                session.Register(Info, listener.Heard);

                session.Unregister(Info, listener.Heard);
                Deliver(session, Info);

                listener.Count.ShouldBe(0);
            }

            [Fact]
            public void Keeps_a_handler_that_replaced_it()
            {
                var session = NewSession();
                var heard = new List<string>();
                Action<ulong, PacketReader> old =
                    (from, r) => heard.Add("old");
                session.Register(Info, old);
                session.Register(Info, (from, r) => heard.Add("new"));

                session.Unregister(Info, old);
                Deliver(session, Info);

                heard.ShouldBe(new[] { "new" });
            }

            [Fact]
            public void Leaves_other_types_alone()
            {
                var session = NewSession();
                var heard = new List<MsgType>();
                Action<ulong, PacketReader> chat =
                    (from, r) => heard.Add(Info);
                session.Register(Info, chat);
                session.Register(
                    State,
                    (from, r) => heard.Add(State));

                session.Unregister(Info, chat);
                Deliver(session, State);

                heard.ShouldBe(new[] { State });
            }

            [Fact]
            public void Does_nothing_for_a_type_with_no_handler()
            {
                var session = NewSession();
                var listener = new Listener();

                Should.NotThrow(
                    () => session.Unregister(
                        Info, listener.Heard));
            }
        }

        public class OnSessionStarted
        {
            [Fact]
            public void Fires_when_a_backend_enters_a_session()
            {
                var session = NewSession();
                var backend = new StubBackend();
                session.Add(backend);
                int started = 0;
                session.OnSessionStarted += () => started++;

                backend.Enter();

                started.ShouldBe(1);
            }

            [Fact]
            public void Fires_on_a_client_before_its_Hello_is_sent()
            {
                var session = NewSession();
                var backend = new StubBackend();
                session.Add(backend);
                int sentBefore = -1;
                session.OnSessionStarted += () => sentBefore = backend.Sent;

                backend.Enter();

                sentBefore.ShouldBe(0);
                backend.Sent.ShouldBe(1);
            }
        }

        private sealed class Listener
        {
            public int Count { get; private set; }

            public void Heard(ulong from, PacketReader r)
            {
                Count++;
            }
        }

        private sealed class StillClock : IClock
        {
            public float RealTime => 0f;

            public float UnscaledTime => 0f;
        }

        /// A client backend that enters when told to and counts what it
        /// sends.
        private sealed class StubBackend : INetBackend
        {
            public string Name => "stub";

            public string Status => "";

            public bool InSession { get; private set; }

            public bool IsHost => false;

            public ulong HostId => 1;

            public ulong LocalId => 2;

            public string HostPassword => "";

            public string JoinPassword => "";

            public int MaxPlayers => 5;

            public bool HostLost => false;

            public int Sent { get; private set; }

            public event Action OnEntered;
#pragma warning disable CS0067 // the session subscribes; the stub never raises
            public event Action OnLeft;
            public event Action<ulong> OnPeerLost;
            public event Action<ulong, byte[], int> OnMessage;
#pragma warning restore CS0067

            public void Enter()
            {
                InSession = true;
                OnEntered.Invoke();
            }

            public bool Send(
                ulong to, byte[] data, int length, int channel, bool reliable)
            {
                Sent++;
                return true;
            }

            public void Kick(ulong peer)
            {
            }

            public void Poll()
            {
            }

            public void Leave()
            {
                InSession = false;
            }

            public void Rejoin()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
