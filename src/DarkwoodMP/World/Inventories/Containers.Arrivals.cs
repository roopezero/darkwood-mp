using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Inventories
{
    /// <summary>
    /// Containers: the others' dropped and placed things arriving here
    /// (ObjSpawn, DeathBagMarker), and the host's enemies' loot.
    /// </summary>
    internal sealed partial class Containers
    {
        /// <summary>
        /// The map marker the game puts for a death bag left in a separate
        /// location (Player.dropBody).
        /// </summary>
        public const string DeathBagMarkerPrefab = SpawnRules.BagMarker;

        private void SendBagMarker(long id, Vector3 pos, ulong except)
        {
            var w = _session.Begin(MsgType.DeathBagMarker);
            DeathBagMarkerMessage.Write(w, id, new Vec3(pos.x, pos.y, pos.z));
            Share(w, except);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent the map marker of death bag #{id} at {pos}");
            }
        }

        private void OnBagMarker(ulong from, PacketReader r)
        {
            if (!Accepts(from))
            {
                return;
            }

            long id = DeathBagMarkerMessage.Read(r, out Vec3 marker);
            var pos = new Vector3(marker.X, marker.Y, marker.Z);
            DeathDrop drop = null;
            if (ObjRef.Spawned.TryGetValue(id, out var go) && go != null)
            {
                drop = go.GetComponent<DeathDrop>();
            }

            if (WorldActions.Debug)
            {
                string bagName = "no such bag here";
                if (drop != null)
                {
                    bagName = drop.name;
                }

                Plugin.Log.LogInfo(
                    $"[world] got the map marker of death bag #{id}: "
                        + bagName);
            }

            if (!SpawnRules.TakesMarker(
                drop != null, drop != null && drop.additionalMapMarker != null))
            {
                return;
            }

            Coop.Guards.Replaying = true;
            try
            {
                drop.additionalMapMarker = Core.AddPrefab(
                    DeathBagMarkerPrefab,
                    pos,
                    Quaternion.Euler(SpawnRules.MarkerTilt, 0f, 0f),
                    null);
            }
            finally
            {
                Coop.Guards.Replaying = false;
            }

            if (_session.IsHost)
            {
                SendBagMarker(id, pos, except: from);
            }
        }

        private void SendSpawn(
            long id, string prefab, Vector3 pos, Quaternion rot,
            string contents, bool placed, int exp, ulong except)
        {
            var w = SpawnMessage(id, prefab, pos, rot, contents, placed, exp);
            Share(w, except);
        }

        private PacketWriter SpawnMessage(
            long id, string prefab, Vector3 pos, Quaternion rot,
            string contents, bool placed, int exp)
        {
            var w = _session.Begin(MsgType.ObjSpawn);
            ObjSpawnMessage.Write(
                w,
                new SpawnedThing
                {
                    Id = id,
                    Placed = placed,
                    Prefab = prefab,
                    Pos = new Vec3(pos.x, pos.y, pos.z),
                    Rot = new Quat(rot.x, rot.y, rot.z, rot.w),
                    Contents = contents,
                    Experience = exp,
                });

            return w;
        }

        /// <summary>
        /// What the spawn rules look at in something another game made: the
        /// only code that reads this game for them. A client's placed thing
        /// (on the host) is checked in the old order, each only once the one
        /// before it passed: a request, a placeable prefab, in reach, and the
        /// items its character carries (not known: no character from it this
        /// session).
        /// </summary>
        private SpawnFacts FactsOf(ulong from, in SpawnedThing thing)
        {
            string prefab = thing.Prefab;
            var facts = new SpawnFacts
            {
                IsHost = _session.IsHost,
                IsClient = _session.IsClient,
                Prefab = prefab,
                Placed = thing.Placed,
                Known = ObjRef.Spawned.ContainsKey(thing.Id),
                PrefabExists = Resources.Load("Prefabs/" + prefab) != null,
            };

            CoopSession coop = Coop.Current;
            if (!facts.IsHost || !facts.Placed || coop == null)
            {
                return facts;
            }

            facts = facts with
            {
                Handling = coop.Authority.Handling,
                Placeable = IsPlaceable(prefab),
            };

            if (!facts.Handling || !facts.Placeable)
            {
                return facts;
            }

            var pos = new Vector3(thing.Pos.X, thing.Pos.Y, thing.Pos.Z);
            facts = facts with
            {
                Reach = coop.Authority.ReachOf(
                    pos, WorldActions.Reach, prefab),
            };

            var chars = coop.Characters;
            if (facts.Reach.Refused || chars == null)
            {
                return facts;
            }

            int? carried = chars.Carries(from, PlacedWith(prefab));
            if (carried.HasValue)
            {
                facts = facts with
                {
                    CharacterKnown = true,
                    Carried = carried.Value,
                };
            }

            return facts;
        }

        private void OnSpawn(ulong from, PacketReader r)
        {
            if (!Accepts(from))
            {
                return;
            }

            SpawnedThing thing = ObjSpawnMessage.Read(r);
            long id = thing.Id;
            bool placed = thing.Placed;
            string prefab = thing.Prefab;
            var pos = new Vector3(thing.Pos.X, thing.Pos.Y, thing.Pos.Z);
            var rot = new Quaternion(
                thing.Rot.X, thing.Rot.Y, thing.Rot.Z, thing.Rot.W);
            string contents = thing.Contents;
            int exp = thing.Experience;
            var answer = SpawnRules.Receive(FactsOf(from, thing));
            if (answer.Step == SpawnStep.Ignore)
            {
                return;
            }

            if (answer.Step == SpawnStep.Unasked)
            {
                Plugin.Log.LogWarning(
                    $"[world] {_session.NameOf(from)} placed {prefab} "
                        + "without asking: ignored");
                return;
            }

            // One placed thing less in the client's character's bags
            // (PlacingStarts), even when its prefab isn't here.
            if (answer.CountsPlaced)
            {
                Coop.Current.Characters.Placed(from);
            }

            if (answer.Step == SpawnStep.Refuse)
            {
                Coop.Current.Authority.Refuse(answer.Verdict);
                if (answer.Verdict.Reason == DarkwoodMP.Refusal.NoSuchPrefab)
                {
                    Plugin.Log.LogWarning($"No prefab {prefab} here");
                }

                return;
            }

            bool thrownSpawn = answer.Thrown;

            GameObject go;
            Coop.Guards.Replaying = true;
            try
            {
                go = Core.AddPrefab(
                    prefab, pos, rot, Core.ItemContainer, worldSpace: true);
                if (go == null)
                {
                    return;
                }

                if (placed)
                {
                    var trap = go.GetComponent<Trigger>();
                    if (trap != null)
                    {
                        // as the game marks what a player sets
                        trap.setByPlayer = true;
                    }
                }

                if (!thrownSpawn)
                {
                    // (the host's isn't saved either)
                    Core.addToSaveable(go, isDynamic: true, assignID: placed);
                }

                WorldGrid grid = Singleton<WorldGrid>.Instance;
                if (grid != null)
                {
                    grid.registerToNode(go);
                }

                var rb = go.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.velocity = Vector3.zero;
                }
            }
            finally
            {
                Coop.Guards.Replaying = false;
            }

            ObjRef.RegisterSpawned(id, go);
            if (thrownSpawn)
            {
                // its flames count as thrown here, as our own copy's did
                ThrownHits.Spawned.Add(go);
            }

            var drop = go.GetComponent<DeathDrop>();
            if (drop != null)
            {
                // the experience its player lost, for whoever takes it
                drop.expAmount = exp;
            }

            var inv = go.GetComponent<Inventory>();
            if (inv != null && contents.Length > 0)
            {
                Apply(inv, contents);
            }

            if (WorldActions.Debug)
            {
                int count = 0;
                if (inv != null)
                {
                    count = inv.getAllItems().Count;
                }

                Plugin.Log.LogInfo(
                    $"[world] made {prefab} #{id} at {pos}: {count} items");
            }

            if (_session.IsHost)
            {
                // pass it on
                SendSpawn(
                    id, prefab, pos, rot, contents, placed, exp, except: from);
            }
        }

        // ---- the host's enemies' loot ---------------------------------------

        private readonly HashSet<Character> _corpsesSent =
            new HashSet<Character>();

        /// <summary>
        /// Host: when an enemy the clients see has died, send them what its
        /// corpse holds.
        /// </summary>
        private void SendCorpseLoot()
        {
            var ne = NetEnemies.Instance;
            if (ne == null)
            {
                return;
            }

            foreach (var c in ne.HostDeadSince(SpawnRules.CorpseDelay))
            {
                if (c == null || !_corpsesSent.Add(c))
                {
                    continue;
                }

                var inv = c.GetComponent<Inventory>();
                if (inv == null)
                {
                    continue;
                }

                Send(ObjRef.Of(c.gameObject), Serialize(inv), except: 0);
            }
        }
    }
}
