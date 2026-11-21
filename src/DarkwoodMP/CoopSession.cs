using System;
using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World;
using UnityEngine.SceneManagement;

namespace DarkwoodMP
{
    /// <summary>
    /// One co-op session, from the moment a backend enters it (before its
    /// first message can arrive) to its end. It owns the subsystems as
    /// instances, and the current WorldScope. ModHost makes it and Coop
    /// points to it; the next session starts from new objects, so nothing
    /// has to be reset by hand.
    /// </summary>
    internal sealed class CoopSession : IDisposable
    {
        public readonly Session Net;

        // ---- subsystems ----------------------------------------------------
        // A subsystem that moves here gets a readonly field, made in the
        // constructor. It takes this CoopSession and hooks into the network
        // through Handle and Hook below, so Dispose undoes it all.

        /// The guards every shared world call goes through (depth, replays,
        /// whether the world is shared).
        public readonly World.Guards Guards;

        /// The host's check on where each client's player says it is.
        public readonly Sync.MoveCheck Moves;

        /// The host's say over what a client's player does to the world.
        public readonly World.Authority Authority;

        /// The tables ObjRef names objects by.
        public readonly ObjNames Names;

        /// Ids the host gives to what the world made by itself.
        public readonly HostIds HostIds;

        /// Host: the save id counters of the joiners let in to the world.
        public readonly JoinerSaveIds Joiners;

        /// The infection's patches: clears and the host's spread.
        public readonly World.InfectionSync Infections;

        private readonly List<(MsgType type, Action<ulong, PacketReader> run)>
            _handlers = new List<(MsgType, Action<ulong, PacketReader>)>();
        private readonly List<Action> _unhooks = new List<Action>();

        public CoopSession(Session net)
        {
            Net = net;
            // Subsystems are made here, before the first message can arrive.
            Guards = new World.Guards(this);
            Moves = new Sync.MoveCheck(this);
            Authority = new World.Authority(this);
            Names = new ObjNames(this);
            HostIds = new HostIds(this);
            Joiners = new JoinerSaveIds();
            Infections = new World.InfectionSync(this);
            // Hosting from the pause menu starts in a loaded world: give it
            // its WorldScope at once.
            WatchWorld();
        }

        /// The loaded world's state; null while no world is loaded (a
        /// client still joining, or the main menu). Read it each time: a
        /// world load replaces it.
        public WorldScope World { get; private set; }

        /// Handle a message type for as long as this session runs.
        public void Handle(MsgType type, Action<ulong, PacketReader> run)
        {
            Net.Register(type, run);
            _handlers.Add((type, run));
        }

        /// Undo a subscription when this session ends, e.g.
        /// <c>Hook(() => Net.OnPeerConnected -= OnPeer)</c> right after
        /// <c>Net.OnPeerConnected += OnPeer</c>. Run newest first.
        public void Hook(Action unhook)
        {
            _unhooks.Add(unhook);
        }

        /// <summary>
        /// Plugin.Update, each frame before the session's messages are
        /// handled: a new WorldScope when a world has loaded (a new player
        /// or a new scene), none once the world is left.
        /// </summary>
        public void WatchWorld()
        {
            Player player = null;
            if (WorldShare.InWorld)
            {
                player = Player.Instance;
            }

            Scene scene = SceneManager.GetActiveScene();
            WorldScope world = World;
            if (world != null && world.Is(player, scene))
            {
                return;
            }

            EndWorld();
            if (player != null)
            {
                World = new WorldScope(player, scene);
            }
        }

        private void EndWorld()
        {
            WorldScope world = World;
            if (world == null)
            {
                return;
            }

            World = null;
            world.Dispose();
        }

        public void Dispose()
        {
            EndWorld();
            for (int i = _unhooks.Count - 1; i >= 0; i--)
            {
                _unhooks[i]();
            }

            _unhooks.Clear();
            foreach (var (type, run) in _handlers)
            {
                Net.Unregister(type, run);
            }

            _handlers.Clear();
        }
    }
}
