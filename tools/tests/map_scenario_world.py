"""Map test steps for one of each shared thing in the test map; same format as map_scenario.py.

The steps are in src/DarkwoodMP/World/MapScenarioWorld.cs. Things are named "NAME" (the nearest of that name
to the test area's centre) or "NAME@DX,DZ" (nearest to the centre plus that offset); doors and windows by
number, the Nth nearest to the centre (in the test world: door 0 is the hideout's front door, 1 is broken).
Most things are done once by each player, so each direction is covered: what one game does, the dumps show
in the other.
"""
import map_scenario_status as _status
import map_scenario_core as _core

# Alone (map:world), the core steps haven't run, so nothing has dropped the rope and alcohol that the "Dropped
# items" steps below pick up and put away: the core's take-item and drop-item steps run first. The full scenario
# runs them in the core part and doesn't use this AT_JOIN.
AT_JOIN = [s for s in _core.STEPS if s[1] in ("take-item", "drop-item")]

PRE_JOIN = [
    # What's what (logged; tells which stations have what).
    ("host", "inspect", "candles_01", "Radio", "Stove", "NightTrader", "Generator_02", "saw_stationary_01",
     "barrelExploding", "gasBottle_01", "broken_glass", "LootContainer_WoodenCrate_1A", {"may_fail": True}),
    # In the save the client loads: a barricade and a locked door.
    ("host", "window-barricade", "2"),
    ("host", "door-lock", "2"),
]

