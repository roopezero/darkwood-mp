using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using DarkwoodMP.Net;
using UnityEngine;
using Chapters = DarkwoodMP.World.Story.Chapters;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A world for trying out what's shared, in a controlled way. It's a copy
    /// of one of your profiles in the mod's cache
    /// (BepInEx/cache/DarkwoodMP/testworld), played through the same save
    /// redirect a joiner uses, so your own saves are never written. The copy is
    /// kept between sessions; "Reset" makes a fresh one.
    ///
    /// The first time it loads, the host's game builds a test area around where
    /// the player stands, out of the game's own prefabs: one of each shared
    /// kind of thing (containers, furniture to drag, a workbench, a generator
    /// with lamps on it, the saw, traps, a radio, the night trader), each with
    /// a label saying what to try. Doors and windows aren't separate pieces in
    /// the game (they belong to its building layouts), so the hideout's own are
    /// used. The player gets a kit (gasoline, matches, throwables, a torch, a
    /// lamp, a flashlight, weapons, planks and nails, traps), and the panel
    /// spawns enemies and moves the clock.
    ///
    /// Friends join it as any hosted world.
    /// </summary>
    public static class TestWorld
    {
        public static string Dir =>
            Path.Combine(Paths.CachePath, "DarkwoodMP", "testworld");
        /// <summary>
        /// Where the area was built ("x z chapter"), in the test world's
        /// folder; gone with a reset.
        /// </summary>
        private static string AreaFile =>
            Path.Combine(Dir, "darkwoodmp-testarea.txt");

        /// <summary>The world being played is the test world.</summary>
        public static bool Active =>
            SaveRedirect.Active && SaveRedirect.Dir == Dir;

        internal sealed class Station
        {
            public string Prefab, Label;
            public bool Draggable;
        }

        // What's built, in rows: each with what to try. The label is drawn over
        // it while in the test world.
        internal static readonly Station[] Stations =
        {
            new Station
            {
                Prefab = "Furnitures/Cottage/Wardrobe_low_small_01",
                Label =
                    "Container: take something, it's gone for everyone. Both "
                        + "open it: 'X is using this'"
            },
            new Station
            {
                Prefab =
                    "Loot_containers/_Locations/LootContainer_WoodenCrate_1A",
                Label = "Loot crate: what one takes is gone for the other"
            },
            new Station
            {
                Prefab =
                    "Loot_containers/_Random_Loot/crate_locked_difficulty1",
                Label = "Locked crate: pick the lock (the host rolls it)"
            },
            new Station
            {
                Prefab = "Furnitures/workbench_01",
                Label =
                    "Workbench: put in / take out at once, upgrade the level"
            },
            new Station
            {
                Prefab = "Furnitures/Cottage/Wardrobe_Big_1",
                Label = "Drag it: it moves for everyone and stays",
                Draggable = true
            },
            new Station
            {
                Prefab = "Furnitures/Cottage/crate_movable_01",
                Label = "Drag it, then walk into it",
                Draggable = true
            },
            new Station
            {
                Prefab = "Furnitures/Generator_02",
                Label =
                    "Generator: pour gasoline, switch on; the lamps here light "
                        + "up"
            },
            new Station
            {
                Prefab = "Furnitures/Lamps/Lamp_old_green_01",
                Label = "Lamp on the generator"
            },
            new Station
            {
                Prefab = "Furnitures/Lamps/Lamp_round_yellow",
                Label = "Lamp on the generator"
            },
            new Station
            {
                Prefab = "Objects/saw_stationary_01",
                Label = "Saw: pour gasoline"
            },
            new Station
            {
                Prefab = "Furnitures/Lamps/candles_01",
                Label =
                    "Candles (no Item in this prefab: nothing to light or "
                        + "share)"
            },
            new Station
            {
                Prefab = "Furnitures/Cottage/Radio",
                Label =
                    "Radio (no Item in this prefab: no switch, nothing shared)"
            },
            new Station
            {
                Prefab = "Furnitures/Stove",
                Label = "Stove (no Item in this prefab: nothing shared)"
            },
            new Station
            {
                Prefab = "Traps/beartrap",
                Label = "Bear trap: step in, or disarm it (crouch)"
            },
            new Station { Prefab = "Traps/chainTrap", Label = "Chain trap" },
            new Station
            {
                Prefab = "Traps/barrelExploding",
                Label = "Exploding barrel: hit it or set it alight"
            },
            new Station { Prefab = "Traps/gasBottle_01", Label = "Gas bottle" },
            new Station
            {
                Prefab = "Traps/broken_glass",
                Label = "Broken glass: makes noise"
            },
            new Station
            {
                Prefab = "Objects/larva_egg_big",
                Label = "Larva egg: hit it open, it's broken for everyone"
            },
            new Station
            {
                Prefab = "Traps/infection_splat",
                Label =
                    "Infection: hurts to walk on; a burning weapon clears it "
                        + "for everyone"
            },
            new Station
            {
                Prefab = "Characters/NPC/NightTrader",
                Label = "Trader: what he has, trades, reputation"
            },
        };

        /// <summary>
        /// Handed to the player (once when the area is built, and from the
        /// panel).
        /// </summary>
        private static readonly (string type, int amount)[] Kit =
        {
            ("gasoline", 3), ("matchstick", 10), ("molotov", 3), ("flare", 3),
            ("gasBomb", 2), ("torch", 1), ("lantern", 1),
            ("flashlight", 1), ("battery9v", 2), ("pitchfork", 1),
            ("weapon_shotgun_02_pumpAction", 1), ("ammo_single_shotgun", 12),
            ("plank_nails", 6), ("nail", 20), ("beartrap", 2), ("chainTrap", 1),
            ("bandages", 5), ("wire", 5), ("rope", 3),
        };

        /// <summary>
        /// Enemies the panel can spawn near the player (CharacterSpawner
        /// types).
        /// </summary>
        public static readonly string[] Enemies =
        {
            "Dog",
            "DogMutated",
            "ChomperRed",
            "ChomperBlack",
            "Centipede",
            "Kamikaze",
            "HumanSpider"
        };

        // ---- starting it (main menu) ----------------------------------------

        /// <summary>A copy exists already.</summary>
        public static bool Exists => File.Exists(
            Path.Combine(Dir, "profiles.dat"))
            || (Directory.Exists(Dir)
                && Directory.GetDirectories(Dir, "prof*").Length > 0);

        public static Vector3? Center()
        {
            try
            {
                if (!File.Exists(AreaFile))
                {
                    return null;
                }

                var xz = File.ReadAllText(AreaFile).Split(' ');
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                return new Vector3(
                    float.Parse(xz[0], inv), 0f, float.Parse(xz[1], inv));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The chapter the area was built in (0: not noted, as files written
        /// before it was).
        /// </summary>
        internal static int AreaChapter()
        {
            try
            {
                string[] xz;
                if (File.Exists(AreaFile))
                {
                    xz = File.ReadAllText(AreaFile).Split(' ');
                }
                else
                {
                    xz = new string[0];
                }

                if (xz.Length > 2 && int.TryParse(xz[2], out int c))
                {
                    return c;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        internal static void Build(Vector3 at)
        {
            var spots = FreeSpots(at, Stations.Length);
            Generator gen = null;
            var lamps = new List<Item>();
            int built = 0;
            for (int i = 0; i < Stations.Length && i < spots.Count; i++)
            {
                var st = Stations[i];
                try
                {
                    var go = Core.AddPrefab(
                        st.Prefab, spots[i], Quaternion.Euler(90f, 0f, 0f),
                        Core.ItemContainer, worldSpace: true);
                    if (go == null)
                    {
                        Plugin.Log.LogWarning($"Test world: no {st.Prefab}");
                        continue;
                    }

                    Core.addToSaveable(go, isDynamic: true, assignID: true);
                    built++;
                    if (go.GetComponent<Generator>() is Generator g && !g.isSaw)
                    {
                        gen = g;
                    }

                    if (st.Prefab.Contains("Lamps/Lamp_")
                        && go.GetComponent<Item>() is Item lamp)
                    {
                        lamps.Add(lamp);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Test world: couldn't place {st.Prefab}: {e.Message}");
                }
            }

            if (gen != null)
            {
                foreach (var l in lamps)
                {
                    if (!gen.powerItems.Contains(l))
                    {
                        gen.powerItems.Add(l);
                    }
                }
            }

            try
            {
                File.WriteAllText(
                    AreaFile,
                    FormattableString.Invariant(
                        $"{at.x:0} {at.z:0} {Chapters.HostChapter}"));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    "Test world: couldn't note where the area is: "
                        + $"{e.Message}");
            }

            GiveKit();
            Plugin.Log.LogInfo(
                $"Test world: built {built} of {Stations.Length} test stations "
                    + $"around {at}");
            Plugin.Notice(
                "Test area built around you: each thing is labelled with what "
                    + "to try.");
        }

        /// <summary>
        /// Spots around a point with nothing solid on them, nearest first,
        /// spaced for furniture.
        /// </summary>
        private static List<Vector3> FreeSpots(Vector3 around, int count)
        {
            var spots = new List<Vector3>();
            const float step = 160f;
            for (int ring = 1; ring < 30 && spots.Count < count; ring++)
            {
                // The ring's square, in the order the saved test worlds were
                // built with (the corners come up twice).
                for (int i = -ring; i <= ring && spots.Count < count; i++)
                {
                    foreach (var off in new[]
                    {
                        new Vector3(i, 0, -ring),
                        new Vector3(i, 0, ring),
                        new Vector3(-ring, 0, i),
                        new Vector3(ring, 0, i)
                    })
                    {
                        if (spots.Count >= count)
                        {
                            break;
                        }

                        var p = around + off * step;
                        if (spots.Contains(p))
                        {
                            continue;
                        }

                        if (Physics.OverlapSphere(
                            p, 70f, ~0, QueryTriggerInteraction.Ignore).Any(
                                c => c.attachedRigidbody == null
                                    || !c.attachedRigidbody.CompareTag(
                                        "Player")))
                        {
                            continue;
                        }

                        spots.Add(p);
                    }
                }
            }

            return spots;
        }

        public static void GiveKit()
        {
            Player instance = Player.Instance;
            Inventory inv = null;
            if (instance != null)
            {
                inv = instance.Inventory;
            }

            if (inv == null)
            {
                return;
            }

            foreach (var (type, amount) in Kit)
            {
                try
                {
                    inv.addItemTypeToPlayer(type, amount, dropIfNoRoom: true);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Test world: couldn't give {type}: {e.Message}");
                }
            }

            Plugin.Log.LogInfo("Test world: kit handed out");
        }

        /// <summary>An enemy at a free spot about 400 from the player.
        /// (spawnCharacterAround takes a non-zero offset as a position in the
        /// world, not one from the player: zero makes it look for a free spot
        /// around them.)</summary>
        public static Character SpawnEnemy(string type)
        {
            var player = Player.Instance;
            if (player == null)
            {
                return null;
            }

            // The game tries 10 random spots 400 from the player and gives up
            // if none is on outdoor ground
            // (CharacterSpawner.getFreeSpotAround); near buildings that happens
            // now and then, so ask again.
            Character c = null;
            int tries = 0;
            while (c == null && tries++ < 5)
            {
                c = Singleton<CharacterSpawner>.Instance.spawnCharacterAround(
                    player.gameObject, Vector3.zero, 400f, type,
                    nocturnal: false, attackPlayer: false);
            }

            string data;
            if (c != null)
            {
                string text = "";
                if (tries > 1)
                {
                    text = $" (try {tries})";
                }

                data =
                    $"Test world: spawned {c.name} at "
                        + $"{c.transform.position}{text}";
            }
            else
            {
                data =
                    $"Test world: couldn't spawn {type} in {tries - 1} tries";
            }

            Plugin.Log.LogInfo(data);
            return c;
        }

        /// <summary>Move the clock (the host's: everyone follows).</summary>
        public static void SetTime(float minute)
        {
            var ctl = Singleton<Controller>.Instance;
            if (ctl == null)
            {
                return;
            }

            ctl.CurrentTime = (int)minute;
            ctl.refreshTime();
            Plugin.Log.LogInfo(
                $"Test world: the time is now {minute:0} (day {ctl.day})");
        }
    }
}
