using DarkwoodMP.World.Inventories;
using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Hideout;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Things that happen to the hideouts as a whole, shared between the games:
    /// the porter (Porter), the ovens (Ovens) and a scripted step that moves
    /// things between two world containers (IsWorldMove).
    /// </summary>
    public static class Hideouts
    {
        private static Session S => Session.Instance;
        private static bool Debug => WorldActions.Debug;

        /// <summary>
        /// Playing together in a world (also while replaying another game's
        /// change, unlike Coop.Guards.Sharing).
        /// </summary>
        internal static bool Together
        {
            get
            {
                return TogetherRules.Together(TogetherOf(S));
            }
        }

        /// <summary>
        /// What TogetherRules knows of the session and the world.
        /// </summary>
        private static TogetherFacts TogetherOf(Session s)
        {
            if (s == null)
            {
                return default;
            }

            return new TogetherFacts
            {
                HasSession = true,
                InWorld = WorldShare.InWorld,
                Loading = WorldShare.Loading,
                IsHost = s.IsHost,
                Peers = s.Peers.Count,
                ClientMode = NetEnemies.ClientMode,
            };
        }

        internal static Location HideoutById(int id)
        {
            if (id <= 0)
            {
                return null;
            }

            foreach (var loc in Object.FindObjectsOfType<Location>())
            {
                if (loc != null
                    && PorterRules.IsHideout(
                        id, loc.hideoutId, loc.isSubLocation))
                {
                    return loc;
                }
            }

            return null;
        }

        // ---- moveInventory between two world containers ---------------------

        /// <summary>
        /// A GameEvent moveInventory step whose source and targets are all
        /// world containers (none is a player's): world state, so the host runs
        /// it and sends what's in each afterwards. One with targets found at
        /// run time (from the location, the parent) stays the player's own
        /// step, as before.
        /// </summary>
        internal static bool IsWorldMove(GameEvent ge)
        {
            return WorldMoveRules.IsWorldMove(FactsOf(ge));
        }

        /// <summary>
        /// What WorldMoveRules knows of a GameEvent step: the only code that
        /// reads the step and its containers for it. The source and targets
        /// are read only for a moveInventory step from a fixed source.
        /// </summary>
        private static WorldMoveFacts FactsOf(GameEvent ge)
        {
            int objects = 0;
            if (ge.locationObjects != null)
            {
                objects = ge.locationObjects.Count;
            }

            var facts = new WorldMoveFacts
            {
                MovesInventory = ge.type == GameEvent.Type.moveInventory,
                HasSource = ge.sourceTransform != null,
                FromParent = ge.getTargetGameObjectsFromParent,
                FromLocation = ge.getTargetFromLocation,
                LocationObjects = objects,
            };

            if (!facts.MovesInventory || !facts.HasSource || facts.FromParent
                || facts.FromLocation || objects > 0)
            {
                return facts;
            }

            var src = ge.sourceTransform.GetComponent<Inventory>();
            if (src == null)
            {
                return facts;
            }

            facts = facts with
            {
                SourceInventory = true,
                SourcePlayers = IsPlayers(src),
            };

            if (facts.SourcePlayers)
            {
                return facts;
            }

            int n = 0;
            bool players = false;
            foreach (var go in Targets(ge))
            {
                var inv = go.GetComponent<Inventory>();
                if (inv == null)
                {
                    continue;
                }

                if (IsPlayers(inv))
                {
                    players = true;
                    break;
                }

                n++;
            }

            return facts with
            {
                TargetInventories = n,
                TargetPlayers = players,
            };
        }

        private static IEnumerable<GameObject> Targets(GameEvent ge)
        {
            if (ge.targetGameObjects != null)
            {
                foreach (var go in ge.targetGameObjects)
                {
                    if (go != null)
                    {
                        yield return go;
                    }
                }
            }

            if (ge.targetUniqueObjects != null
                && Singleton<UniqueObjects>.Instance != null)
            {
                foreach (var name in ge.targetUniqueObjects)
                {
                    var go = Singleton<UniqueObjects>.Instance.getObject(name);
                    if (go != null)
                    {
                        yield return go;
                    }
                }
            }
        }

        private static bool IsPlayers(Inventory inv)
        {
            var p = Player.Instance;
            bool under = inv.GetComponentInParent<Player>() != null;
            bool bag = p != null && inv == p.Inventory;
            bool hotbar = p != null && inv == p.Hotbar;
            return WorldMoveRules.IsPlayers(under, bag, hotbar);
        }

        /// <summary>
        /// Host: run a world move's coroutine, then send the source's and the
        /// targets' contents.
        /// </summary>
        internal static IEnumerator SendAfter(IEnumerator inner, GameEvent ge)
        {
            while (inner.MoveNext())
            {
                yield return inner.Current;
            }

            CoopSession coop = Coop.Current;
            bool host = S != null && S.IsHost;
            if (!WorldMoveRules.SendsAfter(Together, host, coop != null))
            {
                yield break;
            }

            Containers containers = coop.Containers;

            Inventory src = null;
            if (ge.sourceTransform != null)
            {
                src = ge.sourceTransform.GetComponent<Inventory>();
            }

            if (src != null)
            {
                containers.SendContents(src);
            }

            foreach (var go in Targets(ge))
            {
                var inv = go.GetComponent<Inventory>();
                if (inv != null)
                {
                    containers.SendContents(inv);
                }
            }

            if (Debug)
            {
                Transform sourceTransform = ge.sourceTransform;
                string name = null;
                if (sourceTransform != null)
                {
                    name = sourceTransform.name;
                }

                Plugin.Log.LogInfo(
                    $"[world] moveInventory from {name} done here; sent the "
                        + "containers");
            }
        }
    }

    /// <summary>
    /// The porter (Location.transportAllItemsToCurrentHideout) takes everything
    /// in a hideout's unlocked containers to the hideout "the player" stands
    /// in. The game reaches it through a dialogue's world event, which every
    /// game runs (a shared step), and each game would deliver to its own
    /// player's hideout, or lose the items when its player isn't at one. While
    /// playing together it happens once, on the host, for the player who asked
    /// the porter: a joiner's game sends PorterTransport instead of doing it,
    /// the host takes the hideout that player's stand-in is at (refused before
    /// anything is emptied if there's none), does the move and sends
    /// PorterDelivered, and every game empties the same containers and makes
    /// the same hidden package with the same contents.
    /// </summary>
    internal sealed class Porter
    {
        private readonly CoopSession _coop;

        public Porter(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.PorterTransport, OnPorterTransport);
            coop.Handle(MsgType.PorterDelivered, OnPorterDelivered);
            coop.Handle(MsgType.PorterCalled, OnPorterCalled);
        }

        private Session S => _coop.Net;

        private static bool Debug => WorldActions.Debug;

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        /// <summary>
        /// Location.transportAllItemsToCurrentHideout ran in this game; false:
        /// we did (or left) it instead.
        /// </summary>
        internal bool Transport(Location src)
        {
            var part = ScriptedEvents.Current;
            var step = PorterRules.Transport(new TransportFacts
            {
                Together = Hideouts.Together,
                IsClient = S != null && S.IsClient,
                OthersPart = part == ScriptedEvents.Part.SharedOnly,
                JoinersPart = part == ScriptedEvents.Part.HostAndShared,
                Replaying = Coop.Guards.Replaying,
            });

            if (step == TransportStep.RunGame)
            {
                return true;
            }

            var s = S;

            // A client: our player's doing (our own part of the event) asks
            // the host; another player's waits for the host's result. The
            // host: a joiner's event is done when its own PorterTransport
            // comes, for its hideout.
            if (step == TransportStep.Request)
            {
                var w = s.Begin(MsgType.PorterTransport);
                PorterTransportMessage.Write(w, src.hideoutId);
                if (Debug)
                {
                    Plugin.Log.LogInfo(
                        "[world] sent porter transport from hideout "
                            + $"{src.hideoutId}");
                }

                _coop.Authority.Request(
                    w, $"porter transport from hideout {src.hideoutId}",
                    inventory: false);
                return false;
            }

            if (step == TransportStep.Nothing)
            {
                return false;
            }

            Player.Instance.whereAmI.checkWhereAmI();
            var dest = Player.Instance.whereAmI.bigLocation;
            Verdict why = PorterRules.Why(FactsOf(src, dest));
            if (why.Refused)
            {
                Plugin.Log.LogInfo(
                    $"[world] porter transport from {src.name}: not done "
                        + $"({Refusals.Text(why)})");
                return false;
            }

            Deliver(src, dest);
            return false;
        }

        /// <summary>
        /// What PorterRules knows of a move from src to dest (either may be
        /// null): the only code that reads the two hideouts for it.
        /// </summary>
        private HideoutFacts FactsOf(Location src, Location dest)
        {
            var facts = new HideoutFacts { Same = dest == src };
            if (src != null)
            {
                facts = facts with
                {
                    HasSource = true,
                    SourceId = src.hideoutId,
                };
            }

            if (dest != null)
            {
                facts = facts with
                {
                    HasTarget = true,
                    TargetBase = dest.playerBase,
                    TargetId = dest.hideoutId,
                    HasPackagePlace = dest.porterDeliveryPosition != null,
                    TargetName = dest.name,
                };
            }

            return facts;
        }

        private void OnPorterTransport(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !s.IsHost || !WorldShare.InWorld)
            {
                return;
            }

            int hid = PorterTransportMessage.Read(r);
            if (!_coop.Authority.Handling)
            {
                return;
            }

            var proxy = _coop.Authority.Requester;
            Location dest = null;
            Location src = null;
            if (proxy != null)
            {
                dest = NightSpawns.BigLocationAt(proxy.transform.position);
                src = Hideouts.HideoutById(hid);
            }

            Verdict may =
                PorterRules.MayTransport(proxy != null, FactsOf(src, dest));
            if (may.Refused)
            {
                _coop.Authority.Refuse(may);
                return;
            }

            Deliver(src, dest);
        }

        /// <summary>
        /// Host: the body of transportAllItemsToCurrentHideout, delivering to
        /// dest, then tell everyone.
        /// </summary>
        internal void Deliver(Location src, Location dest)
        {
            // empties src's unlocked containers
            var slots = new List<InvSlot>(src.getAllInvSlots());
            long id = HostIds.NewId(), spawnerId = HostIds.NewId();
            var inv = MakePackage(dest, id);
            Inventory.moveSlots(slots, inv.slots);
            foreach (var item in inv.getAllItems(PorterRules.Alcohol))
            {
                if (!InvItemClass.isNull(item))
                {
                    item.type = PorterRules.Bottle;
                    item.assignClass();
                }
            }

            string contents =
                JsonConvert.SerializeObject(new Inventory.SaveState(inv));
            inv.gameObject.SetActive(false);
            MakeSpawner(dest, inv.gameObject, spawnerId);
            PorterLeaves();
            Plugin.Log.LogInfo(
                $"[world] the porter took {slots.Count} slots from {src.name} "
                    + $"to {dest.name}");

            var w = S.Begin(MsgType.PorterDelivered);
            PorterDeliveredMessage.Write(w, new PorterDelivery
            {
                Source = src.hideoutId,
                Target = dest.hideoutId,
                Id = id,
                SpawnerId = spawnerId,
                Contents = contents,
            });

            S.Broadcast(w, Channel.Control, reliable: true);
        }

        private void OnPorterDelivered(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !s.IsClient || from != s.HostId
                || !WorldShare.InWorld)
            {
                return;
            }

            PorterDelivery move = PorterDeliveredMessage.Read(r);
            var src = Hideouts.HideoutById(move.Source);
            var dest = Hideouts.HideoutById(move.Target);
            long id = move.Id, spawnerId = move.SpawnerId;
            string contents = move.Contents;
            if (Debug)
            {
                string fromName = "?";
                if (src != null)
                {
                    fromName = src.name;
                }

                string toName = "?";
                if (dest != null)
                {
                    toName = dest.name;
                }

                Plugin.Log.LogInfo(
                    $"[world] got porter delivery from {fromName} to {toName}");
            }

            bool was = Coop.Guards.Replaying;
            Coop.Guards.Replaying = true;
            try
            {
                // empties them as the host's did
                if (src != null)
                {
                    src.getAllInvSlots();
                }

                if (dest == null || dest.porterDeliveryPosition == null)
                {
                    Plugin.Log.LogWarning("The porter's hideout isn't here");
                    return;
                }

                var inv = MakePackage(dest, id);
                Inventory.SaveState saveState =
                    JsonConvert.DeserializeObject<Inventory.SaveState>(
                        contents);
                if (saveState != null)
                {
                    saveState.loadValues(inv);
                }

                inv.gameObject.SetActive(false);
                MakeSpawner(dest, inv.gameObject, spawnerId);
                PorterLeaves();
                Plugin.Log.LogInfo(
                    $"[world] the porter's package is at {dest.name}: "
                        + $"{inv.getAllItems().Count} items");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't make the porter's package: {e.Message}");
            }
            finally
            {
                Coop.Guards.Replaying = was;
            }
        }

        private Inventory MakePackage(Location dest, long id)
        {
            var go = Core.AddPrefab(
                "Objects/_Unique/porterPackage",
                dest.porterDeliveryPosition.transform.position,
                Quaternion.Euler(90f, 0f, 0f), dest.gameObject,
                worldSpace: true);
            Core.addToSaveable(go, isDynamic: true, assignID: true);
            dest.addToObjects(go);
            ObjRef.RegisterSpawned(id, go);
            return go.GetComponent<Inventory>();
        }

        /// <summary>
        /// The event that shows the package later (each game runs its own, on
        /// its own copy).
        /// </summary>
        private void MakeSpawner(
            Location dest, GameObject package, long id)
        {
            var go = Core.AddPrefab(
                "Objects/_Unique/porterPackageSpawner", Vector3.zero,
                Quaternion.identity, dest.gameObject);
            Core.addToSaveable(go, isDynamic: true, assignID: true);
            dest.addToObjects(go);
            dest.events.Add(go.GetComponent<EventTriggers>());
            go.GetComponent<GameEvents>().events[0].targetGameObjects[0] =
                package;
            ObjRef.RegisterSpawned(id, go);
        }

        private void PorterLeaves()
        {
            var porter = Singleton<UniqueObjects>.Instance.getObject("porter");
            if (porter != null)
            {
                Character porterChar = porter.GetComponent<Character>();
                if (porterChar != null)
                {
                    porterChar.removeMe();
                }
            }

            Singleton<Flags>.Instance.setFlag(
                PorterRules.InTransit, activeModifier: true);
        }

        // ---- the porter whistle ---------------------------------------------

        /// <summary>
        /// Location.spawnPorter (the porter whistle, InvItemClass.use) is about
        /// to run in this game; false: it mustn't. A joiner's whistle asks the
        /// host (PorterCalled), which calls the porter in every game.
        /// </summary>
        internal bool CallPorter(Location loc)
        {
            var s = S;

            // The host's own (or a joiner's, from OnPorterCalled):
            // PorterCalledHere tells the others.
            var step = PorterRules.Call(new CallFacts
            {
                Together = Hideouts.Together,
                Replaying = Coop.Guards.Replaying,
                HasPorterPlace = loc.porterPosition != null,
                IsHost = s != null && s.IsHost,
            });

            if (step == CallStep.RunGame)
            {
                return true;
            }

            var w = s.Begin(MsgType.PorterCalled);
            PorterCalledMessage.Write(
                w, VecOf(loc.porterPosition.transform.position));
            if (Debug)
            {
                Plugin.Log.LogInfo($"[world] sent porter call at {loc.name}");
            }

            _coop.Authority.Request(
                w, $"porter call at {loc.name}", inventory: false);
            return false;
        }

        /// <summary>
        /// Host: the porter was called in our game (its spawner made at loc):
        /// call it in the others' too.
        /// </summary>
        internal void PorterCalledHere(Location loc)
        {
            var called = PorterRules.CalledHere(new CalledFacts
            {
                SpawnerMade = true,
                RanGame = true,
                Together = Hideouts.Together,
                IsHost = S != null && S.IsHost,
                HasPorterPlace = loc.porterPosition != null,
            });

            if (!called)
            {
                return;
            }

            var w = S.Begin(MsgType.PorterCalled);
            PorterCalledMessage.Write(
                w, VecOf(loc.porterPosition.transform.position));
            S.Broadcast(w, Channel.Control, reliable: true);
            Plugin.Log.LogInfo($"[world] the porter was called at {loc.name}");
        }

        /// <summary>
        /// The big location whose porter stands nearest pos (within 50), or
        /// null.
        /// </summary>
        private Location PorterLocationAt(Vector3 pos)
        {
            var locs = new List<Location>();
            var places = new List<Vec3>();
            foreach (var loc in Object.FindObjectsOfType<Location>())
            {
                if (loc == null || loc.porterPosition == null)
                {
                    continue;
                }

                locs.Add(loc);
                places.Add(VecOf(loc.porterPosition.transform.position));
            }

            int best = PorterRules.NearestPorterPlace(places, VecOf(pos));
            if (best < 0)
            {
                return null;
            }

            return locs[best];
        }

        /// <summary>
        /// What PorterRules knows of a joiner's porter call at loc (null for
        /// no porter's place there): the only code that reads the place, the
        /// flags and the stand-in for it.
        /// </summary>
        private CallCheckFacts FactsOf(Location loc, NetPlayerProxy p)
        {
            if (loc == null)
            {
                return default;
            }

            var flags = Singleton<Flags>.Instance;
            var facts = new CallCheckFacts
            {
                HasPlace = true,
                Known = p != null,
                PlaceName = loc.name,
            };

            if (p == null)
            {
                return facts;
            }

            return facts with
            {
                AtPlace =
                    NightSpawns.BigLocationAt(p.transform.position) == loc,
                IsDay = Core.isDay(),
                InTransit = flags.isFlagTrue(PorterRules.InTransit),
                Killed = flags.isFlagTrue(PorterRules.Killed),
                PorterThere = loc.porter != null,
            };
        }

        private void OnPorterCalled(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !WorldShare.InWorld)
            {
                return;
            }

            Vec3 at = PorterCalledMessage.Read(r);
            var pos = new Vector3(at.X, at.Y, at.Z);
            var loc = PorterLocationAt(pos);
            if (s.IsHost)
            {
                if (!_coop.Authority.Handling)
                {
                    return;
                }

                var proxy = _coop.Authority.Requester;
                Verdict may = PorterRules.MayCall(FactsOf(loc, proxy));
                if (may.Refused)
                {
                    _coop.Authority.Refuse(may);
                    return;
                }

                // PorterCallPatch sends it to everyone, the whistler too
                loc.spawnPorter();
                return;
            }

            if (from != s.HostId)
            {
                return;
            }

            if (Debug)
            {
                string placeName = "no porter's place here";
                if (loc != null)
                {
                    placeName = loc.name;
                }

                Plugin.Log.LogInfo($"[world] got porter call: " + placeName);
            }

            if (loc == null)
            {
                return;
            }

            bool was = Coop.Guards.Replaying;
            Coop.Guards.Replaying = true;
            try
            {
                loc.spawnPorter();
            }
            finally
            {
                Coop.Guards.Replaying = was;
            }
        }
    }

    /// <summary>
    /// Hideout ovens (ExperienceMachine): a lit oven and its hideout's shadow
    /// ward are world state, the same in every game and checked by the host
    /// (reach). Which oven is a player's own (Player.experienceMachine: where
    /// they respawn and cook) stays each player's. Lighting one doesn't put out
    /// the player's previous oven in another game, nor while another player
    /// still has it as theirs: the host keeps each joiner's oven and decides.
    /// The host saves one oven (its own player's,
    /// Player.SaveState.expMachineId); after the host reloads, the other ovens
    /// are cold until lit again.
    /// </summary>
    internal sealed class Ovens
    {
        private readonly CoopSession _coop;

        /// <summary>
        /// Host: each joiner's own oven, as far as the host knows.
        /// </summary>
        private readonly Dictionary<ulong, ExperienceMachine> _ovens =
            new Dictionary<ulong, ExperienceMachine>();

        public Ovens(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.OvenLit, OnOvenLit);
            coop.Net.OnPeerConnected += OnPeerConnected;
            coop.Hook(() => coop.Net.OnPeerConnected -= OnPeerConnected);
            coop.Net.OnPeerDisconnected += OnPeerDisconnected;
            coop.Hook(
                () => coop.Net.OnPeerDisconnected -= OnPeerDisconnected);
        }

        private Session S => _coop.Net;

        private static bool Debug => WorldActions.Debug;

        /// <summary>
        /// A joiner loaded the host's save, so its oven is the host's until
        /// it lights another.
        /// </summary>
        private void OnPeerConnected(ulong id)
        {
            if (OvenRules.GivesHostsOven(S.IsHost, Player.Instance != null))
            {
                _ovens[id] = Player.Instance.experienceMachine;
            }
        }

        private void OnPeerDisconnected(ulong id)
        {
            _ovens.Remove(id);
        }

        /// <summary>
        /// Player.setExperienceMachine(machine, doEnable: true) while playing
        /// together; false: done here.
        /// </summary>
        internal bool Light(Player p, ExperienceMachine machine)
        {
            var s = S;
            var prev = p.experienceMachine;
            bool wasOn = machine.isOn;
            machine.enable(); // also makes it our player's own
            if (s.IsHost)
            {
                var putOut = PutOut(prev, machine, except: 0);
                SendOven(machine, putOut);
                return false;
            }

            // Client: lit here at once; the host says whether the old one goes
            // out.
            _coop.Authority.Request(BeginOven(machine, prev),
                $"light the oven {machine.name}", (ok, reason, result) =>
            {
                var back = OvenRules.Rollback(new RollbackFacts
                {
                    Allowed = ok,
                    Exists = machine != null,
                    WasOn = wasOn,
                    StillOurs = Player.Instance != null
                        && Player.Instance.experienceMachine == machine,
                });

                if (back.PutOut)
                {
                    machine.disable();
                }

                if (back.GiveBack)
                {
                    Player.Instance.experienceMachine = prev;
                }
            }, inventory: false);

            return false;
        }

        /// <summary>
        /// Host: put out prev (one player's old oven) unless it's the new one
        /// or someone (except who) still has it as theirs.
        /// </summary>
        private ExperienceMachine PutOut(
            ExperienceMachine prev, ExperienceMachine lit, ulong except)
        {
            bool others = false;
            foreach (var kv in _ovens)
            {
                if (kv.Key != except && kv.Value == prev)
                {
                    others = true;
                }
            }

            var old = new PutOutFacts
            {
                HasOld = prev != null,
                OldIsLit = prev == lit,
                OldIsHosts = Player.Instance != null
                    && Player.Instance.experienceMachine == prev,
                OldIsOthers = others,
            };

            if (!OvenRules.PutsOut(old))
            {
                return null;
            }

            bool was = Coop.Guards.Replaying;
            Coop.Guards.Replaying = true;
            try
            {
                prev.disable();
            }
            finally
            {
                Coop.Guards.Replaying = was;
            }

            return prev;
        }

        /// <summary>
        /// OvenLit: the lit oven, then the one that goes out (an empty ref for
        /// none).
        /// </summary>
        private PacketWriter BeginOven(
            ExperienceMachine lit, ExperienceMachine other)
        {
            var w = S.Begin(MsgType.OvenLit);
            ObjRef goesOut = default;
            if (other != null)
            {
                goesOut = ObjRef.Of(other.gameObject);
            }

            OvenLitMessage.Write(w, new LitOven
            {
                Lit = ObjRef.NameOf(ObjRef.Of(lit.gameObject)),
                Out = ObjRef.NameOf(goesOut),
            });

            return w;
        }

        private void SendOven(
            ExperienceMachine lit, ExperienceMachine putOut)
        {
            var w = BeginOven(lit, putOut);
            if (Debug)
            {
                string note = "";
                if (putOut != null)
                {
                    note = $", {putOut.name} out";
                }

                Plugin.Log.LogInfo($"[world] sent oven {lit.name} lit" + note);
            }

            S.Broadcast(w, Channel.Control, reliable: true);
        }

        private void OnOvenLit(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !WorldShare.InWorld)
            {
                return;
            }

            if (s.IsClient && from != s.HostId)
            {
                return;
            }

            LitOven oven = OvenLitMessage.Read(r);
            var litRef = ObjRef.RefOf(oven.Lit);
            var otherRef = ObjRef.RefOf(oven.Out);
            var lit = ObjRef.Resolve<ExperienceMachine>(litRef);
            var other = ObjRef.Resolve<ExperienceMachine>(otherRef);
            if (s.IsHost)
            {
                if (!_coop.Authority.Handling)
                {
                    return;
                }

                Verdict may = OvenRules.MayLight(FactsOf(lit));
                if (may.Refused)
                {
                    _coop.Authority.Refuse(may);
                    return;
                }

                LightHere(lit);
                _ovens[from] = lit;
                if (Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {s.NameOf(from)} lit the oven {lit.name}");
                }

                SendOven(lit, PutOut(other, lit, except: from));
                return;
            }

            if (Debug)
            {
                string line = $"[world] got oven {litRef} lit";
                string otherOut = "";
                if (otherRef.Valid)
                {
                    otherOut = $", {otherRef} out";
                }

                string missing = "";
                if (lit == null)
                {
                    missing = ": not found here";
                }

                Plugin.Log.LogInfo(line + otherOut + missing);
            }

            var change = OvenRules.FromHost(FactsOf(lit, other));
            if (change.Light)
            {
                LightHere(lit);
            }

            if (change.PutOut)
            {
                Coop.Guards.Replaying = true;
                try
                {
                    other.disable();
                }
                finally
                {
                    Coop.Guards.Replaying = false;
                }
            }
        }

        /// <summary>
        /// Host: what OvenRules knows of a joiner's lit oven (null for none
        /// here): the only code that reads it for MayLight.
        /// </summary>
        private OvenFacts FactsOf(ExperienceMachine lit)
        {
            if (lit == null)
            {
                return default;
            }

            var reach = _coop.Authority.ReachOf(
                lit.transform.position, lit.name);
            return new OvenFacts
            {
                Exists = true,
                Reach = reach with { Reach = WorldActions.Reach },
                AtLocation = lit.transform.GetLocation() != null,
            };
        }

        /// <summary>
        /// A client: what OvenRules knows of the host's lit oven and the one
        /// it put out (either may be null).
        /// </summary>
        private HostOvenFacts FactsOf(
            ExperienceMachine lit, ExperienceMachine other)
        {
            var p = Player.Instance;
            var facts = new HostOvenFacts
            {
                Found = lit != null,
                HasOther = other != null,
                OtherIsLit = other == lit,
                HasPlayer = p != null,
            };

            if (lit != null)
            {
                facts = facts with { On = lit.isOn };
            }

            if (p != null)
            {
                facts = facts with
                {
                    OtherIsOurs = p.experienceMachine == other,
                };
            }

            return facts;
        }

        /// <summary>
        /// Light an oven for the world (its ward, its flags) without making it
        /// our own player's.
        /// </summary>
        private void LightHere(ExperienceMachine lit)
        {
            var p = Player.Instance;
            ExperienceMachine mine = null;
            if (p != null)
            {
                mine = p.experienceMachine;
            }

            bool was = Coop.Guards.Replaying;
            Coop.Guards.Replaying = true;
            try
            {
                lit.enable();
            }
            finally
            {
                Coop.Guards.Replaying = was;
                if (p != null)
                {
                    p.experienceMachine = mine;
                }
            }
        }
    }

    [HarmonyPatch(
        typeof(Location), nameof(Location.transportAllItemsToCurrentHideout))]
    static class PorterTransportPatch
    {
        static bool Prefix(Location __instance)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Porter.Transport(__instance);
        }
    }

    /// <summary>
    /// The porter whistle (Location.spawnPorter): the host calls the porter, in
    /// every game.
    /// </summary>
    [HarmonyPatch(typeof(Location), nameof(Location.spawnPorter))]
    static class PorterCallPatch
    {
        static bool Prefix(Location __instance, out bool __state)
        {
            // Whether it will make the porter's spawner (its own conditions).
            __state = PorterRules.SpawnerMade(
                __instance.porterPosition != null,
                __instance.porter != null,
                Singleton<Flags>.Instance.isFlagTrue(PorterRules.Killed));
            CoopSession coop = Coop.Current;
            if (coop == null || coop.Porter.CallPorter(__instance))
            {
                return true;
            }

            __state = false;
            return false;
        }

        static void Postfix(Location __instance, bool __state)
        {
            CoopSession coop = Coop.Current;
            if (__state && coop != null)
            {
                coop.Porter.PorterCalledHere(__instance);
            }
        }
    }

    [HarmonyPatch(
        typeof(Player), nameof(Player.setExperienceMachine),
        typeof(ExperienceMachine), typeof(bool))]
    static class SetOvenPatch
    {
        static bool Prefix(
            Player __instance, ExperienceMachine machine, bool doEnable)
        {
            if (Coop.Current == null)
            {
                return true;
            }

            var call = new OvenCallFacts
            {
                HasOven = machine != null,
                Enable = doEnable,
                Replaying = Coop.Guards.Replaying,
                Together = Hideouts.Together,
            };

            if (!OvenRules.TakesOver(call))
            {
                return true;
            }

            return Coop.Current.Ovens.Light(__instance, machine);
        }
    }
}
