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

        /// Building and repairing, and a joiner's builds waiting for the
        /// host's answer.
        public readonly Construction Construction;

        /// The host's stack size, sent to the clients.
        public readonly StackShare Stacks;

        /// The host's clock, which every client's follows.
        public readonly TimeSync Time;

        /// Where the NPCs stand: the host sends its own, clients follow.
        public readonly NpcSync Npcs;

        /// Host: which joiner each recruited follower follows.
        public readonly NpcFollowers Followers;

        /// One player at a time talks to an NPC.
        public readonly World.Talk.TalkLock Talk;

        /// Keeps a dialogue closable when its NPC goes away.
        public readonly World.Talk.DialogueGuard Dialogues;

        /// Things dragged and pushed, shared.
        public readonly DragSync Drags;

        /// Redraws an open window after another player's change to it.
        public readonly UiRefresh Ui;

        /// What traders have for sale, and trades with them.
        public readonly TraderSync Traders;

        /// Thrown and dropped things, shared.
        public readonly World.ThrowSync Throws;

        /// Larva eggs and other items broken open, shared.
        public readonly World.Eggs.Eggs Eggs;

        /// The infection's patches: clears and the host's spread.
        public readonly World.Infections.Infections Infections;

        /// Things on fire, shown from the host's world.
        public readonly World.Fire.Fire Fire;

        /// The fuel in generators and saws.
        public readonly World.Fuel.Fuel Fuel;

        /// Host: how much of the world each joiner's game keeps running.
        public readonly Combat.ViewRects Views;

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
            Construction = new Construction(this);
            Stacks = new StackShare(this);
            Time = new TimeSync(this);
            Npcs = new NpcSync(this);
            Followers = new NpcFollowers(this);
            Talk = new World.Talk.TalkLock(this);
            Dialogues = new World.Talk.DialogueGuard(this);
            Drags = new DragSync(this);
            Ui = new UiRefresh(this);
            Traders = new TraderSync(this);
            Throws = new World.ThrowSync(this);
            Eggs = new World.Eggs.Eggs(this);
            Infections = new World.Infections.Infections(this);
            Fire = new World.Fire.Fire(this);
            Fuel = new World.Fuel.Fuel(this);
            Views = new Combat.ViewRects(this);
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
