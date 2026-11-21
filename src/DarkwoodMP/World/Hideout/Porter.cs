using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Scripted;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Hideout
{
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
                OthersPart = part == EventPart.SharedOnly,
                JoinersPart = part == EventPart.HostAndShared,
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
        /// to run in this game; false: it mustn't. calls: the host sends
        /// PorterCalled once it has run (SendCalled), as it makes the porter's
        /// spawner by the game's own conditions.
        /// </summary>
        internal bool Whistled(Location loc, out bool calls)
        {
            bool made = PorterRules.SpawnerMade(
                loc.porterPosition != null,
                loc.porter != null,
                Singleton<Flags>.Instance.isFlagTrue(PorterRules.Killed));
            bool runs = CallPorter(loc);
            calls = PorterRules.CalledHere(new CalledFacts
            {
                SpawnerMade = made,
                RanGame = runs,
                Together = Hideouts.Together,
                IsHost = S.IsHost,
                HasPorterPlace = loc.porterPosition != null,
            });

            return runs;
        }

        /// <summary>
        /// Whistled: whether spawnPorter runs here. A joiner's whistle asks
        /// the host (PorterCalled), which calls the porter in every game.
        /// </summary>
        private bool CallPorter(Location loc)
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
        internal void SendCalled(Location loc)
        {
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

                // LocationSpawnPorter sends it to everyone, the whistler too
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
}

