using HarmonyLib;
using UnityEngine;
using Action = DarkwoodMP.World.WorldActions.Action;

namespace DarkwoodMP.World
{
    // The smaller world objects' patches (ObjectActions). Each does nothing
    // with no session: playing alone is the game's own.

    /// <summary>
    /// Switching an InteractiveItem (from its window's button).
    /// </summary>
    [HarmonyPatch(typeof(InteractiveItem), nameof(InteractiveItem.switchMe))]
    static class SwitchMe
    {
        static void Postfix(InteractiveItem __instance)
        {
            var coop = Coop.Current;
            if (coop == null || !coop.Guards.Sharing)
            {
                return;
            }

            WorldActions.Send(
                Action.InteractiveSwitch, __instance.gameObject,
                f1: __instance.isOn);
        }
    }

    /// <summary>
    /// Item.activate: a note picked up off the ground, or a death bag's
    /// experience taken, destroys the object in this game only; send it as
    /// Removed.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.activate))]
    static class ItemActivate
    {
        static void Prefix(Item __instance, out ObjRef __state)
        {
            __state = default;
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            __state = coop.Objects.BeforeActivate(__instance);
        }

        static void Postfix(Item __instance, ObjRef __state)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Objects.AfterActivate(__instance, __state);
        }

        static System.Exception Finalizer(System.Exception __exception)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Objects.EndActivate();
            }

            return __exception;
        }
    }

    /// <summary>A note picked up: the watched Item.activate took it.</summary>
    [HarmonyPatch(
        typeof(JournalNoteReference), nameof(JournalNoteReference.pickup))]
    static class Pickup
    {
        static void Prefix(JournalNoteReference __instance)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Objects.TookIt(__instance.gameObject);
        }
    }

    /// <summary>
    /// The DeathDrop branch of Item.activate gives the bag's experience
    /// (Player.gainExp) and destroys it.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.gainExp))]
    static class GainExp
    {
        static void Prefix()
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Objects.GainExp();
        }
    }

    /// <summary>
    /// Host: a lure its animals finished eating goes for everyone.
    /// </summary>
    [HarmonyPatch(typeof(Lure), nameof(Lure.removeHealth))]
    static class RemoveHealth
    {
        static void Postfix(Lure __instance)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Objects.LureHealthRemoved(__instance);
        }
    }

    /// <summary>
    /// Feeders: the host's own use goes to everyone (FeederUsed); a client's
    /// waits for the host's yes, as only one player gets each day's feed.
    /// </summary>
    [HarmonyPatch(typeof(Feeder), nameof(Feeder.activate))]
    static class FeederActivate
    {
        static bool Prefix(Feeder __instance)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Objects.BeforeFeeder(__instance);
        }

        static void Postfix(Feeder __instance)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Objects.AfterFeeder(__instance);
        }
    }

    /// <summary>
    /// Spilt gasoline catching fire: send where it started (see
    /// ObjectActions, LiquidLit).
    /// </summary>
    [HarmonyPatch(typeof(Liquid), nameof(Liquid.startBurning))]
    static class StartBurning
    {
        static void Prefix(Liquid __instance, out bool __state)
        {
            __state = false;
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            __state = coop.Objects.BeforeLight(__instance);
        }

        static void Postfix(Liquid __instance, bool __state)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Objects.AfterLight(__instance, __state);
        }
    }

    /// <summary>
    /// A thrown item going off spawns things round it: the host's are the
    /// ones shared (ObjectActions.BeforeSpawn).
    /// </summary>
    [HarmonyPatch(typeof(Explodes), "spawnObjects")]
    static class SpawnObjects
    {
        static bool Prefix(Explodes __instance)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Objects.BeforeSpawn(__instance);
        }

        static System.Exception Finalizer(System.Exception __exception)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Objects.EndSpawn();
            }

            return __exception;
        }
    }

    /// <summary>A burning liquid lighting its neighbours: a spread.</summary>
    [HarmonyPatch(typeof(Liquid), "waitToBurnNeighbors")]
    static class WaitToBurnNeighbors
    {
        static void Prefix()
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Objects.SpreadStarts();
        }

        static System.Exception Finalizer(System.Exception __exception)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Objects.SpreadOver();
            }

            return __exception;
        }
    }

    /// <summary>A melee sensor touching something: our swing?</summary>
    [HarmonyPatch(typeof(MeleeSensor), "OnTriggerEnter")]
    static class OnTriggerEnter
    {
        static void Prefix(MeleeSensor __instance)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Objects.SensorTouches(__instance);
        }

        static System.Exception Finalizer(System.Exception __exception)
        {
            var coop = Coop.Current;
            if (coop != null)
            {
                coop.Objects.SensorDone();
            }

            return __exception;
        }
    }

    /// <summary>
    /// A vine cut at (ObjectActions, VineHit). The detach it may make, and
    /// the parent's break in it, are nested.
    /// </summary>
    [HarmonyPatch(
        typeof(ChainParent), nameof(ChainParent.getHit),
        new[] { typeof(float), typeof(Transform), typeof(Vine) })]
    static class ChainParentGetHit
    {
        static void Prefix(ChainParent __instance, out float __state)
        {
            __state = 0f;
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            __state = coop.Objects.BeforeVineHit(__instance);
        }

        static void Postfix(
            ChainParent __instance, float damage, Transform attackerTransform,
            float __state)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            var cut = new ObjectActions.VineCut
            {
                Attacker = attackerTransform,
                Damage = damage,
                Before = __state,
            };

            coop.Objects.AfterVineHit(__instance, cut);
        }
    }

    /// <summary>
    /// The shadow on a shadow-covered item died (ObjectActions,
    /// ShadowArmorDied). The host sends it; on a client the shadow dies only
    /// when the host says so, whatever its own copy's light check and hits
    /// came to. A shadow on a character (a night enemy) is the enemy's: left
    /// to the game here and to the host's enemies.
    /// die(instant: true) comes only from Item.loadValues, when the save
    /// being loaded says the shadow is already dead. Everyone loaded the
    /// host's save, so it runs in every game and isn't sent (a joiner who
    /// loads after the shadow died would otherwise keep it, and the item
    /// under it stays shut).
    /// </summary>
    [HarmonyPatch(typeof(ShadowArmor), nameof(ShadowArmor.die))]
    static class ShadowArmorDie
    {
        static bool Prefix(
            ShadowArmor __instance, bool instant, out ObjRef __state)
        {
            __state = default;
            var coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            return coop.Objects.BeforeShadowDies(
                __instance, instant, out __state);
        }

        static void Postfix(ShadowArmor __instance, ObjRef __state)
        {
            var coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            coop.Objects.AfterShadowDied(__instance, __state);
        }
    }
}
