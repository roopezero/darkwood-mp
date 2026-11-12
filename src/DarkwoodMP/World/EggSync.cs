using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Larva eggs (Objects/larva_egg_big, "Egg": "Something is floating inside... Maybe I could puncture or pierce
    /// it") and other items that are broken open to get at what's inside (Item.destroyToAccessInventory), shared.
    ///
    /// The egg is an Item with 5 health and an Inventory that holds the Embryo (lifePotion). Breaking it (Item.die,
    /// from a hit, fire or an explosion) plays its burst animation, makes it a trigger collider on layer 14 and lets
    /// its inventory be opened; nothing is spawned. So the break is the one thing to share: EggDestroyed, sent by
    /// the game whose player (or, on the host, whose world) broke it, instead of the ItemHit or ItemBroken
    /// BreakSync would send. The others break their copy with Item.die, found even while it's culled (inactive)
    /// in their game, which is where ItemHit's name lookup could miss it. The Embryo is in the egg's inventory in
    /// every game (everyone loaded the host's save) and taking it is Containers' (slot by slot, the host's call),
    /// so it's taken once. Item.die sends the egg's onDeath trigger, which is the host's world's
    /// to run: a joiner's game leaves its own to the host and runs the shared part of the host's (ScriptedEvents).
    ///
    /// A client's break is a request: the host checks the player is in reach and the egg isn't broken already,
    /// breaks its own and passes it on; if it refuses, the client gets the egg's state back (WorldActions.SendState).
    /// </summary>
    public static class EggSync
    {
        private static Session S => Session.Instance;

        public static void Register(Session s) => s.Register(MsgType.EggDestroyed, OnDestroyed);

        /// <summary>Whether this item is an egg (broken open to get at its contents).</summary>
        public static bool IsEgg(Item i) =>
            i != null && i.destructible && (i.destroyToAccessInventory || i.gameObject.name.IndexOf("egg", System.StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>This game broke the egg; the caller checked it's ours to send (BreakSync.OurPlayers).</summary>
        public static void Send(Item egg)
        {
            var r = ObjRef.Of(egg.gameObject);
            if (!r.Valid) return;
            // The hit that broke it can leave its health below 0; the others' copies are broken at 0 (Break), and
            // so is ours, so its state is the same in every game.
            if (egg.health < 0) egg.health = 0;
            var p = egg.transform.position;
            var w = S.Begin(MsgType.EggDestroyed);
            r.Write(w);
            w.W.Write(p.x); w.W.Write(p.z);
            w.WriteStr(egg.gameObject.name);
            Plugin.Log.LogInfo($"[world] sent egg {r} destroyed ({egg.gameObject.name})");
            if (S.IsHost) S.Broadcast(w, Channel.Control, reliable: true);
            else Authority.Request(w, $"egg {r} destroyed ({egg.gameObject.name})");
        }

        private static void OnDestroyed(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !WorldShare.InWorld) return;
            if (s.IsClient && from != s.HostId) return;
            var obj = ObjRef.Read(r);
            float x = r.R.ReadSingle(), z = r.R.ReadSingle();
            string name = r.ReadStr();

            var egg = Find(obj, x, z, name);
            if (s.IsHost && Authority.Handling)
            {
                if (egg == null) { Authority.Refuse("there's no such egg here"); WorldActions.SendState(from, obj); return; }
                if (egg.destroyed) return; // broken here already (by the host, or another player): nothing to do or pass on
                if (!Authority.InReach(egg.transform.position, WorldActions.ShotReach, egg.gameObject.name)) { WorldActions.SendState(from, obj); return; }
            }
            Plugin.Log.LogInfo($"[world] egg {obj} destroyed (from {s.NameOf(from)})" + (egg == null ? ": not found here" : egg.destroyed ? ": already broken here" : ""));
            if (egg == null || egg.destroyed) return;

            WorldActions.Replaying = true;
            try { Break(egg); }
            finally { WorldActions.Replaying = false; }

            if (s.IsHost)
            {
                var w = s.Begin(MsgType.EggDestroyed);
                obj.Write(w);
                w.W.Write(x); w.W.Write(z);
                w.WriteStr(name);
                s.Broadcast(w, Channel.Control, reliable: true, except: from);
            }
        }

        /// <summary>Break the egg as the game does. A culled (inactive) one can't play its animation, so it's left in its broken state.</summary>
        private static void Break(Item egg)
        {
            egg.health = 0;
            if (egg.gameObject.activeInHierarchy) { egg.die(); return; }
            egg.destroyed = true;
            try { egg.setDeathState(); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't show {egg.gameObject.name} broken: {e.Message}"); }
            Core.sendTriggerInfo(egg.gameObject, global::EventTrigger.Type.onDeath);
        }

        /// <summary>The egg by its name across games, or else the one of that name nearest where it was, active or not.</summary>
        private static Item Find(ObjRef obj, float x, float z, string name)
        {
            var egg = ObjRef.Resolve<Item>(obj);
            if (egg != null) return egg;
            Item best = null; float bestD = ObjRef.PlacedSlack * ObjRef.PlacedSlack;
            foreach (var i in Resources.FindObjectsOfTypeAll<Item>())
            {
                if (i == null || !i.gameObject.scene.IsValid() || i.gameObject.name != name) continue;
                var p = i.transform.position;
                float d = (p.x - x) * (p.x - x) + (p.z - z) * (p.z - z);
                if (d <= bestD) { bestD = d; best = i; }
            }
            if (best == null && WorldActions.Debug) Plugin.Log.LogInfo($"[world] egg {obj} ({name}) not found here");
            return best;
        }
    }
}
