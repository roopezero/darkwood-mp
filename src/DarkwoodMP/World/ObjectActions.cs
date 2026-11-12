using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;
using Action = DarkwoodMP.World.WorldActions.Action;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Smaller world objects, shared as WorldAction kinds:
    ///
    ///   InteractiveSwitch  an InteractiveItem switched in its window (InteractiveItem.switchMe). The others set
    ///                      isOn; the switch's on/off events run in the switcher's game and, for a client's switch,
    ///                      in the host's too (outside the replay, so what they change there reaches everyone).
    ///   Removed            a note picked up off the ground (Item.activate's JournalNoteReference branch), a death
    ///                      bag's experience taken (its DeathDrop branch), and, from the host only, a lure its
    ///                      animals ate (Lure.removeHealth). The others destroy their copy.
    ///   FeederUsed         a feeder used (Feeder.activate): it's empty for everyone until the next dawn. A client's
    ///                      use waits for the host's yes before its player gets the feeder's effect, since the
    ///                      effect can't be taken back.
    ///
    ///   LiquidLit          spilt gasoline catching fire (Liquid.startBurning). Every trail a player pours is shared
    ///                      (Containers.SharedPrefabs), and fire spreads along it by itself in each game, so only
    ///                      where it started is sent: by the host, for any fire that starts there (not a spread),
    ///                      and by a client only when its own player's burning weapon touched it (MeleeSensor);
    ///                      the client's thrown matches and molotovs light it in the host's copy of the throw.
    ///
    ///   VineHit            a vine (ChainParent) cut at (ChainParent.getHit, from MeleeSensor). The host sends every
    ///                      hit in its world, with the health it left; a client sends its own player's, as a
    ///                      request (reach and a damage cap). Each game's own detach follows when health reaches 0,
    ///                      and with it the parent's break (Item.getHit(999), nested, so not sent again).
    ///   ShadowArmorDied    the shadow on a shadow-covered item (ShadowArmor.die) died. The host decides: hits on the
    ///                      item reach it as ItemHit (the shadow takes them), its light check runs there, and it
    ///                      sends the death. A client's own copy of the shadow never dies by itself.
    ///
    /// Keys and quest items aren't objects lying in the world: KeyReference / QuestItemReference.pickup is called
    /// on the item's database prefab when it's taken from a container or a dropped item, which is a slot change
    /// (SlotShare), so there's nothing more to remove for them.
    /// </summary>
    public static class ObjectActions
    {
        public static bool Handles(Action a) => a == Action.InteractiveSwitch || a == Action.Removed || a == Action.FeederUsed || a == Action.LiquidLit
            || a == Action.VineHit || a == Action.ShadowArmorDied;

        /// <summary>The most a player's swing takes off a vine in one hit.</summary>
        public const float VineDamageCap = 1000f;

        /// <summary>Host: whether a client's player could have done this here.</summary>
        internal static bool Allowed(Action a, ObjRef obj, bool on, float x)
        {
            var t = ObjRef.Resolve<Transform>(obj);
            if (t == null) { Authority.Refuse(a == Action.Removed ? "it's already gone" : "not here"); return false; }
            switch (a)
            {
                case Action.VineHit:
                {
                    var cp = t.GetComponent<ChainParent>();
                    if (cp == null) { Authority.Refuse("it isn't a vine"); return false; }
                    if (!cp.attached || cp.health <= 0f) { Authority.Refuse("it's already cut"); return false; }
                    if (x < 0f || x > VineDamageCap) { Authority.Refuse($"a hit of {x:0} is more than any weapon does"); return false; }
                    return Authority.InReach(t.position, WorldActions.ShotReach, t.name); // a vine is long: its root may be far from the cut
                }
                case Action.ShadowArmorDied: Authority.Refuse("the host decides when a shadow dies"); return false;
                case Action.InteractiveSwitch:
                {
                    var ii = t.GetComponent<InteractiveItem>();
                    if (ii == null) { Authority.Refuse("it isn't a switch"); return false; }
                    if (!Authority.InReach(t.position, WorldActions.Reach, t.name)) return false;
                    if (ii.isOn == on) { Authority.Refuse(on ? "it's already on" : "it's already off"); return false; }
                    return true;
                }
                case Action.Removed:
                    if (t.GetComponent<Lure>() != null) { Authority.Refuse("lures are eaten by the host's animals"); return false; }
                    if (t.GetComponent<JournalNoteReference>() == null && t.GetComponent<DeathDrop>() == null) { Authority.Refuse("it can't be picked up"); return false; }
                    return Authority.InReach(t.position, WorldActions.Reach, t.name);
                case Action.LiquidLit:
                {
                    var liquid = t.GetComponent<Liquid>();
                    if (liquid == null || !liquid.flammable) { Authority.Refuse("it can't burn"); return false; }
                    return Authority.InReach(t.position, 1500f, t.name);
                }
                case Action.FeederUsed:
                {
                    var feeder = t.GetComponent<Feeder>();
                    if (feeder == null) { Authority.Refuse("it isn't a feeder"); return false; }
                    if (!Authority.InReach(t.position, WorldActions.Reach, t.name)) return false;
                    if (!feeder.Active) { Authority.Refuse("the feeder was already used today"); return false; }
                    return true;
                }
            }
            return false;
        }

        /// <summary>Make the change in this game (under WorldActions.Replaying).</summary>
        internal static void Apply(Action a, ObjRef obj, bool on, float x, ref float y)
        {
            var t = ObjRef.Resolve<Transform>(obj);
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] got {a} {obj}: " + (t != null ? t.name : "not found here"));
            if (t == null) return;
            switch (a)
            {
                case Action.VineHit:
                {
                    var cp = t.GetComponent<ChainParent>();
                    if (cp == null || !cp.attached || cp.health <= 0f) break;
                    var s = Session.Instance;
                    // The host's own hit says the health it left, so a copy that missed a hit comes back in step.
                    if (s != null && !s.IsHost) cp.health = y + x;
                    var vine = cp.lastChainSegment != null ? cp.lastChainSegment.GetComponent<Vine>() : null;
                    if (vine == null) vine = cp.GetComponentInChildren<Vine>();
                    if (vine != null) cp.getHit(x, null, vine);
                    else { cp.health = Mathf.Max(0f, cp.health - x); if (cp.health <= 0f) cp.detach(); } // getHit needs a segment for its sound
                    if (s != null && s.IsHost) y = cp != null ? cp.health : 0f;
                    break;
                }
                case Action.ShadowArmorDied:
                {
                    var sa = t.GetComponent<ShadowArmor>();
                    if (sa != null) sa.die();
                    break;
                }
                case Action.InteractiveSwitch:
                {
                    var ii = t.GetComponent<InteractiveItem>();
                    if (ii != null) ii.isOn = on; // its events are the switcher's (and the host's), not ours
                    break;
                }
                case Action.Removed:
                    if (t.GetComponent<Item>() != null || t.GetComponent<Lure>() != null) Object.Destroy(t.gameObject);
                    break;
                case Action.FeederUsed:
                    t.GetComponent<Feeder>()?.makeInactive();
                    break;
                case Action.LiquidLit:
                {
                    var liquid = t.GetComponent<Liquid>();
                    if (liquid != null && !liquid.burning) liquid.startBurning();
                    break;
                }
            }
        }

        /// <summary>Host, after applying a client's action: what the host's own game does for it, outside the replay.</summary>
        internal static void AfterOnHost(Action a, ObjRef obj, bool on)
        {
            if (a != Action.InteractiveSwitch) return;
            var ii = ObjRef.Resolve<InteractiveItem>(obj);
            if (ii == null) return;
            try { (on ? ii.onTrigger : ii.offTrigger)?.fireEventTrigger(global::EventTrigger.Type.area, "", force: true); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't run the events of switch {ii.name}: {e.Message}"); }
        }

        // ---- what the local player picks up -------------------------------------------------------

        /// <summary>The Item.activate call in progress, and whether it took the thing (a note read, a bag's experience).</summary>
        internal static Item Activating;
        internal static bool Took;

        internal static void TookIt(GameObject go)
        {
            if (Activating != null && go == Activating.gameObject) Took = true;
        }

        /// <summary>A client's use of a feeder, waiting for the host.</summary>
        internal static void RequestFeeder(Feeder feeder, ObjRef r)
        {
            var s = Session.Instance;
            var w = s.Begin(MsgType.WorldAction);
            w.W.Write((byte)Action.FeederUsed);
            r.Write(w);
            w.W.Write(0f); w.W.Write(0f); w.W.Write((byte)0); w.WriteStr("");
            Authority.Request(w, $"FeederUsed {r} ({feeder.name})", (ok, reason, result) =>
            {
                if (feeder == null || Player.Instance == null) return;
                if (!ok) { Player.Instance.displayMessage(Language.Get("Playermsg_feederInactive", "UI")); return; }
                WorldActions.Replaying = true;
                try { feeder.activate(); }
                finally { WorldActions.Replaying = false; }
            });
        }
    }

    /// <summary>Switching an InteractiveItem (from its window's button).</summary>
    [HarmonyPatch(typeof(InteractiveItem), nameof(InteractiveItem.switchMe))]
    static class InteractiveSwitchPatch
    {
        static void Postfix(InteractiveItem __instance)
        {
            if (!WorldActions.Sharing) return;
            WorldActions.Send(Action.InteractiveSwitch, __instance.gameObject, f1: __instance.isOn);
        }
    }

    /// <summary>
    /// Item.activate: a note picked up off the ground, or a death bag's experience taken, destroys the object
    /// in this game only; send it as Removed. The ObjRef is taken before, while the object is as it was.
    /// </summary>
    [HarmonyPatch(typeof(Item), nameof(Item.activate))]
    static class PickupPatch
    {
        static void Prefix(Item __instance, out ObjRef __state)
        {
            __state = default;
            ObjectActions.Activating = null;
            ObjectActions.Took = false;
            if (!WorldActions.Sharing) return;
            var note = __instance.GetComponent<JournalNoteReference>();
            if ((note != null && !note.dontDestroy) || __instance.GetComponent<DeathDrop>() != null)
            {
                ObjectActions.Activating = __instance;
                __state = ObjRef.Of(__instance.gameObject);
            }
        }

        static void Postfix(Item __instance, ObjRef __state)
        {
            bool took = ObjectActions.Took && ObjectActions.Activating == __instance;
            ObjectActions.Activating = null;
            ObjectActions.Took = false;
            if (!took || !__state.Valid || !WorldActions.Sharing) return;
            WorldActions.Send(Action.Removed, __instance.gameObject);
        }

        static System.Exception Finalizer(System.Exception __exception) { ObjectActions.Activating = null; ObjectActions.Took = false; return __exception; }
    }

    [HarmonyPatch(typeof(JournalNoteReference), nameof(JournalNoteReference.pickup))]
    static class NotePickupPatch
    {
        static void Prefix(JournalNoteReference __instance) => ObjectActions.TookIt(__instance.gameObject);
    }

    /// <summary>The DeathDrop branch of Item.activate gives the bag's experience (Player.gainExp) and destroys it.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.gainExp))]
    static class DeathDropExpPatch
    {
        static void Prefix()
        {
            var a = ObjectActions.Activating;
            if (a != null && a.GetComponent<DeathDrop>() != null) ObjectActions.TookIt(a.gameObject);
        }
    }

    /// <summary>Host: a lure its animals finished eating goes for everyone.</summary>
    [HarmonyPatch(typeof(Lure), nameof(Lure.removeHealth))]
    static class LureEatenPatch
    {
        static void Postfix(Lure __instance)
        {
            if (__instance == null || __instance.health > 0) return;
            var s = Session.Instance;
            if (s == null || !s.IsHost || !WorldActions.Sharing) return;
            WorldActions.Send(Action.Removed, __instance.gameObject, byPlayerKnown: true);
        }
    }

    /// <summary>
    /// Feeders: the host's own use goes to everyone (FeederUsed); a client's waits for the host's yes, as only
    /// one player gets each day's feed.
    /// </summary>
    [HarmonyPatch(typeof(Feeder), nameof(Feeder.activate))]
    static class FeederPatch
    {
        static bool Prefix(Feeder __instance)
        {
            if (!WorldActions.Sharing || !NetEnemies.ClientMode) return true;
            var r = ObjRef.Of(__instance.gameObject);
            if (!r.Valid) return true;
            ObjectActions.RequestFeeder(__instance, r);
            return false;
        }

        static void Postfix(Feeder __instance)
        {
            var s = Session.Instance;
            if (s == null || !s.IsHost || !WorldActions.Sharing) return;
            WorldActions.Send(Action.FeederUsed, __instance.gameObject, byPlayerKnown: true);
        }
    }

    /// <summary>Spilt gasoline catching fire: send where it started (see ObjectActions, LiquidLit).</summary>
    [HarmonyPatch(typeof(Liquid), nameof(Liquid.startBurning))]
    static class LiquidLitPatch
    {
        /// <summary>True while a burning liquid lights its neighbours (Liquid.waitToBurnNeighbors): a spread, not sent.</summary>
        internal static bool Spreading;
        /// <summary>True while the local player's melee sensor touches something.</summary>
        internal static bool OwnSwing;

        static void Prefix(Liquid __instance, out bool __state) => __state = __instance != null && !__instance.burning;

        static void Postfix(Liquid __instance, bool __state)
        {
            if (!__state || Spreading || !__instance.burning || !WorldActions.Sharing) return;
            var s = Session.Instance;
            if (s.IsClient && !OwnSwing) return; // the host's copy of the throw or explosion lights it there
            WorldActions.Send(Action.LiquidLit, __instance.gameObject, byPlayerKnown: true);
        }
    }

    /// <summary>
    /// A thrown item going off spawns things at random spots round it (Explodes.spawnObjects: a molotov's gasoline).
    /// Every thrown item goes off in the host's game too (its own, or its copy of a client's throw), so the host's
    /// are the ones shared (Containers.ThrownSpawns) and a client's game makes none of its own: otherwise each game
    /// had its gasoline in different spots and the fire the host lit in its own wasn't found in the others.
    /// </summary>
    [HarmonyPatch(typeof(Explodes), "spawnObjects")]
    static class ThrownLeavesPatch
    {
        static bool Prefix(Explodes __instance)
        {
            var s = Session.Instance;
            if (s == null || !s.Active || !WorldShare.InWorld || __instance == null || __instance.spawnObject == null) return true;
            if (__instance.GetComponent<ThrownItem>() == null) return true; // only thrown items: those the host always has
            string path = PathOf(__instance.spawnObject);
            if (path == null || !Containers.ThrownSpawns.Contains(path)) return true;
            if (s.IsClient)
            {
                if (!NetEnemies.ClientMode) return true;
                if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] {__instance.name} went off: its {path} comes from the host");
                return false;
            }
            Containers.ThrownSpawning = path;
            return true;
        }

        static System.Exception Finalizer(System.Exception __exception) { Containers.ThrownSpawning = null; return __exception; }

        private static string PathOf(Object prefab)
        {
            foreach (var path in Containers.ThrownSpawns)
            {
                var loaded = Resources.Load("Prefabs/" + path);
                if (loaded != null && (loaded == prefab || loaded.name == prefab.name)) return path;
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(Liquid), "waitToBurnNeighbors")]
    static class LiquidSpreadPatch
    {
        static void Prefix() => LiquidLitPatch.Spreading = true;
        static System.Exception Finalizer(System.Exception __exception) { LiquidLitPatch.Spreading = false; return __exception; }
    }

    [HarmonyPatch(typeof(MeleeSensor), "OnTriggerEnter")]
    static class OwnSwingPatch
    {
        static void Prefix(MeleeSensor __instance) => LiquidLitPatch.OwnSwing = __instance.type == MeleeSensor.MeleeSensorType.player;
        static System.Exception Finalizer(System.Exception __exception) { LiquidLitPatch.OwnSwing = false; return __exception; }
    }

    /// <summary>A vine cut at (ObjectActions, VineHit). The detach it may make, and the parent's break in it, are nested.</summary>
    [HarmonyPatch(typeof(ChainParent), nameof(ChainParent.getHit), new[] { typeof(float), typeof(Transform), typeof(Vine) })]
    static class VineHitPatch
    {
        static void Prefix(ChainParent __instance, out float __state) { WorldActions.Depth++; __state = __instance.health; }
        static void Postfix(ChainParent __instance, float damage, Transform attackerTransform, float __state)
        {
            if (!WorldActions.Outermost() || !WorldActions.Sharing || __instance == null || __instance.health == __state) return;
            if (!BreakSync.OurPlayers(attackerTransform)) return;
            WorldActions.Send(Action.VineHit, ObjRef.Of(__instance.gameObject), __instance.name, damage, __instance.health);
        }
    }

    /// <summary>
    /// The shadow on a shadow-covered item died (ObjectActions, ShadowArmorDied). The host sends it; on a client the
    /// shadow dies only when the host says so, whatever its own copy's light check and hits came to. A shadow on a
    /// character (a night enemy) is the enemy's: left to the game here and to the host's enemies.
    /// </summary>
    [HarmonyPatch(typeof(ShadowArmor), nameof(ShadowArmor.die))]
    static class ShadowArmorDiedPatch
    {
        static bool OnItem(ShadowArmor sa) => sa != null && sa.GetComponent<Item>() != null && sa.GetComponent<Character>() == null;

        static bool Prefix(ShadowArmor __instance, out ObjRef __state)
        {
            __state = default;
            if (!OnItem(__instance) || Session.Instance == null || !WorldShare.InWorld) return true;
            if (Session.Instance.IsClient && !WorldActions.Replaying) return false; // the host's word
            if (Session.Instance.IsHost) __state = ObjRef.Of(__instance.gameObject);
            return true;
        }
        static void Postfix(ShadowArmor __instance, ObjRef __state)
        {
            if (!__state.Valid || !WorldActions.Sharing) return;
            WorldActions.Send(Action.ShadowArmorDied, __state, __instance != null ? __instance.name : "shadow");
        }
    }
}
