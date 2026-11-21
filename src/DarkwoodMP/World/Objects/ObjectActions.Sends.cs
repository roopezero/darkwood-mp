using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Inventories;
using DarkwoodMP.World.Objects;
using UnityEngine;
using Action = DarkwoodMP.World.WorldActions.Action;

namespace DarkwoodMP.World
{
    /// <summary>
    /// What this game sends of its smaller world objects (ObjectPatches):
    /// pickups, lures, feeders, gasoline lit, a thrown item's spawns, vine
    /// hits and shadows dying.
    /// </summary>
    internal sealed partial class ObjectActions
    {
        // ---- what the local player picks up -----------------------------

        /// <summary>
        /// The Item.activate call in progress, and whether it took the thing
        /// (a note read, a bag's experience).
        /// </summary>
        private Item _activating;
        private bool _took;

        /// <summary>
        /// Item.activate starts: watch it when it is a pickup that is sent.
        /// The ObjRef is taken now, while the object is as it was (default
        /// when not watched).
        /// </summary>
        public ObjRef BeforeActivate(Item item)
        {
            _activating = null;
            _took = false;
            if (!_coop.Guards.Sharing)
            {
                return default;
            }

            var note = item.GetComponent<JournalNoteReference>();
            bool kept = note != null && note.dontDestroy;
            bool drop = item.GetComponent<DeathDrop>() != null;
            if (!ObjectRules.WatchesPickup(note != null, kept, drop))
            {
                return default;
            }

            _activating = item;
            return ObjRef.Of(item.gameObject);
        }

        /// <summary>
        /// Item.activate returned: send Removed when it took the thing.
        /// </summary>
        public void AfterActivate(Item item, ObjRef before)
        {
            // both are reset in EndActivate
            bool took = _took && _activating == item;
            if (!took || !before.Valid || !_coop.Guards.Sharing)
            {
                return;
            }

            WorldActions.Send(Action.Removed, item.gameObject);
        }

        /// <summary>Item.activate is over, or threw.</summary>
        public void EndActivate()
        {
            _activating = null;
            _took = false;
        }

        /// <summary>
        /// The thing went in this game (a note picked up): the watched
        /// activate took it.
        /// </summary>
        public void TookIt(GameObject go)
        {
            if (_activating != null && go == _activating.gameObject)
            {
                _took = true;
            }
        }

        /// <summary>
        /// The DeathDrop branch of Item.activate gives the bag's experience
        /// (Player.gainExp) and destroys it.
        /// </summary>
        public void GainExp()
        {
            var a = _activating;
            if (a != null && a.GetComponent<DeathDrop>() != null)
            {
                TookIt(a.gameObject);
            }
        }

        /// <summary>
        /// Host: a lure its animals finished eating goes for everyone.
        /// </summary>
        public void LureHealthRemoved(Lure lure)
        {
            if (lure == null || lure.health > 0)
            {
                return;
            }

            var s = _coop.Net;
            if (s == null || !s.IsHost || !_coop.Guards.Sharing)
            {
                return;
            }

            WorldActions.Send(
                Action.Removed, lure.gameObject, byPlayerKnown: true);
        }

        // ---- feeders ----------------------------------------------------

        /// <summary>
        /// Feeder.activate starts: false (the game's call skipped) when a
        /// client's use waits for the host's yes, as only one player gets
        /// each day's feed.
        /// </summary>
        public bool BeforeFeeder(Feeder feeder)
        {
            var r = default(ObjRef);
            var facts = new FeederAskFacts
            {
                Sharing = _coop.Guards.Sharing,
                ClientMode = NetEnemies.ClientMode,
            };

            if (facts.Sharing && facts.ClientMode)
            {
                r = ObjRef.Of(feeder.gameObject);
                facts = facts with { Named = r.Valid };
            }

            if (facts.Named)
            {
                facts = facts with
                {
                    OursAlone = WorldActions.OursAlone(feeder.gameObject),
                };
            }

            if (!ObjectRules.AsksForFeeder(facts))
            {
                return true;
            }

            RequestFeeder(feeder, r);
            return false;
        }

