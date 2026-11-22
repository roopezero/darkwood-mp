using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Breaking;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Furniture and other destructible items, and exploding barrels and gas
    /// bottles, shared.
    ///
    /// A hit on an item (Item.getHit) is sent as ItemHit with the damage and
    /// the health it left; the others make the same call, which breaks the item
    /// there when its health runs out (Item.die, setDeathState), so a hit that
    /// breaks something is one message (an egg broken open is EggSync's
    /// EggDestroyed instead). An item broken without a hit is ItemBroken, a
    /// repair (setNormalState, from building) ItemRepaired. An Explodes going
    /// off (Explodes.onActivate) is Explode; the others set off their copy, and
    /// what its explosion breaks or damages there isn't sent again.
    ///
    /// The host sends everything that happens in its world (its enemies' hits,
    /// fire, explosions, the host's player). A client sends only its own
    /// player's hits and shots, as requests: the host checks reach and the
    /// numbers, applies them to its copy and passes them on. A client's copy of
    /// an explosion hurts none of the host's enemies (ThrownHits counts every
    /// explosion), since the host's copy does that, and it doesn't damage
    /// doors, windows or furniture either: no hit inside an explosion or from a
    /// flame other than the client's own weapon's is made on a client
    /// (HostsHit), and the host's copy sends the hits it makes. Thrown things
    /// that explode are ThrowSync's: every game has a copy of the throw, so
    /// their explosions aren't sent here.
    /// </summary>
    public static class BreakSync
    {
        private static readonly
            AccessTools.FieldRef<Explodes, bool> ActivatedField =
                AccessTools.FieldRefAccess<Explodes, bool>("activated");
        private static readonly
            AccessTools.FieldRef<Explodes, bool> FlamingField =
                AccessTools.FieldRefAccess<Explodes, bool>("flaming");

        /// <summary>
        /// Whether this Explodes has gone off (it goes off once).
        /// </summary>
        public static bool Activated(Explodes ex) =>
            ex != null && ActivatedField(ex);
        public static bool Flaming(Explodes ex) =>
            ex != null && FlamingField(ex);

        /// <summary>
        /// Client: whether a hit on a door, window or furniture happening now
        /// is the host's to make, so this game doesn't make it. That's anything
        /// inside an explosion or a thrown thing's impact (ThrownHits.Depth
        /// counts every Explodes, whether set off here by our shot, a replay or
        /// a chain, and the look of the host's enemies' explosions), and any
        /// flame but our own weapon's (a molotov's fire, a barrel's, the
        /// world's). The host runs the same explosion and fire on its copy and
        /// sends each hit it makes (DoorHit, WindowHit, ItemHit), which this
        /// game applies outside any explosion or flame: so each hit lands here
        /// once.
        /// Burn's damage over time doesn't run on a client at all (FireSync's
        /// ClientBurnDamagePatch).
        /// </summary>
        internal static bool HostsHit()
        {
            return BreakRules.HostsHit(HitOf(null));
        }

        /// <summary>
        /// Client: our own weapon's flame (a flamethrower) is touching
        /// something now. The host has no copy of that flame, so the hit it
        /// makes on a door, window or furniture is ours to send, as a request.
        /// </summary>
        internal static bool OwnFlame()
        {
            return BreakRules.OwnFlame(HitOf(null));
        }

        /// <summary>
        /// Client: whether a change to an item happening now is our own
        /// player's (and so ours to send).
        /// </summary>
        internal static bool OurPlayers(Transform attacker)
        {
            switch (BreakRules.OwnerOf(HitOf(attacker)))
            {
                case HitOwner.Ours:
                    return true;
                case HitOwner.Others:
                    return false;
                default:
                    return WorldActions.ByLocalPlayer();
            }
        }

        /// <summary>
        /// Where a hit happening now comes from: the only code that reads it
        /// for BreakRules. attacker may be null (none given).
        /// </summary>
        private static HitFacts HitOf(Transform attacker)
        {
            // The rest matters only on a client: not read on the host.
            if (!NetEnemies.ClientMode)
            {
                return default;
            }

            var flame = ThrownHits.CurrentFlame;
            var p = Player.Instance;
            return new HitFacts
            {
                ClientMode = true,
                ThrownDepth = ThrownHits.Depth,
                Flame = flame != null,
                FlameOurs = flame != null
                    && EnemyAreaHits.ByLocalPlayer(flame),
                AttackerIsPlayer = attacker != null && p != null
                    && attacker == p.transform,
            };
        }

        private static readonly
            AccessTools.FieldRef<ShadowArmor, float> ShadowHealthField =
                AccessTools.FieldRefAccess<ShadowArmor, float>("destHealth");

        /// <summary>
        /// The health a shadow-covered item's shadow is going down to (a hit
        /// lowers it, not the item's health); -1 without one.
        /// </summary>
        public static float ShadowHealth(Item i)
        {
            if (i.shadowArmor != null)
            {
                return ShadowHealthField(i.shadowArmor);
            }

            return -1f;
        }

        /// <summary>
        /// An item's health, whether it's broken, and its shadow's health: a
        /// hit on a shadow-covered item changes only the last.
        /// </summary>
        public static ItemState StateOf(Item i)
        {
            return new ItemState
            {
                Health = i.health,
                Destroyed = i.destroyed,
                Shadow = ShadowHealth(i),
            };
        }

        /// <summary>
        /// One Item.getHit: who made it, its damage, whether it was a normal
        /// hit, and the item's state before it.
        /// </summary>
        public struct Hit
        {
            public Transform Attacker;
            public int Damage;
            public bool Normal;
            public ItemState Before;
        }

        /// <summary>
        /// Item.getHit is starting: one call deeper, and its state before.
        /// False when the hit is the host's to make (an explosion, fire): the
        /// host's ItemHit is made here instead.
        /// </summary>
        internal static bool BeforeHit(Item item, out ItemState before)
        {
            Coop.Guards.Depth++;
            before = StateOf(item);
            return !HostsHit();
        }

        /// <summary>
        /// Item.getHit ran: our player's hit is sent, and what it hits moves
        /// with it.
        /// </summary>
        internal static void AfterHit(Item item, in Hit hit)
        {
            bool counts = BreakRules.Counts(
                Coop.Guards.Outermost(), Coop.Guards.Sharing, item != null);
            if (!counts)
            {
                return;
            }

            bool ours = OurPlayers(hit.Attacker);
            // Our player's hit pushes what it hits (a shot's AddForceAtPosition
            // comes right after it): where it comes to rest is our player's
            // doing, however far away they stood.
            if (ours)
            {
                CoopSession coop = Coop.Current;
                if (coop != null)
                {
                    coop.Drags.Hit(item);
                }
            }

            var facts = new ItemHitFacts
            {
                Before = hit.Before,
                After = StateOf(item),
                Ours = ours,
                Destructible = item.destructible,
                OpensByBreaking = item.destroyToAccessInventory,
                Name = item.gameObject.name,
            };

            switch (BreakRules.HitSend(facts))
            {
                case ItemSend.EggDestroyed:
                    // Eggs's message breaks it in the other games,
                    // wherever it is there.
                    Coop.Current.Eggs.Send(item);
                    return;
                case ItemSend.ItemHit:
                    WorldActions.Send(
                        WorldActions.Action.ItemHit,
                        ObjRef.Of(item.gameObject), item.name, hit.Damage,
                        item.health, f1: hit.Normal);
                    return;
            }
        }

        /// <summary>
        /// Item.die ran: broken by itself (not by a hit, which ItemHit
        /// carries, breaking it in the other games too).
        /// </summary>
        internal static void AfterBreak(Item item, bool was)
        {
            if (!BreakRules.Broke(ChangeOf(item, was)) || !OurPlayers(null))
            {
                return;
            }

            ItemSend send = BreakRules.BrokenAs(
                item.destructible, item.destroyToAccessInventory,
                item.gameObject.name);
            if (send == ItemSend.EggDestroyed)
            {
                Coop.Current.Eggs.Send(item);
                return;
            }

            WorldActions.Send(
                WorldActions.Action.ItemBroken, ObjRef.Of(item.gameObject),
                item.name);
        }

        /// <summary>
        /// Item.setNormalState ran: a broken item repaired
        /// (Constructible.construct calls it).
        /// </summary>
        internal static void AfterRepair(Item item, bool was)
        {
            if (!BreakRules.Repaired(ChangeOf(item, was)) || !OurPlayers(null))
            {
                return;
            }

            WorldActions.Send(
                WorldActions.Action.ItemRepaired, ObjRef.Of(item.gameObject),
                item.name);
        }

        /// <summary>
        /// An Item.die or setNormalState that ran, for BreakRules: the
        /// guards are asked first (Outermost leaves the call).
        /// </summary>
        private static ItemChangeFacts ChangeOf(Item item, bool was)
        {
            var facts = new ItemChangeFacts
            {
                Outermost = Coop.Guards.Outermost(),
                Sharing = Coop.Guards.Sharing,
                Exists = item != null,
                WasDestroyed = was,
            };

            if (item == null)
            {
                return facts;
            }

            return facts with { Destroyed = item.destroyed };
        }

        /// <summary>
        /// An explosive as it goes off, for BreakRules.Tracks: the only code
        /// that reads it. ex may be null.
        /// </summary>
        private static ExplodeFacts ExplodeOf(Explodes ex)
        {
            if (ex == null)
            {
                return default;
            }

            return new ExplodeFacts
            {
                Exists = true,
                Activated = Activated(ex),
                Proxy = ex.GetComponent<ProxyItem>() != null,
                Thrown = ex.GetComponent<ThrownItem>() != null
                    || ThrownHits.Copies.Contains(ex.gameObject),
                Character = ex.GetComponent<Character>() != null,
                ByEnemy = EnemyAreaHits.ByEnemy(
                    ex.transform, ex.objectThatSpawnedMe),
            };
        }

        /// <summary>The guards' depth and replay flag now.</summary>
        private static Breaking.Guards GuardsNow()
        {
            return new Breaking.Guards
            {
                Depth = Coop.Guards.Depth,
                Replaying = Coop.Guards.Replaying,
            };
        }

        /// <summary>Set the guards' depth and replay flag.</summary>
        private static void SetGuards(Breaking.Guards set)
        {
            Coop.Guards.Depth = set.Depth;
            Coop.Guards.Replaying = set.Replaying;
        }

        /// <summary>
        /// Item.die or setNormalState is starting: one call deeper, and
        /// whether the item was broken before.
        /// </summary>
        internal static bool BeforeChange(Item item)
        {
            Coop.Guards.Depth++;
            return item.destroyed;
        }

        /// <summary>
        /// A tracked explosive going off: its name, whether this game hosts,
        /// and the guards from before it.
        /// </summary>
        public struct Explosion
        {
            public bool Track;
            public ObjRef Ref;
            public string Name;
            public bool Host;
            public Breaking.Guards Saved;
        }

        /// <summary>
        /// Explodes.onActivate is starting: an exploding barrel or gas bottle
        /// set off in this game (a shot, fire, a chain from another
        /// explosion). onActivate(bool) calls onActivate(), so this covers
        /// both. Named before the call: it may destroy itself.
        ///
        /// On the host, each hit the explosion makes on a door, window or
        /// furniture is sent (DoorHit, WindowHit, ItemHit), since the clients'
        /// copies of it make none (HostsHit). So inside it the host counts as
        /// outermost and sharing, even when the explosion is nested in a hit
        /// (a shot barrel) or a replay of a client's Explode or ItemHit; the
        /// nesting and the Replaying flag are put back after it, so the
        /// Explode itself is still sent only when it's outermost and not a
        /// replay.
        /// </summary>
        internal static Explosion BeforeExplode(Explodes ex)
        {
            if (!BreakRules.Tracks(ExplodeOf(ex)))
            {
                return default;
            }

            bool host = Plugin.Instance.Host.Session.IsHost;
            var now = GuardsNow();
            var bomb = new Explosion
            {
                Track = true,
                Ref = ObjRef.Of(ex.gameObject),
                Name = ex.name,
                Host = host,
                Saved = now,
            };

            SetGuards(BreakRules.Enter(host, now));
            return bomb;
        }

        /// <summary>
        /// Explodes.onActivate ran: the guards are put back, and our player's
        /// explosion is sent.
        /// </summary>
        internal static void AfterExplode(Explodes ex, in Explosion bomb)
        {
            if (!bomb.Track)
            {
                return;
            }

            SetGuards(BreakRules.Leave(bomb.Host, bomb.Saved, GuardsNow()));
            var facts = new ExplodeSendFacts
            {
                Outermost = Coop.Guards.Outermost(),
                Sharing = Coop.Guards.Sharing,
                Activated = Activated(ex),
                ClientMode = NetEnemies.ClientMode,
            };

            switch (BreakRules.ExplodeOwner(facts))
            {
                case HitOwner.Others:
                    return;
                case HitOwner.AskStack:
                    if (!WorldActions.ByLocalPlayer())
                    {
                        // our player's shot; fire and chains are the host's
                        return;
                    }

                    break;
            }

            WorldActions.Send(
                WorldActions.Action.Explode, bomb.Ref, bomb.Name,
                f1: Flaming(ex));
        }
    }
}
