using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using DarkwoodMP.World.Throws;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Thrown things (molotovs, pipe bombs, rocks, flares...), shared. When a
    /// player throws one (Player.throwItem), the same item is thrown in every
    /// other game, from the same spot, with the same velocity and target, so
    /// everyone sees it fly, land, explode and burn. One dropped at the
    /// player's feet instead (Player.dropItem:
    /// a dropOnReleaseAim item when the player stops aiming, or a held
    /// throwable when the player is interrupted) is sent the same way with
    /// distance -1, and dropped (ThrownItem.dropMe) where it fell in the other
    /// games.
    ///
    /// Who it hurts is the host's to decide, as with every other hit on its
    /// enemies: the host's copy (or its own throw) deals the damage, and on a
    /// client nothing a thrown thing does to the host's enemies is sent as a
    /// hit (not its impact, its explosion, or the flames it leaves; see
    /// ThrownHits).
    ///
    /// Something thrown that can be picked up again (a knife, a brick) lands
    /// only in the thrower's game: the others' copies fly and hit, then go, so
    /// it isn't there to pick up twice.
    /// </summary>
    internal sealed class ThrowSync
    {
        private readonly Session _session;
        private static readonly
            AccessTools.FieldRef<Player, GameObject> HeldItem =
                AccessTools.FieldRefAccess<Player, GameObject>("heldItem");

        public ThrowSync(CoopSession coop)
        {
            _session = coop.Net;
            coop.Handle(MsgType.ItemThrown, OnThrown);
        }

        internal static GameObject Held(Player p) => HeldItem(p);

        /// <summary>
        /// What the player holds ready to throw or drop, and its item type
        /// (before Player.throwItem / dropItem).
        /// </summary>
        internal static (GameObject go, string type) Holding(Player p)
        {
            var cur = p.currentItem;
            GameObject held = HeldItem(p);
            string type = null;
            if (!InvItemClass.isNull(cur))
            {
                InvItem kind = cur.baseClass;
                if (kind != null)
                {
                    type = kind.type;
                }
            }

            return (held, type);
        }

        /// <summary>
        /// The local player threw it (called after Player.throwItem).
        /// </summary>
        internal void Thrown(string type, GameObject go, Vector3 from)
        {
            if (!ThrowRules.Shares(FactsOf(go, type)))
            {
                return;
            }

            var ti = go.GetComponent<ThrownItem>();
            var rb = go.GetComponent<Rigidbody>();
            ThrowShot shot = ThrowRules.Thrown(
                VecOf(rb.velocity), VecOf(ti.landTarget), VecOf(from));
            Send(type, go, shot);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[world] threw {type}");
            }
        }

        /// <summary>
        /// The local player dropped it at their feet (after
        /// Player.dropItem).
        /// </summary>
        internal void Dropped(string type, GameObject go)
        {
            if (!ThrowRules.Shares(FactsOf(go, type)))
            {
                return;
            }

            Send(type, go, ThrowRules.Dropped(VecOf(go.transform.position)));
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[world] dropped {type}");
            }
        }

        private static ThrowFacts FactsOf(GameObject go, string type)
        {
            var facts = new ThrowFacts
            {
                Sharing = Coop.Guards.Sharing,
                Type = type,
            };

            if (go == null)
            {
                return facts;
            }

            return facts with
            {
                Held = true,
                Thrown = go.GetComponent<ThrownItem>() != null,
                Body = go.GetComponent<Rigidbody>() != null,
            };
        }

        private void Send(string type, GameObject go, ThrowShot shot)
        {
            var w = _session.Begin(MsgType.ItemThrown);
            ItemThrown.Write(
                w, Report(_session.LocalId, type, go.transform, shot));
            if (_session.IsHost)
            {
                _session.Broadcast(w, Channel.Control, reliable: true);
            }
            else
            {
                _session.SendToHost(w, Channel.Control, reliable: true);
            }
        }

        /// <summary>
        /// The copy of a throw doesn't collide with its thrower's stand-in (the
        /// host's proxy) or puppet.
        /// </summary>
        private static void IgnoreThrower(GameObject go, ulong by)
        {
            var mine = go.GetComponentsInChildren<Collider>(true);
            if (mine.Length == 0)
            {
                return;
            }

            var theirs = new List<Collider>();
            if (NetPlayerProxy.All.TryGetValue(by, out var proxy)
                && proxy != null)
            {
                theirs.AddRange(proxy.GetComponentsInChildren<Collider>(true));
            }

            if (PlayerSync.Instance != null
                && PlayerSync.Instance.Remotes.TryGetValue(by, out var rp)
                && rp.Root != null)
            {
                theirs.AddRange(
                    rp.Root.GetComponentsInChildren<Collider>(true));
            }

            foreach (var a in mine)
            {
                foreach (var b in theirs)
                {
                    if (a != null && b != null)
                    {
                        Physics.IgnoreCollision(a, b);
                    }
                }
            }
        }

        private static ThrowReport Report(
            ulong by, string type, Transform at, ThrowShot shot)
        {
            Quaternion rot = at.rotation;
            return new ThrowReport
            {
                By = by,
                Type = type,
                Pos = VecOf(at.position),
                Rot = new Quat(rot.x, rot.y, rot.z, rot.w),
                Shot = shot,
            };
        }

        private static Vec3 VecOf(Vector3 v) => new Vec3(v.x, v.y, v.z);

        private static Vector3 VectorOf(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        /// <summary>
        /// What OnThrown decides from: this game, the sender, and where the
        /// sender's stand-in is (the host's check of where it was thrown
        /// from).
        /// </summary>
        private ThrownFacts FactsOf(ulong from, in ThrowReport report)
        {
            var s = _session;
            var facts = new ThrownFacts
            {
                InWorld = WorldShare.InWorld,
                IsHost = s.IsHost,
                IsClient = s.IsClient,
                From = from,
                HostId = s.HostId,
                LocalId = s.LocalId,
                ReportedBy = report.By,
                Pos = report.Pos.Ground,
            };

            NetPlayerProxy.All.TryGetValue(from, out var proxy);
            if (proxy == null)
            {
                return facts;
            }

            Vector3 at = proxy.transform.position;
            return facts with { Known = true, StandIn = new Vec2(at.x, at.z) };
        }

        private void OnThrown(ulong from, PacketReader r)
        {
            var s = _session;
            ThrowReport report = ItemThrown.Read(r);
            ThrowReceipt receipt = ThrowRules.Receive(FactsOf(from, report));
            string type = report.Type;
            if (receipt.Refused)
            {
                Plugin.Log.LogInfo(
                    $"[authority] {s.NameOf(from)} threw a {type} from where "
                        + "they aren't: not thrown");
                return;
            }

            ulong by = receipt.By;
            if (receipt.Relays)
            {
                var w = s.Begin(MsgType.ItemThrown);
                ItemThrown.Write(w, report with { By = by });
                s.Broadcast(w, Channel.Control, reliable: true, except: from);
            }

            if (!receipt.Copies)
            {
                return;
            }

            var pos = VectorOf(report.Pos);
            var rot = new Quaternion(
                report.Rot.X, report.Rot.Y, report.Rot.Z, report.Rot.W);
            var vel = VectorOf(report.Shot.Velocity);
            var target = VectorOf(report.Shot.Target);
            float distance = report.Shot.Distance;
            ItemsDatabase instance = Singleton<ItemsDatabase>.Instance;
            InvItem item = null;
            if (instance != null)
            {
                item = instance.getItem(type, instantiate: false);
            }

            if (item == null || item.item == null)
            {
                Plugin.Log.LogWarning(
                    $"Someone threw a {type}, which this game doesn't know");
                return;
            }

            // Who threw it, as the game's code sees them: the host's proxy for
            // them (the AI's view of that player, so the host's enemies blame
            // the right player), else their puppet.
            Transform thrower = null;
            if (NetPlayerProxy.All.TryGetValue(by, out var proxy)
                && proxy != null)
            {
                thrower = proxy.transform;
            }
            else if (PlayerSync.Instance != null
                && PlayerSync.Instance.Remotes.TryGetValue(by, out var rp)
                && rp.Root != null)
            {
                thrower = rp.Root.transform;
            }

            GameObject go;
            Coop.Guards.Replaying = true;
            try
            {
                go = Core.AddPrefab(item.item, pos, rot, null);
            }
            finally
            {
                Coop.Guards.Replaying = false;
            }

            if (go == null)
            {
                return;
            }

            var ti = go.GetComponent<ThrownItem>();
            var rb = go.GetComponent<Rigidbody>();
            if (ti == null || rb == null)
            {
                Object.Destroy(go);
                return;
            }

            var ren = go.GetComponent<Renderer>();
            if (ren != null)
            {
                ren.enabled = true;
            }

            // The thrower's game makes it inside their player and keeps it from
            // hitting them (Player's aim: Physics.IgnoreCollision with the
            // player). The copy starts inside their stand-in here: without the
            // same, a molotov or gas bomb broke on the thrower at once and set
            // them alight.
            IgnoreThrower(go, by);
            CopyEnd end = ThrowRules.Landing(new ThrowCopyFacts
            {
                Distance = distance,
                Recoverable = item.recoverableAfterThrown,
                IsHost = s.IsHost,
            });

            if (ThrowRules.IsDrop(distance))
            {
                // Dropped, as Player.dropItem does it: it lies where it fell
                // and goes off after its delay.
                rb.velocity = Vector3.zero;
                ti.objectThatSpawnedMe = thrower;
                var dex = go.GetComponent<Explodes>();
                if (dex != null)
                {
                    dex.objectThatSpawnedMe = thrower;
                }

                ThrownHits.Copies.Add(go);
                ti.dropMe();
                Land(go, ti, end);

                if (WorldActions.Debug)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {s.NameOf(by)} dropped {type}");
                }

                return;
            }

            rb.drag = 2f;
            rb.velocity = vel;
            if (ti.initialRotationForce == 0f)
            {
                rb.angularVelocity = Vector3.zero;
            }
            else
            {
                rb.AddTorque(0f, ti.initialRotationForce * 1000f, 0f);
            }

            ti.landTarget = target;
            ti.thrown = true;
            ti.objectThatSpawnedMe = thrower;
            ti.setFallSpeed(distance);
            var ex = go.GetComponent<Explodes>();
            if (ex != null)
            {
                ex.objectThatSpawnedMe = thrower;
            }

            ThrownHits.Copies.Add(go);
            Land(go, ti, end);
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[world] {s.NameOf(by)} threw {type}");
            }
        }

        /// <summary>
        /// What becomes of the copy (ThrowRules.Landing): one that can be
        /// picked up again goes after its flight (the thrower's lands); the
        /// host keeps the rest (a flare left burning, a spent molotov) in
        /// its save.
        /// </summary>
        private static void Land(GameObject go, ThrownItem ti, CopyEnd end)
        {
            if (end == CopyEnd.Goes)
            {
                Object.Destroy(go, ThrowRules.GoesAfter(ti.flyTime).Value);
            }
            else if (end == CopyEnd.Saved)
            {
                Core.addToSaveable(go, isDynamic: true);
            }
        }

        /// <summary>
        /// Before Player.throwItem or dropItem: what the player holds. Nothing
        /// with no session.
        /// </summary>
        internal static (GameObject go, string type) Before(Player p)
        {
            if (Coop.Current == null)
            {
                return default;
            }

            return Holding(p);
        }

        /// <summary>
        /// After Player.throwItem: the player threw what they held.
        /// </summary>
        internal static void AfterThrow(
            Player p, (GameObject go, string type) held)
        {
            CoopSession coop = Coop.Current;
            if (coop == null || held.go == null)
            {
                return;
            }

            coop.Throws.Thrown(held.type, held.go, p.transform.position);
        }

        /// <summary>
        /// After Player.dropItem: an item that's dropped when the player stops
        /// aiming (InvItem.dropOnReleaseAim), or the held throwable when the
        /// player is interrupted (force), falls at the player's feet; it's
        /// sent like a throw.
        /// </summary>
        internal static void AfterDrop(
            Player p, (GameObject go, string type) held)
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return;
            }

            bool before = held.go != null;
            bool now = Held(p) != null;
            if (ThrowRules.WasDropped(before, now))
            {
                coop.Throws.Dropped(held.type, held.go);
            }
        }

        /// <summary>
        /// A flame starts touching something (Flame.onCollideWith). Noted for
        /// ThrownHits, and: on the host, an enemy's flame on a proxy hurts
        /// that player (EnemyAreaHits.FlameTouch); on a client, our own
        /// weapon's flame (a flamethrower) sends its burn with the hit on the
        /// host's enemy (NetEnemies.HitEffects). Returns what our flame is
        /// about to set alight, if anything, for FlameTouched.
        /// </summary>
        internal static GameObject FlameTouching(
            Flame flame, Transform at, Collider col)
        {
            if (Coop.Current == null)
            {
                return null;
            }

            ThrownHits.CurrentFlame = flame;
            EnemyAreaHits.FlameTouch(flame, at, col);
            if (!NetEnemies.ClientMode || flame == null || !flame.isActive
                || !EnemyAreaHits.ByLocalPlayer(flame))
            {
                return null;
            }

            if (flame.effect != null)
            {
                NetEnemies.HitEffects =
                    new List<InvItemEffect> { flame.effect };
            }

            var target = FireSync.Flammable(at, col);
            if (target != null && target.GetComponent<Burn>() == null)
            {
                return target;
            }

            return null;
        }

        /// <summary>
        /// The flame's touch is over. On a client, a door, window or furniture
        /// our flame set alight goes to the host as a request
        /// (FireSync.ClientIgnited).
        /// </summary>
        internal static void FlameTouched(GameObject lit)
        {
            if (Coop.Current == null)
            {
                return;
            }

            FlameDone();
            if (lit != null && lit.GetComponent<Burn>() != null)
            {
                FireSync instance = FireSync.Instance;
                if (instance != null)
                {
                    instance.ClientIgnited(lit);
                }
            }
        }

        /// <summary>
        /// No flame is touching anything now (also when the touch threw).
        /// </summary>
        internal static void FlameDone()
        {
            if (Coop.Current == null)
            {
                return;
            }

            ThrownHits.CurrentFlame = null;
            NetEnemies.HitEffects = null;
        }


        /// <summary>
        /// Client: whether what's hitting our player now is an explosion, which
        /// the host reports through our proxy (EnemyAreaHits.Explosion). Not
        /// while we're away in a place of our own (a location, a dream):
        /// explosions there happen only here.
        /// </summary>
        internal static bool HostReports() =>
            ThrownHitRules.HostMayReport(SelfHit()) && InExplosion();

        /// <summary>
        /// What the client looks at for an explosion on its own player.
        /// </summary>
        private static SelfHitFacts SelfHit()
        {
            var facts = new SelfHitFacts
            {
                ClientMode = NetEnemies.ClientMode,
                Depth = ThrownHits.Depth,
                Flame = ThrownHits.CurrentFlame != null,
            };

            // Only a client inside something thrown asks: where the player
            // is costs singleton lookups on every hit.
            if (!facts.ClientMode || facts.Depth <= 0)
            {
                return facts;
            }

            return facts with { Away = EnemyAreaHits.PlayerAway() };
        }

        private static bool InExplosion()
        {
            var st = new System.Diagnostics.StackTrace(2, false);
            for (int i = 0; i < st.FrameCount && i < 12; i++)
            {
                System.Diagnostics.StackFrame frame = st.GetFrame(i);
                System.Type declaringType = null;
                if (frame != null)
                {
                    System.Reflection.MethodBase method = frame.GetMethod();
                    if (method != null)
                    {
                        declaringType = method.DeclaringType;
                    }
                }

                if (declaringType == typeof(Explodes))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Client: whether an effect starting on a character is one the host
        /// reports (CharacterEffects.activate): our own player's, caught in
        /// an explosion, as for HostReports.
        /// </summary>
        internal static bool HostReportsEffect(CharacterEffects effects)
        {
            var player = Player.Instance;
            bool ours = effects != null && player != null
                && effects == player.effects;
            // Called straight from the patch, as HostReports is, so
            // InExplosion's look at the stack starts at the same frame.
            return ThrownHitRules.HostMayReportEffect(SelfHit(), ours)
                && InExplosion();
        }
    }

    /// <summary>
    /// A client's game sends no hits on the host's enemies from anything
    /// thrown: the host's copy of the throw hits them there. That covers the
    /// impact (ThrownItem.onCollide), the explosion (Explodes), what it leaves
    /// burning (flames spawned while it lands or explodes), and the client's
    /// own player being caught in an explosion, which the host's copy reports
    /// through the client's proxy.
    /// </summary>
    internal static class ThrownHits
    {
        /// <summary>
        /// Copies of others' throws in this world. Empty, and keeping
        /// nothing, while no world is shared.
        /// </summary>
        public static HashSet<GameObject> Copies
        {
            get
            {
                ThrownThings here = Here();
                if (here == null)
                {
                    return new HashSet<GameObject>();
                }

                return here.Copies;
            }
        }

        /// <summary>
        /// What thrown things spawned as they landed or exploded (fire,
        /// fuel...), in this world. Empty while no world is shared.
        /// </summary>
        public static HashSet<GameObject> Spawned
        {
            get
            {
                ThrownThings here = Here();
                if (here == null)
                {
                    return new HashSet<GameObject>();
                }

                return here.Spawned;
            }
        }

        /// <summary>
        /// How deep in a thrown thing's impact or explosion we are. Stays 0
        /// while no world is shared.
        /// </summary>
        public static int Depth
        {
            get
            {
                ThrownThings here = Here();
                if (here == null)
                {
                    return 0;
                }

                return here.Depth;
            }

            set
            {
                ThrownThings here = Here();
                if (here != null)
                {
                    here.Depth = value;
                }
            }
        }

        /// <summary>
        /// The flame touching something right now (Flame.onCollideWith).
        /// Stays null while no world is shared.
        /// </summary>
        public static Flame CurrentFlame
        {
            get
            {
                ThrownThings here = Here();
                if (here == null)
                {
                    return null;
                }

                return here.Flame;
            }

            set
            {
                ThrownThings here = Here();
                if (here != null)
                {
                    here.Flame = value;
                }
            }
        }

        /// <summary>
        /// Whether a hit happening now comes from something thrown.
        /// </summary>
        public static bool Now()
        {
            ThrownThings here = Here();
            if (here == null)
            {
                return false;
            }

            return ThrownHitRules.Now(new ThrownHitFacts
            {
                Depth = here.Depth,
                Flame = here.Flame != null,
                UnderSpawned = UnderSpawned(here),
            });
        }

        /// <summary>
        /// Whether the flame touching now, or a parent of it, is something a
        /// thrown thing spawned.
        /// </summary>
        private static bool UnderSpawned(ThrownThings here)
        {
            if (here.Flame == null)
            {
                return false;
            }

            for (var t = here.Flame.transform; t != null; t = t.parent)
            {
                if (here.Spawned.Contains(t.gameObject))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A thrown thing's impact or an explosion starts.
        /// </summary>
        public static void Enter()
        {
            Depth++;
        }

        /// <summary>
        /// The impact or explosion is over.
        /// </summary>
        public static void Leave()
        {
            Depth--;
        }

        /// <summary>
        /// The impact or explosion threw: whatever depth it left is gone.
        /// </summary>
        public static void Failed(System.Exception thrown)
        {
            if (thrown != null)
            {
                Depth = 0;
            }
        }

        /// <summary>
        /// The game spawned something: if it was inside a thrown thing's
        /// landing or explosion (fire, fuel), its flames count as thrown.
        /// </summary>
        public static void Spawn(GameObject go)
        {
            if (Depth > 0 && go != null)
            {
                Spawned.Add(go);
            }
        }

        /// <summary>
        /// The loaded world's, or null with no session or no world.
        /// </summary>
        private static ThrownThings Here()
        {
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return null;
            }

            WorldScope world = coop.World;
            if (world == null)
            {
                return null;
            }

            return world.Thrown;
        }
    }
}