        /// <summary>
        /// Feeder.activate returned: the host's own use goes to everyone
        /// (FeederUsed).
        /// </summary>
        public void AfterFeeder(Feeder feeder)
        {
            var s = _coop.Net;
            if (s == null || !s.IsHost || !_coop.Guards.Sharing)
            {
                return;
            }

            WorldActions.Send(
                Action.FeederUsed, feeder.gameObject, byPlayerKnown: true);
        }

        /// <summary>A client's use of a feeder, waiting for the host.</summary>
        private void RequestFeeder(Feeder feeder, ObjRef r)
        {
            var w = _coop.Net.Begin(MsgType.WorldAction);
            var body = new WorldActionBody
            {
                Action = (byte)Action.FeederUsed,
                Obj = ObjRef.NameOf(r),
                Text = "",
            };

            WorldActionMessage.Write(w, body);
            _coop.Authority.Request(w, $"FeederUsed {r} ({feeder.name})",
                (ok, reason, result) => OnFeederAnswer(feeder, ok));
        }

        /// <summary>
        /// Client: the host's answer to its player's use of a feeder.
        /// </summary>
        private void OnFeederAnswer(Feeder feeder, bool ok)
        {
            var facts = new FeederAnswerFacts
            {
                Here = feeder != null && Player.Instance != null,
                Ok = ok,
            };

            switch (ObjectRules.OnFeederAnswer(facts))
            {
                case FeederOutcome.Inactive:
                    Player.Instance.displayMessage(
                        Language.Get("Playermsg_feederInactive", "UI"));
                    break;
                case FeederOutcome.Activates:
                    _coop.Guards.Replaying = true;
                    try
                    {
                        feeder.activate();
                    }
                    finally
                    {
                        _coop.Guards.Replaying = false;
                    }

                    break;
            }
        }

        // ---- gasoline ---------------------------------------------------

        /// <summary>
        /// True while a burning liquid lights its neighbours
        /// (Liquid.waitToBurnNeighbors): a spread, not sent.
        /// </summary>
        public bool Spreading { get; private set; }

        /// <summary>
        /// True while the local player's melee sensor touches something.
        /// </summary>
        public bool OwnSwing { get; private set; }

        /// <summary>
        /// Liquid.startBurning starts: whether the liquid is out (not
        /// burning) before it.
        /// </summary>
        public bool BeforeLight(Liquid liquid)
        {
            return liquid != null && !liquid.burning;
        }

        /// <summary>
        /// Liquid.startBurning returned: send where a fire started (see
        /// the header, LiquidLit). wasOut: it wasn't burning before.
        /// </summary>
        public void AfterLight(Liquid liquid, bool wasOut)
        {
            var facts = new LitFacts
            {
                WasOut = wasOut,
                Spreading = Spreading,
            };

            if (wasOut)
            {
                facts = facts with
                {
                    Burning = liquid.burning,
                    Sharing = _coop.Guards.Sharing,
                    Client = _coop.Net.IsClient,
                    OwnSwing = OwnSwing,
                };
            }

            if (!ObjectRules.SendsLit(facts))
            {
                return;
            }

            WorldActions.Send(
                Action.LiquidLit, liquid.gameObject, byPlayerKnown: true);
        }

        /// <summary>
        /// A melee sensor touches something: is it our player's?
        /// </summary>
        public void SensorTouches(MeleeSensor sensor)
        {
            OwnSwing = sensor.type == MeleeSensor.MeleeSensorType.player;
        }

        /// <summary>MeleeSensor.OnTriggerEnter is over, or threw.</summary>
        public void SensorDone()
        {
            OwnSwing = false;
        }

        /// <summary>Liquid.waitToBurnNeighbors starts: a spread.</summary>
        public void SpreadStarts()
        {
            Spreading = true;
        }

        /// <summary>Liquid.waitToBurnNeighbors is over, or threw.</summary>
        public void SpreadOver()
        {
            Spreading = false;
        }

        // ---- a thrown item's spawns -------------------------------------