STEPS = [
    # Walking about before the join can take the host through the station's bear trap: start both unhurt.
    ("host", "heal"),
    ("client", "heal"),
    # Room in the host's bags (the test kit fills them).
    ("host", "drop-item", "nail", "20"),
    ("host", "drop-item", "ammo_single_shotgun", "12"),
    ("host", "drop-item", "beartrap", "2"),
    # The hideout's bear trap, disarmed by the host before anyone walks into it (into its bags, or sprung).
    ("host", "trap-disarm", "beartrap@270,-261"),

    # Doors: a lock picked, barricades built and broken, both ways.
    ("client", "door-pick", "2"),
    ("host", "door-barricade", "3"),
    ("client", "door-hit", "3", "40"),
    ("client", "door-barricade", "4"),
    ("host", "door-hit", "4", "1000"),
    # Windows.
    ("host", "window-barricade", "0"),
    ("client", "window-hit", "0", "1000"),
    ("client", "window-barricade", "1"),
    ("host", "window-hit", "1", "50"),
    ("client", "window-hit", "2", "30"),  # the one barricaded before the client joined

    # Dropped items, picked up by the other player (the starter steps dropped rope and alcohol).
    ("client", "pickup", "alcohol"),
    ("host", "pickup", "rope"),
    # Containers: put in, taken out by the other; one player at a time.
    ("client", "put-item", "Wardrobe_low_small_01", "alcohol"),
    ("host", "take-from", "Wardrobe_low_small_01"),
    ("host", "hold-open", "Wardrobe_Big_1_Burned"),
    ("client", "open-busy", "Wardrobe_Big_1_Burned"),
    ("host", "release"),
    ("client", "hold-open", "Wardrobe_Big_1_Burned"),
    ("host", "open-busy", "Wardrobe_Big_1_Burned"),
    ("client", "release"),
    # The wooden loot crate is picked open with the progress bar (Item.disarmTime): shut to the client until
    # picked (the host rolls the pick), then open to both.
    ("host", "put-item", "LootContainer_WoodenCrate_1A", "molotov"),
    ("client", "take-from", "LootContainer_WoodenCrate_1A", "--refused"),
    ("client", "pick-crate", "LootContainer_WoodenCrate_1A", {"settle": 2}),
    ("client", "take-from", "LootContainer_WoodenCrate_1A"),
    # The locked crate has a lock (Locked): shut to the client until lockpicked.
    ("client", "take-from", "crate_locked_difficulty1", "--refused"),
    ("client", "pick-crate", "crate_locked_difficulty1", {"settle": 2}),
    ("host", "take-from", "crate_locked_difficulty1"),
    ("client", "take-from", "crate_locked_difficulty1"),

    # Dragging (the host drags the wardrobe below, once the gas bottle beside where it goes has gone off).
    ("client", "drag", "crate_movable_01", "150", "0"),
    # Power: fuel, the generator on; the saw's fuel.
    ("client", "fuel", "Generator_02@-480,-320", "20"),
    ("client", "switch", "Generator_02@-480,-320", "on", {"settle": 2}),
    ("host", "fuel", "Generator_02@-691,-175", "10"),
    ("host", "fuel", "saw_stationary_01@-480,-160", "15"),
    ("client", "fuel", "saw_stationary_01@-480,-160", "15"),
    # The generator doesn't light its lamps by itself (Generator.turnOn only gives them power): each player
    # switches one on.
    ("host", "switch", "Lamp_old_green_01@480,-320", "on"),
    ("client", "switch", "Lamp_round_yellow", "on"),
    # Furniture placed by the host (ObjSpawn) and hit by each player.
    ("host", "place-furniture", "80", "60"),
    ("host", "hit", "*", "10"),
    ("client", "hit", "*", "10"),
    # Furniture the world makes by itself in each game (as a location's characters): the host gives it an id, the
    # client's copy (made a few seconds later at the same spot) is tied to it, and the host's hit lands. (A copy
    # 18 away is tied too, but then the dumps differ in where it stands, which nothing moves.)
    ("host", "world-furniture", "-80", "60"),
    ("client", "world-furniture", "-80", "60"),
    ("host", "ids-tied"),
    ("client", "ids-tied"),
    ("host", "ids-hit"),
    ("client", "ids-tied", "hit"),

    # Traps: placed and disarmed, placed and stepped in, explosives, broken glass. (The station's bear trap can't
    # be the one stepped in: the walks before the join can spring it. The hideout's was disarmed above.)
    # The one to step in comes first: placed second, just after the client's disarm there, it went off as it
    # was placed. trap-place keeps the placer 110-220 away.
    ("host", "trap-place", "beartrap", "0", "-60"),
    ("host", "trap-fire", "beartrap@0,-60"),
    ("host", "heal"),
    ("host", "trap-place", "beartrap", "0", "-60"),
    ("client", "trap-disarm", "beartrap@0,-60"),
    ("client", "trap-fire", "chainTrap"),
    ("client", "trap-fire", "broken_glass"),
] + _status.DISARM + [
    ("client", "heal"),
    ("client", "explode", "barrelExploding", {"settle": 2}),
    ("host", "ignite", "gasBottle_01", {"settle": 2}),
    ("host", "heal"),
    ("client", "heal"),

    # Fire: doors set alight by each (the other game sees them burn), then a molotov each. Not door 0, beside
    # the centre where the players stand: they catch fire and die there.
    ("host", "door-burn", "2"),
    ("client", "expect-burning", "door", "2"),
    ("client", "door-burn", "4"),
    ("host", "expect-burning", "door", "4"),
    # The wardrobe is dragged away from the infection station first (0 150 put it on the patch, which the
    # molotov then burned, so the issue steps cleared the patch they're meant to keep), then set alight there.
    ("host", "drag", "Wardrobe_Big_1", "-150", "-150"),
    ("host", "throw-molotov", "Wardrobe_Big_1@-630,330"),
    ("client", "throw-molotov", "crate_movable_01"),
    ("host", "heal"),
    ("client", "heal"),

    # The story and the journal, both ways.
    ("host", "flag", "0"),
    ("client", "flag", "1"),
    ("client", "reputation", "0", "3"),
    ("host", "journal", "entry"),
    ("client", "journal", "note", {"settle": 3}),
    # The night trader: the host restocks him; a trade each.
    ("host", "trader-restock"),
    ("client", "trade"),
    ("host", "trade", {"settle": 2}),

    # Enemies: the host spawns two; one killed by each player; the corpses searchable in both.
    ("host", "spawn", "Dog"),
    ("host", "spawn", "Dog", {"settle": 3}),
    ("host", "kill-enemy"),
    ("client", "kill-enemy"),
    ("host", "heal"),
    ("client", "heal"),

    # The clock again, and time for fires to burn out before the dumps.
    ("host", "time", "+2"),
    ("host", "wait", "12", {"settle": 3}),
]
