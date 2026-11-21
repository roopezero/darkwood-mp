using System.Collections.Generic;
using DarkwoodMP.World.Throws;
using UnityEngine;

namespace DarkwoodMP.World
{
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
