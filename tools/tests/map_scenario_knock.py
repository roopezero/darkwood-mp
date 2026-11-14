"""Map test steps for loose things knocked about (DragSync's pushed objects); same format as map_scenario.py. The
steps are in src/DarkwoodMP/World/MapScenarioKnock.cs. Run with tools/run-test.sh map:knock.

map:savage found four of the save's dropped items by the test centre moved 10-20 units in both games, at rest 2-5
units apart. They were the test world's kit, dropped (with the game's random push) because it didn't fit the host's
bags, and still sliding when the host read its world for the client, which had joined at once. kn-slide does that on
purpose before the join. Then a host's enemy (whose frozen copy passes through the client's copy), the client's player and the
host's player each push one of those dropped items, and the dropped items are then compared in every game (the
`dropped` section, mapcompare.py's position tolerance)."""

from mapsteps import SAFE_OFF, SAFE_ON

# The host drops a rope that's still sliding when the world is read for the joiner, and at rest before it's in.
PRE_JOIN = [("host", "kn-slide", "rope")]

STEPS = SAFE_ON + [
    ("host", "ee-day"),
    ("host", "kn-info"), ("client", "kn-info"),
    ("host", "kn-enemy", "0"),
    ("host", "kn-info"), ("client", "kn-info"),
    ("client", "kn-player", "1"),
    ("host", "kn-info"), ("client", "kn-info"),
    ("host", "kn-player", "2"),
    ("host", "kn-info"), ("client", "kn-info"),
    ("all", "compare", "host", "dropped"),
    ("host", "ee-day", "off"),
] + SAFE_OFF

REQUIRE = ["dropped"]