        /// <summary>
        /// A thrown item going off spawns things at random spots round it
        /// (Explodes.spawnObjects: a molotov's gasoline). Every thrown item
        /// goes off in the host's game too (its own, or its copy of a
        /// client's throw), so the host's are the ones shared
        /// (Containers.ThrownSpawns) and a client's game makes none of its
        /// own: otherwise each game had its gasoline in different spots and
        /// the fire the host lit in its own wasn't found in the others.
        /// False skips the game's call.
        /// </summary>
        public bool BeforeSpawn(Explodes ex)
        {
            var s = _coop.Net;
            if (s == null || !s.Active || !WorldShare.InWorld
                || ex == null || ex.spawnObject == null)
            {
                return true;
            }

            if (ex.GetComponent<ThrownItem>() == null)
            {
                return true; // only thrown items: those the host always has
            }

            string path = PathOf(ex.spawnObject);
            if (path == null || !Containers.ThrownSpawns.Contains(path))
            {
                return true;
            }

            if (s.IsClient)
            {
                if (!NetEnemies.ClientMode)
                {
                    return true;
                }

                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {ex.name} went off: its {path} comes "
                            + "from the host");
                }

                return false;
            }

            _coop.Containers.ThrownSpawning = path;
            return true;
        }

        /// <summary>Explodes.spawnObjects is over, or threw.</summary>
        public void EndSpawn()
        {
            _coop.Containers.ThrownSpawning = null;
        }

        private static string PathOf(Object prefab)
        {
            foreach (var path in Containers.ThrownSpawns)
            {
                var loaded = Resources.Load("Prefabs/" + path);
                if (loaded != null
                    && (loaded == prefab || loaded.name == prefab.name))
                {
                    return path;
                }
            }

            return null;
        }

        // ---- vines and shadows ------------------------------------------

        /// <summary>
        /// A ChainParent.getHit call: who hit, the damage, and the vine's
        /// health before it.
        /// </summary>
        public struct VineCut
        {
            public Transform Attacker;
            public float Damage;
            public float Before;
        }

        /// <summary>
        /// ChainParent.getHit starts: one call deeper. Its health now.
        /// </summary>
        public float BeforeVineHit(ChainParent cp)
        {
            _coop.Guards.Depth++;
            return cp.health;
        }

        /// <summary>
        /// ChainParent.getHit returned: at the outermost call, while
        /// sharing, send our players' hit that changed the vine's health.
        /// The detach it may make, and the parent's break in it, are
        /// nested.
        /// </summary>
        public void AfterVineHit(ChainParent cp, in VineCut cut)
        {
            if (!_coop.Guards.Outermost() || !_coop.Guards.Sharing
                || cp == null || cp.health == cut.Before)
            {
                return;
            }

            if (!BreakSync.OurPlayers(cut.Attacker))
            {
                return;
            }

            WorldActions.Send(
                Action.VineHit, ObjRef.Of(cp.gameObject),
                cp.name, cut.Damage, cp.health);
        }

        /// <summary>
        /// ShadowArmor.die starts: false (the game's call skipped) when a
        /// client's copy waits for the host's word. The host's ObjRef for
        /// the death to send, else default.
        /// </summary>
        public bool BeforeShadowDies(
            ShadowArmor sa, bool instant, out ObjRef dying)
        {
            dying = default;
            var facts = new ShadowFacts
            {
                Instant = instant,
                OnItem = sa != null
                    && sa.GetComponent<Item>() != null
                    && sa.GetComponent<Character>() == null,
                InSession = _coop.Net != null,
                InWorld = WorldShare.InWorld,
                Client = _coop.Net.IsClient,
                Host = _coop.Net.IsHost,
                Replaying = _coop.Guards.Replaying,
            };

            switch (ObjectRules.OnShadowDied(facts))
            {
                case ShadowDeath.Waits:
                    return false; // the host's word
                case ShadowDeath.DiesForAll:
                    dying = ObjRef.Of(sa.gameObject);
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>
        /// ShadowArmor.die returned: the host sends the death it named.
        /// </summary>
        public void AfterShadowDied(ShadowArmor sa, ObjRef dying)
        {
            if (!dying.Valid || !_coop.Guards.Sharing)
            {
                return;
            }

            string armorName = "shadow";
            if (sa != null)
            {
                armorName = sa.name;
            }

            WorldActions.Send(Action.ShadowArmorDied, dying, armorName);
        }
    }
}
