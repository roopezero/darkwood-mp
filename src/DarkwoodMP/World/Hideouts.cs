using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Things that happen to the hideouts as a whole, shared between the games.
    ///
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
    ///
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
    public static class Hideouts
    {
        private static Session S => Session.Instance;
        private static bool Debug => WorldActions.Debug;

        /// <summary>
        /// Host: each joiner's own oven, as far as the host knows.
        /// </summary>
        private static readonly Dictionary<ulong, ExperienceMachine> _ovens =
            new Dictionary<ulong, ExperienceMachine>();

        public static void Register(Session s)
        {
            s.Register(MsgType.PorterTransport, OnPorterTransport);
            s.Register(MsgType.PorterDelivered, OnPorterDelivered);
            s.Register(MsgType.OvenLit, OnOvenLit);
            s.Register(MsgType.PorterCalled, OnPorterCalled);
            // A joiner loaded the host's save, so its oven is the host's until
            // it lights another.
            s.OnPeerConnected += id =>
            {
                if (S != null && S.IsHost && Player.Instance != null)
                {
                    _ovens[id] = Player.Instance.experienceMachine;
                }
            };

            s.OnPeerDisconnected += id => _ovens.Remove(id);
            s.OnSessionEnded += () => _ovens.Clear();
        }

        /// <summary>
        /// Playing together in a world (also while replaying another game's
        /// change, unlike Coop.Guards.Sharing).
        /// </summary>
        internal static bool Together
        {
            get
            {
                var s = S;
                return s != null && WorldShare.InWorld && !WorldShare.Loading
                    && ((s.IsHost && s.Peers.Count > 0)
                        || NetEnemies.ClientMode);
            }
        }

        private static Location HideoutById(int id)
        {
            if (id <= 0)
            {
                return null;
            }

            foreach (var loc in Object.FindObjectsOfType<Location>())
            {
                if (loc != null && loc.hideoutId == id && !loc.isSubLocation)
                {
                    return loc;
                }
            }

            return null;
        }

        // ---- the porter -----------------------------------------------------

        /// <summary>
        /// Location.transportAllItemsToCurrentHideout ran in this game; false:
        /// we did (or left) it instead.
        /// </summary>
        internal static bool Transport(Location src)
        {
            if (!Together)
            {
                return true;
            }

            var s = S;
            if (s.IsClient)
            {
                // Our player's doing (our own part of the event): ask the host.
                // Another player's: the host's result comes.
                if (ScriptedEvents.Current != ScriptedEvents.Part.SharedOnly
                    && !Coop.Guards.Replaying)
                {
                    var w = s.Begin(MsgType.PorterTransport);
                    w.W.Write((byte)src.hideoutId);
                    if (Debug)
                    {
                        Plugin.Log.LogInfo(
                            "[world] sent porter transport from hideout "
                                + $"{src.hideoutId}");
                    }

                    Coop.Current.Authority.Request(
                        w, $"porter transport from hideout {src.hideoutId}",
                        inventory: false);
                }

                return false;
            }

            // Host: a joiner's event is done when its own PorterTransport
            // comes, for its hideout.
            if (ScriptedEvents.Current == ScriptedEvents.Part.HostAndShared)
            {
                return false;
            }

            Player.Instance.whereAmI.checkWhereAmI();
            var dest = Player.Instance.whereAmI.bigLocation;
            if (Why(src, dest) is string why)
            {
                Plugin.Log.LogInfo(
                    $"[world] porter transport from {src.name}: not done "
                        + $"({why})");
                return false;
            }

            Deliver(src, dest);
            return false;
        }

        private static string Why(Location src, Location dest)
        {
            if (src == null || src.hideoutId <= 0)
            {
                return "no such hideout to take things from";
            }

            if (dest == null || !dest.playerBase || dest.hideoutId <= 0)
            {
                return "not at a hideout";
            }

            if (dest == src)
            {
                return "already at that hideout";
            }

            if (dest.porterDeliveryPosition == null)
            {
                return $"{dest.name} has no place for the porter's package";
            }

            return null;
        }

        private static void OnPorterTransport(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !s.IsHost || !WorldShare.InWorld)
            {
                return;
            }

            int hid = r.R.ReadByte();
            if (!Coop.Current.Authority.Handling)
            {
                return;
            }

            var proxy = Coop.Current.Authority.Requester;
            if (proxy == null)
            {
                Coop.Current.Authority.Refuse("where they are isn't known yet");
                return;
            }

            var dest = NightSpawns.BigLocationAt(proxy.transform.position);
            var src = HideoutById(hid);
            if (Why(src, dest) is string why)
            {
                Coop.Current.Authority.Refuse(why);
                return;
            }

            Deliver(src, dest);
        }

        /// <summary>
        /// Host: the body of transportAllItemsToCurrentHideout, delivering to
        /// dest, then tell everyone.
        /// </summary>
        internal static void Deliver(Location src, Location dest)
        {
            // empties src's unlocked containers
            var slots = new List<InvSlot>(src.getAllInvSlots());
            long id = HostIds.NewId(), spawnerId = HostIds.NewId();
            var inv = MakePackage(dest, id);
            Inventory.moveSlots(slots, inv.slots);
            foreach (var item in inv.getAllItems("alcohol"))
            {
                if (!InvItemClass.isNull(item))
                {
                    item.type = "bottle";
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
            w.W.Write((byte)src.hideoutId);
            w.W.Write((byte)dest.hideoutId);
            w.W.Write(id);
            w.W.Write(spawnerId);
            w.WriteBigStr(contents);
            S.Broadcast(w, Channel.Control, reliable: true);
        }

        private static void OnPorterDelivered(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !s.IsClient || from != s.HostId
                || !WorldShare.InWorld)
            {
                return;
            }

            var src = HideoutById(r.R.ReadByte());
            var dest = HideoutById(r.R.ReadByte());
            long id = r.R.ReadInt64(), spawnerId = r.R.ReadInt64();
            string contents = r.ReadBigStr();
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

        private static Inventory MakePackage(Location dest, long id)
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
        private static void MakeSpawner(
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

        private static void PorterLeaves()
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
                "porter_inTransit", activeModifier: true);
        }

        // ---- the porter whistle ---------------------------------------------

        /// <summary>
        /// Location.spawnPorter (the porter whistle, InvItemClass.use) is about
        /// to run in this game; false: it mustn't. A joiner's whistle asks the
        /// host (PorterCalled), which calls the porter in every game.
        /// </summary>
        internal static bool CallPorter(Location loc)
        {
            if (!Together || Coop.Guards.Replaying
                || loc.porterPosition == null)
            {
                return true;
            }

            var s = S;
            if (s.IsHost)
            {
                // ours (or a joiner's, from OnPorterCalled): PorterCalledHere
                // tells the others
                return true;
            }

            var w = s.Begin(MsgType.PorterCalled);
            w.Write(loc.porterPosition.transform.position);
            if (Debug)
            {
                Plugin.Log.LogInfo($"[world] sent porter call at {loc.name}");
            }

            Coop.Current.Authority.Request(
                w, $"porter call at {loc.name}", inventory: false);
            return false;
        }

        /// <summary>
        /// Host: the porter was called in our game (its spawner made at loc):
        /// call it in the others' too.
        /// </summary>
        internal static void PorterCalledHere(Location loc)
        {
            if (!Together || !S.IsHost || loc.porterPosition == null)
            {
                return;
            }

            var w = S.Begin(MsgType.PorterCalled);
            w.Write(loc.porterPosition.transform.position);
            S.Broadcast(w, Channel.Control, reliable: true);
            Plugin.Log.LogInfo($"[world] the porter was called at {loc.name}");
        }

        /// <summary>
        /// The big location whose porter stands nearest pos (within 50), or
        /// null.
        /// </summary>
        private static Location PorterLocationAt(Vector3 pos)
        {
            Location best = null;
            float bestD = 50f;
            foreach (var loc in Object.FindObjectsOfType<Location>())
            {
                if (loc == null || loc.porterPosition == null)
                {
                    continue;
                }

                float d = Vector3.Distance(
                    loc.porterPosition.transform.position, pos);
                if (d < bestD)
                {
                    best = loc;
                    bestD = d;
                }
            }

            return best;
        }

        private static void OnPorterCalled(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !WorldShare.InWorld)
            {
                return;
            }

            var pos = r.ReadVector3();
            var loc = PorterLocationAt(pos);
            if (s.IsHost)
            {
                if (!Coop.Current.Authority.Handling)
                {
                    return;
                }

                if (loc == null)
                {
                    Coop.Current.Authority.Refuse(
                        "there's no porter's place there");
                    return;
                }

                var proxy = Coop.Current.Authority.Requester;
                if (proxy == null)
                {
                    Coop.Current.Authority.Refuse(
                        "where they are isn't known yet");
                    return;
                }

                if (NightSpawns.BigLocationAt(proxy.transform.position) != loc)
                {
                    Coop.Current.Authority.Refuse($"they aren't at {loc.name}");
                    return;
                }

                if (!Core.isDay())
                {
                    Coop.Current.Authority.Refuse(
                        "the porter comes in the daytime only");
                    return;
                }

                var flags = Singleton<Flags>.Instance;
                if (flags.isFlagTrue("porter_inTransit"))
                {
                    Coop.Current.Authority.Refuse(
                        "the porter is on the way already");
                    return;
                }

                if (flags.isFlagTrue("porter_killed"))
                {
                    Coop.Current.Authority.Refuse("the porter is dead");
                    return;
                }

                if (loc.porter != null)
                {
                    Coop.Current.Authority.Refuse(
                        "the porter is there already");
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

        // ---- ovens ----------------------------------------------------------

        /// <summary>
        /// Player.setExperienceMachine(machine, doEnable: true) while playing
        /// together; false: done here.
        /// </summary>
        internal static bool Light(Player p, ExperienceMachine machine)
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
            Coop.Current.Authority.Request(BeginOven(machine, prev),
                $"light the oven {machine.name}", (ok, reason, result) =>
            {
                if (ok || machine == null)
                {
                    return;
                }

                if (!wasOn)
                {
                    machine.disable();
                }

                if (Player.Instance != null
                    && Player.Instance.experienceMachine == machine)
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
        private static ExperienceMachine PutOut(
            ExperienceMachine prev, ExperienceMachine lit, ulong except)
        {
            if (prev == null || prev == lit)
            {
                return null;
            }

            if (Player.Instance != null
                && Player.Instance.experienceMachine == prev)
            {
                return null;
            }

            foreach (var kv in _ovens)
            {
                if (kv.Key != except && kv.Value == prev)
                {
                    return null;
                }
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
        private static PacketWriter BeginOven(
            ExperienceMachine lit, ExperienceMachine other)
        {
            var w = S.Begin(MsgType.OvenLit);
            ObjRef.Of(lit.gameObject).Write(w);
            ObjRef goesOut = default;
            if (other != null)
            {
                goesOut = ObjRef.Of(other.gameObject);
            }

            goesOut.Write(w);
            return w;
        }

        private static void SendOven(
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

        private static void OnOvenLit(ulong from, PacketReader r)
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

            var litRef = ObjRef.Read(r);
            var otherRef = ObjRef.Read(r);
            var lit = ObjRef.Resolve<ExperienceMachine>(litRef);
            var other = ObjRef.Resolve<ExperienceMachine>(otherRef);
            if (s.IsHost)
            {
                if (!Coop.Current.Authority.Handling)
                {
                    return;
                }

                if (lit == null)
                {
                    Coop.Current.Authority.Refuse("no such oven here");
                    return;
                }

                if (!Coop.Current.Authority.InReach(
                    lit.transform.position, WorldActions.Reach, lit.name))
                {
                    return;
                }

                if (lit.transform.GetLocation() == null)
                {
                    Coop.Current.Authority.Refuse(
                        "that oven isn't at a hideout");
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

            if (lit != null && !lit.isOn)
            {
                LightHere(lit);
            }

            if (other != null && other != lit && Player.Instance != null
                && Player.Instance.experienceMachine != other)
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
        /// Light an oven for the world (its ward, its flags) without making it
        /// our own player's.
        /// </summary>
        private static void LightHere(ExperienceMachine lit)
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
            if (ge.type != GameEvent.Type.moveInventory
                || ge.sourceTransform == null)
            {
                return false;
            }

            if (ge.getTargetGameObjectsFromParent || ge.getTargetFromLocation
                || (ge.locationObjects != null && ge.locationObjects.Count > 0))
            {
                return false;
            }

            var src = ge.sourceTransform.GetComponent<Inventory>();
            if (src == null || IsPlayers(src))
            {
                return false;
            }

            int n = 0;
            foreach (var go in Targets(ge))
            {
                var inv = go.GetComponent<Inventory>();
                if (inv == null)
                {
                    continue;
                }

                if (IsPlayers(inv))
                {
                    return false;
                }

                n++;
            }

            return n > 0;
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

        private static bool IsPlayers(Inventory inv) =>
            inv.GetComponentInParent<Player>() != null
                || (Player.Instance != null
                    && (inv == Player.Instance.Inventory
                        || inv == Player.Instance.Hotbar));

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
            if (!Together || !S.IsHost || coop == null)
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

    [HarmonyPatch(
        typeof(Location), nameof(Location.transportAllItemsToCurrentHideout))]
    static class PorterTransportPatch
    {
        static bool Prefix(Location __instance) =>
            Hideouts.Transport(__instance);
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
            __state = __instance.porterPosition != null
                && __instance.porter == null
                && !Singleton<Flags>.Instance.isFlagTrue("porter_killed");
            if (Hideouts.CallPorter(__instance))
            {
                return true;
            }

            __state = false;
            return false;
        }

        static void Postfix(Location __instance, bool __state)
        {
            if (__state)
            {
                Hideouts.PorterCalledHere(__instance);
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
            if (machine == null || !doEnable || Coop.Guards.Replaying
                || !Hideouts.Together)
            {
                return true;
            }

            return Hideouts.Light(__instance, machine);
        }
    }
}
