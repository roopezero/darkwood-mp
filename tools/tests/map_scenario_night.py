"""Map test steps for what the night brings and for how smoothly a client draws the host's enemies; same format
as map_scenario.py. The steps are in src/DarkwoodMP/World/MapScenarioNight.cs. Run on their own with
MAP_SCENARIO=map_scenario_night tools/run-test.sh map.

Night events put objects into the world with their spawn steps (World/NightObjects.cs). The host fires the night
mushroom event before the client joins (its mushrooms must reach the client: from the save it loads, or as made
before it came), then the lights events while it plays; "nightobjects" (every object of a kind those steps make,
anywhere) must match. Then eight dogs chase the client while it walks in a circle, and nl-motion counts how its
copies are drawn (EnemyMotion): it fails if they stand still in over 10% of the frames they move."""

SETTLE = 1.5

PRE_JOIN = [
    ("host", "nl-event", "CEvent_spawn_Mushroom"),
]

STEPS = [
    ("host", "nl-inventory"),
    ("both", "compare", "client", "nightobjects"),
    ("host", "nl-event", "CEvent_new_lights", {"settle": 2}),
    # Not CEvent_NR_spawn_deadlyLight: in the test world its light is made on top of a wardrobe, which it pushes a
    # few units, differently in each game (it reaches the client: nightobjects matched; the wardrobe didn't).
    ("host", "nl-event", "CEvent_new_spawn_Mushroom_outside", {"settle": 2}),
    ("both", "compare", "client", "nightobjects"),
    ("host", "i2-safe", "on", {"settle": 0}), ("client", "i2-safe", "on", {"settle": 0}),
    ("client", "ee-arena"),
    ("host", "nl-herd", "8"),
    ("client", "nl-motion", "25"),
    ("host", "nl-clear"),
    ("client", "heal"), ("host", "heal"),
    ("host", "i2-safe", "off", {"settle": 0}), ("client", "i2-safe", "off", {"settle": 0}),
]

POST = []

REQUIRE = ["nightobjects"]
