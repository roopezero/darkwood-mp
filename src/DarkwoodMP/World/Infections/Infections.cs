using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Infections
{
    /// <summary>
    /// Infection, shared: the patches of black growth on the ground
    /// (Traps/infection_splat: an Infection with a Ground of
    /// GroundType.infection that hurts 8 a second and stops running) that fire
    /// clears, as at the border between the Dry Meadow and the Silent Forest
    /// (border_gate_06_meadowForest_01 has nine).
    ///
    /// Cleared: Infection.disappear (a burning melee weapon touching it,
    /// MeleeSensor; a Burn on one clears every patch within 40; burning
    /// gasoline sets Burn on patches next to it) removes its Ground at once and
    /// fades it out. Every patch cleared in a frame goes in one
    /// InfectionCleared. The host sends all of its own (its world's fire, its
    /// player); a client sends only what its own player's burning weapon
    /// touched, as a request: the host clears those within reach of the player
    /// and passes them on. The others clear theirs with Replaying set, so a
    /// cleared patch is never sent back, and a patch already fading isn't
    /// cleared twice.
    ///
    /// Spread: a patch made by Infection.init spreads into up to 8 new ones
    /// around it after 25-30 s (spawnInfection), and spiders lay new ones
    /// (Character.waitToSpawnEgg). That's the world doing it by itself, so it's
    /// the host's: each new patch on the host is InfectionSpawned, made in the
    /// clients' games with the same id (ObjRef Spawned), and a client's
    /// infection never spreads by itself.
    /// </summary>
    internal sealed class Infections
    {
        private readonly CoopSession _coop;

        private readonly List<ClearedPatch> _cleared =
            new List<ClearedPatch>();

        public Infections(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.InfectionCleared, OnCleared);
            coop.Handle(MsgType.InfectionSpawned, OnSpawned);
        }

        private Session S => _coop.Net;

        /// <summary>
        /// Host: inside spawnInfection or a spider laying: a new patch made now
        /// is sent.
        /// </summary>
        public int Growing { get; set; }

        /// <summary>
        /// Before Infection.disappear: whether the patch was fading already
        /// (or isn't there), so the call can't have started its fade.
        /// </summary>
        public static bool WasFading(Infection inf)
        {
            return inf == null || inf.disappearing;
        }

        /// <summary>
        /// After Infection.disappear: send the clear if the rules say it's
        /// this game's to send.
        /// </summary>
        public void Disappeared(Infection inf, bool wasFading)
        {
            // A client's own player's burning weapon only; its copy of the
            // host's fire clears what the host's does, and the host says so.
            if (!InfectionRules.SendsClear(ClearOf(inf, wasFading)))
            {
                return;
            }

            Cleared(inf);
        }

        /// <summary>
        /// Before Infection.spawnInfection: whether it runs. One that runs
        /// counts as growing until EndGrowing.
        /// </summary>
        public bool BeginSpread()
        {
            bool spreads = InfectionRules.MaySpread(NetEnemies.ClientMode);
            if (spreads)
            {
                Growing++;
            }

            return spreads;
        }

        /// <summary>
        /// Before Character.waitToSpawnEgg: a spider laying grows infection.
        /// </summary>
        public void BeginLaying()
        {
            Growing++;
        }

        /// <summary>
        /// A counted spread or laying ended (or threw).
        /// </summary>
        public void EndGrowing()
        {
            Growing = InfectionRules.Left(Growing);
        }

        /// <summary>
        /// After Infection.init: send the patch if the world made it by
        /// itself on the host.
        /// </summary>
        public void Started(Infection inf)
        {
            if (!InfectionRules.SendsSpawned(SpawnOf(inf)))
            {
                return;
            }

            Spawned(inf);
        }

        /// <summary>
        /// A patch was cleared in this game, by something that's ours to send.
        /// </summary>
        private void Cleared(Infection inf)
        {
            var r = ObjRef.Of(inf.gameObject);
            if (!r.Valid)
            {
                return;
            }

            var p = inf.transform.position;
            _cleared.Add(new ClearedPatch
            {
                Patch = ObjRef.NameOf(r),
                At = new Vec2(p.x, p.z),
            });
        }

        /// <summary>
        /// Each frame: send what was cleared this frame, in one message.
        /// </summary>
        public void Update()
        {
            if (_cleared.Count == 0)
            {
                return;
            }

            if (!_coop.Guards.Sharing)
            {
                _cleared.Clear();
                return;
            }

            var w = S.Begin(MsgType.InfectionCleared);
            InfectionCleared.Write(w, _cleared);
            string what = $"infection cleared: {_cleared.Count}";
            _cleared.Clear();
            Plugin.Log.LogInfo($"[world] sent {what}");
            if (S.IsHost)
            {
                S.Broadcast(w, Channel.Control, reliable: true);
            }
            else
            {
                _coop.Authority.Request(w, what, inventory: false);
            }
        }

        private void OnCleared(ulong from, PacketReader r)
        {
            var s = S;
            if (!ArrivalRules.Takes(ArrivalOf(s, from)))
            {
                return;
            }

            List<ClearedPatch> sent = InfectionCleared.Read(r);
            int n = sent.Count;
            var done = new List<ClearedPatch>();
            foreach (var p in sent)
            {
                var inf = Find(p);
                // A client's player clears only what its burning weapon
                // reached.
                if (!InfectionRules.Clears(FactsOf(inf, s.IsHost)))
                {
                    continue;
                }

                _coop.Guards.Replaying = true;
                try
                {
                    Clear(inf);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning(
                        "Couldn't clear infection "
                            + $"{ObjRef.RefOf(p.Patch)}: {e.Message}");
                }
                finally
                {
                    _coop.Guards.Replaying = false;
                }

                done.Add(p);
            }

            string line =
                $"[world] infection cleared: {done.Count} (from "
                    + $"{s.NameOf(from)})";
            string missed = "";
            if (done.Count < n)
            {
                missed =
                    $", {n - done.Count} not here, already gone or out of "
                        + "reach";
            }

            Plugin.Log.LogInfo(line + missed);
            if (!s.IsHost)
            {
                return;
            }

            Verdict verdict = InfectionRules.MayClear(n, done.Count);
            if (verdict.Refused)
            {
                _coop.Authority.Refuse(verdict);
                return;
            }

            if (InfectionRules.Relays(done.Count))
            {
                var w = s.Begin(MsgType.InfectionCleared);
                InfectionCleared.Write(w, done);
                s.Broadcast(w, Channel.Control, reliable: true, except: from);
            }
        }

        /// <summary>
        /// What the arrival rules know of a message from a peer.
        /// </summary>
        private ArrivalFacts ArrivalOf(Session s, ulong from)
        {
            if (s == null)
            {
                return default;
            }

            return new ArrivalFacts
            {
                Session = true,
                InWorld = WorldShare.InWorld,
                Client = s.IsClient,
                FromHost = from == s.HostId,
            };
        }

        /// <summary>
        /// What the infection rules know of a patch of an InfectionCleared
        /// (null: not found here). The only code that reads the patch for
        /// them; on the host, the requester's reach.
        /// </summary>
        private PatchFacts FactsOf(Infection inf, bool host)
        {
            if (inf == null)
            {
                return new PatchFacts { Host = host };
            }

            var facts = new PatchFacts
            {
                Found = true,
                Fading = inf.disappearing,
                Host = host,
            };

            if (!host)
            {
                return facts;
            }

            var reach = _coop.Authority.ReachOf(inf.transform.position);
            return facts with { Reach = reach };
        }

        /// <summary>
        /// What the infection rules know of a patch told to fade: whether it
        /// was fading before the call (wasFading), and the game's state now.
        /// </summary>
        private ClearFacts ClearOf(Infection inf, bool wasFading)
        {
            var facts = new ClearFacts
            {
                WasFading = wasFading,
                Sharing = _coop.Guards.Sharing,
                ClientMode = NetEnemies.ClientMode,
                OwnSwing = _coop.Objects.OwnSwing,
            };

            if (inf == null)
            {
                return facts;
            }

            return facts with { Fading = inf.disappearing };
        }

        /// <summary>
        /// Host: what the infection rules know of a patch that starts.
        /// </summary>
        private SpawnFacts SpawnOf(Infection inf)
        {
            var s = S;
            return new SpawnFacts
            {
                Growing = Growing,
                Exists = inf != null,
                Host = s != null && s.IsHost,
                Sharing = _coop.Guards.Sharing,
            };
        }

        /// <summary>
        /// Clear it as the game does; one culled (inactive) here can't play its
        /// fade, so it's just removed.
        /// </summary>
        private static void Clear(Infection inf)
        {
            if (inf.gameObject.activeInHierarchy)
            {
                inf.disappear();
            }
            else
            {
                inf.disappearing = true;
                Object.Destroy(inf.gameObject);
            }
        }

        /// <summary>
        /// The patch by its name across games, or else the one nearest where it
        /// was (patches are 70 apart).
        /// </summary>
        private static Infection Find(in ClearedPatch p)
        {
            var obj = ObjRef.RefOf(p.Patch);
            var inf = ObjRef.Resolve<Infection>(obj);
            if (inf != null)
            {
                return inf;
            }

            float x = p.At.X;
            float z = p.At.Z;
            Infection best = null;
            float bestD = InfectionRules.FindRadius * InfectionRules.FindRadius;
            foreach (var i in Resources.FindObjectsOfTypeAll<Infection>())
            {
                if (i == null || !i.gameObject.scene.IsValid()
                    || i.disappearing)
                {
                    continue;
                }

                var pos = i.transform.position;
                float d = (pos.x - x) * (pos.x - x) + (pos.z - z) * (pos.z - z);
                if (d <= bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            if (best == null && WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[world] infection {obj} not found here");
            }

            return best;
        }

        // ---- spread (the host's) --------------------------------------------

        /// <summary>
        /// Host: a new patch was made by the world (spread or a spider): send
        /// it.
        /// </summary>
        private void Spawned(Infection inf)
        {
            long id = HostIds.NewId();
            ObjRef.RegisterSpawned(id, inf.gameObject);
            ObjRef parent = default;
            if (inf.transform.parent != null)
            {
                parent = ObjRef.Of(inf.transform.parent.gameObject);
            }

            var p = inf.transform.position;
            var w = S.Begin(MsgType.InfectionSpawned);
            InfectionSpawned.Write(w, new SpawnedPatch
            {
                Id = id,
                At = new Vec3(p.x, p.y, p.z),
                Parent = ObjRef.NameOf(parent),
            });

            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo(
                    $"[world] sent infection spawned Spawned:{id} at {p}");
            }

            S.Broadcast(w, Channel.Control, reliable: true);
        }

        private void OnSpawned(ulong from, PacketReader r)
        {
            var s = S;
            if (!ArrivalRules.TakesFromHost(ArrivalOf(s, from)))
            {
                return;
            }

            SpawnedPatch sent = InfectionSpawned.Read(r);
            long id = sent.Id;
            var pos = new Vector3(sent.At.X, sent.At.Y, sent.At.Z);
            var parentRef = ObjRef.RefOf(sent.Parent);
            bool known = ObjRef.Spawned.TryGetValue(id, out var had);
            if (!InfectionRules.MakesSpawned(known, known && had != null))
            {
                return;
            }

            Transform parent = null;
            if (parentRef.Valid)
            {
                parent = ObjRef.Resolve<Transform>(parentRef);
            }

            _coop.Guards.Replaying = true;
            try
            {
                GameObject parentGO;
                if (parent != null)
                {
                    parentGO = parent.gameObject;
                }
                else
                {
                    parentGO = Core.ItemContainer;
                }

                var go = Core.AddPrefab(
                    "Traps/infection_splat", pos, Quaternion.Euler(90f, 0f, 0f),
                    parentGO, worldSpace: true);
                Infection inf = null;
                if (go != null)
                {
                    inf = go.GetComponent<Infection>();
                }

                if (inf == null)
                {
                    return;
                }

                ObjRef.RegisterSpawned(id, go);
                // the host's spreads; its new patches come as messages
                inf.timesToSpread = 0;
                inf.init();
                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] got infection spawned Spawned:{id} at {pos}");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't make the host's infection: {e.Message}");
            }
            finally
            {
                _coop.Guards.Replaying = false;
            }
        }
    }
}
