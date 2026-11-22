using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Inventories
{
    /// <summary>
    /// Containers: things dropped or placed while playing together, sent once
    /// at rest (ObjSpawn).
    /// </summary>
    internal sealed partial class Containers
    {
        // ---- things dropped while playing together --------------------------

        private sealed class Pending
        {
            public long Id;
            public GameObject Go;
            public string Prefab;
            public bool Placed;
            public float SendAt;
        }

        private readonly List<Pending> _pending = new List<Pending>();

        /// <summary>
        /// A random id for something spawned while playing
        /// (ObjRef.RegisterSpawned).
        /// </summary>
        private long NewId() => ((long)_rng.Next() << 31) | (uint)_rng.Next();

        /// <summary>
        /// Core.AddPrefab made something; share it if it's a shared kind (and
        /// we made it, not a replay).
        /// </summary>
        public void Made(string prefab, GameObject go)
        {
            // Not dropped in a dream of our own: nobody else is there.
            if (!SpawnRules.Shares(FactsOf(prefab, go)))
            {
                return;
            }

            long id = NewId();
            ObjRef.RegisterSpawned(id, go);
            Seconds delay = SpawnRules.SendDelay(Placing, prefab);
            _pending.Add(new Pending
            {
                Id = id,
                Go = go,
                Prefab = prefab,
                Placed = Placing,
                SendAt = Time.time + delay.Value
            });
        }

        /// <summary>
        /// What the spawn rules look at in something Core.AddPrefab made:
        /// the only code that reads it for them.
        /// </summary>
        private MadeFacts FactsOf(string prefab, GameObject go)
        {
            var facts = new MadeFacts
            {
                Exists = go != null,
                Replaying = Coop.Guards.Replaying,
                Sharing = Sharing,
                Prefab = prefab,
                Placing = Placing,
            };

            if (go != null)
            {
                facts = facts with { OursAlone = WorldActions.OursAlone(go) };
            }

            return facts;
        }

        /// <summary>
        /// The Object overload of Core.AddPrefab made something: share it if
        /// our player is placing it.
        /// </summary>
        public void MadeFrom(Object prefab, GameObject go)
        {
            if (_makingDepth > 0)
            {
                return;
            }

            var step = SpawnRules.FromPrefab(FactsOf(prefab, go));
            if (step == MadeFromStep.Thrown)
            {
                ShareThrownSpawn(ThrownSpawning, go);
            }
            else if (step == MadeFromStep.NotPlaceable)
            {
                Plugin.Log.LogWarning(
                    $"Placed {go.name}, which isn't a placeable prefab here: "
                        + "not shared");
            }
            else if (step == MadeFromStep.Share)
            {
                Made(PlaceablePath(prefab), go);
            }
        }

        /// <summary>
        /// What MadeFrom's rule looks at in something made from a prefab
        /// object: the only code that reads it for that rule. The placeable
        /// path is only looked up once the checks before it passed, as
        /// before.
        /// </summary>
        private MadeFromFacts FactsOf(Object prefab, GameObject go)
        {
            var facts = new MadeFromFacts
            {
                Thrown = ThrownSpawning != null,
                Placing = Placing,
                Exists = go != null,
                Replaying = Coop.Guards.Replaying,
                Sharing = Sharing,
            };

            if (facts.Thrown || !facts.Placing || !facts.Exists
                || facts.Replaying || !facts.Sharing)
            {
                return facts;
            }

            return facts with { Placeable = PlaceablePath(prefab) != null };
        }

        /// <summary>
        /// What a thrown item leaves behind when it goes off
        /// (Explodes.spawnObject: a molotov's gasoline), by prefab. The host's
        /// game makes it and sends it; a client's game makes none of its own
        /// (ObjectActions.BeforeSpawn), so the fire the host then lights in it
        /// (ObjectActions, LiquidLit) is found by ObjRef in every game.
        /// Accepted from the host only: Items/Gasoline is also what each game
        /// merges gasoline trails into by itself.
        /// </summary>
        public static readonly HashSet<string> ThrownSpawns =
            new HashSet<string> { SpawnRules.ThrownGasoline };

        /// <summary>
        /// Set while an Explodes on a thrown item spawns its objects on the
        /// host: their prefab path.
        /// </summary>
        internal string ThrownSpawning;

        /// <summary>
        /// How deep inside the string overload of Core.AddPrefab we are,
        /// which makes it with the Object overload: shared from there only
        /// (MadeFrom is skipped while it's above 0).
        /// </summary>
        private int _makingDepth;

        /// <summary>
        /// The string overload of Core.AddPrefab starts (AddPrefabByPath).
        /// </summary>
        public void MakingStarts()
        {
            _makingDepth++;
        }

        /// <summary>
        /// The string overload of Core.AddPrefab is done, or threw.
        /// </summary>
        public void MakingEnds()
        {
            _makingDepth--;
            if (_makingDepth < 0)
            {
                _makingDepth = 0;
            }
        }

        /// <summary>
        /// Host: a thrown item that went off made this (ThrownSpawns): send it
        /// now, before the fire that lights it.
        /// </summary>
        private void ShareThrownSpawn(string prefab, GameObject go)
        {
            if (!SpawnRules.SharesThrown(go != null, _session.IsHost, Sharing))
            {
                return;
            }

            long id = NewId();
            ObjRef.RegisterSpawned(id, go);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent {prefab} #{id} at {go.transform.position} "
                        + "(left by a thrown item)");
            }

            SendSpawn(
                id, prefab, go.transform.position, go.transform.rotation, "",
                false, 0, except: 0);
        }

        /// <summary>
        /// Our player has just placed something (the end of
        /// Player.progressBarCompleted): send it now, in the frame the item was
        /// used up, so a client's request carries that and the item comes back
        /// if the host refuses.
        /// </summary>
        public void SendPlacedNow() => SendPendingSpawns(placedNow: true);

        /// <summary>
        /// Player.progressBarCompleted starts: our player may be placing
        /// something. A client's character goes to the host first, the item
        /// to place still in it (it's used up below), so the host's check of
        /// what it carries (OnSpawn) sees what was picked up or made a moment
        /// ago.
        /// </summary>
        public void PlacingStarts(Player player)
        {
            Placing = player.placingItem;
            if (player.placingItem && NetEnemies.ClientMode
                && Coop.Guards.Sharing)
            {
                CoopSession coop = Coop.Current;
                if (coop != null)
                {
                    coop.Characters.SaveNow();
                }
            }
        }

        /// <summary>
        /// Player.progressBarCompleted is done: what was placed goes now
        /// (SendPlacedNow).
        /// </summary>
        public void PlacingEnds()
        {
            bool placed = Placing;
            Placing = false;
            if (placed)
            {
                SendPlacedNow();
            }
        }

        private void SendPendingSpawns(bool placedNow = false)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                if (p.Go == null)
                {
                    _pending.RemoveAt(i);
                    continue;
                }

                bool condition;
                if (placedNow)
                {
                    condition = !p.Placed;
                }
                else
                {
                    condition = !SpawnRules.Due(
                        new Seconds(Time.time), new Seconds(p.SendAt));
                }

                if (condition)
                {
                    continue;
                }

                _pending.RemoveAt(i);
                var inv = p.Go.GetComponent<Inventory>();
                string contents = "";
                if (inv != null)
                {
                    contents = Serialize(inv);
                }

                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] sent {p.Prefab} #{p.Id} at "
                            + $"{p.Go.transform.position}");
                }

                var drop = p.Go.GetComponent<DeathDrop>();
                if (SpawnRules.AsksHost(p.Placed, _session.IsClient))
                {
                    // Placed by our player: the host decides (within reach, a
                    // thing a player can place). Refused, ours goes, and the
                    // item used up comes back (InventoryWatch).
                    var go = p.Go;
                    long id = p.Id;
                    string prefab = p.Prefab;
                    var w = SpawnMessage(
                        id, prefab, go.transform.position,
                        go.transform.rotation, contents, true, 0);
                    Coop.Current.Authority.Request(w, $"placing {prefab} #{id}",
                        (ok, why, result) =>
                    {
                        if (ok || go == null)
                        {
                            return;
                        }

                        ObjRef.Spawned.Remove(id);
                        CloseIfOpen(go);
                        Object.Destroy(go);
                        Plugin.Log.LogInfo(
                            $"[world] our {prefab} #{id} was refused: removed "
                                + "it");
                    });

                    continue;
                }

                int exp = 0;
                if (drop != null)
                {
                    exp = drop.expAmount;
                }

                SendSpawn(
                    p.Id, p.Prefab, p.Go.transform.position,
                    p.Go.transform.rotation, contents, p.Placed, exp,
                    except: 0);
                if (drop != null && drop.additionalMapMarker != null)
                {
                    SendBagMarker(
                        p.Id, drop.additionalMapMarker.transform.position,
                        except: 0);
                }
            }
        }
    }
}
