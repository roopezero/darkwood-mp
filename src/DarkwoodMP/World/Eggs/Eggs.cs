using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Eggs
{
    /// <summary>
    /// Larva eggs (Objects/larva_egg_big, "Egg": "Something is floating
    /// inside... Maybe I could puncture or pierce it") and other items that are
    /// broken open to get at what's inside (Item.destroyToAccessInventory),
    /// shared.
    ///
    /// The egg is an Item with 5 health and an Inventory that holds the Embryo
    /// (lifePotion). Breaking it (Item.die, from a hit, fire or an explosion)
    /// plays its burst animation, makes it a trigger collider on layer 14 and
    /// lets its inventory be opened; nothing is spawned. So the break is the
    /// one thing to share: EggDestroyed, sent by the game whose player (or, on
    /// the host, whose world) broke it, instead of the ItemHit or ItemBroken
    /// BreakSync would send. The others break their copy with Item.die, found
    /// even while it's culled (inactive) in their game, which is where
    /// ItemHit's name lookup could miss it. The Embryo is in the egg's
    /// inventory in every game (everyone loaded the host's save) and taking it
    /// is Containers' (slot by slot, the host's call), so it's taken once.
    /// Item.die sends the egg's onDeath trigger, which is the host's world's to
    /// run: a joiner's game leaves its own to the host and runs the shared part
    /// of the host's (ScriptedEvents).
    ///
    /// A client's break is a request: the host checks the player is in reach
    /// and the egg isn't broken already, breaks its own and passes it on; if it
    /// refuses, the client gets the egg's state back (WorldActions.SendState).
    /// </summary>
    internal sealed class Eggs
    {
        private readonly CoopSession _coop;

        public Eggs(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.EggDestroyed, OnDestroyed);
        }

        private Session S => _coop.Net;

        /// <summary>
        /// Whether this item is an egg (broken open to get at its contents).
        /// </summary>
        public static bool IsEgg(Item i)
        {
            if (i == null)
            {
                return false;
            }

            return EggRules.IsEgg(
                i.destructible, i.destroyToAccessInventory, i.gameObject.name);
        }

        /// <summary>
        /// This game broke the egg; the caller checked it's ours to send
        /// (BreakSync.OurPlayers).
        /// </summary>
        public void Send(Item egg)
        {
            // in a dream of our own: nobody else has it
            bool alone = WorldActions.OursAlone(egg.gameObject);
            var r = default(ObjRef);
            if (!alone)
            {
                r = ObjRef.Of(egg.gameObject);
            }

            if (!EggRules.Sends(alone, r.Valid))
            {
                return;
            }

            // The hit that broke it can leave its health below 0; the others'
            // copies are broken at 0 (Break), and so is ours, so its state is
            // the same in every game.
            egg.health = EggRules.SentHealth(egg.health);
            var w = Write(BreakOf(egg, r));
            Plugin.Log.LogInfo(
                $"[world] sent egg {r} destroyed ({egg.gameObject.name})");
            if (S.IsHost)
            {
                S.Broadcast(w, Channel.Control, reliable: true);
            }
            else
            {
                _coop.Authority.Request(
                    w, $"egg {r} destroyed ({egg.gameObject.name})");
            }
        }

        private void OnDestroyed(ulong from, PacketReader r)
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

            EggBreak sent = EggDestroyed.Read(r);
            var obj = ObjRef.RefOf(sent.Egg);
            var egg = Find(obj, sent);
            // a client's break comes only as a request (Authority drops a bare
            // one)
            if (s.IsHost)
            {
                Verdict verdict = EggRules.MayBreak(FactsOf(egg));
                if (verdict.Refused)
                {
                    WorldActions.SendState(from, obj);
                    _coop.Authority.Refuse(verdict);
                    return;
                }

                if (!EggRules.Breaks(true, egg.destroyed))
                {
                    // broken here already (by the host, or another player):
                    // nothing to do or pass on
                    return;
                }
            }

            string line =
                $"[world] egg {obj} destroyed (from {s.NameOf(from)})";
            string here = "";
            if (egg == null)
            {
                here = ": not found here";
            }
            else if (egg.destroyed)
            {
                here = ": already broken here";
            }

            Plugin.Log.LogInfo(line + here);
            if (!EggRules.Breaks(egg != null, egg != null && egg.destroyed))
            {
                return;
            }

            _coop.Guards.Replaying = true;
            try
            {
                Break(egg);
            }
            finally
            {
                _coop.Guards.Replaying = false;
            }

            if (s.IsHost)
            {
                s.Broadcast(
                    Write(sent), Channel.Control, reliable: true,
                    except: from);
            }
        }

        private PacketWriter Write(in EggBreak egg)
        {
            var w = S.Begin(MsgType.EggDestroyed);
            EggDestroyed.Write(w, egg);
            return w;
        }

        private static EggBreak BreakOf(Item egg, ObjRef r)
        {
            var p = egg.transform.position;
            return new EggBreak
            {
                Egg = ObjRef.NameOf(r),
                At = new Vec2(p.x, p.z),
                Name = egg.gameObject.name,
            };
        }

        /// <summary>
        /// Host: what the egg rules know of the egg a client says it broke
        /// (null: not found here). The only code that reads the egg for them.
        /// </summary>
        private EggFacts FactsOf(Item egg)
        {
            if (egg == null)
            {
                return default;
            }

            return new EggFacts
            {
                Found = true,
                Destructible = egg.destructible,
                OpensByBreaking = egg.destroyToAccessInventory,
                Destroyed = egg.destroyed,
                Name = egg.gameObject.name,
                Reach = _coop.Authority.ReachOf(egg.transform.position),
            };
        }

        /// <summary>
        /// Break the egg as the game does. A culled (inactive) one can't play
        /// its animation, so it's left in its broken state.
        /// </summary>
        private static void Break(Item egg)
        {
            egg.health = 0;
            if (egg.gameObject.activeInHierarchy)
            {
                egg.die();
                return;
            }

            egg.destroyed = true;
            try
            {
                egg.setDeathState();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't show {egg.gameObject.name} broken: {e.Message}");
            }

            Core.sendTriggerInfo(
                egg.gameObject, global::EventTrigger.Type.onDeath);
        }

        /// <summary>
        /// The egg by its name across games, or else the one of that name
        /// nearest where it was, active or not.
        /// </summary>
        private static Item Find(ObjRef obj, in EggBreak sent)
        {
            string name = sent.Name;
            float x = sent.At.X;
            float z = sent.At.Z;
            var egg = ObjRef.Resolve<Item>(obj);
            if (egg != null)
            {
                return egg;
            }

            Item best = null;
            float bestD = EggRules.FindRadius * EggRules.FindRadius;
            foreach (var i in Resources.FindObjectsOfTypeAll<Item>())
            {
                if (i == null || !i.gameObject.scene.IsValid()
                    || i.gameObject.name != name)
                {
                    continue;
                }

                var p = i.transform.position;
                float d = (p.x - x) * (p.x - x) + (p.z - z) * (p.z - z);
                if (d <= bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            if (best == null && WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] egg {obj} ({name}) not found here");
            }

            return best;
        }
    }
}
