"""Map test steps for repairs; same format as map_scenario.py. The steps are in
src/DarkwoodMP/World/MapScenarioRepair.cs. Run with tools/run-test.sh map:repair.

A joiner's repair used to come undone: the hammer's last stroke on a broken door rebuilds it (Door.barricade calls
unDestroy), the joiner sent it as DoorBarricade, and the host refused a barricade on a broken door, so the host's
state (broken) was put back and the door broke again in the joiner's game. Here the host breaks a door and the client
rebuilds it; the client breaks a piece of furniture and builds it again from its construction menu; then both games
must keep both repaired for several seconds, and the doors and items must be the same in both."""

from mapsteps import SAFE_OFF, SAFE_ON

STEPS = SAFE_ON + [
    # A door, broken by the host (as enemies breaking in would), rebuilt by the client.
    ("host", "rp-door", "1"),
    ("client", "rp-door", "1"),
    ("host", "rp-door-break"),
    ("client", "rp-door-fix"),
    # Furniture the host placed (shared as ObjSpawn), smashed and built again by the client.
    ("host", "place-furniture", "80", "60"),
    ("host", "rp-furniture", "80", "60"),
    ("client", "rp-furniture", "80", "60"),
    ("client", "rp-furniture-break"),
    ("client", "rp-furniture-fix"),
    # Both stay repaired in both games.
    ("client", "rp-hold", "6", {"settle": 0}),
    ("host", "rp-hold", "6"),
    ("all", "compare", "host", "doors,items"),
] + SAFE_OFF

REQUIRE = ["doors", "items"]
