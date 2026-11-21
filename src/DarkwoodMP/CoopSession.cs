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

        /// What's in the world's containers, and who has which open.
        public readonly World.Inventories.Containers Containers;

        /// The containers shared slot by slot: the workbench's storage, and
        /// every container a client changes.
        public readonly World.Inventories.SlotShare Slots;

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
        public readonly World.Doors.Doors Doors;

        public readonly World.Windows.Windows Windows;

        /// Open doors' leaves that players push, shared.
        public readonly World.Doors.DoorPush Pushes;

        /// Traps going off, switched and disarmed, shared.
        public readonly World.TrapSync Traps;

        /// Locks, containers searched and padlocks opened, shared.
        public readonly World.LockShare Locks;

        /// Items switched on and off (lamps, generators), shared.
        public readonly World.SwitchSync Switches;

        /// NPC deaths, shared.
        public readonly World.NpcDeaths NpcDeaths;

        /// Host: how much of the world each joiner's game keeps running.
        public readonly Combat.ViewRects Views;

        /// Client: the host's enemies' hits on our player.
        public readonly Combat.PlayerDamage Damage;

        /// Client: the host's enemies' explosions, seen and heard here.
        public readonly Combat.EnemyExplosions Explosions;

        /// What the host's enemies do that a joiner sees or hears.
        public readonly Combat.EnemyFx EnemyFx;

        /// Items hit, broken and repaired, and explosives going off, shared.
        public readonly World.BreakActions Breaks;

        /// Notes, feeders, gasoline, vines and shadows, shared.
        public readonly World.ObjectActions Objects;

        /// What our player sounds and looks like to the others beyond its
        /// sprites: sounds, shots, blood and burning.
        public readonly Sync.PlayerFx PlayerFx;

        /// Logs what switches off our player's view (a black screen).
        public readonly Sync.ViewWatch View;

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
            Containers = new World.Inventories.Containers(this);
            Slots = new World.Inventories.SlotShare(this);
            Construction = new Construction(this);
            Stacks = new StackShare(this);
            Time = new TimeSync(this);
            Npcs = new NpcSync(this);
            Followers = new NpcFollowers(this);
            Talk = new World.Talk.TalkLock(this);
            Drags = new DragSync(this);
            Ui = new UiRefresh(this);
            Traders = new TraderSync(this);
            Throws = new World.ThrowSync(this);
            Eggs = new World.Eggs.Eggs(this);
            Infections = new World.Infections.Infections(this);
            Fire = new World.Fire.Fire(this);
            Fuel = new World.Fuel.Fuel(this);
            Doors = new World.Doors.Doors(this);
            Windows = new World.Windows.Windows(this);
            // the leaves live as long as the game (ModHost.Leaves); the
            // doors waiting for their push are forgotten when this ends
            var leaves = Plugin.Instance.Host.Leaves;
            Hook(leaves.Clear);
            Pushes = new World.Doors.DoorPush(this, leaves);
            Traps = new World.TrapSync(this);
            Locks = new World.LockShare(this);
            Switches = new World.SwitchSync(this);
            NpcDeaths = new World.NpcDeaths(this);
            Views = new Combat.ViewRects(this);
            Damage = new Combat.PlayerDamage(this);
            Explosions = new Combat.EnemyExplosions(this);
            EnemyFx = new Combat.EnemyFx(this);
            Breaks = new World.BreakActions(this);
            Objects = new World.ObjectActions(this);
            PlayerFx = new Sync.PlayerFx(this);
            View = new Sync.ViewWatch();
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
