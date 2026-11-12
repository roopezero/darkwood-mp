using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Furniture and other destructible items, and exploding barrels and gas bottles, shared.
    ///
    /// A hit on an item (Item.getHit) is sent as ItemHit with the damage and the health it left; the others make
    /// the same call, which breaks the item there when its health runs out (Item.die, setDeathState), so a hit
    /// that breaks something is one message (an egg broken open is EggSync's EggDestroyed instead). An item broken without a hit is ItemBroken, a repair (setNormalState,
    /// from building) ItemRepaired. An Explodes going off (Explodes.onActivate) is Explode; the others set off their
    /// copy, and what its explosion breaks or damages there isn't sent again.
    ///
    /// The host sends everything that happens in its world (its enemies' hits, fire, explosions, the host's player).
    /// A client sends only its own player's hits and shots, as requests: the host checks reach and the numbers,
    /// applies them to its copy and passes them on. A client's copy of an explosion hurts none of the host's
    /// enemies (ThrownHits counts every explosion), since the host's copy does that, and it doesn't damage doors,
    /// windows or furniture either: no hit inside an explosion or from a flame other than the client's own weapon's
    /// is made on a client (HostsHit), and the host's copy sends the hits it makes. Thrown things that explode
    /// are ThrowSync's: every game has a copy of the throw, so their explosions aren't sent here.
    /// </summary>
    public static class BreakSync
    {
        private static readonly AccessTools.FieldRef<Explodes, bool> ActivatedField = AccessTools.FieldRefAccess<Explodes, bool>("activated");
        private static readonly AccessTools.FieldRef<Explodes, bool> FlamingField = AccessTools.FieldRefAccess<Explodes, bool>("flaming");

        /// <summary>Whether this Explodes has gone off (it goes off once).</summary>
        public static bool Activated(Explodes ex) => ex != null && ActivatedField(ex);
        public static bool Flaming(Explodes ex) => ex != null && FlamingField(ex);

        /// <summary>
        /// Client: whether a hit on a door, window or furniture happening now is the host's to make, so this game
        /// doesn't make it. That's anything inside an explosion or a thrown thing's impact (ThrownHits.Depth counts
        /// every Explodes, whether set off here by our shot, a replay or a chain, and the look of the host's
        /// enemies' explosions), and any flame but our own weapon's (a molotov's fire, a barrel's, the world's).
        /// The host runs the same explosion and fire on its copy and sends each hit it makes (DoorHit, WindowHit,
        /// ItemHit), which this game applies outside any explosion or flame: so each hit lands here once.
        /// Burn's damage over time doesn't run on a client at all (FireSync's ClientBurnDamagePatch).
        /// </summary>
        internal static bool HostsHit()
        {
            if (!NetEnemies.ClientMode) return false;
            if (ThrownHits.Depth > 0) return true;
            var flame = ThrownHits.CurrentFlame;
            return flame != null && !EnemyAreaHits.ByLocalPlayer(flame);
        }

        /// <summary>
        /// Client: our own weapon's flame (a flamethrower) is touching something now. The host has no copy of that
        /// flame, so the hit it makes on a door, window or furniture is ours to send, as a request.
        /// </summary>
        internal static bool OwnFlame() =>
            NetEnemies.ClientMode && ThrownHits.Depth == 0 && ThrownHits.CurrentFlame != null && EnemyAreaHits.ByLocalPlayer(ThrownHits.CurrentFlame);

        /// <summary>Client: whether a change to an item happening now is our own player's (and so ours to send).</summary>
        internal static bool OurPlayers(Transform attacker)
        {
            if (!NetEnemies.ClientMode) return true;
            if (ThrownHits.Depth > 0) return false; // inside an explosion: the host's copy of it does the damage
            if (OwnFlame()) return true;
            if (attacker != null && Player.Instance != null && attacker == Player.Instance.transform) return true;
            return WorldActions.ByLocalPlayer();
        }

        private static readonly AccessTools.FieldRef<ShadowArmor, float> ShadowHealthField = AccessTools.FieldRefAccess<ShadowArmor, float>("destHealth");

        /// <summary>The health a shadow-covered item's shadow is going down to (a hit lowers it, not the item's health); -1 without one.</summary>
        public static float ShadowHealth(Item i) => i.shadowArmor != null ? ShadowHealthField(i.shadowArmor) : -1f;

        /// <summary>An item's health, whether it's broken, and its shadow's health: a hit on a shadow-covered item changes only the last.</summary>
        public struct ItemState
        {
            public int Health; public bool Destroyed; public float Shadow;
            public static ItemState Of(Item i) => new ItemState { Health = i.health, Destroyed = i.destroyed, Shadow = ShadowHealth(i) };
            public bool Same(ItemState o) => Health == o.Health && Destroyed == o.Destroyed && Shadow == o.Shadow;
        }
    }

    [HarmonyPatch(typeof(Item), nameof(Item.getHit), new[] { typeof(int), typeof(Transform), typeof(bool) })]
    static class ItemHitPatch
    {
        /// <summary>On a client, a hit the host makes too (an explosion, fire) isn't made here: the host's ItemHit is.</summary>
        static bool Prefix(Item __instance, out BreakSync.ItemState __state)
        {
            WorldActions.Depth++;
            __state = BreakSync.ItemState.Of(__instance);
            return !BreakSync.HostsHit();
        }
        static void Postfix(Item __instance, int damage, Transform attackerTransform, bool normalHit, BreakSync.ItemState __state)
        {
            if (!WorldActions.Outermost() || !WorldActions.Sharing || __instance == null) return;
            if (__state.Same(BreakSync.ItemState.Of(__instance)) || !BreakSync.OurPlayers(attackerTransform)) return;
            // An egg this hit broke: EggSync's message breaks it in the other games, wherever it is there.
            if (!__state.Destroyed && __instance.destroyed && EggSync.IsEgg(__instance)) { EggSync.Send(__instance); return; }
            WorldActions.Send(WorldActions.Action.ItemHit, ObjRef.Of(__instance.gameObject), __instance.name, damage, __instance.health, f1: normalHit);
        }
    }

    /// <summary>Broken by itself (not by a hit, which ItemHit carries, breaking it in the other games too).</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.die))]
    static class ItemBrokenPatch
    {
        static void Prefix(Item __instance, out bool __state) { WorldActions.Depth++; __state = __instance.destroyed; }
        static void Postfix(Item __instance, bool __state)
        {
            if (!WorldActions.Outermost() || !WorldActions.Sharing || __instance == null || __state || !__instance.destroyed) return;
            if (!BreakSync.OurPlayers(null)) return;
            if (EggSync.IsEgg(__instance)) { EggSync.Send(__instance); return; }
            WorldActions.Send(WorldActions.Action.ItemBroken, ObjRef.Of(__instance.gameObject), __instance.name);
        }
    }

    /// <summary>A broken item repaired (Constructible.construct calls setNormalState).</summary>
    [HarmonyPatch(typeof(Item), nameof(Item.setNormalState))]
    static class ItemRepairedPatch
    {
        static void Prefix(Item __instance, out bool __state) { WorldActions.Depth++; __state = __instance.destroyed; }
        static void Postfix(Item __instance, bool __state)
        {
            if (!WorldActions.Outermost() || !WorldActions.Sharing || __instance == null || !__state || __instance.destroyed) return;
            if (!BreakSync.OurPlayers(null)) return;
            WorldActions.Send(WorldActions.Action.ItemRepaired, ObjRef.Of(__instance.gameObject), __instance.name);
        }
    }

    /// <summary>
    /// An exploding barrel or gas bottle set off in this game (a shot, fire, a chain from another explosion).
    /// onActivate(bool) calls onActivate(), so this covers both. Named before the call: it may destroy itself.
    ///
    /// On the host, each hit the explosion makes on a door, window or furniture is sent (DoorHit, WindowHit,
    /// ItemHit), since the clients' copies of it make none (BreakSync.HostsHit). So inside it the host counts as
    /// outermost and sharing, even when the explosion is nested in a hit (a shot barrel) or a replay of a client's
    /// Explode or ItemHit; the nesting and the Replaying flag are put back after it, so the Explode itself is still
    /// sent only when it's outermost and not a replay.
    /// </summary>
    [HarmonyPatch(typeof(Explodes), nameof(Explodes.onActivate), new System.Type[0])]
    static class ExplodePatch
    {
        public struct State { public bool Track; public ObjRef Ref; public string Name; public bool Host; public int Depth; public bool Replaying; }

        static void Prefix(Explodes __instance, out State __state)
        {
            __state = default;
            if (__instance == null || BreakSync.Activated(__instance) || __instance.GetComponent<ProxyItem>() != null) return;
            if (__instance.GetComponent<ThrownItem>() != null || ThrownHits.Copies.Contains(__instance.gameObject)) return; // ThrowSync's
            // An enemy (a kamikaze) or what an enemy threw: those happen only on the host, which sends EnemyExplosion
            // for their sound and flash (EnemyAreaHits); a client's frozen copy must never go off.
            if (__instance.GetComponent<Character>() != null || EnemyAreaHits.ByEnemy(__instance.transform, __instance.objectThatSpawnedMe)) return;
            __state = new State { Track = true, Ref = ObjRef.Of(__instance.gameObject), Name = __instance.name };
            if (Session.Instance != null && Session.Instance.IsHost)
            {
                __state.Host = true; __state.Depth = WorldActions.Depth; __state.Replaying = WorldActions.Replaying;
                WorldActions.Depth = 0; WorldActions.Replaying = false;
            }
            else WorldActions.Depth++;
        }

        static void Postfix(Explodes __instance, State __state)
        {
            if (!__state.Track) return;
            if (__state.Host) { WorldActions.Depth = __state.Depth + 1; WorldActions.Replaying = __state.Replaying; }
            if (!WorldActions.Outermost() || !WorldActions.Sharing || !BreakSync.Activated(__instance)) return;
            if (NetEnemies.ClientMode && !WorldActions.ByLocalPlayer()) return; // our player's shot; fire and chains are the host's
            WorldActions.Send(WorldActions.Action.Explode, __state.Ref, __state.Name, f1: BreakSync.Flaming(__instance));
        }
    }
}
