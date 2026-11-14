"""Map test steps for the move from chapter 1 to chapter 2 with several players in; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioChapter.cs. As a map battery (map:chapter) it has one client game;
with several, as players are when the chapter starts, run it in map4:
MAP_SCENARIO=map_scenario_chapter MAP_CLIENTS=3 tools/run-test.sh map4.

Each client puts things of its own in its hotbar, whose size differs from the host's (the one shown on screen must
have the client's slots, not the host's). Last, one client goes far off the map and joins again: it must come back
beside the host. Everyone goes into the bunker at the end of chapter 1, and c1 goes
out of its exit, whose event starts chapter 2 (a joiner's game asks the host for it; CHAPTER_BY=host: the host's game
runs the chapter's step itself, everyone in the world). Every client follows the host, and must be in the host's chapter 2, on the ground beside the host, with its own
hotbar (not the host's). The chapter changes the world for good, so these are POST steps, after the dumps.
"""
import os

CLIENTS = int(os.environ.get("MAP_CLIENTS", "1"))  # map4 sets it; map:chapter has one client
NAMES = [f"c{n}" for n in range(1, CLIENTS + 1)]

STEPS = []
REQUIRE = []

BUNKER = "outside_bunker_underground_part2_01"  # chapter 1's way out: its exit's event starts chapter 2
EXIT = "leave_bunker_underground_part2_01"
# CHAPTER_BY=host: the host's game runs the chapter step itself, with everyone in the world, instead.
BY = os.environ.get("CHAPTER_BY", "c1")

if BY == "host":
    START = [("host", "chapter-go", {"timeout": 320, "settle": 0})]
else:
    # As players reach the end of chapter 1: all in the bunker, and one of them goes out of its exit.
    START = [
        ("host", "loc-go", BUNKER, {"settle": 3}),
        *[(c, "loc-go", BUNKER, {"settle": 3}) for c in NAMES],
        (BY, "chapter-fire", EXIT, {"settle": 0}),
        ("host", "chapter-go", "wait", {"timeout": 320, "settle": 0}),
    ]

POST = [
    # Hotbars of different sizes, as players' upgrades make them: the host's 4 slots, c1's 5, the others 3.
    ("host", "chapter-mark", "keep", "4"),
    *[(c, "chapter-mark", str(i), "5" if i == 0 else "0") for i, c in enumerate(NAMES)],
    *START,
    *[(c, "chapter-in", {"timeout": 580, "settle": 0}) for c in NAMES],
    ("host", "chapter-all", str(CLIENTS), {"timeout": 260}),
    # A kept place that isn't on the map (as a player left in a separate location): back beside the host.
    (NAMES[-1], "chapter-offmap", {"timeout": 420}),
]
