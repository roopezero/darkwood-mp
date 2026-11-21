using DarkwoodMP.Net;
using DarkwoodMP.World.Stacks;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The session's part of StackSizes: the host sends its stack size
    /// (StackSize) to each player who joins and to everyone when it's
    /// changed, and a client uses it until the session ends, then its own
    /// setting again. One lives for each session, in CoopSession.Stacks.
    /// </summary>
    internal sealed class StackShare
    {
        private readonly CoopSession _coop;

        public StackShare(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.StackSize, OnStackSize);
            coop.Net.OnPeerConnected += OnPeer;
            coop.Hook(() => coop.Net.OnPeerConnected -= OnPeer);
            coop.Hook(Restore);
        }

        /// <summary>
        /// Client: the host's size; StackRules.NoHostSize until it comes.
        /// </summary>
        public int HostSize { get; private set; } = StackRules.NoHostSize;

        /// <summary>Host: send our size to everyone.</summary>
        public void SendAll(int size)
        {
            _coop.Net.Broadcast(Write(size), Channel.Control, reliable: true);
            Plugin.Log.LogInfo($"[stacks] sent {size} to everyone");
        }

        private void OnPeer(ulong id)
        {
            if (_coop.Net.IsHost)
            {
                _coop.Net.SendTo(
                    id, Write(StackSizes.Current), Channel.Control,
                    reliable: true);
            }
        }

        private PacketWriter Write(int size)
        {
            var w = _coop.Net.Begin(MsgType.StackSize);
            StackSize.Write(w, size);
            return w;
        }

        private void OnStackSize(ulong from, PacketReader r)
        {
            bool host = from == _coop.Net.HostId;
            if (!StackRules.TakesSize(_coop.Net.IsClient, host))
            {
                return;
            }

            int size = StackSize.Read(r);
            HostSize = size;
            Plugin.Log.LogInfo($"[stacks] the host's stack size: {size}");
            Plugin.Instance.Host.Stacks.Apply(size);
        }

        /// <summary>
        /// The session is over: a client that used the host's size uses its
        /// own setting again. Coop.Current is null by now, so
        /// StackSizes.Current is our own setting.
        /// </summary>
        private void Restore()
        {
            if (!StackRules.FollowsHost(HostSize))
            {
                return;
            }

            HostSize = StackRules.NoHostSize;
            int own = StackSizes.Current;
            Plugin.Log.LogInfo(
                $"[stacks] session over: our own setting again ({own})");
            Plugin.Instance.Host.Stacks.Apply(own);
        }
    }
}
